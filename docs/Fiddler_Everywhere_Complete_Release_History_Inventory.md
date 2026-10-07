# Fiddler Everywhere --- Consolidated Release-History Inventory

**Scope:** Official Progress/Telerik Fiddler Everywhere release history,
from **v0.1.0 (November 8, 2018)** through **v8.2.0 (September 29,
2026)**.

**Purpose:** This is not primarily a version-by-version changelog. It
combines the release history into two inventories:

1.  **Features and capabilities added to Fiddler Everywhere**
2.  **Bugs, limitations, failures, and security issues that appeared in
    the release history as fixed**

Items that appeared repeatedly in multiple releases are generally
consolidated, but concrete failure modes are retained where they are
useful. Routine wording such as repeated third-party dependency updates
is consolidated rather than duplicated dozens of times.

Official history:
https://www.telerik.com/support/whats-new/fiddler-everywhere/release-history

------------------------------------------------------------------------

# Features and capabilities

## Traffic capture and proxying

-   Capture HTTP traffic.
-   Capture and decrypt HTTPS traffic through Fiddler's local proxy and
    trusted Fiddler CA.
-   HTTP/2 traffic support.
-   WebSocket traffic capture and inspection.
-   Server-Sent Events (SSE) / streaming-response support.
-   gRPC traffic capture and inspection.
-   Protobuf decoding for gRPC.
-   gRPC server-reflection support.
-   MessagePack inspection/decoding.
-   Capture localhost traffic.
-   Capture traffic from browsers and other applications that use the
    configured proxy.
-   Capture Node.js traffic.
-   Capture WebView2 traffic.
-   Terminal-oriented/instrumented traffic capture workflows.
-   **Windows Network Capture mode**, allowing traffic capture beyond
    applications that explicitly use the Fiddler proxy.
-   **Reverse proxy** support.
-   Support for self-generated HTTP/2 CONNECT tunnels.
-   Capture and display CONNECT sessions.
-   Support for streaming response bodies while the response is still in
    progress.
-   Automatic detection/handling of common traffic encodings and
    compression.
-   Controls for enabling/disabling HTTPS capture.
-   Controls for whether the Fiddler CA is trusted.
-   Ability to use a **custom certificate authority certificate**.
-   Ability to place the Fiddler CA in the **macOS system keychain**.
-   Windows machine certificate-store integration.
-   Certificate-management and certificate-trust workflows.
-   System proxy configuration and restoration.
-   Upstream proxy support.
-   PAC / WPAD proxy configuration support.
-   Global proxy policy support.
-   VPN-aware capture and proxy bypass behavior.
-   VPN bypass-list configuration.
-   Automatic Windows authentication support, including Kerberos/NTLM
    scenarios.
-   Support for authenticated upstream proxies.
-   Application/process information associated with captured traffic
    where available.

## Live Traffic session management

-   Live Traffic session grid.
-   View request URL, method, status, protocol, host/domain and other
    session information.
-   Search captured traffic.
-   Search by URL.
-   Search request/response headers and other session content.
-   Filter captured sessions.
-   Save and reuse filters.
-   Sorting.
-   Multi-session selection.
-   Select related sessions.
-   Group sessions by **domain/application**.
-   Compact grid/view options.
-   Mark sessions.
-   Add comments/notes to sessions.
-   Copy session information.
-   Copy header values.
-   Copy **header name only**.
-   Context-menu actions for sessions.
-   Session status indicators.
-   Breakpoint status/count indicators.
-   Preserve portions of grid/application state between sessions.
-   Keyboard shortcuts for common traffic operations.
-   Improved large-session selection and management over time.

## Inspectors

-   Request inspectors.
-   Response inspectors.
-   Automatic/best-inspector selection based on content.
-   Headers inspector.
-   Query parameters inspector.
-   Cookies inspector.
-   Forms inspector.
-   Text inspector.
-   JSON inspector.
-   XML inspector.
-   HTML inspector.
-   Image inspector.
-   Raw/body inspection.
-   HEX/binary inspector.
-   SSE inspector.
-   gRPC inspector.
-   Protobuf inspection.
-   MessagePack inspection.
-   Streaming body inspection.
-   Search within inspector content.
-   Copy values from inspectors.
-   Decode encoded values.
-   Decode-value utility/tool.
-   Display compressed/encoded content after decoding where supported.
-   View request and response sizes.
-   View detailed timing information.
-   Overview panel summarizing a session.
-   Detailed **Timings** view.
-   Detailed **Sizes** view.
-   Copy timing/header information for diagnostics.

