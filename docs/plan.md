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
- Redact `Authorization`, `Cookie` and `Set-Cookie` values by default.

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

### Phase 2: HTTPS blind tunnel

- Implement `CONNECT`: connect to the target, reply `200 Connection Established`, then relay bytes both ways.
- Log host, port, bytes in/out, duration, and the TLS SNI if cheaply available.
- Half-close handling, idle timeouts, cancellation, backpressure.

**Done when:** HTTPS sites load through the proxy and appear in the log as tunnels.

### Phase 3: TLS interception with HTTP/1.1

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
- Optional inspection UI or log viewer; HAR export.
- Packaging as a Windows service or a single-file executable.

## Risks and notes

- **Certificate pinning** (many mobile apps, some desktop apps) fails under interception. This is expected;
  fall back to tunnel mode for those hosts.
- **Security of the CA key:** anyone with the root CA private key can impersonate any site to a client that trusts it.
  Keep it local, never commit it (add to `.gitignore`), and make the install step explicit and removable.
- **Binding:** loopback only, by design, with no option to listen on other interfaces. An open proxy on `0.0.0.0` can be abused.
- **Privacy and legality:** log only traffic you own or have permission to inspect. Redact credentials by default.
- **Chrome quirks:** HSTS and HTTPS-upgrade mean most real-world sites need Phase 2 or 3 before they are usable.
  `http://httpforever.com/` and `http://neverssl.com/` work for Phase 1 testing.
- **Version drift:** the project targets `net10.0`. Check current `SocketsHttpHandler` and Kestrel HTTP/2 options
  when starting Phases 3 and 4.

## Suggested order of work

1. Phase 1 (with tests), commit.
2. Phase 2, commit.
3. Phase 3 spike on the Kestrel / `SslStream` question, then the implementation.
4. Phase 4.
5. Phase 5 items as needed.
