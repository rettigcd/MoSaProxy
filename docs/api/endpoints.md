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