## Composer

-   HTTP Request Composer.
-   Build and send arbitrary HTTP/HTTPS requests.
-   Create a request from captured traffic.
-   Edit and resend captured requests.
-   Multiple Composer tabs.
-   Composer collections.
-   Save requests into collections.
-   Organize and rename Composer collections.
-   Key/value editing for headers.
-   Key/value editing for query parameters.
-   Raw request/body editing.
-   Support for common HTTP methods.
-   **QUERY HTTP method** support.
-   Request-header autocomplete/assistance.
-   Reissue requests for testing without using the original client.

## Replay

-   Replay captured sessions.
-   Replay an individual session.
-   Replay multiple selected sessions.
-   Sequential replay.
-   Parallel/concurrent replay.
-   Advanced replay workflows.
-   Reissue traffic after modification.
-   Use captured requests as a basis for Composer requests.
-   Replay-oriented testing without reproducing the original
    browser/application interaction.

## Rules / AutoResponder / traffic modification

-   AutoResponder / Rules functionality.
-   Rules Builder.
-   Match captured requests using rule conditions.
-   Modify requests.
-   Modify responses.
-   Return locally generated responses.
-   Redirect requests.
-   Map requests to other destinations.
-   Match using URL and other request characteristics.
-   Regular-expression rule matching.
-   Request-time rules.
-   Response-time rules.
-   Ordered rule/action execution.
-   Multiple actions within a rule.
-   Rule validation.
-   Rule editing assistance/autocomplete.
-   Rule suggestions.
-   Rule-set management.
-   Save rules.
-   Share rules.
-   Import/use shared rule sets.
-   Allow unmatched requests to continue to their original destination.
-   Rule support for CONNECT traffic.
-   Rule support for gRPC traffic/headers.
-   **Host remapping** (v8.2.0).
-   **Scripting** (v8.2.0), allowing more programmable traffic handling
    than declarative rules alone.

## Breakpoints

-   Set request breakpoints.
-   Set response breakpoints.
-   Pause traffic before it is sent upstream.
-   Pause traffic before a response is returned to the client.
-   Inspect paused requests/responses.
-   Modify paused requests/responses.
-   Resume traffic after inspection/modification.
-   Track active breakpoints.
-   Display breakpoint counts/state in the UI.

## Saving, snapshots, persistence and export

-   Save captured sessions.
-   Saved Sessions / Snapshots workflows.
-   Add selected Live Traffic sessions to saved snapshots.
-   Reopen saved traffic later.
-   Rename saved items inline.
-   AutoSave functionality.
-   Periodically persist Live Traffic.
-   AutoSave quota/limit handling.
-   Save response resources/body content to files.
-   Import traffic.
-   Export traffic.
-   HAR-oriented import/export workflows.
-   SAZ-oriented compatibility/workflows.
-   Preserve useful session metadata during save/export.
-   Maintain application/session state between launches where
    appropriate.
-   Import settings from Fiddler Classic.
-   Import Fiddler Classic filters.
-   Import Fiddler Classic rules/AutoResponder-related configuration.
-   Migration/import wizard for Fiddler Classic users.

## Collaboration and sharing

-   Share saved sessions/snapshots.
-   Share captured diagnostic traffic with other users.
-   Share Composer collections.
-   Share rules/rule sets.
-   Collaboration around saved traffic.
-   Synchronize/update shared-session state.
-   Account-based workflows.
-   Team/enterprise licensing and administration capabilities.

## Authentication, certificates, proxy and enterprise networking

-   Fiddler-generated root CA.
-   HTTPS interception using generated certificates.
-   Certificate trust/untrust controls.
-   Custom CA support.
-   macOS keychain integration.
-   Windows certificate-store integration.
-   Automatic Windows authentication.
-   Kerberos/NTLM-related authentication handling.
-   Upstream proxy authentication.
-   PAC configuration.
-   WPAD configuration.
-   Global proxy policy handling.
-   Corporate/VPN proxy compatibility improvements.
-   VPN bypass lists.
-   Managed capture/proxy settings for enterprise deployments.
-   Managed macOS capture settings.
-   Enterprise configuration/policy controls.

## Platform support and integration

