using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal enum SupeyMessageKind
    {
        Information,
        Warning,
    }

    /// <summary>Themed OK dialog with scrollable body — replaces MessageBox for Schedule Builder flows.</summary>
    internal sealed class SupeyMessageForm : SupeyForm
    {
        private const int DialogWidth = 500;
        private const int ContentWidth = 420;
        private const int MinBodyHeight = 72;
        private const int MaxBodyHeight = 280;

        private readonly TextBox _messageBox;

        public static void Show(
            IWin32Window owner,
            string title,
            string message,
            SupeyMessageKind kind = SupeyMessageKind.Information,
            string headline = null)
        {
            using (var form = new SupeyMessageForm(title, headline, message, kind))
                form.ShowDialog(owner);
        }

        private SupeyMessageForm(string title, string headline, string message, SupeyMessageKind kind)
        {
            Text = (title ?? "").Trim();
            if (Text.Length == 0)
                Text = "Hiatme Tool Suite";

            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = SupeyTheme.Surface;

            string body = (message ?? "").Trim();
            if (body.Length == 0)
                body = " ";

            string headlineText = (headline ?? "").Trim();
            if (headlineText.Length == 0)
                headlineText = kind == SupeyMessageKind.Warning ? "Notice" : "Done";

            int bodyHeight = MeasureBodyHeight(body);
            int clientHeight = 76 + 28 + bodyHeight + 56 + 24;
            clientHeight = Math.Max(240, Math.Min(520, clientHeight));

            ClientSize = new Size(DialogWidth, clientHeight);
            MinimumSize = new Size(DialogWidth, clientHeight);
            MaximumSize = new Size(DialogWidth, clientHeight);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = SupeyTheme.Surface,
            };

            var footerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 20, 12),
                BackColor = SupeyTheme.Surface,
            };

            var okBtn = new DarkOnAccentMaterialButton
            {
                Text = "OK",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(96, 36),
                DialogResult = DialogResult.OK,
            };

            footerButtons.Controls.Add(okBtn);
            footer.Controls.Add(footerButtons);

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(24, 76, 24, 12),
            };

            var headlineLbl = new Label
            {
                Text = headlineText,
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = kind == SupeyMessageKind.Warning ? SupeyTheme.ErrorText : SupeyTheme.SuccessText,
                BackColor = SupeyTheme.Surface,
            };

            _messageBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                HideSelection = true,
                ShortcutsEnabled = false,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Text = body,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.75f),
            };
            ApplyMessageBoxTheme();

            bodyPanel.Controls.Add(_messageBox);
            bodyPanel.Controls.Add(headlineLbl);

            AcceptButton = okBtn;
            CancelButton = okBtn;

            Controls.Add(bodyPanel);
            Controls.Add(footer);

            SupeyDarkScrollBars.Apply(this);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            ApplyMessageBoxTheme();
        }

        private void ApplyMessageBoxTheme()
        {
            if (_messageBox == null || _messageBox.IsDisposed)
                return;

            _messageBox.BackColor = SupeyTheme.SurfaceElevated;
            _messageBox.ForeColor = SupeyTheme.TextPrimary;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.OnFormClosed(e);
        }

        private static int MeasureBodyHeight(string message)
        {
            using (var probe = new Form())
            using (var g = probe.CreateGraphics())
            {
                Size size = TextRenderer.MeasureText(
                    g,
                    message,
                    new Font("Segoe UI", 9.75f),
                    new Size(ContentWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                return Math.Max(MinBodyHeight, Math.Min(MaxBodyHeight, size.Height + 16));
            }
        }
    }
}
