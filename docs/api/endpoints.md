# MoSaProxy local API: endpoints

Generated from [openapi.json](openapi.json) by the `ApiDocument_Tests` test: do not edit by hand. To regenerate both files after changing an endpoint, run the tests with the environment variable `MOSAPROXY_UPDATE_API_DOCS=1`.

Every request must carry the per-run token in the `X-MoSaProxy-Token` header (or a `token` query parameter), a `Host` header naming the listener, and, if it has an `Origin`, the listener's own. Otherwise it is refused with 401 or 403.

## Status

### `GET /api/status`

**How the proxy is set up and what it has captured** (`GetStatus`)

The proxy's port, whether it intercepts HTTPS and offers HTTP/2, whether capture is on, how many exchanges are captured, and the capture folder and log file.

| Response | Content |
|---|---|
| 200 OK | `application/json` ApiStatus |

## Capture

### `GET /api/capture`

**Whether capture is on** (`GetCapture`)

While capture is off, traffic is still forwarded and logged; only the captured exchanges stop growing.

| Response | Content |
|---|---|
| 200 OK | `application/json` ApiCaptureState |

### `PUT /api/capture`

**Turn capture on or off** (`SetCapture`)

An exchange already running is not affected: it is captured to its end if it began while capture was on, and not at all if it began while off.

Request body: `application/json`, ApiCaptureRequest

| Response | Content |
|---|---|
| 200 OK | `application/json` ApiCaptureState |

## Exchanges

### `GET /api/exchanges`

**A page of captured exchanges, oldest first** (`ListExchanges`)

Only exchanges that have finished are listed. Pass the previous page's next value as after to get the following page, or the highest number already seen to get what is new. host, mode and q match text contained in the value (any case); method must match exactly (any case).

| Parameter | In | Type | Required |
|---|---|---|---|
| `after` | query | integer (int32) | no |
| `limit` | query | integer (int32) | no |
| `host` | query | string | no |
| `method` | query | string | no |
| `status` | query | integer (int32) | no |
| `mode` | query | string | no |
| `q` | query | string | no |

| Response | Content |
|---|---|
| 200 OK | `application/json` ApiExchangePage |

### `GET /api/exchanges/{id}`

**One captured exchange** (`GetExchange`)

The summary, the request and response heads with their headers in the order captured, timings, where the captured bytes came from, and whether a WebSocket log exists.

| Parameter | In | Type | Required |
|---|---|---|---|
| `id` | path | integer (int32) | yes |

| Response | Content |
|---|---|
| 200 OK | `application/json` ExchangeDetail |
| 404 Not Found | `application/json` ProblemDetails |

### `GET /api/exchanges/{id}/request`

**The request exactly as captured** (`GetRequestBytes`)

The request head and body as the bytes captured, including any chunk framing and content coding. For a CONNECT it is whatever followed the request.

| Parameter | In | Type | Required |
|---|---|---|---|
| `id` | path | integer (int32) | yes |

| Response | Content |
|---|---|
| 200 OK | `application/octet-stream` string (byte) |
| 404 Not Found | `application/problem+json` ProblemDetails |

### `GET /api/exchanges/{id}/response`

**The response exactly as captured** (`GetResponseBytes`)

The response head and body as the bytes captured, including any chunk framing and content coding.

| Parameter | In | Type | Required |
|---|---|---|---|
| `id` | path | integer (int32) | yes |

| Response | Content |
|---|---|
| 200 OK | `application/octet-stream` string (byte) |
| 404 Not Found | `application/problem+json` ProblemDetails |

### `GET /api/exchanges/{id}/request/body`

**The request body** (`GetRequestBody`)

Just the body, served with the request's own Content-Type. With decode=true the chunk framing and any gzip, deflate or brotli coding are removed, and the X-MoSaProxy-Decoded response header says whether that worked (it stays false for a coding that is not known or a body that is damaged, and the body is then as captured).

| Parameter | In | Type | Required |
|---|---|---|---|
| `id` | path | integer (int32) | yes |
| `decode` | query | boolean | no |

| Response | Content |
|---|---|
| 200 OK | `application/octet-stream` string (byte) |
| 404 Not Found | `application/problem+json` ProblemDetails |

### `GET /api/exchanges/{id}/response/body`

