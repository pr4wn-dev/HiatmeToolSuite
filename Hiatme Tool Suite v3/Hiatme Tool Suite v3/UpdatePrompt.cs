using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Themed update dialog: cycle through release-note slides while the verified download runs in the
    /// background. When the download finishes, the user chooses when to restart so the updater can install.
    /// </summary>
    internal partial class UpdatePrompt : SupeyForm
    {
        private readonly UpdateManifest _manifest;
        private readonly List<UpdateReleaseNoteItem> _slides;
        private CancellationTokenSource _cts;
        private bool _downloadStarted;
        private bool _downloadComplete;

        private const string DownloadButtonLabel = "DOWNLOAD UPDATE";
        private const string RestartButtonLabel = "RESTART TO INSTALL";

        /// <summary>Populated after the verified download completes.</summary>
        public string DownloadedZipPath { get; private set; }

        private Panel _carouselPanel;
        private Label _sectionLabel;
        private Label _featureLabel;
        private SupeyMaterialButton _prevButton;
        private SupeyMaterialButton _nextButton;
        private Label _pageIndicator;
        private Panel _readyBanner;
        private Label _readyBannerText;
        private int _slideIndex;

        public UpdatePrompt(UpdateManifest manifest)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _slides = UpdateReleaseNotesParser.Parse(_manifest.ReleaseNotes);

            InitializeComponent();
            BuildCarouselUi();

            SupeyDarkScrollBars.Apply(this);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;

            AcceptButton = _installButton;
            CancelButton = _laterButton;

            Text = "Update available";
            _versionLabel.Text = "Current: " + UpdateClient.CurrentVersionDisplay + "    →    New: v" + _manifest.Version;
            if (_manifest.SizeBytes > 0)
                _sizeLabel.Text = "Download size: " + FormatBytes(_manifest.SizeBytes);
            else
                _sizeLabel.Text = "";

            _progress.Visible = false;
            _progressLabel.Visible = false;
            _progress.HandleCreated += (_, __) => ThemeProgressBar();
            ThemeProgressBar();

            ShowSlide(0);
            KeyPreview = true;
            KeyDown += OnCarouselKeyDown;
        }

        private void OnCarouselKeyDown(object sender, KeyEventArgs e)
        {
            if (_slides.Count <= 1)
                return;
            if (e.KeyCode == Keys.Left)
            {
                MoveSlide(-1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                MoveSlide(1);
                e.Handled = true;
            }
        }

        private void BuildCarouselUi()
        {
            _notesLabel.Text = "What's new";
            _notesBox.Visible = false;

            _carouselPanel = new Panel
            {
                Location = new Point(20, 155),
                Size = new Size(540, 148),
                BackColor = SupeyTheme.SurfaceElevated,
            };

            _sectionLabel = new Label
            {
                AutoSize = false,
                Location = new Point(16, 12),
                Size = new Size(508, 20),
                Font = new Font("Segoe UI Semibold", 8.5f),
                ForeColor = SupeyTheme.AccentPrimary,
                BackColor = Color.Transparent,
            };

            _featureLabel = new Label
            {
                AutoSize = false,
                Location = new Point(16, 34),
                Size = new Size(508, 72),
                Font = new Font("Segoe UI", 10.5f),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = Color.Transparent,
            };

            _prevButton = CreateNavButton("◀  Prev", new Point(16, 112));
            _prevButton.Click += (_, __) => MoveSlide(-1);

            _nextButton = CreateNavButton("Next  ▶", new Point(448, 112));
            _nextButton.Click += (_, __) => MoveSlide(1);

            _pageIndicator = new Label
            {
                AutoSize = false,
                Location = new Point(200, 118),
                Size = new Size(140, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8.75f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = Color.Transparent,
                Text = "1 / 1",
            };

            _carouselPanel.Controls.Add(_sectionLabel);
            _carouselPanel.Controls.Add(_featureLabel);
            _carouselPanel.Controls.Add(_prevButton);
            _carouselPanel.Controls.Add(_nextButton);
            _carouselPanel.Controls.Add(_pageIndicator);
            Controls.Add(_carouselPanel);

            _readyBanner = new Panel
            {
                Location = new Point(20, 308),
                Size = new Size(540, 34),
                Visible = false,
                BackColor = SupeyTheme.SurfaceHeader,
            };
            _readyBannerText = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 9f),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = Color.Transparent,
                Text = "Update downloaded and verified — restart when you're ready.",
            };
            _readyBanner.Controls.Add(_readyBannerText);
            Controls.Add(_readyBanner);

            _installButton.Text = DownloadButtonLabel;
        }

        private static SupeyMaterialButton CreateNavButton(string text, Point location)
        {
            return new SupeyMaterialButton
            {
                Text = text,
                AutoSize = false,
                Location = location,
                Size = new Size(76, 28),
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = SupeyTheme.TextPrimary,
                Density = SupeyMaterialButton.MaterialButtonDensity.Default,
            };
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            ThemeCarousel();
            ThemeProgressBar();
        }

        private void ThemeCarousel()
        {
            if (_carouselPanel == null)
                return;
            _carouselPanel.BackColor = SupeyTheme.SurfaceElevated;
            _sectionLabel.ForeColor = SupeyTheme.AccentPrimary;
            _featureLabel.ForeColor = SupeyTheme.TextPrimary;
            _pageIndicator.ForeColor = SupeyTheme.TextSecondary;
            _readyBanner.BackColor = SupeyTheme.SurfaceHeader;
            _readyBannerText.ForeColor = SupeyTheme.TextPrimary;
            if (_prevButton != null)
                _prevButton.NoAccentTextColor = SupeyTheme.TextPrimary;
            if (_nextButton != null)
                _nextButton.NoAccentTextColor = SupeyTheme.TextPrimary;
        }

        private void ThemeProgressBar()
        {
            if (_progress == null || !_progress.IsHandleCreated)
                return;
            try { _progress.SetState(1); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.OnFormClosed(e);
        }

        private void ShowSlide(int index)
        {
            if (_slides.Count == 0)
                return;

            if (index < 0)
                index = _slides.Count - 1;
            else if (index >= _slides.Count)
                index = 0;

            _slideIndex = index;
            var slide = _slides[_slideIndex];
            _sectionLabel.Text = string.IsNullOrWhiteSpace(slide.Section) ? "" : slide.Section.ToUpperInvariant();
            _sectionLabel.Visible = !string.IsNullOrWhiteSpace(slide.Section);
            _featureLabel.Text = slide.Text ?? "";
            _pageIndicator.Text = (_slideIndex + 1) + " / " + _slides.Count;
            _prevButton.Enabled = _slides.Count > 1;
            _nextButton.Enabled = _slides.Count > 1;
        }

        private void MoveSlide(int delta) => ShowSlide(_slideIndex + delta);

        private void EnterDownloadState()
        {
            _downloadStarted = true;
            _installButton.Enabled = false;
            _installButton.Text = "DOWNLOADING…";
            AcceptButton = null;

            _progress.Visible = true;
            _progressLabel.Visible = true;
            _progress.Value = 0;
            _progressLabel.Text = "Starting download…";
            ThemeProgressBar();
        }

        private void OnDownloadComplete()
        {
            _downloadComplete = true;
            _readyBanner.Visible = true;
            _progress.Value = 100;
            _progressLabel.Text = "Verified — ready to install.";
            _installButton.Enabled = true;
            _installButton.Text = RestartButtonLabel;
            AcceptButton = _installButton;
            ThemeProgressBar();
        }

        private void LeaveDownloadStateAfterFailure()
        {
            _downloadStarted = false;
            _downloadComplete = false;
            _installButton.Enabled = true;
            _installButton.Text = DownloadButtonLabel;
            AcceptButton = _installButton;
            _readyBanner.Visible = false;
        }

        private async void OnInstallClicked(object sender, EventArgs e)
        {
            if (_downloadComplete)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            if (_downloadStarted)
                return;

            EnterDownloadState();
            _cts = new CancellationTokenSource();
            var progress = new Progress<double>(p =>
            {
                if (IsDisposed)
                    return;
                int pct = (int)Math.Round(p * 100.0);
                if (pct < 0) pct = 0;
                else if (pct > 100) pct = 100;
                _progress.Value = pct;
                _progressLabel.Text = "Downloading… " + pct + "%  (browse what's new while you wait)";
            });

            try
            {
                string zip = await UpdateClient.DownloadVerifiedAsync(_manifest, progress, _cts.Token);
                DownloadedZipPath = zip;
                OnDownloadComplete();
            }
            catch (OperationCanceledException)
            {
                _progressLabel.Text = "Cancelled.";
                LeaveDownloadStateAfterFailure();
            }
            catch (Exception ex)
            {
                _progressLabel.Text = "Failed.";
                SupeyMessageDialog.ShowWarning(this,
                    "Update failed",
                    "The update could not be downloaded.",
                    ex.Message);
                LeaveDownloadStateAfterFailure();
            }
        }

        private void OnLaterClicked(object sender, EventArgs e)
        {
            if (_downloadStarted && !_downloadComplete)
            {
                try { _cts?.Cancel(); } catch { }
            }
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_downloadStarted && !_downloadComplete)
            {
                try { _cts?.Cancel(); } catch { }
            }
            base.OnFormClosing(e);
        }

        private static string FormatBytes(long b)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double v = b;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1)
            {
                v /= 1024.0;
                u++;
            }
            return v.ToString(u == 0 ? "0" : "0.#") + " " + units[u];
        }
    }
}
