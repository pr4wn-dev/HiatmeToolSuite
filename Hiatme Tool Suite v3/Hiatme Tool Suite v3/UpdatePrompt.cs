using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Modal dialog that shows the version comparison + release notes, then drives the verified download with a
    /// progress bar. Returns DialogResult.OK once the zip is on disk in <see cref="DownloadedZipPath"/>; the caller
    /// is responsible for handing off to <see cref="UpdateClient.LaunchUpdaterAndExit"/> and shutting the app down.
    /// </summary>
    internal partial class UpdatePrompt : MaterialForm
    {
        private readonly UpdateManifest _manifest;
        private CancellationTokenSource _cts;

        /// <summary>Populated after the user clicks Install and the verified download completes.</summary>
        public string DownloadedZipPath { get; private set; }

        public UpdatePrompt(UpdateManifest manifest)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            InitializeComponent();

            try
            {
                var mgr = MaterialSkinManager.Instance;
                mgr.AddFormToManage(this);
                mgr.Theme = MaterialSkinManager.Themes.DARK;
                mgr.ColorScheme = new ColorScheme(Primary.Grey900, Primary.Grey800, Primary.BlueGrey500, Accent.Lime700, TextShade.WHITE);
            }
            catch
            {
                // MaterialSkin is optional polish — never block the update flow on theming.
            }

            Text = "Update available";
            _versionLabel.Text = "Current: " + UpdateClient.CurrentVersionDisplay + "    →    New: v" + _manifest.Version;
            _notesBox.Text = string.IsNullOrWhiteSpace(_manifest.ReleaseNotes)
                ? "No release notes provided."
                : _manifest.ReleaseNotes;
            if (_manifest.SizeBytes > 0)
                _sizeLabel.Text = "Download size: " + FormatBytes(_manifest.SizeBytes);
            else
                _sizeLabel.Text = "";

            _progress.Visible = false;
            _progressLabel.Visible = false;
        }

        private async void OnInstallClicked(object sender, EventArgs e)
        {
            // Lock the dialog so the user can't double-click / cancel mid-IO; X box still works via OnFormClosing.
            _installButton.Enabled = false;
            _laterButton.Enabled = false;
            _progress.Visible = true;
            _progressLabel.Visible = true;
            _progress.Value = 0;
            _progressLabel.Text = "Starting download…";

            _cts = new CancellationTokenSource();
            var progress = new Progress<double>(p =>
            {
                if (IsDisposed) return;
                int pct = (int)Math.Round(p * 100.0);
                if (pct < 0) pct = 0; else if (pct > 100) pct = 100;
                _progress.Value = pct;
                _progressLabel.Text = "Downloading… " + pct + "%";
            });

            try
            {
                string zip = await UpdateClient.DownloadVerifiedAsync(_manifest, progress, _cts.Token);
                DownloadedZipPath = zip;
                _progress.Value = 100;
                _progressLabel.Text = "Verified. Launching updater…";
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                _progressLabel.Text = "Cancelled.";
                _installButton.Enabled = true;
                _laterButton.Enabled = true;
            }
            catch (Exception ex)
            {
                _progressLabel.Text = "Failed.";
                MessageBox.Show(this,
                    "Update failed.\n\n" + ex.Message,
                    "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _installButton.Enabled = true;
                _laterButton.Enabled = true;
            }
        }

        private void OnLaterClicked(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _cts?.Cancel(); }
            catch { }
            base.OnFormClosing(e);
        }

        private static string FormatBytes(long b)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double v = b;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024.0; u++; }
            return v.ToString(u == 0 ? "0" : "0.#") + " " + units[u];
        }
    }
}