-   Windows support.
-   macOS support.
-   Linux support.
-   Linux AppImage packaging/integration.
-   Platform-specific proxy and certificate handling.
-   OS-level capture integration.
-   Windows Network Capture.
-   macOS system keychain support.
-   OS proxy restoration/configuration.
-   Application update mechanism.
-   Application restart/update workflows.
-   Browser-oriented capture workflows.
-   Node.js capture.
-   WebView2 capture.

## User interface and productivity

-   Multi-tab interface.
-   Context menus.
-   Keyboard shortcuts.
-   Zoom controls.
-   Dark-theme/UI refinements.
-   First-run/tutorial flows.
-   Notifications.
-   Status indicators.
-   Search and autocomplete throughout relevant editors.
-   Session grouping.
-   Compact views.
-   Improved grid behavior.
-   Window-state persistence.
-   Saved workspace/application state.
-   Inline rename.
-   Improved dialogs and editors.
-   Copy-oriented inspector shortcuts.

## AI, MCP and agent-oriented capabilities

These are among the newest capability areas in Fiddler Everywhere.

-   **Debugging Assistant**.
-   AI-assisted interpretation/debugging of captured traffic.
-   Ability to use supported/user-selected LLMs for debugging
    assistance.
-   **MCP server** support.
-   Expose Fiddler traffic/debugging capabilities to MCP-compatible AI
    clients and development tools.
-   One-click/setup-oriented MCP integration with supported IDEs/agents.
-   MCP API keys.
-   MCP-scoped traffic capture.
-   MCP session-detail retrieval.
-   Traffic sanitization for AI/MCP workflows.
-   Enterprise controls governing MCP and AI-assistant functionality.
-   **Agent Calls / Agent Inspector**.
-   Inspect AI-agent requests and responses.
-   View agent messages.
-   View tool calls.
-   View model information.
-   View latency information.
-   View token/cost-related information where available.
-   **Agent Cache**.
-   Capture real agent request/response pairs.
-   Selectively cache agent responses.
-   Replay cached agent responses.
-   Agent caching recommendations.
-   Agent-oriented debugging without repeatedly invoking the upstream
    model/service.

------------------------------------------------------------------------

# Bugs, limitations and failure modes recorded as fixed

This section intentionally preserves concrete historical failure modes.
A fixed historical bug does **not** imply that current Fiddler
Everywhere still has the problem.

## Capture failures and missing traffic

-   Localhost traffic was not captured correctly in some configurations.
-   HTTPS capture could appear enabled while not actually functioning
    correctly.
-   HTTPS capture enable/disable state could become inconsistent.
-   Traffic could fail to appear after proxy/capture configuration
    changes.
-   macOS network-extension capture could stop unexpectedly.
-   Node.js traffic capture failed in some cases.
-   gRPC traffic was not captured or interpreted correctly in some
    scenarios.
-   SSE/streaming traffic was not captured/displayed correctly in some
    cases.
-   ChatGPT-style streaming responses exposed capture/display defects.
-   WebSocket tunnels could close incorrectly or produce errors.
-   CONNECT traffic could fail when rules were involved.
-   Network Capture state could become inconsistent.
-   Reverse-proxy/capture configuration produced edge-case failures.
-   Some applications using unusual proxy/network stacks were not
    captured correctly.
-   Traffic could disappear when VPN or upstream-proxy settings
    conflicted with Fiddler.
-   Capturing could fail after network configuration changed while
    Fiddler was running.

## Proxy configuration

-   Fiddler could fail to configure the Windows system proxy.
-   Existing system proxy settings were not always restored correctly.
-   Startup could fail when `HTTP_PROXY` / `HTTPS_PROXY` environment
    variables were present.
-   Upstream proxy settings could be interpreted incorrectly.
-   PAC files caused compatibility issues.
-   WPAD caused compatibility issues.
-   Global proxy policies caused capture/configuration problems.
-   Proxy authentication produced failures.
-   HTTP 407 responses occurred in some corporate proxy environments.
-   Zscaler-related proxy scenarios produced 407/capture problems.
-   Proxy state could become inconsistent after crashes or abnormal
    shutdown.
-   Proxy settings could conflict with other applications modifying the
    system proxy.

## VPN compatibility

-   Cisco AnyConnect and other VPN configurations caused capture/bypass
    problems.
-   VPN bypass behavior was incorrect for some hosts.
-   Duplicate VPN bypass entries could be created.
-   Hosts resolving to multiple endpoints could create duplicate bypass
    entries.
