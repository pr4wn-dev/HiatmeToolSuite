using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Modal dialog that shows the version comparison + release notes, then drives the verified download with a
    /// progress bar. Returns DialogResult.OK once the zip is on disk in <see cref="DownloadedZipPath"/>; the caller
    /// is responsible for handing off to <see cref="UpdateClient.LaunchUpdaterAndExit"/> and shutting the app down.
    /// </summary>
    internal partial class UpdatePrompt : SupeyForm
    {
        private readonly UpdateManifest _manifest;
        private CancellationTokenSource _cts;
        private bool _installInProgress;
        private const string InstallButtonLabel = "INSTALL NOW";

        /// <summary>Populated after the user clicks Install and the verified download completes.</summary>
        public string DownloadedZipPath { get; private set; }

        public UpdatePrompt(UpdateManifest manifest)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            InitializeComponent();

            SupeyDarkScrollBars.Apply(this);
            ApplyNotesBoxTheme();
            SupeyThemeManager.ThemeChanged += OnThemeChanged;

            AcceptButton = _installButton;
            CancelButton = _laterButton;

            try
            {
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
            _progress.HandleCreated += (_, __) => ThemeProgressBar();
            ThemeProgressBar();
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ApplyNotesBoxTheme();
            ThemeProgressBar();
        }

        private void ApplyNotesBoxTheme()
        {
            _notesBox.BackColor = SupeyTheme.SurfaceElevated;
            _notesBox.ForeColor = SupeyTheme.TextPrimary;
        }

        private void ThemeProgressBar()
        {
            if (_progress == null || !_progress.IsHandleCreated) return;
            try { _progress.SetState(1); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.OnFormClosed(e);
        }

        private void EnterInstallState()
        {
            if (_installInProgress) return;
            _installInProgress = true;

            _installButton.Enabled = false;
            _installButton.Text = "INSTALLING…";
            _laterButton.Enabled = false;
            AcceptButton = null;
            CancelButton = null;

            _progress.Visible = true;
            _progressLabel.Visible = true;
            _progress.Value = 0;
            _progressLabel.Text = "Starting download…";
            ThemeProgressBar();
        }

        private void LeaveInstallState()
        {
            _installInProgress = false;
            _installButton.Enabled = true;
            _installButton.Text = InstallButtonLabel;
            _laterButton.Enabled = true;
            AcceptButton = _installButton;
            CancelButton = _laterButton;
        }

        private async void OnInstallClicked(object sender, EventArgs e)
        {
            if (_installInProgress) return;
            EnterInstallState();

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
                LeaveInstallState();
            }
            catch (Exception ex)
            {
                _progressLabel.Text = "Failed.";
                MessageBox.Show(this,
                    "Update failed.\n\n" + ex.Message,
                    "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                LeaveInstallState();
            }
        }

        private void OnLaterClicked(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_installInProgress)
            {
                try { _cts?.Cancel(); }
                catch { }
            }
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
