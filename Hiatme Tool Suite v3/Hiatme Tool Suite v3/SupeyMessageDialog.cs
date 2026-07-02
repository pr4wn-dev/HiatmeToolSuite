using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Themed replacement for MessageBox: dark surface, accent stripe by severity,
    /// heading + wrapping body, optional monospace detail block (e.g. failure lists).
    /// </summary>
    internal sealed class SupeyMessageDialog : SupeyForm
    {
        internal enum Kind
        {
            Info,
            Success,
            Warning,
            Error,
        }

        private const int DialogWidth = 480;
        private const int ContentPad = 24;
        private const int ContentWidth = DialogWidth - ContentPad * 2 - 3; // minus accent stripe
        private const int FooterHeight = 56;

        private SupeyMessageDialog(Kind kind, string title, string heading, string body, string details)
        {
            Text = title ?? "Hiatme Tool Suite";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = SupeyTheme.Surface;

            var accent = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = AccentFor(kind),
            };

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = FooterHeight,
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

            var body_ = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(ContentPad, TitleBarHeight + 14, ContentPad, 8),
            };

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = SupeyTheme.Surface,
            };

            var headingLbl = new Label
            {
                Text = heading ?? "",
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = HeadingColorFor(kind),
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 0, 0, 0),
                BackColor = SupeyTheme.Surface,
            };
            stack.Controls.Add(headingLbl);

            if (!string.IsNullOrWhiteSpace(body))
            {
                stack.Controls.Add(new Label
                {
                    Text = body,
                    Font = new Font("Segoe UI", 9.25f),
                    ForeColor = SupeyTheme.TextSecondary,
                    AutoSize = true,
                    MaximumSize = new Size(ContentWidth, 0),
                    Margin = new Padding(0, 10, 0, 0),
                    BackColor = SupeyTheme.Surface,
                });
            }

            TextBox detailBox = null;
            if (!string.IsNullOrWhiteSpace(details))
            {
                detailBox = new TextBox
                {
                    Text = details.TrimEnd(),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 8.75f),
                    BackColor = SupeyTheme.SurfaceElevated,
                    ForeColor = SupeyTheme.TextPrimary,
                    Width = ContentWidth,
                    Height = 110,
                    Margin = new Padding(0, 12, 0, 0),
                    TabStop = false,
                };
                stack.Controls.Add(detailBox);
                SupeyDarkScrollBars.Apply(detailBox);
            }

            body_.Controls.Add(stack);

            Controls.Add(body_);
            Controls.Add(footer);
            Controls.Add(accent);

            AcceptButton = okBtn;
            CancelButton = okBtn;

            // Size the dialog to its content — no dead space, no clipped text.
            stack.PerformLayout();
            int contentHeight = stack.GetPreferredSize(new Size(ContentWidth, 0)).Height;
            int clientHeight = TitleBarHeight + 14 + contentHeight + 16 + FooterHeight;
            ClientSize = new Size(DialogWidth, Math.Min(clientHeight, 620));

            SupeyDarkScrollBars.Apply(this);
        }

        private static Color AccentFor(Kind kind)
        {
            switch (kind)
            {
                case Kind.Success: return SupeyTheme.SuccessText;
                case Kind.Warning: return SupeyTheme.WarnText;
                case Kind.Error: return SupeyTheme.ErrorText;
                default: return SupeyTheme.AccentPrimary;
            }
        }

        private static Color HeadingColorFor(Kind kind)
        {
            switch (kind)
            {
                case Kind.Warning: return SupeyTheme.WarnText;
                case Kind.Error: return SupeyTheme.ErrorText;
                default: return SupeyTheme.TextPrimary;
            }
        }

        public static void Show(
            IWin32Window owner,
            Kind kind,
            string title,
            string heading,
            string body,
            string details = null)
        {
            using (var dlg = new SupeyMessageDialog(kind, title, heading, body, details))
                dlg.ShowDialog(owner);
        }

        public static void ShowInfo(IWin32Window owner, string title, string heading, string body)
            => Show(owner, Kind.Info, title, heading, body);

        public static void ShowWarning(IWin32Window owner, string title, string heading, string body, string details = null)
            => Show(owner, Kind.Warning, title, heading, body, details);
    }
}