-   VPN configuration changes could interfere with active capture.
-   Fiddler could route traffic incorrectly while a VPN was active.
-   VPN/proxy combinations produced connectivity failures.

## Authentication

-   Automatic Windows authentication did not work in some environments.
-   Kerberos/NTLM scenarios exposed authentication problems.
-   Authenticated upstream proxies could fail.
-   Proxy authentication could result in repeated 407 responses.
-   Authentication state could differ between captured applications and
    normal browsing.
-   Corporate authentication configurations caused connectivity
    failures.

## HTTPS certificates and trust

-   Browsers could continue to report privacy/certificate errors after
    the Fiddler CA was trusted.
-   Certificate errors could be logged even though the certificate was
    trusted.
-   Root-certificate installation/trust state could become inconsistent.
-   Certificate removal/untrust workflows could fail.
-   macOS certificate/keychain behavior produced platform-specific
    problems.
-   Windows certificate-store behavior produced configuration-specific
    problems.
-   Custom certificate authority configurations exposed edge cases.
-   HTTPS decryption could stop working after certificate configuration
    changes.
-   Certificate state in the UI did not always match actual OS trust
    state.

## HTTP/2

-   HTTP/2 traffic exposed capture and decoding problems.
-   HTTP/2 CONNECT handling had edge cases.
-   Rules did not always behave correctly with HTTP/2/CONNECT sessions.
-   HTTP/2 session metadata/display could be incorrect.
-   Certain HTTP/2 connections failed under proxy/network
    configurations.

## gRPC / Protobuf

-   gRPC reflection failed to resolve for some sessions.
-   Protobuf messages produced inspector warnings.
-   Protobuf content was not decoded correctly in some cases.
-   gRPC headers were not always available to Rules correctly.
-   Rules did not always apply to gRPC traffic.
-   Large gRPC payloads produced display/performance problems.
-   gRPC inspector selection could be wrong.
-   gRPC traffic could be displayed using an inappropriate inspector.
-   gRPC capture failed under some Node.js/client configurations.
-   Reflection-dependent message decoding failed for some services.

## Server-Sent Events and streaming responses

-   SSE traffic did not always select the SSE inspector.
-   SSE content could render incorrectly.
-   Streaming bodies caused the inspector view to jump while data
    arrived.
-   Streaming content caused UI refresh problems.
-   Streaming responses could be incomplete or missing.
-   Chat/LLM streaming traffic exposed response-body capture issues.
-   Large or long-running streaming sessions could affect
    responsiveness.

## WebSockets

-   WebSocket tunnel closing could generate errors.
-   WebSocket sessions could be displayed incorrectly.
-   WebSocket capture/inspection exposed connection-state edge cases.
-   WebSocket data could interact badly with filters/session state.

## MessagePack and binary traffic

-   MessagePack content was not decoded in some cases.
-   Binary payloads could select the wrong inspector.
-   HEX inspector search behavior was inconsistent.
-   HEX inspector selection/highlighting had defects.
-   Large binary bodies caused rendering/performance issues.

## Request and response bodies

-   POST request bodies could be missing.
-   Response bodies could be missing.
-   Bodies could be associated with the wrong session in edge cases.
-   Compressed bodies could fail to decode.
-   MCP response bodies could be compressed/not surfaced as expected.
-   Large bodies could freeze or significantly slow the UI.
-   Streaming bodies could update incorrectly.
-   Body inspectors could choose an inappropriate format.
-   Encoded content was not always decoded correctly.

## Inspectors

-   The automatically selected "best" inspector was sometimes wrong.
-   HEX inspector could be missing when needed.
-   SSE inspector could be missing/not selected.
-   gRPC inspector could fail to appear.
-   JSON rendering had formatting/display defects.
-   JSON could be interpreted incorrectly in table-like views.
-   Text/raw views had selection/search problems.
-   Inspector state could change unexpectedly when switching sessions.
-   Copy actions could return incorrect/incomplete data.
-   Search highlighting/selection could behave inconsistently.
-   Inspector layout could break for unusual payload sizes.
-   Header values were not always displayed/copied correctly.

## Timings and sizes

-   Timing values could be inconsistent.
-   Timing information could disagree between views.
-   Session duration/timing display could be misleading.
-   Size information could be incorrect.
-   Overview calculations could be slow.
-   Timing/size panels had UI/display defects.
-   Copying timing information did not always behave correctly.

## Search and filtering

