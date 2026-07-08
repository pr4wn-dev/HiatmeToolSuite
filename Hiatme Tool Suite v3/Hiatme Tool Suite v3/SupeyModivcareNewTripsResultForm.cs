using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Themed popup after a manual Modivcare new-trip check from Schedule Builder.</summary>
    internal sealed class SupeyModivcareNewTripsResultForm : SupeyForm
    {
        private const int DialogWidth = 560;
        private const int ContentPad = 24;
        private const int FooterHeight = 56;
        private const int BodyTopPad = 16;
        private const int BodyBottomPad = 16;
        private const int SectionGap = 12;
        private const int CardInnerPad = 12;
        private const int MinListHeight = 120;
        private const int MaxListHeight = 240;
        private const int RowHeight = 24;
        private const int EmptyMessageMinHeight = 72;

        private readonly FsModivcareNewTripsSyncResult _result;
        private TableLayoutPanel _stack;
        private Label _headlineLbl;
        private Label _subtitleLbl;
        private Label _statsLbl;
        private SupeyCard _contentCard;
        private Panel _cardBody;
        private ListView _tripList;
        private Label _emptyLbl;
        private SupeyMaterialButton _goReservesBtn;

        public bool GoToReservesRequested { get; private set; }

        public static bool Show(IWin32Window owner, FsModivcareNewTripsSyncResult result)
        {
            if (result == null)
                return false;

            using (var form = new SupeyModivcareNewTripsResultForm(result))
                return form.ShowDialog(owner) == DialogResult.OK && form.GoToReservesRequested;
        }

        private SupeyModivcareNewTripsResultForm(FsModivcareNewTripsSyncResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));
            BuildUi();
            PopulateContent();
            ApplyLayoutMetrics();
            SupeyListViewHelpers.EnableDoubleBufferRecursively(this);
            SupeyDarkScrollBars.Apply(this);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        private int ContentWidth => DialogWidth - (ContentPad * 2);

        private int CardInnerWidth => ContentWidth - (CardInnerPad * 2);

        private void BuildUi()
        {
            Text = "Modivcare new trips";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = SupeyTheme.Surface;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = FooterHeight,
                BackColor = SupeyTheme.Surface,
            };

            var footerFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, ContentPad, 12),
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

            _goReservesBtn = new SupeyMaterialButton
            {
                Text = "Go to Reserves",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(132, 36),
                Margin = new Padding(0, 0, 10, 0),
                Visible = false,
            };
            _goReservesBtn.Click += (s, e) =>
            {
                GoToReservesRequested = true;
                DialogResult = DialogResult.OK;
                Close();
            };

            footerFlow.Controls.Add(okBtn);
            footerFlow.Controls.Add(_goReservesBtn);
            footer.Controls.Add(footerFlow);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(ContentPad, TitleBarHeight + BodyTopPad, ContentPad, BodyBottomPad),
            };

            _stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Width = ContentWidth,
                BackColor = SupeyTheme.Surface,
            };
            _stack.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));

            _headlineLbl = MakeStackLabel(
                "",
                new Font("Segoe UI Semibold", 11f),
                SupeyTheme.TextPrimary,
                new Padding(0));

            _subtitleLbl = MakeStackLabel(
                "",
                SupeyTheme.CaptionFont,
                SupeyTheme.TextSecondary,
                new Padding(0, 6, 0, 0));

            _contentCard = new SupeyCard
            {
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = new Padding(CardInnerPad),
                Margin = new Padding(0, SectionGap, 0, 0),
                Width = ContentWidth,
                Height = MinListHeight + (CardInnerPad * 2),
            };

            _cardBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };

            _tripList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
                Font = SupeyTheme.BodyFont,
                MultiSelect = false,
            };
            ApplyTripListColumnWidths();
            ListViewMinWidthEnforcer.Attach(_tripList);

            _emptyLbl = MakeStackLabel(
                "",
                SupeyTheme.BodyFont,
                SupeyTheme.TextMuted,
                new Padding(8, 4, 8, 4));
            _emptyLbl.TextAlign = ContentAlignment.MiddleCenter;
            _emptyLbl.Visible = false;
            _emptyLbl.Dock = DockStyle.Fill;

            _cardBody.Controls.Add(_tripList);
            _cardBody.Controls.Add(_emptyLbl);
            _contentCard.Controls.Add(_cardBody);

            _statsLbl = MakeStackLabel(
                "",
                SupeyTheme.CaptionFont,
                SupeyTheme.TextMuted,
                new Padding(0, SectionGap, 0, 0));

            _stack.Controls.Add(_headlineLbl, 0, 0);
            _stack.Controls.Add(_subtitleLbl, 0, 1);
            _stack.Controls.Add(_contentCard, 0, 2);
            _stack.Controls.Add(_statsLbl, 0, 3);

            body.Controls.Add(_stack);

            AcceptButton = okBtn;
            CancelButton = okBtn;

            Controls.Add(body);
            Controls.Add(footer);
        }

        private static Label MakeStackLabel(string text, Font font, Color color, Padding margin)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = SupeyTheme.Surface,
                AutoSize = true,
                MaximumSize = new Size(DialogWidth - (ContentPad * 2), 0),
                Margin = margin,
            };
        }

        private void ApplyTripListColumnWidths()
        {
            if (_tripList == null || _tripList.Columns.Count == 0)
                return;

            int w = CardInnerWidth;
            int tripW = 96;
            int puW = 64;
            int sectionW = 92;
            int clientW = Math.Max(120, w - tripW - puW - sectionW);
            _tripList.Columns[0].Width = tripW;
            if (_tripList.Columns.Count > 1)
                _tripList.Columns[1].Width = clientW;
            if (_tripList.Columns.Count > 2)
                _tripList.Columns[2].Width = puW;
            if (_tripList.Columns.Count > 3)
                _tripList.Columns[3].Width = sectionW;
        }

        private void ApplyLayoutMetrics()
        {
            int listHeight = _tripList.Visible
                ? MeasureListHeight(_result.Added?.Count ?? 0)
                : MeasureEmptyMessageHeight(_emptyLbl.Text);

            _contentCard.Height = listHeight + (CardInnerPad * 2);
            _contentCard.Width = ContentWidth;

            if (_statsLbl.Visible && !string.IsNullOrWhiteSpace(_statsLbl.Text))
                _statsLbl.Margin = new Padding(0, SectionGap, 0, 0);
            else
                _statsLbl.Margin = Padding.Empty;

            _stack.PerformLayout();
            _stack.Width = ContentWidth;

            int stackHeight = _stack.GetPreferredSize(new Size(ContentWidth, 0)).Height;
            int clientHeight = TitleBarHeight
                + BodyTopPad
                + stackHeight
                + BodyBottomPad
                + FooterHeight;

            clientHeight = Math.Max(340, Math.Min(640, clientHeight));
            ClientSize = new Size(DialogWidth, clientHeight);
            MinimumSize = new Size(DialogWidth, clientHeight);
        }

        private void PopulateContent()
        {
            string dateLabel = _result.ServiceDate.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
            string dateLine = "Service date  ·  " + dateLabel;

            switch (_result.Failure)
            {
                case FsModivcareNewTripsSyncFailure.NoSchedule:
                    ShowMessageState(
                        "No schedule loaded",
                        dateLine,
                        "Load a schedule preview before checking Modivcare for new trips.",
                        SupeyTheme.TextSecondary,
                        "");
                    return;

                case FsModivcareNewTripsSyncFailure.ModivcareUnavailable:
                    ShowMessageState(
                        "Modivcare not available",
                        dateLine,
                        "Could not connect to Modivcare. Sign in on the Modivcare tab and try again.",
                        SupeyTheme.ErrorText,
                        "");
                    return;

                case FsModivcareNewTripsSyncFailure.DownloadFailed:
                    ShowMessageState(
                        "Download failed",
                        dateLine,
                        "Modivcare did not return a trip list for this date. Try again in a moment.",
                        SupeyTheme.ErrorText,
                        BuildCompareStats());
                    return;

                case FsModivcareNewTripsSyncFailure.NoModivcareTrips:
                    ShowMessageState(
                        "No trips on Modivcare",
                        dateLine,
                        "Modivcare returned no trips for this service date.",
                        SupeyTheme.TextSecondary,
                        "");
                    return;
            }

            if (_result.HasAddedTrips)
            {
                EnsureTripListColumns();
                int count = _result.Added.Count;
                _headlineLbl.Text = count + " new trip" + (count == 1 ? "" : "s") + " added to Reserves";
                _headlineLbl.ForeColor = SupeyTheme.SuccessText;
                _subtitleLbl.Text = dateLine + Environment.NewLine
                    + "These trips were on Modivcare but missing from your schedule.";
                _goReservesBtn.Visible = true;

                _tripList.BeginUpdate();
                _tripList.Items.Clear();
                foreach (var entry in _result.Added)
                {
                    if (entry?.Trip == null)
                        continue;

                    var trip = entry.Trip;
                    string client = FormatClientName(trip);
                    var item = new ListViewItem((trip.TripNumber ?? "").Trim())
                    {
                        Tag = trip,
                    };
                    if (item.Text.Length == 0)
                        item.Text = "(no trip #)";
                    item.SubItems.Add(client.Length > 0 ? client : "—");
                    item.SubItems.Add(FormatPuTime(trip.PUTime));
                    item.SubItems.Add(BucketLabel(entry.Bucket));
                    _tripList.Items.Add(item);
                }
                _tripList.EndUpdate();
                _tripList.Visible = true;
                _emptyLbl.Visible = false;
                _statsLbl.Text = BuildCompareStats();
                _statsLbl.Visible = !string.IsNullOrWhiteSpace(_statsLbl.Text);
                return;
            }

            ShowMessageState(
                "No new trips",
                dateLine,
                "Every Modivcare trip for this date is already on the schedule or marked rerouted.",
                SupeyTheme.TextPrimary,
                BuildCompareStats());
        }

        private void EnsureTripListColumns()
        {
            if (_tripList.Columns.Count > 0)
            {
                ApplyTripListColumnWidths();
                return;
            }

            _tripList.Columns.Add("Trip #", 96);
            _tripList.Columns.Add("Client", 180);
            _tripList.Columns.Add("PU", 64);
            _tripList.Columns.Add("Section", 92);
            ApplyTripListColumnWidths();
        }

        private void ShowMessageState(
            string headline,
            string subtitle,
            string cardMessage,
            Color headlineColor,
            string stats)
        {
            _headlineLbl.Text = headline;
            _headlineLbl.ForeColor = headlineColor;
            _subtitleLbl.Text = subtitle;
            _tripList.Visible = false;
            _emptyLbl.Visible = true;
            _emptyLbl.Text = cardMessage;
            _statsLbl.Text = stats;
            _statsLbl.Visible = !string.IsNullOrWhiteSpace(stats);
        }

        private string BuildCompareStats()
        {
            if (_result.ModivcareTripCount <= 0 && _result.SkippedOnSchedule <= 0 && _result.SkippedRerouted <= 0)
                return "";

            var parts = new List<string>();
            if (_result.ModivcareTripCount > 0)
                parts.Add(_result.ModivcareTripCount + " Modivcare trip" + (_result.ModivcareTripCount == 1 ? "" : "s"));
            if (_result.SkippedOnSchedule > 0)
                parts.Add(_result.SkippedOnSchedule + " already on schedule");
            if (_result.SkippedRerouted > 0)
                parts.Add(_result.SkippedRerouted + " rerouted");
            return string.Join("  ·  ", parts);
        }

        private int MeasureEmptyMessageHeight(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return EmptyMessageMinHeight;

            using (var probe = new Form())
            using (var g = probe.CreateGraphics())
            {
                Size size = TextRenderer.MeasureText(
                    g,
                    message,
                    SupeyTheme.BodyFont,
                    new Size(CardInnerWidth - 16, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter);

                return Math.Max(EmptyMessageMinHeight, size.Height + 24);
            }
        }

        private static int MeasureListHeight(int rowCount)
        {
            if (rowCount <= 0)
                return MinListHeight;
            int rows = Math.Max(3, Math.Min(8, rowCount));
            return Math.Max(MinListHeight, Math.Min(MaxListHeight, (rows * RowHeight) + 28));
        }

        private static string FormatClientName(MCDownloadedTrip trip)
        {
            if (trip == null)
                return "";
            string client = (trip.ClientFullName ?? "").Trim();
            if (client.Length == 0)
                client = ((trip.ClientFirstName ?? "") + " " + (trip.ClientLastName ?? "")).Trim();
            return client;
        }

        private static string FormatPuTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "—";
            string formatted = SupeyTripTimes.FormatForSchedule(raw);
            return string.IsNullOrWhiteSpace(formatted) ? "—" : formatted;
        }

        private static string BucketLabel(ScheduleBuilderReserveBuckets.ReserveBucket bucket)
        {
            switch (bucket)
            {
                case ScheduleBuilderReserveBuckets.ReserveBucket.WillCall:
                    return "Will calls";
                case ScheduleBuilderReserveBuckets.ReserveBucket.Reroute:
                    return "Reroutes";
                case ScheduleBuilderReserveBuckets.ReserveBucket.Cancel:
                    return "Cancels";
                default:
                    return "Reservers";
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;

            if (_tripList != null && !_tripList.IsDisposed)
            {
                _tripList.BackColor = SupeyTheme.ListBody;
                _tripList.ForeColor = SupeyTheme.ListText;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.OnFormClosed(e);
        }
    }
}
