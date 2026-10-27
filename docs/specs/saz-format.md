# Fiddler `.saz` (Session Archive Zip) format

**Status:** descriptive spec of the archive format that Fiddler writes.

How to read this document: statements marked **(inferred)** come from memory of Fiddler's behaviour or from public documentation and have
not been checked against a Fiddler-written archive. Statements marked **(unknown)** are open (collected in section 11). Everything else
has been checked against a Fiddler-written archive. The excerpts below are shortened and use made-up hosts and values.

## 1. Big picture

A `.saz` is an ordinary zip file. Each captured HTTP exchange ("session") is stored as three files in a `raw/` folder, numbered from 1:

| File | Holds |
|---|---|
| `raw/NNN_c.txt` | The **c**lient's request, as bytes: request line, headers, blank line, body. |
| `raw/NNN_s.txt` | The **s**erver's response, as bytes: status line, headers, blank line, body. |
| `raw/NNN_m.xml` | **m**etadata: timers, session flags, connection info. XML. |
| `raw/NNN_w.txt` | Optional. WebSocket messages for that session **(inferred)**. |
| `raw/NNN_g.txt` | Optional. gRPC messages for that session **(inferred, from Telerik's description of the format)**. |

Two further files sit at the archive root: `_index.htm` (a human-readable table for people who unzip the file) and `[Content_Types].xml`
(an Open Packaging Conventions content-type map). A zip comment identifies the writer. Fiddler rebuilds its session list from `raw/`,
so `_index.htm` is a convenience, not data (the loader does not need it, section 10.1).

## 2. The zip container

| Property | Value |
|---|---|
| Entries | One `raw/` folder entry, three files per session, `_index.htm` and `[Content_Types].xml`. |
| Entry order | `raw/` (directory entry), then for each session in number order `NNN_c.txt`, `NNN_s.txt`, `NNN_m.xml`, then `_index.htm`, then `[Content_Types].xml` last. |
| Path separator | Forward slash (`raw/001_c.txt`). |
| Compression | Deflate for every entry, including the empty directory entry. |
| Writer | "version made by" 5.1, host system FAT (MS-DOS); .NET `System.IO.Compression`-style output with Zip64 extra fields on entries. |
| Timestamps | All entries carry the save time (the moment the archive was written), not the capture time. |
| Archive comment | `Fiddler (vX.Y.Z.W) Session Archive. See https://api.getfiddler.com/r/?fiddler` |
| Encryption | Fiddler can password-protect a `.saz` with zip encryption **(inferred)**; a reader should be prepared for it. |

## 3. Numbering and names

- Files are named `NNN_c.txt`, `NNN_s.txt`, `NNN_m.xml` with the session number **zero-padded to 3 digits** (`001`, `002`, ...).
  What Fiddler writes past 999 sessions (4 digits, or a wider pad chosen from the total) is **(unknown)**. Its loader accepts any width:
  unpadded and 4-digit numbers load (section 10.1).
- The number is the session ID (`SID` in the XML). A saved subset of a longer capture may have gaps in the numbering **(inferred)**; the
  `SID` in the XML is the authoritative number.
- The three files of a session share one number. What Fiddler writes for a session with no response (an empty `_s.txt`, or no file) is **(unknown)**; the loader accepts both (section 10.1).

## 4. `NNN_c.txt` - the request

Raw HTTP request bytes:

```
GET https://example.com/path?x=1 HTTP/1.1\r\n
User-Agent: ...\r\n
Host: example.com\r\n
...\r\n
\r\n
<body bytes, if any>
```

- **No byte-order mark.** Header lines end in CRLF; the header block ends with an empty CRLF line.
- Header **case and order are preserved** exactly as the client sent them (`User-Agent`, `sec-ch-ua`, ...).
- **Request-target form depends on the session type:**
  - Decrypted HTTPS and plain HTTP sessions: the **absolute URL** (`GET https://host/path HTTP/1.1`), even where the client sent only the
    path. Fiddler rewrites the request line to the absolute form for decrypted HTTPS. So the scheme and the full URL are recoverable
    from the first line alone.
  - CONNECT tunnel sessions: authority form (`CONNECT example.com:443 HTTP/1.1`).
- **Every request file must be a parseable HTTP request: a request line (`METHOD target HTTP/x`) and an end of headers (a blank line).**
  Fiddler checks this for each session when it loads an archive. A request without them makes it reject that session, with
  `Invalid data was present for session [raw/N]` and `Request corrupt, unable to find end of headers`; the other sessions still load.
  A file that holds anything else (the bytes of another protocol, a request cut off before its blank line, or nothing) must be turned
  into a valid request before it is written if the session is to appear.
- The body follows the blank line as received on the wire.
- Request bodies are stored as sent, so compressed or chunked bodies stay compressed or chunked **(inferred; consistent with the
  Fiddler UI offering "decode" as a view, not a storage change)**.

### 4.1 CONNECT sessions: the request file also carries the TLS ClientHello

For a CONNECT tunnel the `_c.txt` does not end after the CONNECT headers. After the blank line Fiddler appends a **human-readable text
report**, with LF-only line breaks, of the ClientHello it parsed from the tunnelled bytes:

```
CONNECT example.com:443 HTTP/1.1\r\n
Host: example.com:443\r\n
...\r\n
\r\n
A SSLv3-compatible ClientHello handshake was found. Fiddler extracted the parameters below.\n
\n
Version: 3.3 (TLS/1.2)\n
Random: 6D E0 1F B9 ... (32 bytes, hex)\n
"Time": 6/2/2068 10:47:09 AM\n          <- the first 4 random bytes read as a Unix time; meaningless
SessionID: D2 3D 3F EB ...\n
Extensions: \n
\tgrease (0x4a4a)\tempty\n
\tserver_name\texample.com\n
\tALPN\t\th2, http/1.1\n
...
Ciphers: \n
\t[1301]\tTLS_AES_128_GCM_SHA256\r\n     <- note: CRLF on cipher lines
...
```

This is not HTTP data; it is a diagnostic. A tool that reads `_c.txt` as a request must stop at the first blank line for CONNECT sessions.
The mixed LF / CRLF line endings are real.

## 5. `NNN_s.txt` - the response

Raw HTTP response bytes: status line, headers, blank line, body, all as received from the server (headers CRLF, original case and order).
No BOM.

- Bodies are stored **exactly as sent on the wire**: gzip stays gzip and `Transfer-Encoding: chunked` bodies keep their chunk framing.
  A reader must decode these itself.
- Fiddler adds nothing to ordinary responses. (When Fiddler itself synthesises a response, for example a 502 for a connection failure,
  the body is Fiddler's own HTML **(inferred)**.)

### 5.1 CONNECT sessions: the response file is a synthetic message plus the server certificate report

For a CONNECT tunnel the "response" is Fiddler's own `200 Connection Established`, followed by a text report of the server-side TLS
handshake:

```
HTTP/1.1 200 Connection Established\r\n
FiddlerGateway: Direct\r\n
StartTime: 08:12:42.241\r\n
Connection: close\r\n
\r\n
Encrypted HTTPS traffic flows through this CONNECT tunnel. HTTPS Decryption is enabled in Fiddler, so decrypted sessions running in this tunnel will be shown in the Web Sessions list.\n
\n
Secure Protocol: Tls12\n
Cipher: Aes128 128bits\n
Hash Algorithm: Sha256 ?bits\n
Key Exchange: ECDHE_RSA (0xae06) 256bits\n
\n
== Server Certificate ==========\r\n
[Subject]\r\n  CN=*.example.com, ...\r\n
[Issuer]\r\n  ...\r\n
[Serial Number]\r\n  ...\r\n
[Not Before]\r\n  12/14/2025 7:00:00 PM\r\n
[Not After]\r\n  1/5/2027 6:59:59 PM\r\n
[Thumbprint]\r\n  D19C45...\r\n
[SubjectAltNames]\r\n*.example.com, example.com, ...
```

- `FiddlerGateway:` and `StartTime:` (local time, `HH:mm:ss.fff`) are headers added by Fiddler, not by any server.
- The wording ("HTTPS Decryption is enabled...") varies with Fiddler's settings: a tunnel that is not decrypted says so instead
  **(inferred)**.
- These are not the tunnel's bytes. Fiddler does not store the encrypted traffic.

## 6. `NNN_m.xml` - session metadata

UTF-8 **with a BOM** (`EF BB BF`), CRLF line breaks, two-space indent, **no trailing newline** after `</Session>`.

### 6.1 Shape

```xml
<?xml version="1.0" encoding="utf-8"?>
<Session SID="1" BitFlags="81">
  <SessionTimers ClientConnected="..." ClientBeginRequest="..." GotRequestHeaders="..." ClientDoneRequest="..."
                 GatewayTime="0" DNSTime="2" TCPConnectTime="9" HTTPSHandshakeTime="29"
                 ServerConnected="..." FiddlerBeginRequest="..." ServerGotRequest="..." ServerBeginResponse="..."
                 GotResponseHeaders="..." ServerDoneResponse="..." ClientBeginResponse="..." ClientDoneResponse="..." />
  <PipeInfo />
  <TunnelInfo BytesEgress="0" BytesIngress="0" />      <!-- CONNECT sessions only -->
  <SessionFlags>
    <SessionFlag N="x-egressport" V="61424" />
    ...
  </SessionFlags>
</Session>
```

Element order is always `SessionTimers`, `PipeInfo`, [`TunnelInfo`], `SessionFlags`.

### 6.2 `Session` attributes

| Attribute | Meaning |
|---|---|
| `SID` | Session number; equals `NNN` in the file name. |
| `BitFlags` | Fiddler's `SessionFlags` enum value, written in **hexadecimal without a prefix** (values such as `2100` and `1100` only decode sensibly as hex). |

The enum is published in Telerik's [FiddlerCore API reference](https://www.telerik.com/fiddler/fiddlercore/documentation/api/fiddler.sessionflags)
(older copy with hex values: <https://bayden.com/om/html/C94927C5.htm>). It is a `[Flags]` enum; each name below is a single bit.

| Bit (hex) | Name | Bit (hex) | Name |
|---|---|---|---|
| `0x1` | IsHTTPS | `0x8000` | ProtocolViolationInRequest |
| `0x2` | IsFTP | `0x10000` | ProtocolViolationInResponse |
| `0x4` | Ignored | `0x20000` | ResponseBodyDropped |
| `0x8` | ClientPipeReused | `0x40000` | IsWebSocketTunnel |
| `0x10` | ServerPipeReused | `0x80000` | SentToSOCKSGateway |
| `0x20` | RequestStreamed | `0x100000` | RequestBodyDropped |
| `0x40` | ResponseStreamed | `0x200000` | IsRPCTunnel |
| `0x80` | RequestGeneratedByFiddler | `0x400000` | IsModifiedByRule |
| `0x100` | ResponseGeneratedByFiddler | `0x800000` | IsGRPCTunnel |
| `0x200` | LoadedFromSAZ | `0x1000000` | IsHTTP2OverCONNECTTunnel |
| `0x400` | ImportedFromOtherTool | `0x2000000` | IsSignalR |
| `0x800` | SentToGateway | `0x4000000` | IsSocketIO |
| `0x1000` | IsBlindTunnel | `0x8000000` | IsCachedSession |
| `0x2000` | IsDecryptingTunnel | | |
| `0x4000` | ServedFromCache | | |

Values that Fiddler writes, decoded with that table:

| Value | Session | Decoding |
|---|---|---|
| `0x2100` | A CONNECT tunnel that Fiddler decrypted | IsDecryptingTunnel + ResponseGeneratedByFiddler. Fully explained. |
| `0x1100` | A CONNECT tunnel passed through undecrypted | IsBlindTunnel + ResponseGeneratedByFiddler. Fully explained. |
| `0x13` | A request decrypted inside a tunnel, on a reused origin connection | IsHTTPS + ServerPipeReused + `0x2`, which the table calls IsFTP and which makes no sense on an HTTPS request. **`0x2` is unexplained.** ClientPipeReused (`0x8`) is absent although `PipeInfo` says `CltReuse="true"`. |
| `0x51` | A direct HTTPS request (no CONNECT) on a new connection | IsHTTPS + ServerPipeReused + ResponseStreamed. `PipeInfo` shows no reuse, so **`0x10` is unexplained** here. |

So the published table explains the tunnel values but not every bit Fiddler writes on ordinary requests. The value for a plain HTTP
session is **(unknown)**; by the table it would carry no IsHTTPS bit.

### 6.3 `SessionTimers`

All sixteen attributes are always present. Twelve are timestamps, four are durations.

**Timestamps** are ISO 8601 with 7 fractional digits and the **local UTC offset** (`2026-10-27T08:12:04.6603782-04:00`). A timer that never
happened is `0001-01-01T00:00:00` (no offset); for example the three server-response timers of a CONNECT session.

| Attribute | Moment |
|---|---|
| `ClientConnected` | The client's TCP connection to Fiddler was accepted. For a reused client connection this is earlier than the request. |
| `ClientBeginRequest` | First byte of the request read. |
| `GotRequestHeaders` | Request headers complete. |
| `ClientDoneRequest` | Request body complete. |
| `ServerConnected` | Connection to the origin established (earlier than the request when a server connection is reused). |
| `FiddlerBeginRequest` | Fiddler began sending the request upstream. |
| `ServerGotRequest` | Request fully written upstream. |
| `ServerBeginResponse` | First byte of the response read. |
| `GotResponseHeaders` | Response headers complete. |
| `ServerDoneResponse` | Response body complete. |
| `ClientBeginResponse` | Fiddler began sending the response to the client. |
| `ClientDoneResponse` | Response fully sent to the client. |

**Durations** are integer milliseconds: `GatewayTime` (talking to an upstream proxy), `DNSTime`, `TCPConnectTime`, `HTTPSHandshakeTime`.
For a session that reused a connection they are all `0`.

Order of attributes in the file: `ClientConnected`, `ClientBeginRequest`, `GotRequestHeaders`, `ClientDoneRequest`, `GatewayTime`, `DNSTime`,
`TCPConnectTime`, `HTTPSHandshakeTime`, `ServerConnected`, `FiddlerBeginRequest`, `ServerGotRequest`, `ServerBeginResponse`,
`GotResponseHeaders`, `ServerDoneResponse`, `ClientBeginResponse`, `ClientDoneResponse`.

### 6.4 `PipeInfo`

Always present, either an empty element or with these attributes:

| Form | Meaning |
|---|---|
| `<PipeInfo />` | A fresh connection. |
| `<PipeInfo CltReuse="true" Reused="true" />` | The client's connection and the server connection were both reused (keep-alive). |

Other attributes (for example a lone `Reused`, or `CltReuse` only) are **(unknown)**.

### 6.5 `TunnelInfo`

Present only on CONNECT sessions: `<TunnelInfo BytesEgress="0" BytesIngress="0" />`. The byte counts are the bytes sent to the origin
(egress) and received from it (ingress) through the tunnel. For a decrypted tunnel they are `0`, because the traffic is accounted to the
inner sessions; for a tunnel passed through undecrypted they are the totals.

### 6.6 `SessionFlags`

A list of `<SessionFlag N="name" V="value" />`: Fiddler's free-form per-session string dictionary. Order is not stable between sessions;
names are lower-case; values are always strings. Any flag Fiddler or an extension sets is saved, so readers must accept unknown names.
Flags Fiddler writes:

| Name | Written on | Value / meaning |
|---|---|---|
| `x-responsebodytransferlength` | Every session | Response body length as it crossed the wire, with thousands separators (`2,970`); `0` for a CONNECT. |
| `x-egressport` | Every session | Local port Fiddler used to connect upstream. |
| `x-clientip` | Every session | Client address (`127.0.0.1`); empty for a request Fiddler made itself. |
| `x-clientport` | Every session | Client's source port; `0` for a request Fiddler made itself. |
| `x-hostip` | Every session | Resolved IP address of the origin. |
| `x-processinfo` | Sessions from a client process | `process:pid` of the client (`chrome:33556`). |
| `x-supported-client-alpn` | Sessions from a client process | ALPN list the client offered (`h2, http/1.1`). |
| `x-supported-client-tls-protocols` | Sessions from a client process | Bit mask of TLS versions the client offered (`15360`). |
| `https-client-version` | Sessions from a client process | TLS version negotiated with the client (`Tls13`). |
| `https-client-snihostname` | CONNECT sessions | SNI the client sent. |
| `https-client-sessionid` | CONNECT sessions | TLS session ID, hex pairs separated by spaces. |
| `https-server-version` | Sessions where Fiddler completed a TLS handshake with the origin | TLS version negotiated with the origin (`Tls12`). |
| `https-server-cipher` | Same | Cipher negotiated with the origin (`Aes128 128bits`). |
| `https-server-sessionid` | Requests Fiddler made itself | TLS session ID of the origin connection. |
| `x-serversocket` | Requests that reused an origin connection | `REUSE ServerPipe#37`: the connection pool entry the request used. The form for a new connection is **(unknown)**; no flag was written for those. |
| `x-autoauth` | Requests Fiddler made itself | `(default)`. |
| `x-builder-maxredir` | Requests Fiddler made itself | `3`. |
| `x-overridesslprotocols` | Requests Fiddler made itself | `tls1.0;tls1.1;tls1.2`. |

Flags that control how a session is shown. Fiddler reads these from the metadata when it loads an archive and applies them:

| Name | Value | Effect |
|---|---|---|
| `ui-color` | A colour name (`red`) | Text colour of the session's row. |
| `ui-backcolor` | A colour name (`LightYellow`) | Background colour of the row. |
| `ui-bold` | `true` | Row shown in bold. |
| `ui-comments` | Free text | The session's text in the Comments column. |

Other flags that Fiddler writes in other situations **(inferred)**: `x-breakrequest`, `x-breakresponse`, `x-replaceonrequest`,
`x-fromcache`, `x-Auto-Authed`, `x-clientcert`, `x-original-url`, `x-ssl-error`.

## 7. `_index.htm` - the human-readable table

UTF-8 **with a BOM**, a single line, no doctype. It exists for people who unzip the archive; Fiddler does not need it (section 10.1).
Structure:

```html
<html><head><style>body,thead,td,a,p{font-family:verdana,sans-serif;font-size: 10px;}</style></head><body>
<table cols=12><thead><tr><th>&nbsp;</th><th>#</th><th>Result</th><th>Protocol</th><th>Host</th><th>URL</th><th>Body</th>
<th>Caching</th><th>Content-Type</th><th>Process</th><th>Comments</th><th>Custom</th></tr></thead><tbody>
<tr><td><a href='raw\001_c.txt'>C</a>&nbsp;<a href='raw\001_s.txt'>S</a>&nbsp;<a href='raw\001_m.xml'>M</a></td>
    <td>1</td><td>200</td><td>HTTPS</td><td>example.com</td><td>/path?x=1&amp;y=2</td>
    <td>4,805</td><td>max-age=900</td><td>text/plain</td><td></td><td></td><td></td></tr>
...
</tbody></table></body></html>
```

- Twelve columns, in the order above. Links use **backslashes** (`raw\001_c.txt`), single-quoted.
- `Protocol` is `HTTP` or `HTTPS`. For a CONNECT tunnel the protocol is `HTTP`, `Host` is the literal text `Tunnel to`, and `URL` is
  the authority (`example.com:443`), `Body` is `0`.
- `URL` is path and query only (no host) for ordinary sessions; HTML-encoded (`&amp;`).
- `Body` is the response body length with thousands separators. `Process` is the `x-processinfo` flag.
- `Caching` is the `Cache-Control` value, followed by `; Expires: ` and the `Expires` value when there is one (`max-age=900; Expires: Tue,
  27 Oct 2026 12:27:47 GMT`). `Content-Type` is the header value as sent. What `Caching` shows when only `Expires` or `Pragma` is present
  is **(unknown)**.
- `Comments` shows the session's comment and `Custom` a user-defined column **(inferred)**; both are empty by default.

## 8. `[Content_Types].xml`

**No BOM.** Exactly this (lines separated by CRLF, nothing after the last tag):

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="htm" ContentType="text/html" />
<Default Extension="xml" ContentType="application/xml" />
<Default Extension="txt" ContentType="text/plain" />
</Types>
```

It is the Open Packaging Conventions marker; it is the last entry in the zip. Note the space before `?>` in the declaration.

## 9. Encodings and line endings, at a glance

| File | BOM | Line breaks |
|---|---|---|
| `NNN_c.txt`, `NNN_s.txt` | none | CRLF in the HTTP head; the body is raw bytes; CONNECT reports mix LF and CRLF (sections 4.1, 5.1) |
| `NNN_m.xml` | UTF-8 BOM | CRLF, no final newline |
| `_index.htm` | UTF-8 BOM | none (one line) |
| `[Content_Types].xml` | none | CRLF |

The header block of `_c.txt` / `_s.txt` should be treated as Latin-1 / bytes, not UTF-8, because header values may hold arbitrary bytes.

## 10. Reading a `.saz` (minimum viable reader)

1. Open as zip. Enumerate entries matching `^raw/(\d+)_(c|s|m|w)\.(txt|xml)$`; group by the number.
2. For each number read `_c.txt` as bytes, split at the first `\r\n\r\n` into head and body. (Fiddler treats a request with no `\r\n\r\n` as an error, see section 4.)
3. Do the same for `_s.txt`.
4. Parse `_m.xml` (skip the BOM). Take timers from `SessionTimers`, everything else from `SessionFlags`.
5. If the request method is `CONNECT`, treat the remainder after the blank line as the ClientHello report and the remainder of the response
   as the certificate report; there is no HTTP body.
6. Decode bodies per the `Content-Encoding` / `Transfer-Encoding` headers.

### 10.1 What Fiddler's loader requires

Checked by loading archives that each differed from a working one in a single way. Fiddler loaded every one of them with all sessions:

| Change | Result |
|---|---|
| No `_index.htm` | Loads. |
| No zip comment | Loads. |
| No `[Content_Types].xml` | Loads. |
| No `raw/` folder entry | Loads. |
| `[Content_Types].xml` and `_index.htm` stored first | Loads. |
| Sessions stored in reverse order | Loads. |
| Only the `raw/NNN_*` files (none of the above) | Loads. |
| A session with no `_s.txt` | Loads. |
| A session with an empty `_s.txt` | Loads. |
| Unpadded numbers (`raw/1_c.txt`) | Loads. |
| Numbers of four digits (`raw/1000_c.txt`) | Loads. |

So the per-session files under `raw/` are all that the loader needs: the index, the content-types file, the comment, the folder entry and the
entry order are conventions of Fiddler's writer, and the width of the number is not significant. A request that is not parseable HTTP
is the one thing found so far that a session cannot load with (section 4). Not tried: a session with no `_c.txt` or no `_m.xml`.

## 11. Open questions

The loader's tolerance of the container (what it requires and ignores) is settled in section 10.1. What remains is what Fiddler itself writes.

1. Width of the zero padding that Fiddler writes past 999 sessions. (The loader accepts any width.)
2. The `0x2` bit Fiddler sets on decrypted requests, and the `0x10` on a non-reused direct HTTPS request (section 6.2).
3. The exact form of `_w.txt` (WebSocket messages), and any `ui-*` flags beyond `ui-color`, `ui-backcolor` and `ui-bold`; neither is documented.
4. Password-protected archives: the encryption method and how the password is requested.
5. Whether Fiddler writes an empty `_s.txt` or omits the file for a session with no response. (The loader accepts both.)
6. The `BitFlags` value of a plain HTTP session, and the `x-serversocket` form for a request on a new origin connection.
