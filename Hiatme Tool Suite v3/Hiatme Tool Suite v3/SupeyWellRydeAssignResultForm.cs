using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Themed assign summary after Schedule Builder ASSIGN (same path as Analyze Trips).</summary>
    internal sealed class SupeyWellRydeAssignResultForm : SupeyForm
    {
        private const int DialogWidth = 680;
        private const int ContentPad = 24;
        private const int FooterHeight = 56;
        private const int BodyTopPad = 16;
        private const int BodyBottomPad = 16;
        private const int SectionGap = 12;
        private const int MinDialogHeight = 620;
        private const int RowHeight = 44;

        private readonly FsWellRydeAssignResult _result;
        private Label _headlineLbl;
        private Label _subtitleLbl;
        private Label _statsLbl;
        private Panel _listHost;
        private SupeyListView _list;
        private Label _emptyLbl;

        public static void Show(IWin32Window owner, FsWellRydeAssignResult result)
        {
            if (result == null)
                return;
            using (var form = new SupeyWellRydeAssignResultForm(result))
            {
                SupeyForm.CenterOnWorkingArea(form, owner);
                form.ShowDialog(owner);
            }
        }

        private SupeyWellRydeAssignResultForm(FsWellRydeAssignResult result)
        {
            _result = result;
            BuildUi();
            PopulateContent();
            ApplyLayoutMetrics();
            SupeyListViewHelpers.EnableDoubleBufferRecursively(this);
            SupeyDarkScrollBars.Apply(this);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        private int ContentWidth => DialogWidth - (ContentPad * 2);

        private int CardInnerWidth => ContentWidth - 2;

        private void BuildUi()
        {
            Text = "Assign";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Sizable = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
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
            footerFlow.Controls.Add(okBtn);
            footer.Controls.Add(footerFlow);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(ContentPad, TitleBarHeight + BodyTopPad, ContentPad, BodyBottomPad),
            };

            _headlineLbl = new Label
            {
                Text = "",
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = SupeyTheme.TextPrimary,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Dock = DockStyle.Top,
                BackColor = SupeyTheme.Surface,
            };

            _subtitleLbl = new Label
            {
                Text = "",
                Font = SupeyTheme.CaptionFont,
                ForeColor = SupeyTheme.TextSecondary,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Dock = DockStyle.Top,
                Padding = new Padding(0, 6, 0, 0),
                BackColor = SupeyTheme.Surface,
            };

            _statsLbl = new Label
            {
                Text = "",
                Font = SupeyTheme.CaptionFont,
                ForeColor = SupeyTheme.TextMuted,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Dock = DockStyle.Bottom,
                Padding = new Padding(0, SectionGap, 0, 0),
                BackColor = SupeyTheme.Surface,
            };

            _listHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = SupeyTheme.Divider,
                Margin = new Padding(0, SectionGap, 0, SectionGap),
            };

            var listBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.ListBody,
                Padding = Padding.Empty,
            };

            _list = new SupeyListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
                Font = new Font("Segoe UI", 11f),
                MultiSelect = false,
                OwnerDraw = true,
                GridLines = true,
                HideSelection = true,
                UseCompatibleStateImageBehavior = false,
                SuppressHoverRepaintFix = true,
            };
            _list.DrawColumnHeader += List_DrawColumnHeader;
            _list.DrawItem += List_DrawItem;
            _list.DrawSubItem += List_DrawSubItem;
            _list.Resize += (s, e) => ApplyColumnWidths();
            _list.SmallImageList = new ImageList
            {
                ImageSize = new Size(1, RowHeight),
                ColorDepth = ColorDepth.Depth32Bit,
            };

            _emptyLbl = new Label
            {
                Text = "",
                Font = SupeyTheme.BodyFont,
                ForeColor = SupeyTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false,
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.ListBody,
            };

            listBody.Controls.Add(_list);
            listBody.Controls.Add(_emptyLbl);
            _listHost.Controls.Add(listBody);
            try { ListViewHeaderEmptyAreaPainter.Attach(_list); } catch { }

            body.Controls.Add(_listHost);
            body.Controls.Add(_statsLbl);
            body.Controls.Add(_subtitleLbl);
            body.Controls.Add(_headlineLbl);
            AcceptButton = okBtn;
            CancelButton = okBtn;
            Controls.Add(body);
            Controls.Add(footer);
        }

        private void ApplyLayoutMetrics()
        {
            _listHost.BackColor = SupeyTheme.Divider;
            ClientSize = new Size(DialogWidth, MinDialogHeight);
            MinimumSize = new Size(DialogWidth, MinDialogHeight);
            ApplyColumnWidths();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SupeyForm.CenterOnWorkingArea(this, Owner);
            ApplyColumnWidths();
        }

        private int ListClientWidth
        {
            get
            {
                if (_list == null || !_list.IsHandleCreated || _list.ClientSize.Width <= 0)
                    return CardInnerWidth;

                int w = _list.ClientSize.Width;
                if (!_closing && _list.Items.Count > 0 && _list.ClientSize.Height > 0)
                {
                    const int headerH = 32;
                    int visibleRows = Math.Max(1, (_list.ClientSize.Height - headerH) / Math.Max(1, RowHeight));
                    if (_list.Items.Count > visibleRows)
                        w -= SystemInformation.VerticalScrollBarWidth;
                }
                return Math.Max(160, w);
            }
        }

        private void PopulateContent()
        {
            string dateLine = "Service date  ·  "
                + _result.ServiceDate.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);

            int sent = _result.SentSlots;
            _headlineLbl.Text = sent == 1
                ? "1 trip assigned to WellRyde"
                : sent.ToString() + " trips assigned to WellRyde";
            _headlineLbl.ForeColor = sent > 0 ? SupeyTheme.SuccessText : SupeyTheme.WarnText;

            _subtitleLbl.Text = dateLine + Environment.NewLine
                + _result.DriversSent.ToString() + " driver"
                + (_result.DriversSent == 1 ? "" : "s")
                + "  ·  unassigned all Assigned trips, then assigned the driver tabs.";

            if (!_result.PortalWritesEnabled)
                _subtitleLbl.Text += Environment.NewLine + "Portal writes were off — WellRyde was not changed.";

            _statsLbl.Text = "WellRyde now  ·  "
                + _result.AssignedOnWellRyde.ToString() + " Assigned  ·  "
                + _result.ReservedOnWellRyde.ToString() + " Reserved"
                + (_result.Skipped > 0
                    ? "  ·  " + _result.Skipped.ToString() + " skipped"
                    : "");
            _statsLbl.Visible = true;

            if (_result.Skips.Count > 0)
                ShowSkipList();
            else if (_result.Drivers.Count > 0)
                ShowDriverList();
            else
                ShowEmptyCard("No driver-tab trips matched WellRyde. Reserves were not assigned.");
        }

        private void ShowSkipList()
        {
            _listColumnLayoutSuspended = true;
            _list.Columns.Clear();
            _list.Columns.Add("Trip #", 96);
            _list.Columns.Add("Driver", 140);
            _list.Columns.Add("Why skipped", 200);

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var row in _result.Skips)
            {
                var item = new ListViewItem(string.IsNullOrWhiteSpace(row.TripNumber) ? "(no trip #)" : row.TripNumber);
                item.SubItems.Add(row.DriverName ?? "—");
                item.SubItems.Add(string.IsNullOrWhiteSpace(row.Reason) ? "Skipped" : row.Reason);
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            _list.Visible = true;
            _emptyLbl.Visible = false;
            _listColumnLayoutSuspended = false;
            ApplyColumnWidths();
        }

        private void ShowDriverList()
        {
            _listColumnLayoutSuspended = true;
            _list.Columns.Clear();
            _list.Columns.Add("Driver", 180);
            _list.Columns.Add("Sent", 72, HorizontalAlignment.Right);

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var row in _result.Drivers)
            {
                var item = new ListViewItem(row.DriverName ?? "—");
                item.SubItems.Add(row.Sent.ToString());
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            _list.Visible = true;
            _emptyLbl.Visible = false;
            _listColumnLayoutSuspended = false;
            ApplyColumnWidths();
        }

        private void ShowEmptyCard(string message)
        {
            _list.Visible = false;
            _emptyLbl.Visible = true;
            _emptyLbl.Text = message;
        }

        private void List_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            SupeyListViewHelpers.DrawColumnHeader(e);
        }

        private void List_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            SupeyListViewHelpers.SuppressDefaultDrawItem(e);
        }

        private void List_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (e?.Item == null || e.Graphics == null)
                return;

            Color bg = e.ItemIndex % 2 == 0 ? SupeyTheme.ListBody : SupeyTheme.ListBodyAlt;
            SupeyListViewHelpers.DrawSubItemCellBackground(e, bg);

            var textBounds = new Rectangle(
                e.Bounds.Left + 12,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - 16),
                e.Bounds.Height);
            TextFormatFlags align = TextFormatFlags.Left;
            if (e.Header != null && e.Header.TextAlign == HorizontalAlignment.Right)
                align = TextFormatFlags.Right;

            TextRenderer.DrawText(
                e.Graphics,
                e.SubItem?.Text ?? "",
                e.Item?.ListView?.Font ?? _list.Font,
                textBounds,
                SupeyTheme.ListText,
                align | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private bool _listColumnLayoutSuspended;
        private bool _closing;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            base.OnFormClosing(e);
        }

        private void ApplyColumnWidths()
        {
            if (_closing || _list == null || _list.IsDisposed || !_list.IsHandleCreated || _list.Columns.Count == 0 || _listColumnLayoutSuspended)
                return;

            if (_list.Columns.Count == 2)
                ApplyDriverColumnWidths();
            else if (_list.Columns.Count >= 3)
                ApplySkipColumnWidths();
        }

        private int MeasureColumnTextWidth(int columnIndex, string headerText, int cellPad = 24)
        {
            int max = TextRenderer.MeasureText(headerText ?? "", ListViewOwnerDrawFonts.Header).Width + cellPad;
            if (_list?.Items == null)
                return max;

            foreach (ListViewItem item in _list.Items)
            {
                if (item == null)
                    continue;

                string text;
                if (columnIndex == 0)
                    text = item.Text;
                else if (columnIndex < item.SubItems.Count)
                    text = item.SubItems[columnIndex]?.Text;
                else
                    text = "";

                max = Math.Max(max, TextRenderer.MeasureText(text ?? "", _list.Font).Width + cellPad);
            }
            return max;
        }

        private void ApplySkipColumnWidths()
        {
            if (_list.Columns.Count < 3)
                return;

            int avail = ListClientWidth;
            int tripW = Math.Min(MeasureColumnTextWidth(0, "Trip #", 20), 120);
            tripW = Math.Max(80, tripW);
            int driverW = Math.Min(MeasureColumnTextWidth(1, "Driver", 20), 180);
            driverW = Math.Max(100, driverW);
            int reasonW = avail - tripW - driverW;
            if (reasonW < 120)
            {
                reasonW = 120;
                driverW = Math.Max(100, avail - tripW - reasonW);
            }

            _list.Columns[0].Width = tripW;
            _list.Columns[1].Width = driverW;
            _list.Columns[2].Width = avail - tripW - driverW;
        }

        private void ApplyDriverColumnWidths()
        {
            if (_list.Columns.Count < 2)
                return;

            int avail = ListClientWidth;
            const int sentW = 80;
            int driverW = MeasureColumnTextWidth(0, "Driver", 20);
            driverW = Math.Max(100, Math.Min(driverW, avail - sentW - 4));

            _list.Columns[0].Width = driverW;
            _list.Columns[1].Width = avail - driverW;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            BackColor = SupeyTheme.Surface;
            _list.BackColor = SupeyTheme.ListBody;
            _list.ForeColor = SupeyTheme.ListText;
            if (_listHost != null)
                _listHost.BackColor = SupeyTheme.Divider;
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }
}
