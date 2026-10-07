# mitmproxy --- Consolidated Release-History Inventory

**Scope:** Official `mitmproxy/mitmproxy` `CHANGELOG.md` / **Release
History**, from the earliest listed mitmproxy release (**0.5, June 27,
2011**) through **12.2.3 (May 12, 2026)**, plus the currently documented
**Unreleased / mitmproxy next** section as of October 2026.

**Purpose:** This report is organized like the corresponding Fiddler
Everywhere inventory. It is not primarily a version-by-version
changelog. It combines the history into:

1.  **Features and capabilities added to mitmproxy**
2.  **Bugs, limitations, failures, and security issues recorded as
    fixed**

Repeated or closely related entries are consolidated where useful, but
concrete technical failure modes are retained.

Official source:\
https://github.com/mitmproxy/mitmproxy/blob/main/CHANGELOG.md

------------------------------------------------------------------------

# Product family and interfaces

mitmproxy is not just one UI. The release history covers a family of
tools and APIs:

-   **mitmproxy** --- interactive terminal/TUI proxy.
-   **mitmweb** --- browser-based graphical interface.
-   **mitmdump** --- non-interactive command-line traffic
    capture/processing tool.
-   **Python addon API** --- programmable event-driven interception and
    modification.
-   **mitmproxy_rs** --- Rust-based lower-level networking/protocol
    functionality used by modern releases.
-   Historical **pathod/pathoc** traffic-generation and fuzzing tools,
    later deprecated/removed.

------------------------------------------------------------------------

# Features and capabilities

## Core HTTP/HTTPS interception

-   HTTP proxying.
-   HTTPS man-in-the-middle interception.
-   Dynamic generation of interception certificates.
-   Automatic per-host leaf certificate generation.
-   Upstream certificate inspection.
-   Upstream certificate information used when generating interception
    certificates.
-   SNI support.
-   Certificate forwarding/relaying capabilities.
-   Per-domain interception certificates and private keys.
-   Custom CA support.
-   Password-protected CA/private-key support.
-   Certificate expiration checking.
-   Certificate-installation helper site (`mitm.it`).
-   Certificate-installation instructions for desktop/mobile platforms.
-   Client certificate support.
-   TLS version configuration.
-   TLS negotiation hooks and events.
-   TLS client/server established and failed hooks.
-   TLS 1.3 support.
-   TLS 1.3 SSLKEYLOGFILE secrets.
-   TLS 1.3 Post-Handshake Authentication.
-   CRL information copied to generated certificates when present
    upstream.
-   Secure Web Proxy / TLS-over-TLS support.
-   Ability to remove TLS on upstream HTTP connections.
-   Connection strategies controlling eager vs. lazy upstream connection
    establishment.
-   Host-header-based proxying when the destination is otherwise
    unknown.
-   Connection timeout/idle cleanup.
-   Configurable TCP inactivity timeout.

## HTTP/1.x

-   HTTP/1.x request and response capture.
-   Streaming HTTP/1 request bodies.
-   Chunked transfer encoding.
-   HTTP trailers.
-   Sending HTTP trailers.
-   Displaying HTTP trailers.
-   Multiple `Content-Length` handling and validation.
-   Configurable inbound-header validation.
-   Support for malformed/technically invalid traffic when validation is
    intentionally disabled.
-   Preserve header case and order.
-   Request/response manipulation.
-   Request destination rewriting.
-   Host-header manipulation.
-   Keep-host-header behavior in reverse proxy mode.
-   Request retry behavior.
-   HEAD request handling.
-   CONNECT request handling.
-   Hooks for successful/failed HTTP CONNECT.

## HTTP/2

-   HTTP/2 protocol support.
-   HTTP/2 enabled by default beginning with mitmproxy 2.0.
-   HTTP/2 client connections forwarded to HTTP/1 upstream servers.
-   HTTP/1 ↔ HTTP/2 interoperability.
-   HTTP/2 redirects/request-destination changes.
-   HTTP/2 request replay.
-   HTTP/2 request streaming.
-   Header normalization.
-   HTTP/2 stream reset handling.
-   HTTP/2 flow inspection/modification.
-   HTTP/2 → HTTP/1 translation.
-   HTTP/2 CONNECT handling.
-   Historical HTTP/2 Push Promise support (later removed).

## HTTP/3 and QUIC

-   Experimental HTTP/3 and QUIC support introduced in 10.0.
-   Transparent HTTP/3 proxying.
-   HTTP/3 in HTTPS reverse-proxy mode.
-   QUIC ClientHello/SNI inspection.
-   QUIC integration with host allow/ignore controls.
-   HTTP/3/QUIC traffic handled by the modern proxy core.

## WebSockets

-   WebSocket protocol support.
-   WebSockets enabled by default once implementation matured.
-   WebSocket traffic capture.
-   WebSocket message inspection.
-   WebSocket flow filters.
-   WebSocket message timestamps.
-   WebSocket interception.
-   WebSocket traffic exposed to addons.
-   WebSocket display in mitmweb.
-   WebSocket performance improvements.
-   Message-stream direction indicators.

## Raw TCP

-   Generic TCP proxying.
-   Raw TCP connection proxying.
-   TCP flow display.
-   TCP contentviews.
-   Support for protocols beginning with a **server-side greeting**,
    such as SMTP.
