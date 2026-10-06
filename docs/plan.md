# mos: Traffic-Logging Proxy Plan

## Goal

A forward proxy that logs traffic for both HTTP and HTTPS, and speaks both HTTP/1.1 and HTTP/2.
Browsers and other clients are pointed at it with a normal proxy setting
(e.g. Chrome `--proxy-server`).

## Where we are

`Program.cs` is a proof of concept:

- Plain-HTTP only, one request per connection (`Connection: close`).
- Hand-rolled HTTP/1.1 parsing over `TcpListener`.
- `CONNECT` (HTTPS) is rejected with 405.
- Chunked request bodies are rejected with 501.
- Logging is a single `Console.WriteLine` of method and URL.

The hand-rolled parser is fine for a spike but should not be extended into HTTP/2. See Phase 1.

## Key design decisions

### 1. HTTPS: tunnel or intercept?

A proxy handles HTTPS through `CONNECT host:443`. After that the traffic is encrypted end to end.

| Mode | What we can log | Cost |
|---|---|---|
| **Blind tunnel** | Host, port, bytes transferred, timing | Simple. No certificates. |
| **TLS interception (MITM)** | Full URL, headers, bodies, status codes | Needs a local CA that the client trusts. Certificate pinning breaks. |

**Plan:** support both. Blind tunnel is the default and the fallback. Interception is enabled per host or
globally, and only after the user installs the mos root CA in a test browser profile or OS store. Interception is
for the user's own traffic and test devices only.

### 2. HTTP/2

HTTP/2 only ever appears through TLS (ALPN `h2`) in practice, so it matters only in intercept mode.
Two connections are involved, and each negotiates its own protocol:

- **Client to proxy:** the proxy presents a generated certificate and offers ALPN `h2` and `http/1.1`.
- **Proxy to origin:** the proxy negotiates with the real server, preferring `h2`.

The two sides do not have to match. The proxy can serve h2 to the client and use HTTP/1.1 upstream, or the
reverse. Because requests are translated to a protocol-neutral model in the middle, logging is the same in
both cases.

### 3. Do not hand-roll HTTP/2

HTTP/2 framing, HPACK and flow control are a large amount of work to get right. Use the platform:

- **Server side:** Kestrel (HTTP/1.1 + HTTP/2, ALPN, `SslStream`).
- **Client side:** `HttpClient` / `SocketsHttpHandler` with `HttpVersion.Version20` and
  `HttpVersionPolicy.RequestVersionOrLower`.

Open question for Phase 3: Kestrel does not natively understand `CONNECT` as a proxy command. The plan is
to accept the connection ourselves, answer `CONNECT`, then hand the decrypted-by-us stream to Kestrel (or
run `SslStream` ourselves and feed the resulting stream to an HTTP/2 server). A short spike decides this.
Alternatives: YARP as the forwarding engine, or a loopback Kestrel listener behind our `TcpListener`.

### 4. Logging

Define one protocol-neutral record, independent of HTTP version:

```
Timestamp, ClientEndpoint, Scheme, Host, Port, Method, Path+Query, HttpVersion (client side / origin side),
Status, RequestHeaders, ResponseHeaders, RequestBytes, ResponseBytes, DurationMs, Mode (plain/tunnel/intercept), Error
```

- Console output stays one concise line per exchange.
- A structured sink (JSON Lines file) is added next.
- Bodies are off by default, then optionally captured with a size cap, to avoid memory blow-ups and
  accidental storage of secrets.
- Nothing is redacted. The JSONL log records `Authorization`, `Cookie` and `Set-Cookie` values as they appear,
  the same as session capture (decision 5). Treat log files as sensitive.

### 5. Session capture and `.saz` output

Eventual goal: save a `.saz` file like Fiddler does, so sessions can be opened in Fiddler and similar tools.
A `.saz` is a zip archive. Each request/response exchange is stored as three files, numbered from 1:

| File | Contents |
|---|---|
| `1_c.txt` | Raw bytes sent by the client (request line, headers, body) |
| `1_s.txt` | Raw bytes sent by the server (status line, headers, body) |
| `1_m.xml` | Metadata about the exchange (timings, client/server endpoints, flags, and so on) |

