(function () {
  // Same-origin only (no cross-origin fetch = no CORS). Use relative URLs.
  var API_BASE = '';

  function el(id) {
    return document.getElementById(id);
  }

  function toast(message, type) {
    const t = el('toast');
    t.textContent = message;
    t.className = 'toast show ' + (type || '');
    clearTimeout(t._tid);
    t._tid = setTimeout(function () {
      t.classList.remove('show');
    }, 3500);
  }

  function formatUptime(seconds) {
    if (seconds == null) return '—';
    if (seconds < 60) return seconds + 's';
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    if (m < 60) return m + 'm ' + s + 's';
    const h = Math.floor(m / 60);
    const mm = m % 60;
    return h + 'h ' + mm + 'm ' + s + 's';
  }

  function formatDate(iso) {
    if (!iso) return '—';
    try {
      const d = new Date(iso);
      return d.toLocaleString();
    } catch (e) {
      return iso;
    }
  }

  function escapeHtml(s) {
    if (s == null) return '';
    var div = document.createElement('div');
    div.textContent = s;
    return div.innerHTML;
  }

  function escapeAttr(s) {
    if (s == null) return '';
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
  }

  function rowHtml(c) {
    return (
      '<tr data-client-id="' + escapeAttr(c.id) + '">' +
      '<td class="id-cell" title="' + escapeAttr(c.id) + '">' + escapeHtml(c.id) + '</td>' +
      '<td>' + escapeHtml(c.pcname || '—') + '</td>' +
      '<td>' + escapeHtml(c.language || '—') + '</td>' +
      '<td>' + escapeHtml(c.model || '—') + '</td>' +
      '<td>' + escapeHtml(c.version || '—') + '</td>' +
      '<td>' + escapeHtml(formatDate(c.connectedAt)) + '</td>' +
      '<td><button type="button" class="btn btn-disconnect" data-action="disconnect">Disconnect</button></td>' +
      '</tr>'
    );
  }

  function renderClientList(list) {
    const tbody = el('clients-tbody');
    if (!list || list.length === 0) {
      tbody.innerHTML = '<tr><td colspan="7" class="empty">No clients connected</td></tr>';
      return;
    }
    tbody.innerHTML = list.map(rowHtml).join('');
  }

  function updateCount(count) {
    el('clients-count').textContent = count != null ? count : '—';
  }

  function updateUptime(seconds) {
    el('uptime-value').textContent = formatUptime(seconds);
  }

  function loadHealth() {
    fetch(API_BASE + '/api/health')
      .then(function (r) { return r.json(); })
      .then(function (data) {
        el('status-value').textContent = data.ok ? 'Running' : 'Error';
        el('status-value').className = 'status-value' + (data.ok ? ' ok' : '');
        updateCount(data.clients);
        updateUptime(data.uptimeSeconds);
      })
      .catch(function () {
        el('status-value').textContent = 'Unreachable';
        el('status-value').className = 'status-value';
        el('clients-count').textContent = '—';
        el('uptime-value').textContent = '—';
      });
  }

  function loadClients() {
    const tbody = el('clients-tbody');
    tbody.innerHTML = '<tr><td colspan="7" class="empty">Loading…</td></tr>';
    fetch(API_BASE + '/api/clients')
      .then(function (r) { return r.json(); })
      .then(function (data) {
        renderClientList(data.clients || []);
        updateCount((data.clients || []).length);
      })
      .catch(function () {
        tbody.innerHTML = '<tr><td colspan="7" class="empty">Failed to load clients</td></tr>';
      });
  }

  function connectDashboardWs() {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    var host = window.location.hostname;
    if (host === 'localhost') host = '127.0.0.1';
    const port = window.location.port || (protocol === 'wss:' ? 443 : 80);
    const wsUrl = protocol + '//' + host + ':' + port + '/dashboard-ws';
    const ws = new WebSocket(wsUrl);

    ws.onopen = function () {
      el('status-value').textContent = 'Running';
      el('status-value').className = 'status-value ok';
    };

    ws.onmessage = function (ev) {
      try {
        var msg = JSON.parse(ev.data);
        switch (msg.type) {
          case 'snapshot':
            renderClientList(msg.clients || []);
            updateCount((msg.clients || []).length);
            if (msg.uptimeSeconds != null) updateUptime(msg.uptimeSeconds);
            break;
          case 'clientConnected':
            (function () {
              var c = msg.client;
              if (!c || !c.id) return;
              var tbody = el('clients-tbody');
              var existing = tbody.querySelector('tr[data-client-id="' + escapeAttr(c.id) + '"]');
              if (existing) existing.remove();
              var empty = tbody.querySelector('tr.empty');
              if (empty) empty.remove();
              tbody.insertAdjacentHTML('beforeend', rowHtml(c));
              var n = tbody.querySelectorAll('tr[data-client-id]').length;
              updateCount(n);
            })();
            break;
          case 'clientDisconnected':
            (function () {
              var id = msg.clientId;
              var tbody = el('clients-tbody');
              var row = id && tbody ? Array.prototype.find.call(tbody.querySelectorAll('tr[data-client-id]'), function (tr) { return tr.getAttribute('data-client-id') === id; }) : null;
              if (row) row.remove();
              var tbody = el('clients-tbody');
              var left = tbody.querySelectorAll('tr[data-client-id]').length;
              if (left === 0)
                tbody.innerHTML = '<tr><td colspan="7" class="empty">No clients connected</td></tr>';
              updateCount(left);
            })();
            break;
        }
      } catch (e) { /* ignore */ }
    };

    ws.onclose = function () {
      el('status-value').textContent = 'Disconnected';
      el('status-value').className = 'status-value';
      setTimeout(connectDashboardWs, 3000);
    };

    ws.onerror = function () {};
  }

  function refreshAll() {
    loadHealth();
    loadClients();
    toast('Refreshed', 'success');
  }

  function disconnectClient(clientId) {
    fetch(API_BASE + '/api/clients/' + encodeURIComponent(clientId) + '/disconnect', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
    })
      .then(function (r) { return r.json(); })
      .then(function () {
        toast('Client disconnected', 'success');
      })
      .catch(function () {
        toast('Failed to disconnect client', 'error');
      });
  }

  function restartServer() {
    if (!confirm('Restart the server? It will exit; run with "npm run dev" (nodemon) so it starts again.')) return;
    fetch(API_BASE + '/api/restart', { method: 'POST' })
      .then(function () {
        toast('Server restarting…', 'success');
      })
      .catch(function () {
        toast('Restart requested (server may have already exited)', 'success');
      });
  }

  el('btn-refresh').addEventListener('click', refreshAll);
  el('btn-refresh-clients').addEventListener('click', function () {
    loadClients();
    toast('Client list refreshed', 'success');
  });
  el('btn-restart').addEventListener('click', restartServer);

  el('clients-tbody').addEventListener('click', function (e) {
    var btn = e.target;
    if (btn.getAttribute('data-action') !== 'disconnect') return;
    var row = btn.closest('tr');
    var id = row && row.getAttribute('data-client-id');
    if (id) disconnectClient(id);
  });

  loadHealth();
  connectDashboardWs();
  setInterval(loadHealth, 10000);
})();