-   Configurable TCP inactivity timeout.
-   TCP connection event handling.
-   TCP flows available to addons.

## UDP

-   Raw UDP support.
-   UDP streams exposed through the API.
-   Modern Rust-backed UDP implementation.
-   UDP error/disconnect handling.
-   UDP-based protocol support used by DNS/QUIC functionality.

## DTLS

-   DTLS support.
-   Datagram-oriented encrypted traffic handling.

## DNS

-   DNS proxy mode.
-   DNS over UDP.
-   DNS over TCP.
-   Support for all DNS query types.
-   DNS response spoofing through addons.
-   DNS-over-HTTPS contentview.
-   DNS resource-record parsing.
-   Compressed domain-name parsing.
-   Operating-system DNS server discovery.
-   `getaddrinfo` fallback when OS DNS servers cannot be determined.
-   DNS hooks/addon integration before resolution.
-   DNS messages exposed through the addon API.

## Proxy modes

-   Regular explicit HTTP/HTTPS forward proxy.
-   **Transparent proxy mode**.
-   Transparent proxy support across Linux/macOS and historically
    Windows/OpenBSD/FreeBSD variants.
-   **Reverse proxy mode**.
-   HTTPS reverse proxy.
-   HTTP/3 HTTPS reverse proxy.
-   **Upstream proxy mode**.
-   Forward requests through another HTTP proxy.
-   **SOCKS v5 proxy mode**.
-   SOCKS v5 proxy authentication.
-   **WireGuard mode** for transparent proxying.
-   **Local Capture / Local Redirect mode**.
-   Local capture on Windows.
-   Local capture on macOS.
-   Local capture on Linux.
-   Ability to spawn **multiple proxy servers/modes in one mitmproxy
    instance**.
-   Host allow lists.
-   Host ignore lists.
-   Global/private-client blocking controls.
-   Host-header-based proxying for DNS-spoofing workflows.

## Local Capture / Local Redirect

-   Capture traffic from local applications without configuring each
    application's proxy.
-   Local Redirect Mode on Windows.
-   Local Redirect Mode on macOS.
-   Local Capture Mode on Linux.
-   Modern platform-specific capture implementations.
-   Documentation and capture UI support in mitmweb.

## WireGuard

-   Built-in WireGuard proxy mode.
-   Transparent traffic routing through WireGuard.
-   Generated WireGuard configuration.
-   Performance and stability improvements over subsequent releases.
-   Multi-interface/NIC handling.

## Reverse proxy

-   HTTP reverse proxy.
-   HTTPS reverse proxy.
-   HTTP/3 reverse proxy.
-   Preserve/modify Host header.
-   Correct non-standard port handling.
-   Custom certificate support.
-   Embedded application routing.

## Upstream proxy

-   Forward proxy traffic to another HTTP proxy.
-   HTTPS/TLS upstream proxy connections.
-   Secure Web Proxy / TLS-over-TLS.
-   Proxy authentication.
-   IPv4/IPv6 authority handling.
-   IDNA hostname support.

## Proxy authentication

-   Restrict access to the mitmproxy proxy.
-   Basic authentication.
-   htpasswd-backed authentication.
-   bcrypt and SHA-1 htpasswd formats in the modern parser.
-   LDAP proxy-authentication backend.
-   LDAP search-filter configuration.
-   SOCKS v5 proxy authentication.
-   Store proxy-authentication information with flows.

## Traffic capture and flow model

-   Capture requests and responses as **flows**.
-   HTTP flows.
-   WebSocket flows.
-   TCP flows.
-   UDP flows.
-   DNS flows.
-   Flow serialization.
-   Flow dump files.
-   Stream flows to disk as they arrive.
-   Load saved flows.
-   Filter flows.
-   Search flows.
-   Mark flows.
-   Custom flow markers, including characters/emoji.
-   Add comments to flows.
-   Filter/search flow comments.
-   Duplicate flows.
-   Revert modified/duplicated flows.
-   Kill/intercept flows.
-   Flow metadata.
-   Flow timing information.
-   Connection information.
-   Generated certificate associated with a flow.
-   Proxy authentication associated with a flow.

## Filtering

-   Rich flow-filter expression language.
-   Filter by URL.
-   Filter by domain.
-   Filter by assets.
-   Filter WebSocket flows.
-   Filter body content.
-   Filter request body.
-   Filter response body.
-   Filter markers.
-   Filter comments.
-   Case-insensitive filters by default in modern releases.
-   Optional case-sensitive filtering.
-   Unicode filtering.
-   `pretty_host` matching.
-   Allow-host and ignore-host filters.
-   Filter expressions used throughout commands/addons/UI.

## Search and highlighting

-   Search requests.
-   Search responses.
-   Search HTTP request/response bodies.
-   Search within an individual request/response.
-   Search highlighting.
-   Search/filter traffic in terminal UI.
-   Search/filter traffic in mitmweb.

## Interception and breakpoints

-   Intercept matching flows.
-   Pause requests/responses.
-   Edit intercepted flows.
-   Resume or kill intercepted traffic.
-   Intercept filter.
-   Intercept WebSocket traffic.
-   Event hooks for intercepted traffic.
-   Addons can programmatically choose whether to ignore/intercept
    traffic.

## Request and response modification