(Fiddler puts these under a `raw/` folder inside the zip, alongside a `[Content_Types].xml`. Confirm the exact
layout and the `_m.xml` schema against a real `.saz` file before implementing the final writer.)

Two-step approach:

1. **Intermediate step: capture folder (Phase 1b).** The same three files per exchange are written directly into a
   sub-folder of the logging directory. Each run creates its own sub-folder, and files are numbered starting at 1
   and incrementing with every exchange (`1_c.txt`, `1_s.txt`, `1_m.xml`, `2_c.txt`, ...). This is easy to inspect
   and debug, and the file format is already the one the `.saz` needs.
2. **Final step: `.saz` writer (Phase 5).** Zip the capture folder's contents into a `.saz`, either on shutdown,
   on demand, or as a command that converts an existing run folder.

Design points to settle during Phase 1b:

- **Raw bytes versus reconstructed bytes.** The end goal is to capture exactly what is on the wire, not a
  reconstruction. Kestrel parses requests before our handler sees them, so while we build on Kestrel the files
  are a reconstruction: request/status line, headers, a blank line, then the body. Known differences from the
  wire, seen in the first live capture: Kestrel reports request headers in its own order (not the order the client
  sent them) with normalized casing; the origin's `Transfer-Encoding: chunked` header is kept but the body is stored
  with the chunk framing already removed; and the request-side `Host`/`Proxy-Connection` headers appear as Kestrel
  presents them.
  For HTTP/2 the headers are rendered in HTTP/1.1 text form (Fiddler does the same). This is a deliberate
  stop-gap: Phase 6 replaces or taps Kestrel to get byte-exact capture. Keep the capture code behind a small
  interface (for example `ISessionCapture`) so that the source of the bytes can change without touching the
  rest of the proxy.
- **Bodies.** Capture needs the full request and response bodies, streamed to disk while they are forwarded (not
  buffered in memory), with an optional size cap. Bodies are stored as received: still compressed if the origin
  compressed them, and with chunked framing removed (the reconstructed headers say so). Binary data (images and so on)
  is stored as-is, which is why these are files and not fields in the JSONL log.
- **Tunnels.** A blind HTTPS tunnel (Phase 2) has no readable content, so it produces only the `CONNECT` line and
  response in `_c.txt` / `_s.txt` plus a metadata file. Intercepted HTTPS (Phase 3) is stored decrypted.
- **Off by default.** Enabled with an option (for example `--capture <dir>`), because it writes everything to disk.
- **Not redacted.** Raw capture contains credentials, cookies and personal data, exactly like a Fiddler session.
  The capture folder, the JSONL log and any `.saz` must be treated as sensitive: never commit them (add the folder to
  `.gitignore`) and delete them when done.
- **Numbering.** Exchange numbers are assigned in the order requests arrive, so the number is stable even when
  responses finish out of order. Phase 1b writes inline through buffered file streams; a background queue is a
  later optimization if capture is found to slow forwarding.
- **Metadata.** `_m.xml` should carry at least the fields in the log record (timestamps, client endpoint,
  protocol versions, status, byte counts, mode, error), written in Fiddler's schema where practical.

## Testing approach

- **Long-term functionality is tested in the test project** (`test/mos.Tests`). When implementing any phase,
  add tests there as part of the work and run them with `dotnet test` from the repo root. A phase is not done
  until its tests are in the test project and passing.
- **Short-term spikes are exempt.** Throwaway experiments (e.g. the Phase 3 Kestrel-vs-`SslStream` spike)
  can be checked by hand or with a scratch script and do not need tests in the test project. If a spike's
  code is kept, add tests for it at that point.
- Manual checks with a browser (e.g. Chrome with `--proxy-server`) are a complement to tests, not a
  replacement for them.
- How to write tests (Given / When / Then comments, naming, Shouldly, etc.) is in
  [test/test_instructions.md](../test/test_instructions.md).

## Phases

### Phase 1: Solid HTTP/1.1 proxy

