# MoSaProxy: Traffic-Logging Proxy Plan

## Goal

A forward proxy that logs traffic for both HTTP and HTTPS, and speaks both HTTP/1.1 and HTTP/2.
Browsers and other clients are pointed at it with a normal proxy setting
(e.g. Chrome `--proxy-server`).

What is built is described in [capabilities.md](capabilities.md); this plan lists only what is left.
See also [current-landscape/landscape.md](current-landscape/landscape.md): the other proxy servers, the features they offer, and the save formats wanted.

## Testing approach

- **Long-term functionality is tested in the test project** (`test/MoSaProxy.Core.Tests`). When implementing any item,
  add tests there as part of the work and run them with `dotnet test` from the repo root. An item is not done
  until its tests are in the test project and passing.
- **Short-term spikes are exempt.** Throwaway experiments can be checked by hand or with a scratch script and do not
  need tests in the test project. If a spike's code is kept, add tests for it at that point.
- Manual checks with a browser (e.g. Chrome with `--proxy-server`) are a complement to tests, not a
  replacement for them.
- How to write tests (Given / When / Then comments, naming, Shouldly, etc.) is in
  [test/test_instructions.md](../test/test_instructions.md).

## Remaining work

### Save formats

Core should produce every save format so that every front end gets them. `.saz` is done; these are not started, and their
timing is open:

- **mitmproxy flow file.**
- **`.har`.**
- One exporter shape that `.saz`, mitmproxy flow and `.har` plug into.

### `.saz` fidelity