-   Edit requests.
-   Edit responses.
-   Modify headers.
-   Set headers based on matching rules.
-   Modify body.
-   Universal regex-based `replace` operations.
-   Block requests.
-   Map remote URLs.
-   Map local files.
-   Anti-cache functionality.
-   Sticky cookies.
-   Sticky authentication.
-   Modify query parameters.
-   Modify multipart forms.
-   Modify URLs.
-   Modify request destination/host/port.
-   Change status codes/reason phrases.
-   Create synthetic responses.
-   Create responses to CONNECT requests.
-   Redirect traffic.
-   Remove TLS upstream.
-   DNS spoofing through addons.
-   Custom programmable modifications through Python.

## Map Local / local response substitution

-   Map requests to local files/resources.
-   Persist MapLocal configuration across option changes.
-   Use mappings in combination with filters.
-   Develop/test clients without contacting the real upstream resource.

## Modify Body / Modify Headers

-   Dedicated body-modification addon.
-   Dedicated header-modification addon.
-   Regex/string replacement.
-   Conditional application through flow filters.
-   Request and response manipulation.

## Client replay

-   Replay captured requests against real servers.
-   Replay individual flows.
-   Replay sets of flows.
-   Concurrent client replay.
-   Configurable replay concurrency.
-   HTTP/2 replay.
-   Replay while preserving relevant headers/body data.

## Server replay

-   Replay captured responses without contacting the original upstream
    server.
-   Server replay from the current captured buffer.
-   Load replay flows.
-   Add flows to the server replay buffer dynamically.
-   Match incoming requests to recorded responses.
-   Fine-grained matching controls.
-   Ignore selected request contents/parameters during replay matching.
-   Configure behavior when no replayable response exists.
-   Lazy connection strategy supports response replay without opening an
    upstream connection.
-   mitmdump server-replay workflows.

## Save/load flows

-   Serialize captured traffic to mitmproxy flow files.
-   Load flow files.
-   Stream flows to disk.
-   Flush flow files incrementally.
-   Version flow-file formats independently from mitmproxy versions.
-   Compatibility handling for older flow files.
-   Corrupted-flow handling.

## HAR support

-   Read/import HAR files through standard flow-loading APIs.
-   Write/export HAR files.
-   `save.har` command.
-   `hardump` option in mitmdump.
-   HAR support for failed CONNECT traffic.
-   HAR cookie/path handling.
-   HAR export of binary/non-text payloads.
-   HAR import with UTF-8 BOM handling.

## Export

-   Export requests as cURL commands.
-   Export requests as HTTPie commands.
-   Raw request export.
-   Raw response export.
-   Export captured flow data.
-   Clipboard copying in mitmweb.
-   Preserve request/response headers in raw exports.
-   External editor integration.

## Contentviews / inspectors

mitmproxy has accumulated a large set of content-aware views:

-   Raw content.
-   Hexadecimal content.
-   Raw hex streams.
-   JSON.
-   HTML.
-   XML.
-   JavaScript.
-   CSS.
-   URL-encoded forms.
-   Images.
-   WebP images.
-   AVIF images.
-   Favicons/icon images.
-   WBXML.
-   AMF.
-   MsgPack / MessagePack.
-   MQTT.
-   gRPC.
-   Protobuf.
-   DNS-over-HTTPS.
-   TCP stream contentviews.
-   Pretty-formatted views.
-   Syntax highlighting.
-   JSON syntax highlighting.
-   MsgPack syntax highlighting.
-   CSS syntax highlighting.
-   JavaScript syntax highlighting.
-   Character-set inference.
-   CSS `@charset` handling.
-   UTF-8 fallback for content types.
-   MIME-type detection.
-   gzip/deflate decoding.
-   raw-deflate (`deflateRaw`) support.
-   Brotli support.
-   zstd support.
-   Interactive/re-encodable contentviews.

## Interactive contentviews

Beginning with the redesigned contentview system in mitmproxy 12:

-   Contentviews can be **interactive**.
-   Pretty-printed content can be edited.
-   Edited content can be re-encoded back to the original binary format.
-   Protobuf can be displayed as editable YAML and serialized back to
    protobuf.
-   Unknown protobuf messages can be inspected.
-   Existing proto definitions can be applied.
-   MsgPack view is interactive.
-   Contentviews use tree-sitter-based syntax highlighting.
-   Contentviews can be written in Python.
-   Contentviews can be written in **Rust** for performance and access
    to the Rust ecosystem.

## gRPC / Protobuf

-   gRPC/protobuf contentview.
-   Apply custom protobuf definitions.
-   Decode fields according to custom definitions.
-   Inspect protobuf without known definitions in newer releases.
-   Editable protobuf/YAML representation.
-   Re-serialize edited protobuf.
-   gRPC traffic over HTTP/2.
-   Example addons for selected gRPC traffic.

## Compression and encodings

-   gzip.
-   deflate.
-   raw deflate.
-   Brotli.
-   zstd.
-   Automatic pretty-view decoding.
-   Manual encode/decode shortcut.
-   Content-encoding inference.
-   Fast compression settings for addon performance.
-   Multi-frame zstd decompression.

## mitmproxy terminal UI

