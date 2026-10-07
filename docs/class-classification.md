# MoSaProxy: classes classified as Core, UI, Mix or Other

A snapshot of every class, struct and record in the `app/` project, sorted by what it is about. It is meant for deciding what belongs in a reusable proxy library and what is front-end code. Build plan: [plan.md](plan.md).

## The four categories

| Category | Meaning |
|---|---|
| **Core** | Focuses on the mechanics of being a proxy server: listening, forwarding requests, tunneling, intercepting TLS, WebSockets, certificates, shutdown, and the data that describes what the proxy did. |
| **UI** | Focuses on the user interface: what is printed, how it is coloured, what the user is asked, and the commands and help text shown to them. |
| **Mix** | Has both Core and UI in it. |
| **Other** | Neither. Here that means recording and exporting what the proxy saw, integration with the operating system, and small formatting helpers. |

A class is placed by its main focus. A line or two of console output inside an otherwise-core class does not make it Mix; those places are listed in [Where core code touches the console](#where-core-code-touches-the-console).

## Summary

| Category | Types |
|---|---|
| Core | 21 |
| UI | 13 |
| Mix | 2 |
| Other | 13 |
| **Total** | **49** (48 declared types, plus the `Program` class the compiler generates) |

## Core (21)

| Type | Kind | File | Why |
|---|---|---|---|
| `ProxyHost` | class | `ProxyHost.cs` | Starts Kestrel on loopback and runs the two-stage stop (stop accepting, drain, abort). |
| `ShutdownReport` | record | `ProxyHost.cs` | What was still running when the drain period ended. |
| `ProxyHandler` | class | `ProxyHandler.cs` | Forwards each HTTP request to its origin, including HTTP/2 negotiation, bodies, trailers, and timeouts. |
| `ProxyOptions` | class | `ProxyOptions.cs` | The settings the proxy runs with, and the one definition of each default (timeouts, interception, rotation, drain, and so on). It holds no parsing or user-facing text. |
| `TunnelHandler` | class | `TunnelHandler.cs` | Handles `CONNECT` ahead of Kestrel: blind tunnels, sniffing TLS versus HTTP, and TLS interception. |
| `TunnelRelay` | static class | `TunnelRelay.cs` | Copies bytes both ways between client and origin, with half-close and idle handling. |
| `WebSocketProxy` | class | `WebSocketProxy.cs` | Opens a WebSocket to the origin, accepts the client's, and copies messages and close codes. |
| `WebSocketSummary` | record | `WebSocketProxy.cs` | Result of one proxied WebSocket connection. |
| `WebSocketProxy.Totals` | nested class | `WebSocketProxy.cs` | Message and byte counters for the two directions. |
| `CertificateAuthority` | class | `CertificateAuthority.cs` | Creates and loads the root certificate and issues the per-host certificates used to intercept TLS. |
| `HeaderRules` | static class | `HeaderRules.cs` | Which headers must not be forwarded (hop-by-hop and `Connection`-named). Also holds a small helper that flattens headers for the log. |
| `CountingStream` | class | `CountingStream.cs` | Counts the bytes that pass through a stream while forwarding. |
| `ExchangeRecord` | record | `ExchangeLog.cs` | The data describing one exchange (method, URL, status, protocols, sizes, timing, mode, error). Produced by the proxy; consumed by logs, capture and the console. |
| `ProxyActivity` | class | `ProxyActivity.cs` | Counts requests, tunnels and WebSockets in flight, so a shutdown can wait for and report them. |
| `ProxyActivity.Scope` | nested class | `ProxyActivity.cs` | Decrements the count when an item finishes. |
| `ResourceLimits` | static class | `ResourceLimits.cs` | Recognizes the failures that mean the system ran out of something (disk, memory, sockets, file handles). |
| `DuplexStream` | internal class | `TunnelHandler.cs` | Joins a read stream and a write stream into one, for relaying and TLS. |
| `DuplexPipe` | internal class | `TunnelHandler.cs` | Reader and writer as one pipe, used to hand the decrypted stream to Kestrel. |
| `ObservedStream` | internal class | `TunnelHandler.cs` | Remembers how much was read and the first read failure, to detect a client that rejects the certificate. |
| `TlsConnectionFeature` | internal class | `TunnelHandler.cs` | Tells Kestrel a connection is TLS. |
| `TlsApplicationProtocolFeature` | internal class | `TunnelHandler.cs` | Tells Kestrel which protocol (`h2` or `http/1.1`) the handshake negotiated. |

## UI (13)

| Type | Kind | File | Why |
|---|---|---|---|
| `ColorConsole` | class | `ConsoleEx.cs` | Writes coloured, thread-safe text to the console (or plain text to any writer, for tests). |
| `ConsoleEx` | static class | `ConsoleEx.cs` | Shorthand for writing to the real console. |
| `ConsoleInterpolatedStringHandler` | ref struct | `ConsoleEx.cs` | Lets a message mix text and colour changes in one interpolated string. |
| `ForegroundColor` | readonly record struct | `ConsoleEx.cs` | Marker for a foreground colour change inside a message. |
| `BackgroundColor` | readonly record struct | `ConsoleEx.cs` | Marker for a background colour change. |
| `RestoreForeground` | readonly record struct | `ConsoleEx.cs` | Marker that puts the foreground colour back. |
| `RestoreBackground` | readonly record struct | `ConsoleEx.cs` | Marker that puts the background colour back. |
| `Fg` | static class | `ConsoleEx.cs` | The named foreground colours. |
| `Bg` | static class | `ConsoleEx.cs` | The named background colours. |
| `ConsoleExchangeLog` | class | `ExchangeLog.cs` | Prints one coloured line per exchange (blue methods, status coloured by class). |
| `InterceptionSetup` | static class | `InterceptionSetup.cs` | The startup dialog for HTTPS interception: reports the trust state, asks `[y/N]`, and prints the Chrome commands. It drives `RootTrust`, but its job is the conversation with the user. |
| `ChromeLauncher` | static class | `ChromeLauncher.cs` | Builds the ready-to-paste Chrome commands shown to the user. |
| `ProxyOptionsParser` | static class | `ProxyOptionsParser.cs` | Turns the command-line arguments into a `ProxyOptions`, and words the error and usage text. Starts from the defaults in `ProxyOptions`. |

## Mix (2)

| Type | Kind | File | Core part | UI part |
|---|---|---|---|---|
| `ResourceLimitMonitor` | class | `ResourceLimits.cs` | Remembers the first limit hit and completes a task the program waits on | Prints `Limit reached: ...` in red |
| `Program` (generated from `Program.cs`) | class | `Program.cs` | Starts the proxy, waits for stop or a limit, drains and stops, writes the archive | Console messages, Ctrl+C handling, argument errors, exit codes |

## Other (13)

| Type | Kind | File | What it is |
|---|---|---|---|
| `JsonLinesExchangeLog` | class | `ExchangeLog.cs` | Writes the JSON Lines log, with rotation. |
| `CompositeExchangeLog` | class | `ExchangeLog.cs` | Sends each record to several logs and reports a failing one. |
| `FileSessionCapture` | class | `SessionCapture.cs` | Starts a capture run folder and numbers the exchanges. |
| `FileExchangeCapture` | internal class | `SessionCapture.cs` | Writes one exchange's `N_c.txt`, `N_s.txt`, `N_w.txt` and `N_m.xml`. |
| `NullSessionCapture` | class | `SessionCapture.cs` | Capture turned off. |
| `NullExchangeCapture` | class | `SessionCapture.cs` | Capture turned off, per exchange. |
| `TeeReadStream` | class | `SessionCapture.cs` | Copies a body into a capture file while it is read. |
| `SazExporter` | static class | `SazExporter.cs` | Packs a capture folder into a Fiddler `.saz` archive. |
| `SazExporter.Exchange` | nested record | `SazExporter.cs` | One exchange's metadata as read back for the export. |
| `SazExporter.IndexRow` | nested record | `SazExporter.cs` | One row of the archive's `_index.htm`. |
| `RootTrust` | class | `RootTrust.cs` | Checks, installs and removes the root certificate in a trust store. |
| `WindowsUserRootStore` | class | `RootTrust.cs` | The current Windows user's trusted root store. |
| `HttpVersions` | static class | `HttpVersions.cs` | Formats a protocol version as `HTTP/1.1`, `HTTP/2` and so on, for logs and captures. |

## Interfaces and enums

Not asked for, but listed so the picture is complete.

| Type | Kind | File | Category |
|---|---|---|---|
| `ActivityKind` | enum | `ProxyActivity.cs` | Core |
| `TunnelHandler.Sniffed` | nested enum | `TunnelHandler.cs` | Core |
| `IExchangeLog` | interface | `ExchangeLog.cs` | Other |
| `ISessionCapture` | interface | `SessionCapture.cs` | Other |
| `IExchangeCapture` | interface | `SessionCapture.cs` | Other |
| `IRootStore` | interface | `RootTrust.cs` | Other |
| `RootTrustState` | enum | `RootTrust.cs` | Other |

## Judgment calls

These could reasonably go the other way.

- **Recording and exporting is Other, not Core.** The log writers, capture files and `.saz` export record what the proxy saw rather than forward it. A front end would still reuse all of them, so in a future split they would most likely sit with Core.
- **`RootTrust` and `WindowsUserRootStore` are Other.** They manage the operating system's trust store, which supports interception but is not proxy mechanics.
- **`InterceptionSetup` is UI, not Mix.** It calls `RootTrust` to do the install, but that is a single call; everything else it does is prompting and printing.
- **`ExchangeRecord` is Core.** It has no behaviour, but it is the proxy's own description of what happened.
- **`HeaderRules` is Core**, even though one method (`ForLog`) only exists to prepare headers for the log.
- **`ConsoleExchangeLog` is UI** although it implements the same interface as the file log.
- **`ProxyOptions` is Core, `ProxyOptionsParser` is UI.** `ProxyOptions` used to be Mix, because it held both the settings and the command-line parsing with its error and usage text. The parsing moved into `ProxyOptionsParser`, so what is left in `ProxyOptions` is configuration the proxy needs, whichever front end sets it. The doc comments on its settings still name the command-line flags, which is documentation, not behaviour.

## Where core code touches the console

Eight lines in classes classified above as Core or Other write straight to the console, mostly short diagnostics that capture or logging could not do something. These are the places to change first if the core is ever split from the console front end.

| Type | What it prints |
|---|---|
| `ProxyHandler` | `Capture unavailable: ...` |
| `TunnelHandler` | `Capture unavailable: ...` and `Tunnel error after the CONNECT was logged: ...` |
| `JsonLinesExchangeLog` | `Log rotation failed (...); continuing to append to ...` |
| `CompositeExchangeLog` | `Log sink failed: ...` |
| `FileExchangeCapture` | `Capture disabled for ...`, `Capture metadata failed ...` and `Capture write failed: ...` |

## Keeping this list current

List every type in the app with:

```
grep -nE "^\s*((public|internal|private|protected|file)\s+)?((static|sealed|abstract|readonly|partial|ref)\s+)*(class|struct|record( struct| class)?|interface|enum)\s+[A-Za-z_]" app/*.cs
```

Anything in the output that is not in the tables above is new and needs a category. `Program.cs` declares no type itself; the compiler turns its top-level statements into the `Program` class, which is why it is listed by hand.