The remaining differences between `SazExporter` output and a file Fiddler wrote are tracked in [saz-export.md](saz-export.md)
(CONNECT report text, Fiddler's own `_w.txt` frame format, `BitFlags` for a plain-HTTP tunnel, HTTP/2 reuse marks).
None stops Fiddler from loading the file.

### Core cleanup

Core should hold everything that is not tied to a user interface, and each front end should only present. Not yet done:

- Replace Core's direct console diagnostics with events.
- A Core service for the orchestration every front end would otherwise repeat.
- Anything still marked Mix in [class-classification.md](class-classification.md) should be split into Core and UI parts.

### Temporary capture folder: follow-ups

The temporary capture folder is built (see [capabilities.md](capabilities.md)). What is left:

- Decide whether to offer a way to turn capture off for pure forwarding. Every body is written to disk on every run, and credentials
  and cookies reach the temp folder even when no export is asked for. `--capture-max-body` limits the size, not the exposure.
- Run the real program with a real console interrupt, to see the `--saz` archive written before the folder is deleted. The two
  parts are tested separately, and a killed process was checked by hand (the next start deleted its folder), but the graceful path
  through `Program.cs` has not been seen end to end.

### Browser UI

A browser front end over the same Core as the console program. The browser sends its ordinary traffic through the proxy
(port 8080) and loads the UI directly from a second loopback port (for example 9090). Chrome skips the proxy for `localhost`,
`127.0.0.1` and `[::1]` by default, so the UI's own requests are not logged as traffic; for any other host name, use
`--proxy-bypass-list`. The proxy listener must never serve the UI, because it treats every request as one to forward.

The API and the UI are separate pieces of work joined by one contract: the JSON shape of an exchange and the endpoints below.
Build in this order:

1. **Core exposes exchanges and a live stream.** This is the "Core cleanup" work above, narrowed to what the API needs:
   - An exchange store: a stable id per exchange (the capture number `N`), summaries for listing, the full record for one
     exchange, and the request and response bytes. The store is the temporary capture folder (`CaptureRun`), so it is always
     there and the UI always works.
   - An event for "exchange started" and "exchange finished" (so the UI can show a request that is still running), replacing
     direct console output.
   - Tests in `test/MoSaProxy.Core.Tests` through the Core types only, with no web server.
2. **A local API.** A second Kestrel listener (same process, so live data is direct) that exposes the store. The endpoints are
   thin: each calls Core and returns JSON. Starting set:

   | Endpoint | Purpose |
   |---|---|
   | `GET /api/status` | Proxy port, interception on or off, HTTP/2 on or off, capture and log paths, counts, whether the root CA is trusted, version. |
   | `GET /api/exchanges` | Page of summaries (id, time, method, scheme, host, path, status, protocol each side, mode, bytes, duration). Query: `after` (id, for paging and catching up), `limit`, `host`, `method`, `status`, `mode`, `q` (text in the URL). |
   | `GET /api/exchanges/{id}` | The full record of one exchange: the log fields, headers each way, the capture source labels, timings, error. |
   | `GET /api/exchanges/{id}/request` | The request exactly as captured (`N_c.txt`), as bytes. |
   | `GET /api/exchanges/{id}/response` | The response exactly as captured (`N_s.txt`), as bytes. |
   | `GET /api/exchanges/{id}/request/body`, `.../response/body` | Just the body. `?decode=true` undoes `Content-Encoding` and chunking so the UI can show text, images and JSON. |
   | `GET /api/exchanges/{id}/websocket` | The messages of a WebSocket exchange (direction, time, type, size, text or hex). |
   | `GET /api/stream` | Server-Sent Events: one event when an exchange starts, one when it finishes, and one per WebSocket message. Supports `Last-Event-ID` so a reconnecting page misses nothing. |
   | `GET /api/export/saz` | Downloads a `.saz` of all exchanges, or of those named in `?ids=`. |
   | `POST /api/stop` | Starts the graceful shutdown (the drain period), like the first Ctrl+C. |

   - **OpenAPI and endpoint documentation.** The API describes itself in OpenAPI, and the endpoint documentation is produced
     from that description, not written by hand, so the two cannot drift apart.
     - Use ASP.NET Core's built-in OpenAPI support (`Microsoft.AspNetCore.OpenApi`, available for `net10.0`) on minimal-API
       endpoints. Give each endpoint a name, a summary, typed request and response models, and its error responses, so the
       description is useful and not just a list of paths.
     - The exchange summary and the full record are named types shared by the endpoints, the stream events and the UI, so they
       appear once as OpenAPI schemas. Endpoints that return bytes (`/request`, `/response`, the bodies, the `.saz`) and the
       `/api/stream` event stream do not infer well and need their content types and event shape declared by hand.
     - The running app serves the description at `/openapi/v1.json` (behind the same safety checks as the rest of the API).
     - The checked-in documentation is `docs/api/openapi.json`, generated at build time (the
       `Microsoft.Extensions.ApiDescription.Server` build task) or by a small command. A readable reference, `docs/api/endpoints.md`
       (endpoint, parameters, response, errors, an example), is generated from the same file. A test fails when the checked-in
       `openapi.json` no longer matches what the code produces, so changing an endpoint means regenerating the documents.
     - Optional later: serve an interactive page (Scalar or Swagger UI) from the same listener, and generate the UI's TypeScript
       types from `openapi.json` if the UI moves to a framework.
     - Tests: the description is valid OpenAPI, every mapped endpoint appears in it with a summary, and each response type
       matches what the endpoint really returns (spot-checked through the real listener).

   Left for later: changing settings while running (the options are fixed at start-up today), clearing the capture, replaying a
   request, and the HAR and mitmproxy exports (add `/api/export/har` and `/api/export/flow` when those formats exist).
   - **Safety.** The listener is loopback-only. The UI shows credentials and cookies, and a web page on another site could
     try to call a local API, so every request must be checked: reject a wrong `Host` header (DNS rebinding), reject any
     `Origin` other than the UI's own, and require a per-run token that the console prints in the UI's address.
   - Tests: request each endpoint through the real listener with the fake origin, including paging, filters, the stream
     (events arrive, and `Last-Event-ID` resumes) and each rejection above.
3. **A minimal UI.** Static HTML and JavaScript served by the same listener, with no build step: a table of exchanges fed by
   `/api/stream`, and a detail pane showing the request, response, headers and body of the selected row. During development it can
   run against canned JSON files before the endpoints exist. Checked by hand in Chrome; add a test that the listener serves the
   page and nothing outside the UI folder.

Once these work, grow both together (filters, search, export buttons, settings). Add the new types to
[class-classification.md](class-classification.md) (the API as UI, the store as Core).

### HTTPS interception

- Automatic fallback to a blind tunnel for hosts whose handshake fails (certificate pinning). For now, use `--intercept-bypass`.
- Rename the root CA's subject, file names and folder from "mos" to MoSaProxy. This needs a migration so a root already trusted
  on a machine keeps being recognized (see the note in `CertificateAuthority.cs`).

### HTTP/2

- Map stream resets and `GOAWAY` between the two sides explicitly (each side's own HTTP/2 stack handles its own connection today).
- Capture: write trailers, and support `PUSH_PROMISE` (a push makes that direction give up and fall back to the `parsed` files).
- Of several identical concurrent requests (same method, `:authority` and `:path`) the captured responses can be swapped between them.

### Byte-exact capture (Phase 6 leftovers)

- A client that sends its request and then stops sending is treated by Kestrel as having gone away, so it is logged as
  "client disconnected" and not answered. Fixing that needs our own HTTP/1.1 front end instead of Kestrel (the spike's option B).
- The wire bytes of an exchange are held in memory until it ends. Keep capture off the hot path (a bounded queue with a defined
  drop or back-pressure policy) if capture is found to slow forwarding.
- Compare against a packet capture (needs a capture driver; the tests compare against the bytes a raw client and origin wrote).
- Confirm the stop-on-a-limit path end to end: a real full disk could not be reproduced, so the stop and exit code 2 in `Program.cs` have not been seen on one.

### Packaging

- Run as a Windows service, or ship as a single-file executable.

### Not wanted

A viewer UI beyond the browser UI above (such as a desktop viewer). Proxy authentication and access control by client IP (the listener accepts loopback connections only).

## Risks and notes

- **Certificate pinning** (many mobile apps, some desktop apps) fails under interception. This is expected;
  fall back to tunnel mode for those hosts.
- **Security of the CA key:** anyone with the root CA private key can impersonate any site to a client that trusts it.
  Keep it local, never commit it, and make the install step explicit and removable.
- **Binding:** loopback only, by design, with no option to listen on other interfaces. An open proxy on `0.0.0.0` can be abused.
- **Privacy and legality:** log only traffic you own or have permission to inspect. Nothing is redacted: the JSONL log,
  raw session capture and `.saz` files all contain credentials and cookies, so keep them out of version control.
- **Chrome quirks:** HSTS and HTTPS-upgrade mean most real-world sites need a tunnel or interception to be usable.
  `http://httpforever.com/` and `http://neverssl.com/` work for plain-HTTP testing.
- **Version drift:** the project targets `net10.0`. Check current `SocketsHttpHandler` and Kestrel HTTP/2 options when upgrading.
- **Reliance on undocumented behaviour:** matching an HTTP/1.1 origin connection's bytes to an exchange relies on
  `SocketsHttpHandler` writing the request on the caller's own async flow (an `AsyncLocal`). Keep a test on it.