-   Interactive terminal user interface.
-   Flow list.
-   Flow detail views.
-   Multi-pane layouts.
-   Table UI.
-   Sort flows by size/type/URL.
-   Search.
-   Filters.
-   Intercept/edit flows.
-   Command bar.
-   Typed commands.
-   Command history.
-   Terminal-style editing shortcuts.
-   Configurable keybindings (`keys.yaml`).
-   Quick Help bar.
-   Hide Quick Help.
-   Event log.
-   Flow markers.
-   Flow comments.
-   Color palettes for light/dark terminals.
-   Follow mode/tail new flows.
-   Transfer-rate display.
-   External-editor integration.
-   Hex editor.
-   Contentviews.
-   Query-parameter display.
-   Configurable trailing-newline handling.

## mitmweb

-   Browser-based GUI.
-   Flow table/list.
-   Flow detail panels.
-   Request/response inspection.
-   Flow filtering.
-   Body-content filtering.
-   Options editor.
-   Static web-based flow viewer.
-   Capture tab.
-   Optional index column.
-   Timing tab.
-   Local-timezone display.
-   Copy request/response data to clipboard.
-   Color-coded HTTP status codes.
-   Syntax-highlighted content.
-   WebSocket message display.
-   Authentication token.
-   Optional password protection.
-   Web token via Bearer authentication.
-   XSRF protection.
-   Dark theme (current unreleased history).
-   System/light/dark theme selection.
-   Status-code and method badges.
-   Indicator tooltips.
-   Reverse flow ordering.
-   Selected-flow count.
-   Large flow-file support.
-   Run mitmweb below a URL subdirectory.
-   IPv6 web-host support.
-   Browser cache-busting for frontend assets.
-   Vite-based frontend build in modern releases.

## mitmdump

-   Headless/non-interactive capture.
-   Scripting/addon execution.
-   Flow file read/write.
-   Server replay.
-   Client replay.
-   HAR dumping.
-   Event logging.
-   Filter messages.
-   Content-view output cutoff.
-   Failed CONNECT display.
-   Designed for automation and command-line workflows.

## Python addon scripting

Scripting is a core capability, not an add-on feature bolted onto the
UI.

-   Load Python addons/scripts.
-   Multiple scripts.
-   Script arguments.
-   Hot script reloading.
-   Event-driven hooks.
-   HTTP request/response hooks.
-   TCP hooks.
-   UDP hooks.
-   DNS hooks.
-   WebSocket hooks.
-   TLS ClientHello hook.
-   TLS established/failed client/server hooks.
-   HTTP CONNECT hooks.
-   Async event hooks.
-   Addon-defined commands.
-   Addon-defined typed options.
-   Thread-safe logging.
-   Standard Python `logging`.
-   Access and modify flows.
-   Create requests programmatically.
-   Create responses programmatically.
-   Intercept/kill/ignore flows.
-   DNS spoofing.
-   Traffic modification.
-   Custom contentviews.
-   Embedded application support.
-   Addon examples/documentation.

## Typed commands and options

-   Typed command mechanism.
-   Addons expose commands.
-   Typed settings/options store.
-   Console command integration.
-   mitmweb options editor.
-   Sequence options from CLI.
-   Editable addon options.
-   Modern web support for addon option types.
-   Configuration through YAML.

## Embedded web applications

-   WSGI application hosting inside the proxy.
-   ASGI application support.
-   Host embedded applications on proxy hostnames.
-   Listen on all ports for a particular hostname.
-   Serve both HTTP and HTTPS through embedded apps.

## Browser launching

-   Browser addon.
-   Launch isolated proxy-configured Chrome.
-   Launch isolated proxy-configured Firefox.
-   Current unreleased history adds isolated proxy-configured
    **Microsoft Edge** launching.

## Configuration

-   YAML configuration.
-   `config.yaml` / `config.yml`.
-   Configuration directory.
-   Command-line options.
-   Typed option system.
-   Addon options.
-   Environment-variable controls.
-   User-defined keybindings.
-   External editor configuration.
-   Multiple proxy-mode server specifications.

## Mobile/device interception

-   Android certificate-installation documentation.
-   Android production-build interception documentation using Magisk.
-   iOS/transparent-mode compatibility improvements.
-   WireGuard mode can simplify interception of remote/mobile devices.
-   Certificate installation through `mitm.it`.

## Platform support

-   Linux.
-   macOS.
-   Windows.
-   Historical transparent-mode support for OpenBSD and FreeBSD.
-   ARM/Raspberry Pi Docker images.
-   macOS ARM64 builds.
-   Docker images.
-   Docker Hub publishing.
-   GitHub Container Registry publishing.
-   Homebrew support.
-   Precompiled binaries/app bundles.
-   Windows installer historically.
-   Modern Python package installation.

## Performance architecture

-   Asyncio-based core.
-   Modern **sans-I/O** proxy architecture separating protocol logic
    from I/O.
-   Rust networking components (`mitmproxy_rs`).
-   Reuse OpenSSL contexts.
-   Improved memory use.
-   Improved large-flow-file loading.
-   Improved HTTP-body processing complexity.
-   Reduced mitmweb flow-table subscription overhead.
-   Fast compression settings.
-   Connection timeout/cleanup.
-   Modern async event hooks.

------------------------------------------------------------------------

# Bugs, limitations, and failure modes recorded as fixed

A historical FIX entry means mitmproxy experienced the issue at some
point. It does **not** imply the current version still has it.

## HTTP request/response handling

