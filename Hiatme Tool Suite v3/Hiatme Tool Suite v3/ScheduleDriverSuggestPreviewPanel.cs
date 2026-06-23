using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class ScheduleDriverSuggestPreviewPanel : Panel
    {
        private readonly Label _captionLbl;
        private readonly SupeyListView _list;

        public ScheduleDriverSuggestPreviewPanel()
        {
            BackColor = Color.FromArgb(24, 24, 24);
            SupeyDarkScrollBars.Apply(this);

            _captionLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI Semibold", 8.25f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0),
            };

            _list = new SupeyListView
            {
                Dock = DockStyle.Fill,
                MultiSelect = false,
                View = View.Details,
                FullRowSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                GridLines = false,
                BorderStyle = BorderStyle.None,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
                Font = new Font("Segoe UI", 9f),
                OwnerDraw = true,
            };

            ScheduleBuilderSuggestPreviewBinder.ConfigureListView(_list);
            _list.DrawColumnHeader += OnDrawColumnHeader;
            _list.DrawItem += OnDrawItem;
            _list.DrawSubItem += OnDrawSubItem;
            SupeyDarkScrollBars.Apply(_list);
            ListViewHeaderEmptyAreaPainter.Attach(_list);

            Controls.Add(_list);
            Controls.Add(_captionLbl);
        }

        public void SetPreview(
            MCDownloadedTrip insertedTrip,
            ScheduleBuilderDriverSuggestion suggestion,
            System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.List<ScheduleBuilderPreviewLine>> linesByTab,
            bool showGroupColors)
        {
            if (insertedTrip == null || suggestion == null || linesByTab == null)
            {
                _list.Items.Clear();
                _captionLbl.Text = "";
                return;
            }

            ScheduleBuilderSuggestPreviewBinder.Populate(
                _list, insertedTrip, suggestion, linesByTab, showGroupColors, out string caption);
            _captionLbl.Text = caption ?? "";
        }

        private static void OnDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            SupeyListViewHelpers.DrawColumnHeader(e);
        }

        private static void OnDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            var tag = e.Item?.Tag as SuggestPreviewRowTag;
            if (tag?.IsGroupBar == true)
            {
                e.DrawDefault = false;
                if (e.Item == null)
                    return;
                Rectangle bounds;
                try
                {
                    bounds = e.Item.ListView.GetItemRect(e.ItemIndex, ItemBoundsPortion.Entire);
                }
                catch
                {
                    bounds = e.Bounds;
                }

                SupeyListViewHelpers.PaintMergedDetailsRow(
                    e.Graphics, bounds, tag.BarColor, tag.GroupBarText, tag.BarFore, e.Item.ListView.Font, boldText: true);
                return;
            }

            SupeyListViewHelpers.SuppressDefaultDrawItem(e);
        }

        private static void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            var tag = e.Item?.Tag as SuggestPreviewRowTag;
            if (tag?.IsGroupBar == true || tag?.IsGap == true)
            {
                e.DrawDefault = false;
                return;
            }

            Color bg = e.Item?.SubItems[e.ColumnIndex].BackColor ?? SupeyTheme.ListBody;
            Color fg = e.Item?.SubItems[e.ColumnIndex].ForeColor ?? SupeyTheme.ListText;

            SupeyListViewHelpers.DrawSubItemCellBackground(e, bg);
            var textBounds = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                SupeyListViewHelpers.GetCellDisplayText(e.Item?.ListView, e.ColumnIndex, e.SubItem?.Text ?? ""),
                e.Item?.ListView?.Font ?? SystemFonts.DefaultFont,
                textBounds,
                fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds, e.Item?.ListView);
        }
    }
}
