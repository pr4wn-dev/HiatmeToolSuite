# Hiatme Server (Node.js middleman)

Runs **alongside XAMPP** on this PC. Clients connect here; Hiatme Tool Suite and (later) the GoDaddy site request video/pictures through this server.

## Dashboard

Open **http://localhost:3000** in a browser for the admin dashboard:

- **Server status** – Running / unreachable, connected client count, uptime
- **Connected clients** – Table with ID, PC name, language, model, version, connected time
- **Disconnect** – Per-client button to close that client’s connection
- **Refresh** – Reload status and client list
- **Restart server** – Exits the process (use `npm run dev` so nodemon restarts it)

## Quick start

```bash
cd Server
npm install
npm start
```

- HTTP API: **http://localhost:3000**
- WebSocket (for clients): **ws://localhost:3000/ws**

## API (for Tool Suite / website)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/clients` | List connected clients (id, pcname, language, model, version). |
| POST | `/api/request-media` | Request media from a client. Body: `{ "clientId": "<id>", "mediaType": "picture" \| "screenshot" \| "video" }`. Server forwards to client and returns the response. |
| GET | `/api/health` | Health check. |

## Client protocol (WebSocket)

1. **Connect** to `ws://<server-host>:3000/ws`.

2. **Register** (required first message):
   ```json
   { "type": "register", "id": "unique-client-id", "pcname": "PC-NAME", "language": "en-US", "model": "", "version": "" }
   ```
   Server replies: `{ "type": "registered", "clientId": "..." }`.

3. **Handle media requests** from server:
   Server sends:
   ```json
   { "type": "requestMedia", "requestId": "req_...", "mediaType": "picture" }
   ```
   Client should capture and reply with:
   ```json
   { "type": "mediaResponse", "requestId": "req_...", "data": "<base64 image/video data>", "mimeType": "image/jpeg" }
   ```
   Or on error: `{ "type": "mediaResponse", "requestId": "req_...", "error": "error message" }`.

## Port

Default port is **3000**. Set `PORT` to use another (e.g. for XAMPP coexistence):

```bash
set PORT=8443
npm start
```

## Next steps

- **Hiatme Client (C#):** Update to connect to `ws://<server>:3000/ws` and send `register` + handle `requestMedia` (or add a TCP→WS bridge in this server).
- **Hiatme Tool Suite:** Call `GET /api/clients` and `POST /api/request-media` instead of talking to the old C# Server.
