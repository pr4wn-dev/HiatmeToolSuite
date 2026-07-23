using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Day performance review for one driver — preview of what we would email EOD.
    /// </summary>
    internal sealed class DriverHabitsReviewForm : SupeyForm
    {
        private readonly SupeyListView _list;

        public DriverHabitsReviewForm(HiatmeAiClient.DriverHabitsReviewDoc review)
        {
            if (review == null)
                throw new ArgumentNullException(nameof(review));

            string driver = (review.Driver ?? "").Trim();
            if (string.IsNullOrEmpty(driver))
                driver = "Driver";

            Text = "Performance review — " + driver;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(780, 600);
            MinimumSize = new Size(780, 600);
            MaximumSize = new Size(780, 600);
            BackColor = SupeyTheme.Surface;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
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
            var closeBtn = new DarkOnAccentMaterialButton
            {
                Text = "CLOSE",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(96, 36),
                DialogResult = DialogResult.OK,
            };
            footerButtons.Controls.Add(closeBtn);
            footer.Controls.Add(footerButtons);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(22, 72, 22, 10),
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = SupeyTheme.Surface,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            root.Controls.Add(BuildHero(review, driver), 0, 0);
            root.Controls.Add(BuildSummaryStrip(review.Summary), 0, 1);

            int tripN = review.Improve?.Count ?? 0;
            var section = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = tripN == 0
                    ? "Trips to review"
                    : ("Trips to review · " + tripN.ToString(CultureInfo.InvariantCulture)),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.75f),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.Surface,
            };
            root.Controls.Add(section, 0, 2);

            var listCard = new SupeyCard
            {
                Dock = DockStyle.Fill,
                SurfaceLevel = SupeyCard.Surface.Standard,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = new Padding(1),
                Margin = new Padding(0),
            };

            _list = new SupeyListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
                Font = ListViewOwnerDrawFonts.Cell,
                GridLines = true,
            };
            SupeyListViewHelpers.EnableDoubleBufferRecursively(_list);
            _list.Columns.Add("Trip", 118);
            _list.Columns.Add("Habit", 128);
            _list.Columns.Add("Client", 150);
            _list.Columns.Add("Sched", 72);
            _list.Columns.Add("Actual", 72);
            _list.Columns.Add("Note", 200);
            _list.DrawColumnHeader += List_DrawColumnHeader;
            _list.DrawItem += List_DrawItem;
            _list.DrawSubItem += List_DrawSubItem;

            PopulateList(review.Improve);
            listCard.Controls.Add(_list);
            root.Controls.Add(listCard, 0, 3);

            string blurbText = !string.IsNullOrWhiteSpace(review.EmailIntro)
                ? ("Email would open with: " + review.EmailIntro.Trim().Replace("\r\n", " / ").Replace("\n", " / "))
                : (review.PreviewBlurb
                    ?? "Preview of the day performance note for this driver.");
            var blurb = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Text = blurbText,
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.Surface,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            body.Controls.Add(root);
            body.Controls.Add(blurb);

            AcceptButton = closeBtn;
            CancelButton = closeBtn;
            Controls.Add(body);
            Controls.Add(footer);

            SupeyDarkScrollBars.Apply(this);
            Shown += (_, __) =>
            {
                try { SupeyDarkScrollBars.Apply(_list); } catch { }
                try { _list.Invalidate(true); } catch { }
            };
        }

        private static Control BuildHero(
            HiatmeAiClient.DriverHabitsReviewDoc review,
            string driver)
        {
            string dateLabel = !string.IsNullOrWhiteSpace(review.DateLabel)
                ? review.DateLabel.Trim()
                : (review.ServiceDate ?? "");

            string rankText = !string.IsNullOrWhiteSpace(review.RankLine)
                ? review.RankLine.Trim()
                : (!string.IsNullOrWhiteSpace(review.RankLabel)
                    ? review.RankLabel.Trim()
                    : (review.Rank.HasValue && review.RankOf > 0
                        ? ("Your daily rank: " + review.Rank.Value.ToString(CultureInfo.InvariantCulture)
                            + " of " + review.RankOf.ToString(CultureInfo.InvariantCulture) + " drivers.")
                        : ""));

            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0, 0, 0, 6),
            };

            var rankLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = string.IsNullOrEmpty(rankText) ? "Daily rank: —" : rankText,
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.Surface,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var dateLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Text = dateLabel,
                Font = new Font("Segoe UI", 9f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.Surface,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var nameLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = driver,
                Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.Surface,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var headlineLbl = new Label
            {
                Dock = DockStyle.Fill,
                Text = review.Headline ?? "",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.Surface,
                TextAlign = ContentAlignment.TopLeft,
            };

            // Dock Top: last added sits highest.
            host.Controls.Add(headlineLbl);
            host.Controls.Add(nameLbl);
            host.Controls.Add(dateLbl);
            host.Controls.Add(rankLbl);
            return host;
        }

        private static Control BuildSummaryStrip(HiatmeAiClient.DriverHabitsReviewSummary s)
        {
            s = s ?? new HiatmeAiClient.DriverHabitsReviewSummary();
            var host = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0, 2, 0, 6),
            };
            host.Controls.Add(StatChip("Late PU", s.LatePu));
            host.Controls.Add(StatChip("Late DO", s.LateDo));
            host.Controls.Add(StatChip("Early PU", s.EarlyPu));
            host.Controls.Add(StatChip("Early DO", s.EarlyDo));
            host.Controls.Add(StatChip("Unfinished", s.Unfinished));
            host.Controls.Add(StatChip(
                "Late mins",
                (int)Math.Round(s.LateMinutes),
                s.LateMinutes.ToString("0", CultureInfo.InvariantCulture) + "m"));
            return host;
        }

        private static Control StatChip(string caption, int value, string valueText = null)
        {
            var card = new SupeyCard
            {
                SurfaceLevel = SupeyCard.Surface.Standard,
                ShowBorder = true,
                CornerRadius = 6,
                Margin = new Padding(0, 0, 8, 0),
                Size = new Size(108, 58),
                Padding = new Padding(6, 5, 6, 5),
            };

            var cap = new Label
            {
                Dock = DockStyle.Top,
                Height = 16,
                Text = caption,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = Color.Transparent,
                Font = SupeyTheme.CaptionFont,
            };
            var val = new Label
            {
                Dock = DockStyle.Fill,
                Text = valueText ?? value.ToString(CultureInfo.InvariantCulture),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
            };
            card.Controls.Add(val);
            card.Controls.Add(cap);
            return card;
        }

        private void PopulateList(List<HiatmeAiClient.DriverHabitsReviewTrip> improve)
        {
            improve = improve ?? new List<HiatmeAiClient.DriverHabitsReviewTrip>();
            if (improve.Count == 0)
            {
                var empty = new ListViewItem("—");
                empty.SubItems.Add("None");
                empty.SubItems.Add("");
                empty.SubItems.Add("");
                empty.SubItems.Add("");
                empty.SubItems.Add("No habit flags for this day.");
                empty.ForeColor = SupeyTheme.TextSecondary;
                empty.Tag = "empty";
                _list.Items.Add(empty);
                return;
            }

            foreach (var t in improve)
            {
                if (t == null) continue;
                var item = new ListViewItem((t.TripNo ?? "").Trim());
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(t.HabitLabel ?? t.Habit ?? "");
                item.SubItems.Add(t.Client ?? "");
                item.SubItems.Add(t.SchedTime ?? "—");
                item.SubItems.Add(t.ActualTime ?? "—");
                item.SubItems.Add(t.Note ?? "");
                item.Tag = t;
                Color fg = SupeyTheme.ListText;
                if (t.Open)
                    fg = Color.FromArgb(220, 95, 75);
                item.ForeColor = fg;
                foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
                    sub.ForeColor = fg;
                _list.Items.Add(item);
            }
        }

        private void List_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            e.DrawDefault = false;
            SupeyListViewHelpers.PaintColumnHeaderChrome(e.Graphics, e.Bounds, drawRightSplitter: true);
            var bounds = new Rectangle(
                e.Bounds.Left + 10,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - 12),
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                e.Header?.Text ?? "",
                ListViewOwnerDrawFonts.Header,
                bounds,
                SupeyTheme.ListHeaderText,
                TextFormatFlags.Left
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis);
        }

        private void List_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = false;
        }

        private void List_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            e.DrawDefault = false;
            if (e?.Item == null || e.Graphics == null)
                return;

            bool selected = e.Item.Selected && _list.Focused;
            Color bg = selected ? SupeyTheme.ListSelected : SupeyTheme.ListBody;
            using (var brush = new SolidBrush(bg))
                e.Graphics.FillRectangle(brush, e.Bounds);

            Color fg = selected
                ? SupeyTheme.ListSelectedText
                : (e.SubItem?.ForeColor ?? e.Item.ForeColor);
            if (fg.IsEmpty || fg == Color.Transparent)
                fg = SupeyTheme.ListText;

            var textBounds = new Rectangle(
                e.Bounds.Left + 10,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - 12),
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                e.SubItem?.Text ?? "",
                ListViewOwnerDrawFonts.Cell,
                textBounds,
                fg,
                TextFormatFlags.Left
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis);
        }
    }
}
