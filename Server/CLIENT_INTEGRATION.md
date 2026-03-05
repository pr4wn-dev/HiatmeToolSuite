# How the Hiatme Client (C#) should connect to this server

The Node server in `Server/` expects **WebSocket** connections from clients. The current C# Client uses **raw TCP** to the old C# Server. You have two paths:

## Option A: Add WebSocket support to the C# Client (recommended)

1. Add a WebSocket client NuGet package (e.g. **ClientWebSocket** in .NET, or **WebSocketSharp**).
2. Connect to `ws://<server-ip>:3000/ws` (configurable; replace 127.0.0.1 with your XAMPP/server machine IP if needed).
3. First message after connect must be **register** (JSON):
   ```json
   { "type": "register", "id": "<unique-id>", "pcname": "PC-NAME", "language": "en-US", "model": "", "version": "" }
   ```
   Use the same identity data you already send today (e.g. machine name, OS, etc.).
4. Listen for **requestMedia** from the server:
   ```json
   { "type": "requestMedia", "requestId": "req_...", "mediaType": "picture" | "screenshot" | "video" }
   ```
5. When you capture the image/video, reply with **mediaResponse**:
   ```json
   { "type": "mediaResponse", "requestId": "req_...", "data": "<base64>", "mimeType": "image/jpeg" }
   ```
   Or on error: `{ "type": "mediaResponse", "requestId": "req_...", "error": "message" }`.

Keep the rest of the Client UI/behavior; only the transport and message format change to match `Server/README.md`.

## Option B: Keep TCP and add a bridge in Node

If you want to keep the existing C# Client unchanged (same TCP and `[VERI]`/`<EOF>` protocol), we can add a small **TCP server** in the Node app that listens on 8443, accepts the current protocol, and forwards to the WebSocket middleman (so the Node server still has one client list and one API). Then the C# Client keeps connecting to `127.0.0.1:8443` (or your server IP). This is more work and duplicates protocol logic; Option A is cleaner long-term and matches the future Android app (which will speak WebSocket/HTTP).

## Configurable server URL

In both cases, make the server address **configurable** in the Client (e.g. in `MainValues` or a config file) so you can point to the PC running XAMPP/Node (e.g. `ws://192.168.1.x:3000/ws` or `127.0.0.1:8443` for a bridge).
