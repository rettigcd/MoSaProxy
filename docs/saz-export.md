# MoSaProxy's `.saz` export: current state

What MoSaProxy writes when it saves a session as a Fiddler `.saz`, and how that differs from a file Fiddler wrote.
The format itself is described in [the `.saz` spec](specs/saz-format.md); this document is about MoSaProxy only.
The comparison was made against one Fiddler-written file (Fiddler 6.0.20262.10021, 375 sessions).

## Summary

- **Code:** `SazExporter` ([core/SazExporter/SazExporter.cs](../core/SazExporter/SazExporter.cs)) packs a capture run folder into a `.saz`.
  From the console: `--saz` writes one when the proxy stops, and `--export-saz RUN_DIR [FILE]` converts an existing folder.
- **Matches the reference file:** the zip layout (the `raw/` folder entry, entry order, file names padded to the width of the highest session number, comment naming MoSaProxy as the
  writer); `_m.xml` in Fiddler's form (BOM, CRLF, `Session` / `SessionTimers` / `PipeInfo` / `TunnelInfo` / `SessionFlags`, real timers and
  `BitFlags` where the proxy can observe them); `_index.htm` with Fiddler's twelve columns; `[Content_Types].xml` byte for byte; and the
  absolute request line for requests inside a tunnel.
- **Verified:** Fiddler opens an archive MoSaProxy wrote (a run of about 1,500 sessions). The first attempt reported
  `Request corrupt, unable to find end of headers` for a session whose request was not HTTP and left that session out, loading the
  others (see the next bullet); once that was fixed the session loaded too. This confirms the layout above is acceptable to Fiddler.
  Variants of a small archive, each with one thing removed or changed, showed that the loader needs none of the `_index.htm`, the zip
  comment, the content-types file, the `raw/` folder entry or a particular entry order, and accepts unpadded numbers and missing or empty
  response files (see section 10.1 of the spec). Matching those details is therefore for fidelity with Fiddler's own files, not for loading.
- **Requests that are not HTTP:** Fiddler rejects a session whose request lacks an end of headers (it reports the session and loads
  the rest). A request with no blank line is made loadable on export, so the session appears: one cut off part way through its headers gets the blank line, and anything else (bytes of another
  protocol sent through a tunnel, or nothing) becomes a `POST /` to `not-http.invalid` with a note header and the bytes as its body.
- **Data the export relies on** is recorded at capture time: when each stage of an exchange happened (`ExchangeTimes`), whether the client
  and origin connections were reused, the origin connection's number, the client's port and the TLS version negotiated with the client.

## Differences still to fix

These are the places where `SazExporter` output differs from what Fiddler wrote. None is known to
stop Fiddler from loading the file (that is still untested), but each is a place to look first if it does not.

| # | Area | Fiddler (reference) | `SazExporter` today |
|---|---|---|---|
| (a) | `PipeInfo`, `TunnelInfo` | Always `PipeInfo`; `TunnelInfo` on CONNECT | **Fixed** for HTTP/1.x. `PipeInfo` on every session, with `CltReuse="true"` when the client's connection had already carried a request and `Reused="true"` when the origin connection had; `TunnelInfo` (BytesEgress / BytesIngress) on CONNECT sessions. The same facts set the ClientPipeReused / ServerPipeReused bits of `BitFlags`. HTTP/2 sessions never get the reuse marks |
| (b) | CONNECT sessions | Separate numbered session per tunnel, with the ClientHello / certificate reports | MoSaProxy writes tunnel exchanges as its own sessions; the report text is not generated |
| (c) | `_w.txt` (WebSocket messages) | One record per frame: a header (`Request-Length:` or `Response-Length:`, `ID`, `BitFlags`, three timestamps), then the raw frame bytes as sent, masked for the client's ([spec section 5.3](specs/saz-format.md)). | MoSaProxy's own readable log of whole messages (timestamp, direction, type, size, the decoded text, or a hex dump for binary). Whether Fiddler shows these as WebSocket messages has not been tried. MoSaProxy handles messages, not frames, so matching Fiddler needs frames rebuilt from the messages (unmasked frames for the origin's, a mask chosen for the client's) or the proxy to record the real frames. |
| (d) | A CONNECT whose tunnel carried plain HTTP (a `ws://` or `http://` client going through a tunnel) | `BitFlags` `0x200100`. | `0x2100` (a decrypted tunnel): the exporter cannot tell this kind of tunnel from a decrypted one. |

Differences that are fully fixed have been removed from this table, and those fixed as far as the available information allows
are in the next section.

## Done as far as the information allows