-   Search could fail to find expected session data.
-   Filtering state could be lost or applied incorrectly.
-   Saved filters produced unsaved-change warnings in incorrect
    contexts.
-   An unsaved-filter warning could appear while closing Snapshots.
-   Filters incorrectly affected save/export/share operations.
-   Search/filter state could hide sessions unintentionally during
    persistence operations.
-   Header/body search had content-type-specific edge cases.
-   Search selection/highlighting had UI defects.

## Live Traffic grid

-   Grid state could be lost.
-   Compact view had display issues.
-   Large session selections caused poor performance.
-   Selecting many sessions could freeze or slow the UI.
-   Session marking/copying behavior could be incorrect.
-   Related-session selection produced incorrect selections in some
    cases.
-   Sorting/filtering caused unexpected UI state.
-   Grid columns/layout could render incorrectly.
-   Active-tab/grid refresh did not always occur after keyboard
    shortcuts.
-   Grouping/display state could become inconsistent.

## Rules / AutoResponder

-   Regular-expression rules failed on macOS/Linux in some releases.
-   Rules did not always execute.
-   Rules executed at the wrong stage/time.
-   Rules did not apply correctly to gRPC headers.
-   Rules did not work correctly with CONNECT traffic.
-   Multiple rule actions could execute in an unexpected order.
-   Rule validation produced incorrect results.
-   Rules Builder autocomplete had defects.
-   Rules Builder suggestions had defects.
-   Rules Builder layout had defects.
-   Editing a rule and pressing Escape could discard unsaved changes
    unexpectedly.
-   Rule/action editors could lose changes.
-   Shared rules could get out of sync.
-   Imported rules had compatibility problems.
-   Rule conditions could behave differently across platforms.
-   Unmatched traffic passthrough had edge cases.
-   AutoResponder behavior differed from expected Fiddler Classic
    behavior in some migration scenarios.

## Composer

-   Composer could fail to send the request body.
-   Composer could crash when working with some JSON payloads.
-   Composer could freeze while editing some JSON.
-   Headers could become hidden when many header values were present.
-   Header editing had UI/validation defects.
-   Query-parameter editing had defects.
-   Composer tab state could be lost.
-   Composer collection state could be lost.
-   Drag-and-drop in Composer collections had problems.
-   Renaming Composer collections had problems.
-   Requests created from captured sessions could differ unexpectedly
    from the original.
-   Request editing/reissue behavior had inconsistencies.

## Replay

-   Replayed requests could differ from the captured request.
-   Request bodies/headers could be lost during replay.
-   Multi-session replay exposed ordering/concurrency issues.
-   Replay behavior could interact incorrectly with rules.
-   Reissued requests could fail under proxy/authentication
    configurations.
-   Replay UI/state had edge cases.

## Breakpoints

-   Breakpoints did not always trigger correctly.
-   Breakpoint state/count display could be incorrect.
-   Paused traffic could interact incorrectly with rules.
-   Resume behavior had edge cases.
-   UI state could fail to indicate active breakpoints accurately.

## Saved Sessions / Snapshots

-   Snapshot state could be lost or displayed incorrectly.
-   Adding sessions to snapshots had selection/state issues.
-   Unsaved-change warnings appeared incorrectly.
-   Saved-session filters could behave differently from Live Traffic
    filters.
-   Shared saved sessions could become out of sync.
-   Renaming saved items exposed UI defects.
-   Quota/state handling could prevent expected actions.
-   Application closing could produce incorrect snapshot/filter
    warnings.

## AutoSave

-   AutoSave could not be disabled when quota limiting was active.
-   AutoSave state could be inconsistent.
-   AutoSave quota handling had defects.
-   Persisted Live Traffic did not always match expected session state.
-   AutoSave could interact incorrectly with filtering/snapshots.

## Save Resource / export / import

-   Save Resource tree selections could be wrong.
-   Resource selection state could be lost.
-   Export could incorrectly honor active search/filter state.
-   Save operations could omit sessions hidden by UI state.
-   Export/share behavior could differ from what was visibly selected.
-   Imported traffic could lose or misinterpret metadata.
-   Fiddler Classic import/migration exposed compatibility issues.
-   Importing filters/rules/settings had edge cases.

## Sharing and collaboration

-   Email recipients could be duplicated when addresses were separated
    by spaces.
