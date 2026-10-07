# Existing proxy servers and what MoSaProxy should offer

Background research for the project. The build plan is in [plan.md](plan.md).

## Existing proxy servers

Ranked, best first:

1. mitmproxy
2. Fiddler Everywhere
3. Charles Proxy
4. HTTP Toolkit
5. Proxyman
6. Burp Suite

## Features the other proxy servers implement

- HTTP/1
- HTTP/2
- HTTP/3
- HTTPS man-in-the-middle (decrypting HTTPS)
- WebSockets
- gRPC
- TLS 1.3
- Record and save
- Scriptable (mitmproxy is the best of them)

## Ways to use mitmproxy

mitmproxy ships three front ends over the same engine:

| Tool | What it is |
|---|---|
| `mitmproxy` | Terminal UI |
| `mitmweb` | Browser-based GUI |
| `mitmdump` | Headless, for automation |

## Desired MoSaProxy features

### Save as

- HAR
- SAZ (Fiddler)
- mitm (mitmproxy's own saved-flow format)

## Where MoSaProxy stands today

This part is not from the research above; it records the state of the project against the lists, so the gaps are visible. Details are in [plan.md](plan.md).

| Feature | In MoSaProxy today |
|---|---|
| HTTP/1 | Done |
| HTTP/2 | Done, on both sides (browser to proxy, and proxy to website), with fallback to HTTP/1.1 |
| HTTP/3 | Not planned |
| HTTPS man-in-the-middle | Done (`--intercept`, with a local root certificate and an offer to trust it at startup) |
| WebSockets | Done, including WebSockets over HTTP/2 |
| gRPC | Not tested. The pieces it needs, HTTP/2 and passing response trailers through, exist, but it has not been tried with a real gRPC call |
| TLS 1.3 | Used where the browser and the operating system negotiate it; there is no setting for it |
| Record and save | Done: a JSON Lines log, per-exchange capture files, and `.saz` export |
| Scriptable | Not started |

| Way of using it | In MoSaProxy today |
|---|---|
| Terminal UI | No |
| Browser-based GUI | No (a viewer was ruled out for now; several front ends sharing one Core are the long-term goal, see [class-classification.md](class-classification.md)) |
| Headless | Yes: MoSaProxy is a command-line program, like `mitmdump` |

| Save as | In MoSaProxy today |
|---|---|
| SAZ | Done, but not yet confirmed to open in Fiddler |
| HAR | Not started. Wanted as a Core capability (every front end gets it); it was only left out of Phase 5, so the timing is open |
| mitm (mitmproxy) | Not started. Also wanted as a Core capability |

## Gaps found in the release histories of mitmproxy and Fiddler Everywhere

Source: the two release-history inventories in this folder ([mitmproxy](mitmproxy_Complete_Release_History_Inventory.md) and
[Fiddler Everywhere](Fiddler_Everywhere_Complete_Release_History_Inventory.md)), read in full: their feature lists, and their lists of
fixed bugs and security issues. This section is MoSaProxy measured against them (written October 2026). It is a list of candidates, not a
plan; nothing here is decided.

### High-impact features MoSaProxy does not have

| # | Feature | Who has it | Why it matters here |
|---|---|---|---|
| 1 | Scripting / addon API | mitmproxy (its core idea); Fiddler added it in 8.2 | Already listed above as not started. Core could expose events (request, response, connect, error) so every front end gets scripting. |
| 2 | Modify traffic: rules, map local / map remote, header and body rewrite, block | both | The other half of what these tools are used for. MoSaProxy only observes. |
| 3 | Breakpoints / intercept (pause, edit, resume) | both | Needs a front end, but the pause point belongs in Core. |
| 4 | Replay (resend captured requests; serve captured responses) | both | `.saz` can be written but not read back or replayed. |
| 5 | Upstream proxy chaining | both | MoSaProxy sets `UseProxy = false`, so it cannot be used behind a corporate proxy. Both bug lists are full of PAC, WPAD and 407 problems, so it is easy to get wrong. |
| 6 | Capture without proxy settings: SOCKS5, local or network capture, transparent mode | both | Many applications ignore proxy settings. SOCKS5 would be the cheap first step. |
| 7 | Per-process attribution ("which application sent this") | Fiddler | The client is always local, so Windows can map the client port to a process id. |
| 8 | Detailed timings (connect, TLS, first byte) | Fiddler (Timings view); HAR needs them | The wire tap can measure them. The planned HAR export and the `.saz` timers would use them. |
| 9 | Import / load (HAR, `.saz`, native flow files) | both | A prerequisite for replay and for testing the exporters by round trip. |
| 10 | cURL / HTTPie export | mitmproxy | Cheap. mitmproxy had command-injection bugs here; sanitise the output. |
| 11 | Raw TCP flows | mitmproxy | Non-HTTP tunnels are relayed blindly, but only byte counts are recorded, not the bytes. |
| 12 | MCP server (traffic exposed to AI tools) | Fiddler 8.x | Fits the Core-plus-several-front-ends design. Fiddler pairs it with traffic sanitising and API keys; MoSaProxy keeps credentials in its logs on purpose, so it would need the same control. |
| 13 | Content decoding (gzip, Brotli, zstd, protobuf, gRPC) | both | Captures keep the encoded bytes. Any viewer, HAR body or cURL export needs decoding. |

Judged not worth pursuing for now: HTTP/3 and QUIC (a browser uses a proxy's `CONNECT`, not QUIC), DNS, UDP and DTLS, WireGuard, reverse
proxy mode, proxy authentication (dropped earlier), and mobile-device interception (it needs a listener that is not loopback, which is
ruled out).

### Lessons from their bug lists (worth testing)

- **Streaming.** Fiddler had many Server-Sent Events and LLM-streaming defects. Test that events arrive incrementally, not at the end.
- **gRPC.** Trailers are passed through, but no real gRPC call has been tried.
- **Hostnames.** mitmproxy hit bugs with IPv6 literals in `CONNECT`, internationalised and punycode hostnames, uppercase `HTTP://`,
  trailing-dot hosts, and IPv4 fallback when IPv6 is unavailable. MoSaProxy already puts IP addresses into certificates and issues
  90-day leaf certificates (which avoids the browser lifetime limit), but the hostname cases are untested.
- **Request framing.** Both had request-smuggling fixes, including HTTP/2 to HTTP/1 translation. Add tests for conflicting
  `Content-Length` and `Transfer-Encoding`.
- **Web front-end security.** mitmweb's worst vulnerability (CVE-2025-23217) let a request tunnelled through the proxy reach its own
  localhost API; an earlier one was DNS rebinding. Any web front end must require a token by default, because the proxy forwards to
  loopback addresses.
- **Slow and odd clients.** Pipelined requests and half-closing clients crashed or hung mitmproxy. MoSaProxy hit the half-close case
  itself (see [plan.md](plan.md), Phase 6).

### Suggested order, if these are taken up

Scripting first, since modification and breakpoints can hang off the same events. Then detailed timings (small, and it unblocks an
accurate HAR export), per-process attribution, and the streaming and gRPC tests.
