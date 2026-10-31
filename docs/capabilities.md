# MoSaProxy: capabilities

What MoSaProxy does today. For what is still to do, see [plan.md](plan.md).

## Big picture

MoSaProxy is a forward proxy that logs the traffic passing through it. A browser or other client is pointed at it with a normal
proxy setting (for example Chrome `--proxy-server`), and every exchange is written to a console line, an optional JSON Lines log
and a temporary folder of Fiddler-style session files, which `--saz FILE` packs into a `.saz` that Fiddler opens.
It listens on loopback (`127.0.0.1`) only, by design: there is no option to bind other interfaces, so it needs no authentication
or access control.

The code is a `core/` library (`MoSaProxy.Core`) that holds everything not tied to a user interface, and a console program in
`app/` that only presents it. [class-classification.md](class-classification.md) maps every type to Core or UI. The proxy runs on
Kestrel, with a connection middleware in front of it for `CONNECT` and TLS.

## Protocols

- **HTTP/1.1 and HTTP/2**, on both sides of the proxy. The two sides negotiate independently (an h2 client with an HTTP/1.1
  origin, or the reverse); logging is the same either way because requests go through a protocol-neutral record.
  HTTP/2 appears only on intercepted HTTPS (ALPN `h2`); `--no-http2` offers only `http/1.1`.
- Keep-alive, chunked bodies and streaming (bodies are never buffered whole), correct hop-by-hop header handling,
  `Expect: 100-continue`, absolute-URI and origin-form requests, 400/502/504 error responses and upstream timeouts.
  No `Via` or `X-Forwarded-For` header is added.