-   Shared-session changes did not always synchronize correctly.
-   Shared snapshots could display stale state.
-   Sharing dialogs had UI defects.
-   Share operations could incorrectly use filtered session state.
-   Collaboration state could become inconsistent after remote changes.

## Performance

-   Huge responses could freeze the application.
-   Large response bodies caused inspector slowdowns.
-   Large gRPC payloads caused display/performance issues.
-   Overview calculations could be slow.
-   Selecting large numbers of sessions could be slow.
-   Search/filter operations could become slow with large captures.
-   Streaming traffic could cause repeated UI updates and poor
    responsiveness.
-   Startup could be slow in some configurations.
-   Some network failures caused long waits instead of clean failures.

## Crashes and stability

-   Fiddler Everywhere could crash on startup when network access was
    blocked.
-   Certain Windows configurations caused startup crashes/failures.
-   Linux/Ubuntu configurations caused launch failures.
-   The preconfigured/instrumented browser could crash.
-   Composer could crash on particular payloads.
-   Large payloads could freeze/crash the UI.
-   SignalR disconnects could result in repeated unexpected errors.
-   Failure to reach Progress/Telerik services could leave the
    application in a broken state.
-   Application state could fail to initialize correctly after
    network/service failures.
-   Update/restart workflows could leave the application unable to
    relaunch.

## Application updates

-   Application update installation failed in some cases.
-   Restart after update failed.
-   macOS Big Sur exposed update/restart issues.
-   Linux/AppImage updates/integration had problems.
-   Runtime/dependency updates were required to restore compatibility.
-   Update notifications/UI had defects.

## Linux

-   Ubuntu/Linux launch failures occurred.
-   AppImage integration required fixes.
-   Platform-specific Rules/regex behavior failed.
-   Proxy/certificate behavior differed from Windows/macOS.
-   Desktop integration/update behavior had defects.

## macOS

-   Network-extension capture could stop.
-   Root CA/keychain trust had issues.
-   System keychain behavior required fixes.
-   Proxy restoration/configuration had issues.
-   Rules/regex behavior had platform-specific failures.
-   macOS Big Sur exposed update issues.
-   Managed capture settings required platform-specific fixes.

## Windows

-   System proxy configuration could fail.
-   Windows authentication scenarios failed.
-   Certificate machine-store behavior had issues.
-   Some Windows configurations caused startup failures.
-   Network Capture mode exposed early/beta defects.
-   VPN/proxy combinations produced Windows-specific problems.
-   Disabled OS animations could leave a loading animation running
    indefinitely.

## UI and layout

-   Active tabs were not always visible.
-   Keyboard shortcuts changed state without refreshing the UI.
-   Tutorials could block window controls.
-   Buttons/icons could be missing.
-   Notification close buttons could be missing.
-   Dialog controls could overlap.
-   Small screens produced flicker/layout problems.
-   Restored windows could open off-screen.
-   Application/window state could restore incorrectly.
-   Loading animation could remain active when OS animations were
    disabled.
-   Context menus could have incorrect state.
-   Autocomplete lists had layout/selection issues.
-   Search highlighting had visual problems.
-   Inline editing/rename had defects.
-   Compact-view layout had defects.

## Notifications

-   Some notifications lacked a close button.
-   Notifications could display at inappropriate times.
-   Repeated error notifications could occur after connectivity
    failures.
-   Update/trial/account notifications had state problems.

## Account, licensing and trial behavior

-   Debugging Assistant continued to work after the Fiddler Everywhere
    trial expired.
-   Account/licensing state could fail to refresh correctly.
-   Network/service failures could interfere with account-dependent
    application state.
-   Quota-related functionality could expose inconsistent controls.

## Debugging Assistant / AI

-   Debugging Assistant could remain enabled after trial expiration.
-   Model lists could become stale.
-   Assistant availability/state could fail to refresh.
-   AI-related enterprise policy/state required fixes.
-   Traffic/session information supplied to AI workflows could be
    incomplete in edge cases.

## MCP

-   MCP structured responses were incompatible in some scenarios.
-   MCP session-detail responses could omit bodies.
-   MCP session details could return incorrect body information.
-   Compressed MCP bodies were not handled correctly.
-   MCP setup/integration naming or configuration had defects.
-   MCP API/setup workflows required compatibility fixes.
-   MCP traffic sanitization/scoping exposed early integration issues.
-   MCP core/server integration required fixes across releases.

## Agent Calls / Agent Inspector

