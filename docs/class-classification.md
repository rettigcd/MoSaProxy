# MoSaProxy: classes classified as Core, UI, Mix or Other

A snapshot of every class, struct and record in the `core/` and `app/` projects, sorted by what it is about. Build plan: [plan.md](plan.md).

## The goal this serves

**Core holds everything that is not tied to a user interface.** The intention is to build several front ends, each one referencing Core and doing only what is specific to it, so that nothing is written twice. Core should be able to save what it captured as `.saz` (Fiddler), as a mitmproxy flow file, and as `.har`. Today `.saz` is done; the other two are not started.

Core lives in `core/` (`MoSaProxy.Core`, namespace `MoSaProxy`); the console front end in `app/` references it. Types listed as Core are in `core/`, UI types in `app/`. The section [What this means for several front ends](#what-this-means-for-several-front-ends) lists the work that follows from it.

## The four categories

| Category | Meaning |
|---|---|
| **Core** | Everything that is not tied to a user interface: the mechanics of being a proxy server (listening, forwarding, tunneling, intercepting TLS, WebSockets, certificates, shutdown), the data that describes what the proxy did, recording and saving it (the log, the capture files, and the `.saz` format, with mitmproxy flow and `.har` to come), and supporting services such as the operating system's trust store. A front end should only have to ask Core to do things and show what Core reports. |
| **UI** | Tied to the user interface: what is printed, how it is coloured, what the user is asked, the commands and help text shown, and wiring the pieces together for one particular front end. |
| **Mix** | Has both Core and UI in it. None at the moment; a type that starts mixing should be split. |
| **Other** | Neither. None at the moment: anything that is not tied to a UI is Core. |

A class is placed by its main focus. A line or two of console output inside an otherwise-core class does not make it Mix; those places are listed in [Where Core code touches the console](#where-core-code-touches-the-console).

## Summary

| Category | Types |
|---|---|
| Core | 36 |
| UI | 15 |
| Mix | 0 |
| Other | 0 |
| **Total** | **51** (50 declared types, plus the `Program` class the compiler generates) |

## Core (36)

| Type | Kind | File | Why |
|---|---|---|---|
| `ProxyHost` | class | `ProxyHost/ProxyHost.cs` | Starts Kestrel on loopback and owns the proxy's lifecycle: the two-stage stop (stop accepting, drain, abort) and `RunUntilStoppedAsync`, which decides how to stop (drain on a stop request, at once on a limit). |
| `ShutdownReport` | record | `ProxyHost/ShutdownReport.cs` | What was still running when the drain period ended. |
| `StopResult` | record | `ProxyHost/StopResult.cs` | How a run ended: why it stopped, the shutdown report, and the first limit hit, if any. |
| `ProxyHandler` | class | `ProxyHandler.cs` | Forwards each HTTP request to its origin, including HTTP/2 negotiation, bodies, trailers, and timeouts. |
| `ProxyOptions` | class | `ProxyOptions.cs` | The settings the proxy runs with, and the one definition of each default (timeouts, interception, rotation, drain, and so on). It holds no parsing or user-facing text. |
| `TunnelHandler` | class | `TunnelHandler/TunnelHandler.cs` | Handles `CONNECT` ahead of Kestrel: blind tunnels, sniffing TLS versus HTTP, and TLS interception. |
| `TunnelRelay` | static class | `TunnelRelay.cs` | Copies bytes both ways between client and origin, with half-close and idle handling. |
| `WebSocketProxy` | class | `WebSocketProxy/WebSocketProxy.cs` | Opens a WebSocket to the origin, accepts the client's, and copies messages and close codes. |
| `WebSocketSummary` | record | `WebSocketProxy/WebSocketSummary.cs` | Result of one proxied WebSocket connection. |
| `WebSocketProxy.Totals` | nested class | `WebSocketProxy/WebSocketProxy.cs` | Message and byte counters for the two directions. |
| `CertificateAuthority` | class | `CertificateAuthority.cs` | Creates and loads the root certificate and issues the per-host certificates used to intercept TLS. |
| `HeaderRules` | static class | `HeaderRules.cs` | Which headers must not be forwarded (hop-by-hop and `Connection`-named). Also holds a small helper that flattens headers for the log. |
| `CountingStream` | class | `CountingStream.cs` | Counts the bytes that pass through a stream while forwarding. |
| `ExchangeRecord` | record | `ExchangeLog/ExchangeRecord.cs` | The data describing one exchange (method, URL, status, protocols, sizes, timing, mode, error). Produced by the proxy; consumed by logs, capture and the console. |
| `ProxyActivity` | class | `ProxyActivity.cs` | Counts requests, tunnels and WebSockets in flight, so a shutdown can wait for and report them. |
| `ProxyActivity.Scope` | nested class | `ProxyActivity.cs` | Decrements the count when an item finishes. |
| `ResourceLimits` | static class | `ResourceLimits.cs` | Recognizes the failures that mean the system ran out of something (disk, memory, sockets, file handles). |
| `ResourceLimitMonitor` | class | `ResourceLimits.cs` | Remembers the first limit hit, raises the `LimitReached` event once, and completes a task the program waits on. It prints nothing itself. |
| `HttpVersions` | static class | `HttpVersions.cs` | Formats a protocol version as `HTTP/1.1`, `HTTP/2` and so on, for logs and captures. |
| `DuplexStream` | internal class | `TunnelHandler/DuplexStream.cs` | Joins a read stream and a write stream into one, for relaying and TLS. |
| `DuplexPipe` | internal class | `TunnelHandler/DuplexPipe.cs` | Reader and writer as one pipe, used to hand the decrypted stream to Kestrel. |
| `ObservedStream` | internal class | `TunnelHandler/ObservedStream.cs` | Remembers how much was read and the first read failure, to detect a client that rejects the certificate. |
| `TlsConnectionFeature` | internal class | `TunnelHandler/TlsConnectionFeature.cs` | Tells Kestrel a connection is TLS. |
| `TlsApplicationProtocolFeature` | internal class | `TunnelHandler/TlsApplicationProtocolFeature.cs` | Tells Kestrel which protocol (`h2` or `http/1.1`) the handshake negotiated. |
| `JsonLinesExchangeLog` | class | `ExchangeLog/JsonLinesExchangeLog.cs` | Writes the JSON Lines log, with rotation. |
| `CompositeExchangeLog` | class | `ExchangeLog/CompositeExchangeLog.cs` | Sends each record to several logs and reports a failing one. |
| `FileSessionCapture` | class | `SessionCapture/FileSessionCapture.cs` | Starts a capture run folder and numbers the exchanges. |
| `FileExchangeCapture` | internal class | `SessionCapture/FileExchangeCapture.cs` | Writes one exchange's capture files: `N_c.txt` (request), `N_s.txt` (response), `N_w.txt` (WebSocket messages) and `N_m.xml` (metadata). |
| `NullSessionCapture` | class | `SessionCapture/NullSessionCapture.cs` | Capture turned off. |
| `NullExchangeCapture` | class | `SessionCapture/NullExchangeCapture.cs` | Capture turned off, per exchange. |
| `TeeReadStream` | class | `SessionCapture/TeeReadStream.cs` | Copies a body into a capture file while it is read. |
| `SazExporter` | static class | `SazExporter/SazExporter.cs` | Packs a capture folder into a Fiddler `.saz` archive: the zip layout, the content-types file, Fiddler's `Session` metadata, and the readable `_index.htm`. It has no console code. |
| `SazExporter.Exchange` | nested record | `SazExporter/SazExporter.cs` | One exchange's metadata as read back for the export. |
| `SazExporter.IndexRow` | nested record | `SazExporter/SazExporter.cs` | One row of the archive's `_index.htm`. |
| `RootTrust` | class | `RootTrust.cs` | Checks, installs and removes the root certificate in a trust store. |
| `WindowsUserRootStore` | class | `RootTrust.cs` | The current Windows user's trusted root store (the Windows implementation of `IRootStore`). |

## UI (15)

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
| `ConsoleExchangeLog` | class | `ConsoleExchangeLog.cs` | Prints one coloured line per exchange (blue methods, status coloured by class). |
| `InterceptionSetup` | static class | `InterceptionSetup.cs` | The startup dialog for HTTPS interception: reports the trust state, asks `[y/N]`, and prints the Chrome commands. It drives `RootTrust`, but its job is the conversation with the user. |
| `ChromeLauncher` | static class | `ChromeLauncher.cs` | Builds the ready-to-paste Chrome commands shown to the user. |
| `ProxyOptionsParser` | static class | `ProxyOptionsParser.cs` | Turns the command-line arguments into a `ProxyOptions`, and words the error and usage text. Starts from the defaults in `ProxyOptions`. |
| `ResourceLimitConsoleReporter` | class | `ResourceLimitConsoleReporter.cs` | Subscribes to `ResourceLimitMonitor.LimitReached` and prints `Limit reached: ...` in red. |
| `Program` (generated from `Program.cs`) | class | `Program.cs` | The console front end and orchestrator: parses the arguments, runs the `--untrust-ca` and `--export-saz` commands, wires the log, limit monitor and reporter, starts the host, shows the startup and stop messages, counts Ctrl+C presses, writes the `.saz`, and returns the exit code. The decisions about how to stop live in `ProxyHost.RunUntilStoppedAsync`. |

## Mix (0)

No type is Mix at the moment. A type that starts mixing proxy mechanics with console output should be split the way `ProxyOptions` and `ResourceLimitMonitor` were, and the rows for it would go here until then.

## Other (0)

No type is Other at the moment. The category exists for anything that is neither Core nor UI, but the aim is that everything not tied to a user interface is Core.

## Interfaces and enums

Listed so the picture is complete.

| Type | Kind | File | Category |
|---|---|---|---|
| `ActivityKind` | enum | `ProxyActivity.cs` | Core |
| `StopReason` | enum | `ProxyHost/StopResult.cs` | Core |
| `TunnelHandler.Sniffed` | nested enum | `TunnelHandler/TunnelHandler.cs` | Core |
| `IExchangeLog` | interface | `ExchangeLog/ExchangeRecord.cs` | Core |
| `ISessionCapture` | interface | `SessionCapture/NullSessionCapture.cs` | Core |
| `IExchangeCapture` | interface | `SessionCapture/NullExchangeCapture.cs` | Core |
| `IRootStore` | interface | `RootTrust.cs` | Core |
| `RootTrustState` | enum | `RootTrust.cs` | Core |

## Judgment calls

These could reasonably go the other way.

- **Everything not tied to a UI is Core, by decision.** That includes the JSON log writers, the capture files and the `.saz` export, and the operating system's trust store. The first version of this document had a separate "Other" category for recording, exporting and operating-system integration; with the goal of several front ends sharing one Core, there is no reason for them to be anywhere else, and the category is now empty.
- **`RootTrust` and `WindowsUserRootStore` are Core, although the second is Windows-only.** A Core can hold platform-specific implementations behind an interface (`IRootStore`). A Linux or macOS store would be another class implementing it, chosen by operating system. (`RootTrust` itself is already platform-neutral; `WindowsUserRootStore` is not usable on Linux as it stands, see the discussion in the project history.)
- **`ChromeLauncher` is UI, but it probably should not be.** Building the Chrome command lines is not specific to a console: a GUI would show the same commands. It is classified as UI only because it is currently used just by the console program. It is a candidate to move to Core.
- **`InterceptionSetup` is UI, but it contains logic every front end would repeat.** The decision flow (check the trust state, offer to install, offer to replace an older root, fall back to the throwaway profile) is not about the console; the prompting and printing around it is. It is a candidate to split into a Core flow plus a thin UI that asks the questions.
- **`ExchangeRecord` is Core.** It has no behaviour, but it is the proxy's own description of what happened.
- **`HeaderRules` is Core**, even though one method (`ForLog`) only exists to prepare headers for the log.
- **`ConsoleExchangeLog` is UI** although it implements the same interface as the file log.
- **`ProxyOptions` is Core, `ProxyOptionsParser` is UI.** `ProxyOptions` used to be Mix, because it held both the settings and the command-line parsing with its error and usage text. The parsing moved into `ProxyOptionsParser`, so what is left in `ProxyOptions` is configuration the proxy needs, whichever front end sets it. The doc comments on its settings still name the command-line flags, which is documentation, not behaviour.
- **`ResourceLimitMonitor` is Core, `ResourceLimitConsoleReporter` is UI.** The monitor used to print the limit itself (so it was Mix). It now raises a `LimitReached` event and the reporter, which subscribes to it, does the printing. That event is the way for Core to tell a front end something without knowing about the console.
- **`Program` is UI.** It used to be Mix because it also held the stop policy: wait for a stop request or a limit, then drain or stop at once. That moved into `ProxyHost.RunUntilStoppedAsync` (with `StopReason` and `StopResult`), and the program reports what is happening through the `onStopping` callback. What remains is orchestration and console work. See below for how much of that orchestration every front end would have to repeat.

## What this means for several front ends

These follow from the goal; none is decided or started.

1. **A separate Core project. Done:** `core/MoSaProxy.Core` holds the Core types and `app/` references it. The namespace stays `MoSaProxy`.
2. **Core must stop writing to the console.** The eight places in the table below write diagnostics straight to the console. Each should become an event or a small diagnostics interface (the way `LimitReached` already works), with the console front end subscribing.
3. **Orchestration that every front end would repeat.** `Program` currently does these things, and a second front end would have to do them again:
   - build the logs and connect the limit monitor to them;
   - start the host, run until stopped, and report the result;
   - export the `.saz` after the stop;
   - run the `--untrust-ca` and `--export-saz` commands.

   A Core "session" or "application" service that does them and returns structured results would leave each front end with only the presenting. `InterceptionSetup`'s flow and `ChromeLauncher` (above) belong in the same group.
4. **Saving in several formats.** `.saz` is one exporter that reads a capture folder. `.har` and the mitmproxy flow format would each be another, so they should sit behind one common shape, and every front end gets all of them for free. They need more of the exchange's detail than `.saz` does; for example HAR has a timing breakdown, which the current metadata only approximates.

## Where Core code touches the console

Eight lines in Core classes write straight to the console (all with `Console.Error.WriteLine`), mostly short diagnostics that capture or logging could not do something. Each is a place to change for point 2 above.

| File | Method | What it prints |
|---|---|---|
| `ProxyHandler.cs` | `HandleAsync` | `Capture unavailable: ...` |
| `TunnelHandler/TunnelHandler.cs` | `HandleAsync` | `Capture unavailable: ...` |
| `TunnelHandler/TunnelHandler.cs` | `HandleAsync` | `Tunnel error after the CONNECT was logged: ...` |
| `ExchangeLog/JsonLinesExchangeLog.cs` | `Rotate` | `Log rotation failed (...); continuing to append to ...` |
| `ExchangeLog/CompositeExchangeLog.cs` | `Write` | `Log sink failed: ...` |
| `SessionCapture/FileExchangeCapture.cs` | `Open` | `Capture disabled for ...` |
| `SessionCapture/FileExchangeCapture.cs` | `Complete` | `Capture metadata failed for exchange ...` |
| `SessionCapture/FileExchangeCapture.cs` | `Fail` | `Capture write failed: ...` |

## Keeping this list current

List every type in the app with:

```
grep -nE "^\s*((public|internal|private|protected|file)\s+)?((static|sealed|abstract|readonly|partial|ref)\s+)*(class|struct|record( struct| class)?|interface|enum)\s+[A-Za-z_]" app/*.cs core/*.cs core/*/*.cs
```

Anything in the output that is not in the tables above is new and needs a category. `Program.cs` declares no type itself; the compiler turns its top-level statements into the `Program` class, which is why it is listed by hand.
