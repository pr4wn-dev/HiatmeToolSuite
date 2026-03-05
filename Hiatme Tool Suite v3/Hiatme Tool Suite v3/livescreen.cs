using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    public partial class livescreen : Form
    {
        public string ID = "";
        private System.Windows.Forms.Timer _pollTimer;
        private readonly string _clientId;

        public livescreen(string clientId, string displayName = null)
        {
            InitializeComponent();
            comboBox1.SelectedIndex = 4;
            _clientId = ID = clientId;
            Text = "Live Screen - " + (displayName ?? clientId);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex > -1)
            {
                button1.Enabled = false;
                button2.Enabled = true;
                _pollTimer = new System.Windows.Forms.Timer();
                _pollTimer.Interval = 500;
                _pollTimer.Tick += PollTimer_Tick;
                _pollTimer.Start();
            }
        }

        private async void PollTimer_Tick(object sender, EventArgs e)
        {
            await RequestScreenshotAsync();
        }

        private async Task RequestScreenshotAsync()
        {
            try
            {
                var baseUrl = (MainValues.ServerApiBase ?? "http://localhost:3000").TrimEnd('/');
                var payload = new JObject
                {
                    ["clientId"] = _clientId,
                    ["mediaType"] = "screenshot"
                };
                var content = new StringContent(payload.ToString(), System.Text.Encoding.UTF8, "application/json");
                var response = await Form1.ServerHttpClient.PostAsync(baseUrl + "/api/request-media", content);
                if (!response.IsSuccessStatusCode) return;
                var json = await response.Content.ReadAsStringAsync();
                var jo = JObject.Parse(json);
                var data = jo["data"]?.ToString();
                if (string.IsNullOrEmpty(data)) return;
                var bytes = Convert.FromBase64String(data);
                using (var ms = new MemoryStream(bytes))
                {
                    var img = Image.FromStream(ms);
                    if (pictureBox1.IsDisposed) return;
                    pictureBox1.Invoke((MethodInvoker)delegate
                    {
                        var old = pictureBox1.Image;
                        pictureBox1.Image = (Image)img.Clone();
                        old?.Dispose();
                    });
                }
            }
            catch { }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (button1.Enabled == false)
            {
                _pollTimer?.Stop();
                _pollTimer?.Dispose();
                _pollTimer = null;
                button2.Enabled = false;
                button1.Enabled = true;
            }
        }

        private void livescreen_FormClosing(object sender, FormClosingEventArgs e)
        {
            button2.PerformClick();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Quality hint; server/client can use this later if needed
        }
    }
}