-   Requests were not retried correctly after timeout.
-   Requests without bodies broke under HTTP/2 in some cases.
-   Multiple `Content-Length` headers were handled incorrectly.
-   Uppercase URI schemes such as `HTTP://` could produce 400 errors.
-   Chunked bodies could be lost instead of retained.
-   Multipart forms could lose boundary values after editing.
-   Multipart HTTP request editing failed in the CLI.
-   Query parameters were incorrect in ASGI applications.
-   ASGI apps sometimes did not receive request bodies.
-   Successive redirected requests could be sent to the wrong host.
-   Host headers could be missing during replay.
-   Host headers with nonstandard ports were wrong in reverse-proxy
    mode.
-   Empty cookie attributes could be rewritten incorrectly.
-   HTTP reason phrases could be wrong.
-   Modified reason phrases in mitmweb were not sent.
-   Uppercase content encodings were not decoded correctly.
-   Raw exports could incorrectly remove or alter headers.
-   HEAD response export could incorrectly zero Content-Length.
-   cURL export could omit Content-Length.
-   cURL/HTTPie exports could alter escaped data.
-   Invalid HTTP transfer encodings were rejected even when validation
    was disabled.
-   Pipelined HTTP flows could crash the proxy.

## HTTP/2

-   Header normalization problems.
-   Replay failures for HTTP/2 flows.
-   Missing Host header during HTTP/2 replay.
-   Broken requests without body.
-   WINDOW_UPDATE frames swallowed on closed streams.
-   Stream reset handling problems.
-   Informational responses caused errors.
-   Multiple Cookie headers were mishandled when translating HTTP/2 →
    HTTP/1.
-   Empty HTTP/2 DATA frames were sent incorrectly.
-   HTTP reason values were incorrect.
-   Request-smuggling vulnerability in HTTP/2 → HTTP/1 translation.
-   HTTP/2 implementation errors historically caused support to be
    disabled by default before later maturation.

## HTTP/3 / QUIC

-   QUIC connections could fail to start when `allow_hosts` or
    `ignore_hosts` was configured.
-   Fragmented QUIC ClientHello messages could yield incorrect/missing
    SNI.
-   Early HTTP/3/QUIC support required compatibility fixes.
-   DNS/network-discovery issues could prevent QUIC/local networking
    features from starting.

## WebSockets

-   mitmweb WebSocket connection could fail after frontend changes.
-   WebSocket message view jumped to the top whenever a new message
    arrived.
-   WebSocket interception performance needed improvement.
-   Message direction indicator could point the wrong way.
-   Incoming WebSocket traffic could steal focus from the console
    command panel.
-   WebSocket/event display had UI/state defects.

## TCP/UDP

-   UDP remote-disconnect errors were mishandled.
-   TCP timeout was historically hardcoded.
-   Server-side greeting protocols were problematic before the new proxy
    core.
-   Raw TCP/protocol detection could mistake non-HTTP protocols for
    HTTP.
-   SSH connections could be incorrectly handled as HTTP.
-   TCP flow/contentview integration required fixes.

## DNS

-   Duplicate DNS answers could be returned.
-   DNS server discovery failure could crash startup.
-   Compressed DNS resource-record names were parsed incorrectly.
-   WireGuard/DNS endpoint determination could be wrong on multi-NIC
    systems.
-   DNS resolution timing prevented user addons from acting before
    resolution.
-   DNS-over-TCP and uncommon query types were unsupported until added.

## TLS / certificates

-   SSL interception had historical stability problems.
-   Generated certificates could expire in the distant past because of
    an OpenSSL overflow.
-   Root and leaf certificates sharing identifiers confused some
    clients.
-   Windows Schannel clients rejected generated certificate chains.
-   Strict OpenSSL 3.x clients rejected generated certificates.
-   Intermediate custom CAs could break leaf certificate generation.
-   Punycode domains could break certificate generation.
-   Cyrillic-domain certificate support required fixes.
-   Custom CA configurations could raise errors.
-   Custom certificates could be ignored in reverse-proxy mode.
-   Certificate issuer/subject parsing failed on escaped special
    characters.
-   OpenSSL CN requirements caused root/leaf conflicts.
-   Certificate generation had SAN/CN edge cases.
-   Invalid certificates could crash mitmproxy.
-   macOS TLS negotiation could hang/freeze.
-   Certificate files stored on disk caused race conditions and Windows
    filename problems before being moved in-memory.
-   Custom CA SubjectKeyIdentifier values could cause authority/subject
    key identifier mismatch errors.
-   Browser certificate lifetime restrictions required repeated
    reductions in generated leaf-certificate validity.
-   Chromium's certificate lifetime limits required another adjustment
    in 2026.
-   TLS protocol versions rejected by OpenSSL could crash capability
    checks.
-   TLS version mismatches historically produced poor error messages.

## Proxy modes / routing

-   Transparent mode could connect back to itself.
-   Transparent mode failed on IPv6-enabled macOS systems.
-   IPv6 transparent-mode issues existed on Windows/macOS.
-   `allow_hosts` behaved incorrectly.
-   `ignore_hosts` could terminate requests instead of forwarding them.
-   `allow_hosts` / `ignore_hosts` broke with IPv6.
-   Connections could be incorrectly ignored with `allow_hosts`.
-   Plaintext `ignore_hosts` matching did not account for HTTP Host
    headers.
