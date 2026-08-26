using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Themed replacement for MessageBox: dark surface, accent stripe by severity,
    /// heading + wrapping body, optional monospace detail block, OK or two-action footer.
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
        private const int ContentWidth = DialogWidth - ContentPad * 2;
        private const int FooterHeight = 56;

        private SupeyMessageDialog(
            Kind kind,
            string title,
            string heading,
            string body,
            string details,
            string primaryText,
            string secondaryText,
            DialogResult primaryResult,
            DialogResult secondaryResult)
        {
            Text = title ?? "Hiatme Tool Suite";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Sizable = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = SupeyTheme.Surface;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = FooterHeight,
                BackColor = SupeyTheme.SurfaceElevated,
            };

            var footerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 10, 18, 10),
                BackColor = SupeyTheme.SurfaceElevated,
            };

            bool hasSecondary = !string.IsNullOrWhiteSpace(secondaryText);
            var primaryBtn = new DarkOnAccentMaterialButton
            {
                Text = string.IsNullOrWhiteSpace(primaryText) ? "OK" : primaryText.Trim(),
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(Math.Max(96, MeasureBtnWidth(primaryText ?? "OK")), 36),
                DialogResult = primaryResult,
                Margin = new Padding(0, 0, 0, 0),
            };
            footerButtons.Controls.Add(primaryBtn);

            SupeyMaterialButton secondaryBtn = null;
            if (hasSecondary)
            {
                secondaryBtn = new SupeyMaterialButton
                {
                    Text = secondaryText.Trim(),
                    AutoSize = false,
                    Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                    Size = new Size(Math.Max(96, MeasureBtnWidth(secondaryText)), 36),
                    DialogResult = secondaryResult,
                    Margin = new Padding(0, 0, 8, 0),
                };
                footerButtons.Controls.Add(secondaryBtn);
            }

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
                Font = new Font("Segoe UI Semibold", 12f),
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
                    Font = new Font("Segoe UI", 9.5f),
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

            AcceptButton = primaryBtn;
            CancelButton = (IButtonControl)secondaryBtn ?? primaryBtn;

            stack.PerformLayout();
            int contentHeight = stack.GetPreferredSize(new Size(ContentWidth, 0)).Height;
            int clientHeight = TitleBarHeight + 14 + contentHeight + 16 + FooterHeight;
            ClientSize = new Size(DialogWidth, Math.Min(Math.Max(clientHeight, 200), 620));

            SupeyDarkScrollBars.Apply(this);
        }

        private static int MeasureBtnWidth(string text)
        {
            int len = (text ?? "").Trim().Length;
            return Math.Min(160, 28 + len * 8);
        }

        private static Color HeadingColorFor(Kind kind)
        {
            switch (kind)
            {
                case Kind.Success: return SupeyTheme.SuccessText;
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
            using (var dlg = new SupeyMessageDialog(
                kind, title, heading, body, details,
                "OK", null, DialogResult.OK, DialogResult.Cancel))
            {
                SupeyForm.CenterOnWorkingArea(dlg, owner);
                dlg.ShowDialog(owner);
            }
        }

        public static void ShowInfo(IWin32Window owner, string title, string heading, string body)
            => Show(owner, Kind.Info, title, heading, body);

        public static void ShowSuccess(IWin32Window owner, string title, string heading, string body, string details = null)
            => Show(owner, Kind.Success, title, heading, body, details);

        public static void ShowWarning(IWin32Window owner, string title, string heading, string body, string details = null)
            => Show(owner, Kind.Warning, title, heading, body, details);

        /// <summary>Yes/No style confirm. Returns <see cref="DialogResult.Yes"/> or <see cref="DialogResult.No"/>.</summary>
        public static DialogResult Confirm(
            IWin32Window owner,
            Kind kind,
            string title,
            string heading,
            string body,
            string yesText = "Yes",
            string noText = "No")
        {
            using (var dlg = new SupeyMessageDialog(
                kind, title, heading, body, null,
                yesText, noText, DialogResult.Yes, DialogResult.No))
            {
                SupeyForm.CenterOnWorkingArea(dlg, owner);
                var result = dlg.ShowDialog(owner);
                if (result == DialogResult.Yes || result == DialogResult.No)
                    return result;
                return DialogResult.No;
            }
        }

        /// <summary>
        /// Two-action prompt. Primary returns <see cref="DialogResult.Yes"/>,
        /// secondary returns <see cref="DialogResult.No"/>.
        /// </summary>
        public static DialogResult Ask(
            IWin32Window owner,
            Kind kind,
            string title,
            string heading,
            string body,
            string primaryText,
            string secondaryText,
            string details = null)
        {
            using (var dlg = new SupeyMessageDialog(
                kind, title, heading, body, details,
                primaryText, secondaryText, DialogResult.Yes, DialogResult.No))
            {
                SupeyForm.CenterOnWorkingArea(dlg, owner);
                var result = dlg.ShowDialog(owner);
                if (result == DialogResult.Yes || result == DialogResult.No)
                    return result;
                return DialogResult.No;
            }
        }
    }
}
