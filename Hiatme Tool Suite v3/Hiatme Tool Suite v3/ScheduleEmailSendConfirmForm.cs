using System;
using System.Drawing;
using System.Windows.Forms;
namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Confirm sending the full schedule workbook to one driver.</summary>
    internal sealed class ScheduleEmailSendConfirmForm : SupeyForm
    {
        public ScheduleEmailSendConfirmForm(
            string driverDisplayName,
            string toEmail,
            DateTime serviceDate,
            string fromAddress)
        {
            Text = "Email schedule";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 280);
            MinimumSize = new Size(460, 280);
            MaximumSize = new Size(460, 280);
            BackColor = SupeyTheme.Surface;

            try
            {
            }
            catch { }

            string driver = (driverDisplayName ?? "").Trim();
            string email = (toEmail ?? "").Trim();
            string from = (fromAddress ?? "").Trim();
            string dateLabel = serviceDate.ToString("dddd, MMMM d, yyyy");

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

            var sendBtn = new DarkOnAccentMaterialButton
            {
                Text = "SEND",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(96, 36),
                DialogResult = DialogResult.OK,
            };

            var cancelBtn = new SupeyMaterialButton
            {
                Text = "CANCEL",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = SupeyTheme.TextSecondary,
                Size = new Size(96, 36),
                Margin = new Padding(0, 0, 8, 0),
                DialogResult = DialogResult.Cancel,
            };

            footerButtons.Controls.Add(sendBtn);
            footerButtons.Controls.Add(cancelBtn);
            footer.Controls.Add(footerButtons);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(24, 76, 24, 12),
            };

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = SupeyTheme.Surface,
            };

            stack.Controls.Add(MakeLabel("Send schedule to this driver?", new Font("Segoe UI Semibold", 11f), SupeyTheme.TextPrimary, 0), 0, 0);
            stack.Controls.Add(MakeLabel(
                "Attaches the full workbook (.xlsx, all driver tabs) for " + dateLabel + ".",
                new Font("Segoe UI", 9f), SupeyTheme.TextSecondary, 8), 0, 1);
            stack.Controls.Add(MakeLabel("Driver: " + (driver.Length > 0 ? driver : "(unknown)"),
                new Font("Segoe UI", 9.75f), SupeyTheme.TextPrimary, 14), 0, 2);
            stack.Controls.Add(MakeLabel("Email: " + email,
                new Font("Segoe UI", 9.75f), SupeyTheme.TextPrimary, 4), 0, 3);
            if (from.Length > 0)
            {
                stack.Controls.Add(MakeLabel("From: " + from,
                    new Font("Segoe UI", 9f), SupeyTheme.TextSecondary, 4), 0, 4);
            }

            body.Controls.Add(stack);

            AcceptButton = sendBtn;
            CancelButton = cancelBtn;

            Controls.Add(body);
            Controls.Add(footer);
        }

        private static Label MakeLabel(string text, Font font, Color color, int topMargin)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                MaximumSize = new Size(400, 0),
                Margin = new Padding(0, topMargin, 0, 0),
                BackColor = SupeyTheme.Surface,
            };
        }
    }
}