- **WebSockets**, over HTTP/1.1 upgrades and HTTP/2 extended `CONNECT`, for `wss://` (intercepted), `ws://` inside a tunnel
  and absolute-form upgrades. The proxy opens the origin leg first (so the origin's subprotocol is known), then accepts the
  client and copies messages both ways with the close code and reason. Each leg has its own ping/pong; compression is not
  negotiated through.
- **gRPC-style trailers** are passed on over HTTP/2.

## HTTPS

| Mode | What is logged | Notes |
|---|---|---|
| **Blind tunnel** (default) | Host, port, bytes each way, duration | `CONNECT` is answered with `200 Connection Established` and bytes are relayed. No certificates. Idle timeout 5 minutes. |
| **TLS interception** (`--intercept`) | Full URL, headers, bodies, status | A local root CA mints a certificate per host. The origin's real certificate is validated; an untrusted one gives the client a 502. |

- `--intercept-bypass a.com,*.b.com` keeps the listed hosts as blind tunnels (for example hosts that pin certificates).
- With interception on, the first bytes of a `CONNECT` decide what it carries: a TLS handshake is decrypted, plain HTTP
  (such as `ws://` on port 80) goes to the HTTP layer, and anything else, or a silent client (3 s), is relayed blindly.
- **The root CA** is created on first use in `--ca-dir` (default `%LOCALAPPDATA%\mos\ca`, outside the repository). At startup
  the proxy checks whether the root is in the current Windows user's trusted roots. If it is, it prints plain Chrome commands.
  If not, and the console is interactive, it asks `[y/N]` before installing the public certificate (Windows shows its own
  confirmation) and offers to replace a root from an earlier CA folder. If the user declines, it prints commands for a throwaway
  Chrome profile with `--ignore-certificate-errors`. `--untrust-ca` removes every MoSaProxy root from the user's trusted roots
  and exits.
- A client that does not trust the root is detected right after the handshake and logged as a TLS failure with a hint.
- `--open [URL]` starts Chrome already pointed at the proxy (with the certificate-errors switch only when the root is not trusted).
- The certificate subject and file names still say "mos" (see [Naming](#naming)).

## Logging

- **Console:** one concise line per exchange.
- **JSON Lines** (`--log FILE`): one protocol-neutral record per exchange: timestamp, client endpoint, scheme, host, port, method,
  path and query, HTTP version on each side, status, headers, byte counts, duration, mode (plain, tunnel, intercept, websocket,
  websocket-closed, rejected) and error. A WebSocket gets a record when the handshake succeeds and another when it closes (with
  message counts and byte totals). Requests the proxy rejects, or that the client abandons, are logged as `rejected`.
- **Rotation:** `--log-max-mb N` (default 50, 0 = never) and `--log-keep N` (default 5): `traffic.jsonl` becomes
  `traffic.1.jsonl`, older files shift up and the oldest beyond the keep count is deleted. A failed rotation is reported once and
  logging carries on in the current file.
- **Nothing is redacted.** `Authorization`, `Cookie` and `Set-Cookie` values are logged as they appear. Treat logs as sensitive.

## Session capture

Capture is always on. Each run writes, for every exchange, `N_c.txt` (the request), `N_s.txt` (the response) and `N_m.xml`
(metadata), numbered from 1 in the order requests arrive. WebSockets also get `N_w.txt` (readable text, binary as hex). Capture is
not redacted. `--capture-max-body BYTES` keeps the whole head and cuts the body.

- **Temporary folder.** The files go in a folder of the run's own, `%TEMP%\MoSaProxyCaptures\<date-time>` (`CaptureRun`), and the
  folder is deleted when the program exits: a normal stop, a stop on a limit, or an unhandled error. There is no option to keep it;
  `--saz FILE` is how a session is kept. If the folder cannot be deleted (a viewer holds a file open) the program says so and
  exits anyway.
- **Crashed runs.** A killed process cannot delete its folder, so every start deletes the folders of runs that are gone. Every
  folder directly in `MoSaProxyCaptures` is a run folder. A running proxy holds a `.lock` file in its folder open exclusively, and
  a folder is deleted only if that file can be opened, so a second MoSaProxy never deletes the first one's folder. The check and
  the creation of a new folder happen under one machine-wide lock.
- **Byte-exact for HTTP/1.1**, plain and intercepted: a wire tap under Kestrel (`core/WireTap/`) copies the bytes the client sent
  and the bytes the origin sent, including odd casing, header order and chunk framing. Malformed or abandoned requests that
  Kestrel rejects are captured too.
- **HTTP/2** is decoded from the frames (HPACK, `core/Http2/`) and written as HTTP/1.1-style text (`GET /path HTTP/2`), headers in the
  order sent. The raw frames are not saved.
- **Source labels:** each exchange's metadata says where its two files came from: `wire`, `http2-decoded`, `parsed` (rebuilt from
  headers Kestrel or `HttpClient` had already parsed) or `generated` (the proxy's own error pages and `CONNECT` reply).
- **Tunnels** produce the `CONNECT` line and the proxy's reply. With interception, the `CONNECT` is exchange 1 and each decrypted
  request is its own exchange.
- A capture failure never breaks forwarding.

## `.saz` export

`SazExporter` packs the run's capture folder into a Fiddler `.saz`. `--saz FILE` writes it when the proxy stops, after the drain
so that it is complete and before the temporary folder is deleted. There is no other way to export: the folder does not outlive the run.
Fiddler opens the result. What is written, how it differs from a file Fiddler wrote, and what Fiddler's loader accepts are in
[saz-export.md](saz-export.md); the format itself is in [specs/saz-format.md](specs/saz-format.md).

## Running and stopping

- `dotnet run --project app -- [options]`; the default port is 8080 (`--port N`). `--connect-timeout SECONDS` (default 15).
- **Graceful shutdown:** the first Ctrl+C stops accepting connections and drains for `--drain-timeout SECONDS` (default 10,
  0 = stop at once): running requests finish, tunnels end after 1 s of quiet, WebSockets are sent close code 1001, idle
  connections close. Whatever is left at the deadline is aborted (still logged and captured) and counted in the final message.
  A second Ctrl+C skips the wait.
- **Stops when the system runs out of something:** a full disk, too many open files, out of memory or out of sockets. The proxy
  prints `Limit reached: <limit> (while <what was being done>)`, stops at once with no drain, still tries to write the `.saz`, and
  exits with code 2. Other errors (a locked file, an unreachable origin) are not limits and behave as usual. There are no caps on
  log size, capture size, connections or tunnels beyond the explicit options above.

## Save formats

| Format | State |
|---|---|
| `.saz` (Fiddler) | Done (`SazExporter`) |
| mitmproxy flow file | Not started |
| `.har` | Not started |

## Naming

The project is **MoSaProxy** (formerly "mos"): solution `MoSaProxy.slnx`, program `app/MoSaProxy.csproj`, tests
`test/MoSaProxy.Core.Tests` and `test/MoSaProxy.App.Tests`, namespace `MoSaProxy`, and the `x-mosaproxy-*` flags in exported `.saz`
files. Three identifiers deliberately still say "mos", so that a root certificate already installed as trusted keeps being
recognized: the certificate subject (`CN=mos Proxy Root CA (local test CA), O=mos`), its file names (`mos-root-ca.pfx` and `.cer`), and its
default folder (`%LOCALAPPDATA%\mos\ca`). Renaming them needs a migration (see the note in `CertificateAuthority.cs`).

## Design notes

- **Do not hand-roll HTTP/2.** The server side is Kestrel; the client side is `HttpClient` / `SocketsHttpHandler` with
  `RequestVersionOrLower`. Kestrel will not upgrade a `CONNECT`, so `TunnelHandler` is a connection middleware in front of its
  parser; for interception it does the TLS handshake with `SslStream` and hands the decrypted stream on.
- **Lessons from getting Chrome to trust the proxy:** Chrome's `--ignore-certificate-errors-spki-list` never matches a self-signed
  root, because the root is never sent. Giving .NET extra certificates to send installs them into the user's `CurrentUser\CA` store
  as a side effect, and stale copies break later chain building, so the CA is a single root and a test checks that the proxy leaves
  no certificates in the user's stores. The headless Chrome net-log (`--log-net-log=file --net-log-capture-mode=Everything`) is the
  quickest way to diagnose a certificate error.
- **Security:** anyone with the root CA's private key can impersonate any site to a client that trusts it. Keep it local, never
  commit it, and use `--untrust-ca` to remove it. Interception is for your own traffic and test devices only. The log, capture
  folder and `.saz` all contain credentials and cookies: keep them out of version control and delete them when done.