- Replace the hand-rolled parser with Kestrel (or keep the parser only if there is a reason to).
- Keep-alive support, chunked request and response bodies, streaming (no buffering of whole bodies).
- Correct hop-by-hop header handling. Policy: no `Via` or `X-Forwarded-For` headers are added. `Expect: 100-continue` is handled.
- Absolute-URI and origin-form handling, error responses (400/502/504), upstream timeouts.
- Introduce the log record and a logging abstraction; console + JSONL sinks.
- Configuration: port and log path, via CLI (`--port`, `--log`). The listener is always bound to loopback (`127.0.0.1`); there is no option to bind other interfaces.
- Tests: an in-process fake origin server plus proxy tests for GET, POST, chunked, large bodies, redirects.

**Done when:** Chrome browses `http://` sites reliably through the proxy and every exchange is logged.

### Phase 1b: Capture folder (Fiddler-style session files) *(done)*

- Add a capture sink, enabled by `--capture <dir>`. Each run creates a new sub-folder under `<dir>`.
- For every exchange, write `N_c.txt`, `N_s.txt` and `N_m.xml`, with `N` starting at 1 for each run and
  incrementing per request in arrival order.
- Stream request and response bodies to disk while forwarding (no full buffering), with an optional size cap.
- Settle the design points listed in decision 5 (reconstructed versus raw bytes, body handling).
- Tests: a request/response through the fake origin produces the three files with the expected request line,
  headers, body bytes and metadata; numbering restarts at 1 in a new run folder; binary bodies are byte-identical;
  capture failures never break forwarding.

**Done when:** browsing through the proxy with `--capture` produces a run folder whose files match what was sent
and received.

### Phase 2: HTTPS blind tunnel *(done)*

Outcome of the Kestrel check: Kestrel's `IHttpUpgradeFeature` refuses to upgrade a `CONNECT` request ("non-upgradable"),
so tunnels are handled by a connection middleware (`TunnelHandler`) that runs in front of Kestrel's HTTP parser.
It peeks at the first bytes of each connection, takes over those that begin with `CONNECT `, and hands everything
else to Kestrel unchanged. This is also the hook point for the Phase 6 wire tap (option A).
Kestrel's `ConnectionClosed` token fires as soon as the client half-closes, so tunnels are not tied to it; they end
when the origin closes, on error, after an idle timeout (`TunnelIdleTimeout`, default 5 minutes), or at proxy shutdown.
Connecting to the origin times out after `ConnectTimeout` (default 15 seconds, set with `--connect-timeout SECONDS`). TLS SNI is not logged (it would
require parsing the ClientHello); that is left for Phase 3, where TLS is terminated anyway.

- Implement `CONNECT`: connect to the target, reply `200 Connection Established`, then relay bytes both ways.
- Log host, port, bytes in/out, duration, and the TLS SNI if cheaply available.
- Half-close handling, idle timeouts, cancellation, backpressure.

**Done when:** HTTPS sites load through the proxy and appear in the log as tunnels.

### Phase 3: TLS interception with HTTP/1.1 *(done)*

Implemented in `CertificateAuthority` (root and per-host certificates) and `TunnelHandler` (interception path).
Enabled with `--intercept`; `--intercept-bypass a.com,*.b.com` keeps hosts as blind tunnels; `--ca-dir` chooses where
the root is stored (default `%LOCALAPPDATA%\mos\ca`, outside the repository).

- The Kestrel-vs-`SslStream` question is settled: after `CONNECT` the connection middleware answers 200, does the TLS
  handshake itself with `SslStream` (ALPN offers only `http/1.1` for now), replaces the connection's transport with the
  decrypted stream, and calls `next`, so Kestrel parses the decrypted HTTP/1.1 and `ProxyHandler` forwards it. The
  CONNECT target host and port are carried in the connection's `Items`, and the upstream request goes to
  `https://<connect host>:<port><path>`. The same hook makes HTTP/2 (Phase 4) a matter of offering `h2` in ALPN.
- The CONNECT itself is logged and captured as its own exchange as soon as the 200 is sent; each decrypted request is
  a separate exchange (so capture numbering is CONNECT = 1, first request = 2, and so on).