-   Outgoing connections could bind to the wrong network interface.
-   IPv4 fallback failed when IPv6 was unavailable.
-   IPv6 CONNECT authority parsing was incorrect.
-   IPv6 upstream proxy authorities could be malformed.
-   IDNA hostnames were encoded incorrectly in upstream mode.
-   Reverse-proxy Host headers could be wrong.
-   Local capture mode had multiple platform-specific bugs.
-   macOS local mode failed to start in some releases.
-   WireGuard generated an incorrect endpoint with multiple NICs.
-   Multiple server modes could conflict through global listen-port
    handling.

## Replay

-   Server replay could corrupt Date headers.
-   Replay timestamps could be wrong.
-   Server replay had matching/behavior bugs.
-   HTTP/2 replay failed.
-   Request replay failed when proxy bound to a local interface.
-   mitmdump could exit prematurely in server-replay mode.
-   Missing replayable responses required explicit behavior controls.
-   Replay flow matching needed finer-grained controls.
-   Replayed requests could miss Host headers.

## Flow serialization and files

-   Saved flows failed to load in mitmweb.
-   Flow serialization broke in mitmweb.
-   Older flow files could fail to import.
-   Corrupted flow dumps could crash mitmproxy.
-   Flow file format was historically coupled too tightly to application
    version.
-   Flow-file writing did not flush after each flow, limiting downstream
    processing.
-   Large flow files exceeded mitmweb limits.
-   File compatibility checks were overly strict.
-   Flow imports from old releases failed.

## HAR

-   Empty response content could produce corrupt HAR files.
-   Missing cookie `path` attributes broke HAR creation.
-   Failed CONNECT URLs were saved incorrectly.
-   UTF-8 BOMs in HAR files caused loading issues.
-   Void responses converted incorrectly to HAR.
-   Corrupted compressed bodies could crash HAR export.
-   Binary payload classification could throw `IndexError` during HAR
    export.
-   HEAD response exports could alter Content-Length incorrectly.

## Export

-   Flow export could crash.
-   cURL export could crash.
-   cURL/HTTPie export had command-injection vulnerabilities.
-   Escaped characters in exported cURL/HTTPie commands could change the
    transmitted data.
-   UTF-8 surrogate characters could crash export.
-   Raw exports removed headers incorrectly.
-   Raw response export mishandled Content-Length.
-   cURL export omitted Content-Length.

## Contentviews / decoding

-   JPEG parsing problems.
-   zstd decompression failed across frames.
-   Brotli decoding could crash.
-   Uppercase encoding names were not decoded.
-   Raw binary payloads produced decode/JSON errors.
-   Content-encoding inference failed for some types.
-   Chinese/non-ASCII content could be incorrectly classified as binary.
-   JSON view escaped non-ASCII unnecessarily.
-   JSON key sorting changed content presentation unexpectedly.
-   XML beginning with CRLF could select the wrong contentview.
-   AVIF and Microsoft-icon images failed to render.
-   Multiple blank lines rendered incorrectly.
-   MsgPack/contentview hot reload problems.
-   External viewer files could be deleted before the viewer finished
    opening them.
-   Hex editor dependency caused macOS issues.

## mitmproxy terminal UI

-   Keyboard control could be lost after launching an external editor.
-   Quotes could break the console command prompt.
-   Home/End keys could crash some screens.
-   Clearing the event log could crash.
-   Flow focus next/previous could crash.
-   Duplicating flows could crash.
-   Reverting duplicated flows could crash.
-   Regex escape characters in half-open strings could crash the
    console.
-   Command history could fail on file I/O errors.
-   Help overlay could fail to appear.
-   Command panel focus could be stolen by incoming traffic.
-   Palette settings failed.
-   Quit messages appeared in inappropriate states.
-   Negative durations could crash the UI.
-   External-editor and command parsing had multiple edge cases.

## mitmweb

-   Blank page on Windows.
-   Blank detail page when server address was missing.
-   Options page could crash.
-   Flow list race condition.
-   Flow list did not remain at bottom.
-   Splitter could accidentally behave as drag-and-drop.
-   WebSocket connection failed in a regression.
-   Root-relative URLs broke when hosted in a subdirectory.
-   Concurrent mitmweb instances overwrote each other's auth cookies.
-   Auth cookie incorrectly always used default web port.
-   XSRF cookie was not picked up.
-   Editors could not clear content to an empty string.
-   Flow-table subscriptions scaled O(rows), hurting performance.
-   Filter input could lose partially typed text on unrelated rerenders.
-   Flow table/event log could enter infinite update cycles.
-   Sorted-column header was unreadable in dark theme.
-   Copying on macOS failed.
-   CSP compatibility broke with newer Chrome.
-   Event-log display issues.
-   Flow detail/edit controls failed in some releases.
-   Frontend caching could leave stale application code.

## mitmdump

-   `mitmdump -nr` failed to exit automatically.
-   `-n` exit behavior regressed.
-   Server replay could terminate mitmdump prematurely.
-   Failed CONNECT requests were not displayed.
-   Log-write failures could create an endless loop after a parent
    process exited.

## Scripts / addons

-   User-script exceptions were handled poorly.
-   Running scripts on a flow twice could traceback.
-   Imports matching hook names caused problems.
-   Script hot reloading was unreliable.
-   Relative script paths resolved against the working directory instead
    of configuration location.