-   Some Amazon Bedrock agent calls were missing from Agent Calls.
-   Agent metadata could be incomplete.
-   Model information could be incorrect/stale.
-   Agent-call presentation had early UI/integration defects.
-   Tool/message information could be missing for some provider formats.

## Agent Cache

-   Agent Cache origin IDs could be incorrect.
-   Cached response/session association could be wrong.
-   Cache recommendation logic/state required fixes.
-   Cache UI/state exposed early-release defects.
-   Agent-cache integration required follow-up fixes after introduction.

## Security and dependency issues

Fiddler Everywhere's release history repeatedly includes upgrades of
third-party dependencies to address potential security vulnerabilities.
These are consolidated here rather than repeated for every release.

Notable explicitly identified issues include:

-   **CVE-2026-86158** --- Missing Authentication in the local .NET
    backend of Progress Telerik Fiddler Everywhere.
-   **CVE-2026-86157** --- Exposure of Privileged IPC Functionality in
    Progress Telerik Fiddler Everywhere.
-   Repeated upgrades of third-party dependencies to mitigate potential
    security vulnerabilities.
-   Core backend runtime upgrades to supported/security-patched
    versions.
-   Platform/runtime dependency updates required for security and
    compatibility.

------------------------------------------------------------------------

# High-level capability map

For quick reference, the release history shows Fiddler Everywhere
evolving into a tool covering all of these major areas:

-   HTTP/HTTPS interception
-   HTTP/2
-   WebSockets
-   SSE / streaming
-   gRPC / Protobuf
-   MessagePack
-   Binary/HEX inspection
-   System proxy capture
-   Windows Network Capture
-   Reverse proxy
-   Upstream proxy / PAC / WPAD
-   VPN-aware proxying
-   Windows authentication
-   Custom/root CA management
-   Live Traffic analysis
-   Search/filter/grouping
-   Timings and size analysis
-   Composer
-   Replay
-   Breakpoints
-   Rules / AutoResponder
-   Host remapping
-   Scripting
-   Snapshots / saved sessions
-   AutoSave
-   Import/export
-   Fiddler Classic migration
-   Sharing/collaboration
-   Windows/macOS/Linux support
-   Debugging Assistant
-   MCP server/integration
-   Agent Calls inspection
-   Agent Cache

------------------------------------------------------------------------

# Historical release range

The official release-history index currently spans **111 releases**,
beginning with:

-   v0.1.0 --- November 8, 2018
-   v0.2.0 --- November 22, 2018
-   v0.2.1 --- December 6, 2018
-   v0.2.2 --- January 10, 2019
-   v0.3.0 --- April 4, 2019
-   v0.4.0 --- July 15, 2019
-   v0.5.0 --- August 15, 2019

and continuing through the 1.x, 2.x, 3.x, 4.x, 5.x, 6.x and 7.x
generations to:

-   v7.7.0 --- March 12, 2026 --- introduced Agent Cache
-   v7.7.1 --- March 18, 2026
-   v7.7.2 --- March 26, 2026
-   v7.7.3 --- April 14, 2026
-   v7.8.0 --- May 28, 2026
-   v8.0.1 --- July 22, 2026
-   v8.0.2 --- July 30, 2026
-   v8.1.0 --- August 31, 2026 --- domain/application grouping, QUERY
    Composer method, AppImage updates, header-name copy
-   v8.2.0 --- September 29, 2026 --- scripting and host remapping

For the exact chronological list and the original NEW/FIXED wording for
each release, see the official Progress/Telerik release-history index.

------------------------------------------------------------------------

# Notes on interpretation

-   A feature appearing in a historical **NEW** entry establishes that
    Fiddler Everywhere added that capability at that point; the
    capability may have been expanded or redesigned in later releases.
-   A historical **FIXED** entry establishes that the product
    experienced that defect or limitation at some point. It does **not**
    mean the current release still has the problem.
-   Similar bugs reported in multiple releases have been combined where
    doing so does not lose useful technical information.
-   Generic entries such as "updated third-party dependencies to address
    potential security vulnerabilities" occurred repeatedly and are
    deliberately consolidated.
-   This inventory is intended to answer **"What has Fiddler Everywhere
    been capable of, and what kinds of problems has it historically
    encountered?"**, rather than **"What changed in version X?"**

## Source

Progress Telerik --- Fiddler Everywhere Release History\
https://www.telerik.com/support/whats-new/fiddler-everywhere/release-history

Compiled October 2026.