- The origin's certificate is validated normally; an untrusted origin certificate gives the client a 502.
- A client that does not trust the root is detected as a read failure on the decrypted stream right after the
  handshake (TLS 1.3 lets the server finish its side first) and logged as a TLS failure with a hint.
- Making Chrome trust the proxy: startup prints (1) a command for a throwaway Chrome profile started with
  `--ignore-certificate-errors` (use that window only for browsing through the proxy), and (2) the `certutil`
  command for installing the root for the current Windows user, which gives real trust in every browser.
- Lessons learned while getting Chrome to work (so they are not repeated):
  - Chrome's `--ignore-certificate-errors-spki-list` only matches certificates the server actually sends in the
    handshake, and a self-signed root is never sent, so passing the root's hash does nothing. (With the host
    certificate's own hash it works, but that changes per host.) It is not used.
  - Sending a root or intermediate on Windows means giving .NET's `SslStreamCertificateContext` extra
    certificates, and it installs them into the user's `CurrentUser\CA` certificate store as a side effect. Stale
    copies with the same subject name then break later chain building (`NotSignatureValid`). A two-level CA built
    on this was tried and dropped. A test checks that the proxy leaves no certificates in the user's stores.
  - The headless Chrome net-log (`--log-net-log=file --net-log-capture-mode=Everything`) shows the exact
    certificate verification result and is the quickest way to diagnose a certificate error.
- Not done yet: automatic fallback to a blind tunnel for hosts whose handshake fails (certificate pinning).
  Use `--intercept-bypass` for those hosts for now.

Original scope for reference:

- Generate and persist a root CA (key stored with restrictive file permissions). Export the cert for
  install into a test browser profile.
