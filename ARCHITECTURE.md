# Hiatme Suite – Architecture (intended)

## Overview

- **Server:** XAMPP on this PC (Apache + PHP/MySQL, optional Node.js). This is the **middleman**.
- **Clients:** Desktop app now; **Android app** later. Clients connect to the server and can provide video, pictures, etc. when asked.
- **Consumers:**  
  1. **Hiatme Tool Suite** (desktop) – connects to server, requests video/pictures from clients; server forwards to client and returns data.  
  2. **GoDaddy website** (future) – same: connect to server, request video etc. from clients via server.

So: **Tool Suite / Website → Server (XAMPP) → Client → Server → Tool Suite / Website.**

---

## Intended flow

```
┌─────────────────────┐         ┌─────────────────────────────────┐         ┌─────────────────────┐
│  Hiatme Tool Suite  │ ──────► │  SERVER (XAMPP – this PC)        │ ◄────── │  Clients             │
│  (desktop)          │ request │  - Apache / PHP / Node            │ connect │  - Desktop app (now) │
│                     │ video,  │  - Middleman: forwards requests  │         │  - Android app       │
│                     │ pics…   │    to clients, returns responses │         │    (future)          │
└─────────────────────┘         └─────────────────────────────────┘         └─────────────────────┘
            │                                    ▲
            │                                    │
            │                                    │ (future)
            │                                    │
            ▼                                    │
┌─────────────────────┐                         │
│  GoDaddy website    │ ────────────────────────┘
│  (future)           │   request video, etc. from clients via server
└─────────────────────┘
```

- **Clients** register/stay connected to the server so the server knows who is online.
- **Tool Suite** (and later the website) call the server with “get video from client X” (or similar).
- **Server** asks the right client for the data, then sends it back to the requester.

---

## Where the current code fits

| Piece | Current state | Intended role |
|-------|----------------|----------------|
| **XAMPP** | Your server on this PC | **The** server: clients and Tool Suite/website talk to it. Can use PHP and/or Node for APIs and real-time (e.g. WebSockets). |
| **Hiatme Client** (C#) | Connects to `127.0.0.1:8443` via raw TCP; sends identity; can request client list | **Client** that connects to the **XAMPP server** (once server speaks the same protocol or a new API). Today it’s built to talk to the C# Server. |
| **Hiatme Server** (C#) | Listens on 8443, keeps list of clients, custom `[VERI]`/`<EOF>` protocol | **Replaced by XAMPP** as the middleman. Logic (client list, forwarding requests) should live on XAMPP (PHP/Node). |
| **Hiatme Tool Suite** (C#) | Has camera/livescreen, “Victim”, IP/port in MainValues | Connects to **XAMPP server** to request video/pictures from clients; server requests from client and returns to Tool Suite. |
| **Update** | Downloads from hiatme.com/updates | Unchanged; can stay as-is. |

So: **server = XAMPP**; C# Server is a legacy/stand-in until the middleman is implemented on XAMPP.

---

## What needs to happen

1. **On XAMPP (server)**  
   - **API** (PHP or Node) that:  
     - Lets clients **register** and maintain a connection (or heartbeat) so the server knows which clients are online.  
     - Lets **Tool Suite** (and later the website) ask for “video/pictures from client X”.  
   - **Protocol/transport:**  
     - Either: implement the current TCP/`[VERI]` protocol in Node (or PHP with sockets), **or**  
     - Preferable long-term: **REST/HTTPS + WebSockets** (or long polling) so the GoDaddy site and Tool Suite can use the same API and you can use normal web auth (e.g. tokens).

2. **Hiatme Client**  
   - Point it at the **XAMPP server** (configurable host/port or URL) instead of 127.0.0.1:8443.  
   - If the server moves to HTTP/WebSockets, add a small client that talks that protocol; keep the existing “identity + request client list” behavior on top of it.

3. **Hiatme Tool Suite**  
   - Add (or refactor) a **connection to the XAMPP server** (HTTP client or WebSocket) to:  
     - Request video/pictures for a given client.  
     - Receive the stream/data the server got from the client and show it in **Kamera** / **livescreen**.

4. **Future**  
   - **Android app:** same role as the C# Client (register with server, respond to video/picture requests).  
   - **GoDaddy site:** same as Tool Suite from the server’s point of view: authenticate, then call server API to request video etc. from clients; server talks to clients and returns data.

---

## Summary

- **Server = XAMPP** on this PC; clients and Tool Suite/website all go through it.
- **Clients** (desktop now, Android later) connect to XAMPP and serve video/pics when the server asks.
- **Tool Suite** and **GoDaddy site** only talk to the server; the server is the middleman that talks to clients.
- Current **C# Hiatme Server** is the previous middleman; its behavior (client list, forwarding) should move into XAMPP so one central server fits your diagram.

---

## Implementation order (decided)

1. **Server first** – Node.js middleman in `Server/` (runs alongside XAMPP). Run: `cd Server && npm install && npm start`. WebSocket: `ws://localhost:3000/ws`; HTTP: `http://localhost:3000`. See `Server/README.md`.
2. **Client next** – Update Hiatme Client to connect to this server (WebSocket or TCP bridge).
3. **Tool Suite last** – Call `GET /api/clients` and `POST /api/request-media` instead of the old C# Server.

If you want, next step can be: a minimal **Node or PHP API on XAMPP** (e.g. “list clients”, “request snapshot from client X”) and how the C# Client and Tool Suite would call it.