-   Addon logging was not thread-safe before fixes.
-   Addon options were omitted from `--options` output.
-   Async/event-loop leaks occurred in testing.
-   Body modification crashed when replacement strings contained
    backslash sequences.
-   Setting message content could be unnecessarily expensive before
    faster compression settings.
-   Deprecated APIs generated warnings or required migration.

## Authentication

-   Proxy authentication had connection-reuse regressions.
-   SOCKS authentication support was absent before addition.
-   LDAP backend functionality required fixes/extensions.
-   Basic-auth passwords containing colons were rejected before the
    current unreleased fix.
-   mitmweb auth cookies conflicted across concurrent instances.
-   mitmweb authentication originally exposed a security issue through
    proxy-assisted SSRF.

## IPv6

-   IPv6-only environments were unsupported/broken in some earlier
    releases.
-   IPv6 transparent proxy issues.
-   IPv6 authority parsing in CONNECT.
-   IPv6 formatting errors.
-   IPv6 upstream proxy formatting.
-   IPv6 `allow_hosts`/`ignore_hosts` regression.
-   IPv6 scope suffix issues.
-   IPv4 fallback when IPv6 unavailable.
-   IPv6 web-host URL formatting.
-   IPv4-mapped IPv6 remote-client blocking.

## Performance and resource use

-   SSL connections could exhaust resources.
-   Large tnetstrings caused slow parsing.
-   Large HTTP bodies had non-linear processing-time growth.
-   Large flow-file loading was historically slow.
-   Flow-table UI subscriptions scaled poorly.
-   OpenSSL contexts consumed excess resources before reuse.
-   Compression settings slowed addon content assignment.
-   WebSocket interception needed performance improvement.
-   Socket-processing limits on Windows were improved by event-loop
    changes.
-   Certificate-on-disk handling caused races/resource issues.

## Crashes and stability

Historical crash/freeze fixes include:

-   Invalid certificates.
-   Corrupted flow dumps.
-   Flow export.
-   cURL export.
-   Brotli content.
-   Flow duplication/revert.
-   Event-log clearing.
-   Console regex input.
-   Option editing.
-   Home/End key handling.
-   Pipelined HTTP flows.
-   DNS server discovery on startup.
-   mitmweb missing server connection.
-   macOS TLS negotiation freeze.
-   Raw message-body editing.
-   Corrupt compressed HAR content.
-   Unsupported TLS version checks.
-   Various startup/platform-specific failures.

## Security issues

The release history explicitly records a number of security fixes.

### Web/API exposure

-   **CVE-2018-14505** --- mitmweb DNS rebinding vulnerability.
-   **CVE-2025-23217** --- mitmweb API authentication issue: localhost
    restriction could be bypassed by tunneling requests through the
    proxy in an SSRF-style attack. Authentication token became required
    by default.
-   Optional mitmweb password protection was added.

### Request smuggling / HTTP parsing

-   **CVE-2022-24766** --- request-smuggling vulnerability.
-   **GHSA-847f-9342-265h** --- request-smuggling vulnerability
    affecting HTTP/2 → HTTP/1 translation through hyper-h2.

### Command injection / unsafe output

-   Command-injection vulnerabilities when exporting flows as
    cURL/HTTPie commands.
-   Unsanitized user input echoed in HTTP error responses.

### Authentication / LDAP

-   **GHSA-527g-3w9m-29hv** --- LDAP injection vulnerability fixed in
    2026.

### Dependencies / TLS libraries

-   OpenSSL updates for **CVE-2022-3602** and **CVE-2022-3786**.
-   Repeated cryptography/OpenSSL/dependency updates.
-   Migration away from unmaintained `passlib` htpasswd parsing.

------------------------------------------------------------------------

# Major architectural changes visible in the history

## Addon architecture

Mitmproxy progressively moved substantial internal functionality into
addons. This made its own functionality use the same extension
mechanisms available to users.

## Asyncio

Mitmproxy 4 moved the core to asyncio, improving performance and
concurrency.

## New proxy core / sans-I/O architecture

Mitmproxy 7 introduced a substantially rewritten proxy core:

-   Protocol logic separated from I/O.
-   Better testing.
-   Reduced race conditions.
-   Better protocol-layer isolation.
-   HTTP/1 ↔ HTTP/2 interoperability.
-   Server-side-greeting protocols.
-   Better timeout behavior.
-   Lazy/eager connection strategies.
-   Host-header-based routing.

## Rust networking core

Modern versions increasingly use `mitmproxy_rs` for
performance-sensitive networking/protocol functions, including UDP and
platform capture functionality.

## Interactive contentview architecture

Mitmproxy 12 redesigned contentviews so that decoded/pretty data can be
edited and encoded back to the wire format, including protobuf and
MsgPack. Contentviews can also be implemented in Rust.

------------------------------------------------------------------------

# Capabilities particularly relevant to a programmable traffic recorder/replayer

For comparison with a custom proxy or Fiddler-style workflow, the
release history confirms mitmproxy supports:

-   HTTP/1.x capture.
-   HTTP/2 capture.
-   HTTP/3 / QUIC.
-   HTTPS MITM.
-   WebSockets.
-   Raw TCP.
-   Raw UDP.
-   DNS.
-   DTLS.
-   Transparent proxying.
-   Local application capture.
-   Reverse proxying.
-   Upstream proxying.
-   SOCKS.
-   WireGuard.
-   Python scripting.
-   Async event hooks.
-   Request/response modification.
-   Header/body modification.
-   URL/host remapping.
-   Map Local.
-   Client replay.
-   Server replay.
-   Concurrent replay.
-   Save/load native flow files.
-   HAR import/export.
-   cURL/HTTPie export.
-   Programmable filters.
-   Custom contentviews.
-   Protobuf/gRPC decoding and editing.
-   Embedded ASGI/WSGI applications.
-   Headless operation through mitmdump.

This is substantially more programmable than a UI-first proxy because
Python addons participate directly in the proxy's event lifecycle.

------------------------------------------------------------------------

# Historical timeline highlights

Rather than reproducing every release number, these are the major
capability eras visible in the changelog:

-   **2011 --- 0.5:** early proxy operation and headless/no-bind
    behavior.
-   **2013 --- 0.9:** transparent proxying, SNI, server replay, rich
    contentviews, filters, WSGI apps, proxy authentication.
-   **2014 --- 0.10:** multiple scripts, upstream proxying, SSL reverse
    proxy, request/response search.
-   **2016 --- 1.0:** mitmweb, Windows support, mature WebSockets,
    improved HTTP/2, gzip/deflate, event logging.
-   **2017 --- 2.0:** HTTP/2 enabled by default.
-   **2018 --- 3.x/4.x:** typed commands/options, major addon
    architecture, request streaming, asyncio core, configurable
    keybindings.
-   **2019--2020 --- 5.x:** TLS 1.3, table UI, zstd, IPv6 improvements,
    MsgPack, ASGI, TCP flow UI.
-   **2020 --- 6.x:** TLS 1.3 key logging and further replay/proxy
    improvements.
-   **2021 --- 7.x:** completely new sans-I/O proxy core, HTTP/1↔HTTP/2
    interoperability, raw TCP/server greetings, secure upstream
    proxying.
-   **2022 --- 8.x/9.x:** async hooks mature, gRPC/protobuf view, raw
    UDP, WireGuard, DTLS, multiple proxy servers.
-   **2023 --- 10.x:** experimental HTTP/3/QUIC, HAR read/write, Local
    Redirect mode, DNS-over-HTTPS and raw hex views.
-   **2024 --- 11.x:** transparent HTTP/3, DNS over TCP, Capture UI
    improvements, Local Capture expansion.
-   **2025 --- 12.x:** interactive/re-encodable contentviews, editable
    Protobuf/MsgPack, Rust contentviews, TLS 1.3 post-handshake auth,
    streamed-body storage, body filtering.
-   **2026:** certificate/security fixes and continued
    mitmweb/proxy-core refinement.
-   **Current unreleased history:** Edge browser launcher, mitmweb dark
    theme, UI improvements, QUIC fixes, Basic-auth password improvement,
    and additional proxy/core fixes.

------------------------------------------------------------------------

# Differences in emphasis compared with Fiddler Everywhere

The release histories suggest somewhat different product philosophies.

## mitmproxy emphasizes

-   Programmability through Python addons.
-   Headless automation with mitmdump.
-   Protocol-level extensibility.
-   Transparent/local/WireGuard capture modes.
-   Raw TCP and UDP.
-   DNS proxying.
-   HTTP/3 and QUIC.
-   Client **and server** replay.
-   Embedded ASGI/WSGI applications.
-   Custom contentviews.
-   Direct manipulation of flows from code.
-   Open-source extensibility.

## Fiddler Everywhere emphasizes

-   GUI-driven traffic investigation.
-   Interactive inspectors and workflow.
-   Composer.
-   Rules Builder.
-   Team sharing/collaboration.
-   Integrated breakpoint workflows.
-   Enterprise administration.
-   Newer AI/MCP/Agent debugging features.

There is significant overlap, but mitmproxy's release history
consistently exposes lower-level programmable networking capabilities.

------------------------------------------------------------------------

# Notes on interpretation

-   A feature appearing in a historical release entry establishes that
    mitmproxy added that capability at that point. Some features were
    later redesigned, deprecated, or removed.
-   A historical fix establishes that the issue existed at some point.
    It does **not** imply current mitmproxy still has that defect.
-   Some old features (notably `pathod`, `pathoc`, HTTP/2 Push Promises,
    and older addon APIs) were later deprecated or removed.
-   This report intentionally consolidates repetitive dependency
    updates, documentation fixes, build/CI changes, and minor typo
    fixes.
-   The current **Unreleased** section is included only where it reveals
    useful forthcoming/current-main capabilities or defects; it is not a
    released version.
-   mitmproxy's separate **API Changelog** contains breaking addon-API
    changes. Those are relevant to addon authors but are not duplicated
    exhaustively here.
-   The report is designed to answer: **"What has mitmproxy been capable
    of, and what kinds of problems has it historically encountered?"**

------------------------------------------------------------------------

# Sources

Primary source:

mitmproxy --- `CHANGELOG.md` / Release History\
https://github.com/mitmproxy/mitmproxy/blob/main/CHANGELOG.md

Addon API breaking-change reference:

https://github.com/mitmproxy/mitmproxy/blob/main/docs/src/content/addons/api-changelog.md

Compiled October 2026.
