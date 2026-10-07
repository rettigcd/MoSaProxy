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