Differences from Fiddler that `SazExporter` now handles as far as the information in hand allows. Each needs a Fiddler-written
reference, or Fiddler's own definition, to go further.

| # | Area | What was done | What is not known |
|---|---|---|---|
| 1 | `BitFlags` | Built from the published flags: CONNECT = ResponseGeneratedByFiddler + IsDecryptingTunnel or IsBlindTunnel (`2100` / `1100`, matching Fiddler); any other session = IsHTTPS for https, plus ClientPipeReused / ServerPipeReused, plus IsWebSocketTunnel for a WebSocket. Plain HTTP matches Fiddler exactly (`0`, `0x10`, `0x18`) ([spec section 6.2](specs/saz-format.md)). | The `0x2` bit in the one `0x13` value Fiddler wrote (later captures gave `0x19`, which we now write for every request decrypted in a tunnel); the `0x10` on a direct HTTPS request that did not reuse a connection; the streaming bits (RequestStreamed, ResponseStreamed), which MoSaProxy does not record.  |
| 2 | `_index.htm` | Written in Fiddler's form: BOM, one line, the same 12 columns, single-quoted backslash links, `Tunnel to` rows for CONNECT, URL as sent (not percent-escaped), Body with thousands separators, Caching as `Cache-Control` plus `; Expires: ...`, Content-Type as sent. | The Process column (client `process:pid`), Comments and Custom are left empty because MoSaProxy does not record them. The Caching rule is read from the sample only for Cache-Control and Expires; when only Expires or `Pragma` is present, or how a `Vary`/`ETag` affects the cell, is not known. Whether the Host column adds a non-default port. Whether a WebSocket session gets a fourth link. |
| 3 | `SessionTimers` | Real moments from the proxy for ClientConnected, GotRequestHeaders, ClientDoneRequest, FiddlerBeginRequest, GotResponseHeaders, ClientBeginResponse and ClientDoneResponse; a CONNECT has the three server-response timers unset (`0001-01-01T00:00:00`), as Fiddler writes them. HTTP/1.x and HTTP/2 requests both. | Approximated, because the proxy cannot observe them: ClientBeginRequest (= GotRequestHeaders), ServerConnected (= FiddlerBeginRequest), ServerGotRequest (the later of FiddlerBeginRequest and ClientDoneRequest), ServerBeginResponse (= GotResponseHeaders; `HttpClient` does not expose the first byte) and ServerDoneResponse (= ClientDoneResponse, because the body is copied as it arrives). `GatewayTime`, `DNSTime`, `TCPConnectTime` and `HTTPSHandshakeTime` are always `0`: measuring them means taking over connection setup in the `HttpClient` and matching each connection to the exchange that opened it. CONNECT sessions have no measured timers at all yet (only the start and end). |
| 4 | Request line of a request inside a tunnel | The exporter rewrites a path-only request line (`GET /p HTTP/1.1`) to the absolute form (`GET https://host/p HTTP/1.1`) and copies every other byte unchanged. The capture files keep what the client sent. | The reference file has only HTTP/1.1 requests inside tunnels, so the form Fiddler writes for an HTTP/2 request (we write `GET https://host/p HTTP/2`) is not confirmed. Whether Fiddler writes a non-default port in the URL exactly as we do (`host:8443`) is assumed from how URLs normally look. |
| 5 | `SessionFlags` | Written: `x-clientip`, `x-clientport`, `x-responsebodytransferlength` (thousands separators; `0` for a CONNECT), `x-serversocket` (`REUSE ServerPipe#N`, only for a request that reused an origin connection, as in the reference file), and `https-client-version` (the TLS version inside an intercepted tunnel, `None` for plain HTTP). Lower-case names, as Fiddler writes them. Plus MoSaProxy's own `x-mosaproxy-*` flags, which Fiddler ignores. | Not written, because MoSaProxy does not observe them: `x-hostip` (the origin's address; `HttpClient` does not expose it), `x-egressport` (the local port towards the origin), `x-processinfo` (the client process: needs a Windows port-to-process lookup), `https-server-version` and `https-server-cipher` (the origin handshake, see the CONNECT row in the table above), and the ClientHello facts `x-supported-client-alpn`, `x-supported-client-tls-protocols`, `https-client-snihostname` and `https-client-sessionid` (need the ClientHello parse). `https-client-version` is absent on the CONNECT session itself (the handshake happens after the CONNECT is recorded). Also not written: the failure flags of a session Fiddler could not connect (`x-failsession-when`, `x-aborted-when`, `x-dns-failover`) and the host-alias flags (`x-usedvirtualhost`, `x-overridecertcn`). |