**The response body** (`GetResponseBody`)

Just the body, served with the response's own Content-Type. With decode=true the chunk framing and any gzip, deflate or brotli coding are removed, and the X-MoSaProxy-Decoded response header says whether that worked (it stays false for a coding that is not known or a body that is damaged, and the body is then as captured).

| Parameter | In | Type | Required |
|---|---|---|---|
| `id` | path | integer (int32) | yes |
| `decode` | query | boolean | no |

| Response | Content |
|---|---|
| 200 OK | `application/octet-stream` string (byte) |
| 404 Not Found | `application/problem+json` ProblemDetails |

### `GET /api/exchanges/{id}/websocket`

**The log of a WebSocket exchange's messages** (`GetWebSocketLog`)

The readable log the proxy wrote for the messages sent each way. Only exchanges whose detail says a WebSocket log exists have one.

| Parameter | In | Type | Required |
|---|---|---|---|
| `id` | path | integer (int32) | yes |

| Response | Content |
|---|---|
| 200 OK | `text/plain` string |
| 404 Not Found | `application/problem+json` ProblemDetails |

## Stream

### `GET /api/stream`

**Live events as exchanges are captured** (`StreamEvents`)

Server-Sent Events, kept open. Each event has an id that only grows, a name and JSON data. Events: exchange-started {id}; exchange-finished {id} (the exchange can now be read); websocket-message {exchange, fromClient, type, totalBytes}; capture {enabled}; reset {} (sent when the events asked for after Last-Event-ID are no longer remembered: fetch the exchange list again). A connection with no Last-Event-ID (header, or the lastEventId query parameter) gets only what happens from then on; one with it first gets the events it missed. A comment line is sent every 15 seconds to keep the connection alive. A browser's EventSource cannot set headers, so pass the token as the token query parameter.

| Response | Content |
|---|---|
| 200 OK |  |

## Models

### ApiCaptureRequest

| Property | Type |
|---|---|
| `enabled` | boolean |

### ApiCaptureState

| Property | Type |
|---|---|
| `enabled` | boolean |
| `exchangesCaptured` | integer (int32) |

### ApiExchangePage

| Property | Type |
|---|---|
| `items` | ExchangeSummary[] |
| `next` | integer (int32)? |

### ApiStatus

| Property | Type |
|---|---|
| `version` | string |
| `proxyPort` | integer (int32) |
| `intercept` | boolean |
| `http2` | boolean |
| `captureEnabled` | boolean |
| `exchangesCaptured` | integer (int32) |
| `captureFolder` | string? |
| `logFile` | string? |

### ExchangeDetail

| Property | Type |
|---|---|
| `summary` | ExchangeSummary |
| `client` | string |
| `clientPort` | integer (int32) |
| `clientTlsVersion` | string? |
| `clientConnectionReused` | boolean |
| `originConnectionReused` | boolean |
| `originConnectionId` | integer (int32)? |
| `clientMessages` | integer (int32)? |
| `originMessages` | integer (int32)? |
| `requestSource` | string |
| `responseSource` | string |
| `requestBodyTruncated` | boolean |
| `responseBodyTruncated` | boolean |
| `times` | map of string (date-time) |
| `request` | null or HttpHead |
| `response` | null or HttpHead |
| `hasWebSocketLog` | boolean |

### ExchangeSummary

| Property | Type |
|---|---|
| `id` | integer (int32) |
| `timestamp` | string (date-time) |
| `method` | string |
| `scheme` | string |
| `host` | string |
| `port` | integer (int32) |
| `path` | string |
| `url` | string |
| `status` | integer (int32)? |
| `clientHttpVersion` | string |
| `originHttpVersion` | string? |
| `mode` | string |
| `requestBytes` | integer (int64) |
| `responseBytes` | integer (int64) |
| `durationMs` | number (double) |
| `error` | string? |

### HttpHead

| Property | Type |
|---|---|
| `firstLine` | string |
| `headers` | KeyValuePairOfstringAndstring[] |

### KeyValuePairOfstringAndstring

| Property | Type |
|---|---|
| `key` | string? |
| `value` | string? |

### ProblemDetails

| Property | Type |
|---|---|
| `type` | string? |
| `title` | string? |
| `status` | integer (int32)? |
| `detail` | string? |
| `instance` | string? |