- Mint per-host leaf certificates on demand (SAN = host), cache them, and respect SNI.
- After `CONNECT`, terminate TLS with the leaf certificate, parse the decrypted HTTP/1.1, forward over a
  new upstream TLS connection (validating the origin's real certificate), and log full exchanges.
- Interception allow/deny list (e.g. never intercept banking sites; fall back to tunnel).
- Resolve the Kestrel-vs-`SslStream` question from decision 3.

**Done when:** `https://` pages load in a profile that trusts the mos CA, with full URLs and headers logged.

### Phase 4: HTTP/2

- Offer ALPN `h2` and `http/1.1` to the client; serve HTTP/2 on the intercepted connection.
- Upstream: prefer `h2` where the origin supports it, fall back to HTTP/1.1.
- Map HTTP/2 pseudo-headers (`:method`, `:scheme`, `:authority`, `:path`) to the neutral record.
- Handle stream multiplexing, header-size limits, trailers, and `GOAWAY` / reset propagation.
- Log the negotiated protocol for each side.

**Done when:** Chrome negotiates h2 with the proxy, a site that supports h2 is fetched over h2 upstream, and each stream is logged as its own entry.

### Phase 5: Hardening and extras

- WebSocket upgrades (HTTP/1.1 `Upgrade`, and HTTP/2 extended `CONNECT`) pass through and log the handshake.
- Optional proxy authentication, and access control by client IP.
- Request and connection limits, graceful shutdown, log rotation.
- **`.saz` export:** package a capture run folder (Phase 1b) into a Fiddler-compatible `.saz` file, and verify it
  opens in Fiddler. Include a command to convert an existing run folder.
- Optional inspection UI or log viewer; HAR export.
- Packaging as a Windows service or a single-file executable.

### Phase 6: Byte-exact capture (replace or bypass Kestrel)

Goal: `N_c.txt` and `N_s.txt` contain exactly the bytes that crossed the wire, so the `.saz` matches what Fiddler
would record. Everything up to Phase 5 runs on Kestrel; this phase removes that limitation. Three options,
to be chosen by a spike at the start of the phase:

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. Wire tap under Kestrel** | Keep Kestrel for parsing and forwarding, but copy the raw bytes before they are parsed. On the client side use a Kestrel connection middleware (`ListenOptions.Use(...)`), which sees the raw duplex stream; placed after the TLS middleware it sees decrypted bytes. On the origin side use `SocketsHttpHandler.PlaintextStreamFilter` (or `ConnectCallback`), which exposes the plaintext stream to the origin. | Smallest change; keeps Kestrel's HTTP/2, limits and security fixes. | Raw bytes arrive per connection, not per request, so they must be split into exchanges (keep-alive, pipelining, HTTP/2 multiplexing). The proxy still re-serializes headers, so the client-side and origin-side captures differ; that matches Fiddler (`_c` is what the client sent, `_s` is what the origin sent). |
| **B. Own HTTP/1.1 front end** | Replace Kestrel with our own listener and parser that keeps the original bytes of each message. | Full control; byte-exact by construction; no framework behavior to work around. | We must write and maintain parsing, chunked, keep-alive and error handling, and security hardening. HTTP/2 would still need separate work (Phase 4's `SslStream` + framing code, or a library). |
| **C. Fork Kestrel** | Fork the Kestrel sources from dotnet/aspnetcore and add capture hooks inside the parser. | Full HTTP/1.1 and HTTP/2 behavior with byte-level access. | Kestrel is a very large codebase with deep internal dependencies; the fork has to be kept in sync with upstream security fixes, and may not build outside the aspnetcore repo. Likely the highest long-term cost. |

Initial leaning: try **A** first, because it is the cheapest and its viability can be tested in a day. Fall back to
**B** for HTTP/1.1 if the tap cannot deliver clean per-exchange bytes. **C** is the last resort.

Notes for the spike:

- Check how connection middleware and `PlaintextStreamFilter` behave with HTTP/2. Raw HTTP/2 is binary frames
  (HPACK-compressed headers), so a byte-exact HTTP/2 capture is not readable text. Decide whether `_c.txt` /
  `_s.txt` hold the raw frames for HTTP/2 or the HTTP/1.1-style rendering that Fiddler writes, and whether to store
  both.
- Make sure capture stays off the hot path (bounded queue, drop or back-pressure policy defined).
- Confirm the final files open correctly in Fiddler.

**Done when:** for HTTP/1.1 traffic (plain and intercepted HTTPS), the bytes in `N_c.txt` and `N_s.txt` are identical
to a packet-level capture of the same exchange, verified by tests against the fake origin and by comparison with
Fiddler output for a sample session.

## Risks and notes

- **Kestrel does not give byte-exact capture.** Until Phase 6, the capture files are reconstructions and may differ
  from the wire (header spacing and casing, folded headers, chunk framing, malformed requests that Kestrel rejects
  before our code runs). Anything that needs to see malformed or unusual traffic exactly will need Phase 6.
- **Certificate pinning** (many mobile apps, some desktop apps) fails under interception. This is expected;
  fall back to tunnel mode for those hosts.
- **Security of the CA key:** anyone with the root CA private key can impersonate any site to a client that trusts it.
  Keep it local, never commit it (add to `.gitignore`), and make the install step explicit and removable.
- **Binding:** loopback only, by design, with no option to listen on other interfaces. An open proxy on `0.0.0.0` can be abused.
- **Privacy and legality:** log only traffic you own or have permission to inspect. Nothing is redacted: the JSONL log,
  raw session capture and `.saz` files all contain credentials and cookies, so keep them out of version control.
- **Chrome quirks:** HSTS and HTTPS-upgrade mean most real-world sites need Phase 2 or 3 before they are usable.
  `http://httpforever.com/` and `http://neverssl.com/` work for Phase 1 testing.
- **Version drift:** the project targets `net10.0`. Check current `SocketsHttpHandler` and Kestrel HTTP/2 options
  when starting Phases 3 and 4.

## Suggested order of work

1. Phase 1 (with tests), commit. *(done)*
2. Phase 1b: capture folder, commit.
3. Phase 2, commit.
4. Phase 3 spike on the Kestrel / `SslStream` question, then the implementation. Keep the Phase 6 goal in mind
   (byte-exact capture) when choosing how the decrypted stream is handed to the HTTP layer.
5. Phase 4.
6. Phase 5 items as needed, including the `.saz` writer.
7. Phase 6: byte-exact capture. Starts with the A / B / C spike.
