using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Material-style field dropdown: fixed width, tall rows, owner-draw selection — matches
    /// <see cref="SupeyComboBox"/> popup styling without a native ComboBox HWND.
    /// </summary>
    internal sealed class SupeyFieldDropDownPopup : ToolStripDropDown
    {
        private const int RowPadX = 16;

        private readonly ListBox _list;
        private readonly ToolStripControlHost _host;
        private int _pickedIndex = -1;

        public SupeyFieldDropDownPopup()
        {
            _list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 44,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
            };
            _list.DrawItem += List_DrawItem;
            _list.MouseUp += List_MouseUp;
            _list.KeyDown += List_KeyDown;

            _host = new ToolStripControlHost(_list)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
            };

            AutoSize = false;
            AutoClose = true;
            DropShadowEnabled = true;
            Padding = new Padding(1);
            Margin = Padding.Empty;
            BackColor = SupeyTheme.BorderSubtle;
            Items.Add(_host);

            Closed += (_, __) =>
            {
                int idx = _pickedIndex;
                _pickedIndex = -1;
                if (idx >= 0)
                    ItemPicked?.Invoke(idx);
            };
        }

        public event Action<int> ItemPicked;

        public void ShowBelow(Control anchor, string[] labels, int selectedIndex, int rowHeight, int maxItems, int width)
        {
            if (anchor == null || labels == null || labels.Length == 0) return;

            _pickedIndex = -1;
            _list.Font = anchor.Font;
            _list.ItemHeight = rowHeight;
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Items.AddRange(labels);
            _list.EndUpdate();

            if (selectedIndex >= 0 && selectedIndex < _list.Items.Count)
                _list.SelectedIndex = selectedIndex;

            int visible = Math.Min(_list.Items.Count, Math.Max(1, maxItems));
            int listH = visible * rowHeight + 2;
            int w = Math.Max(width, 120);

            _list.Size = new Size(w - 2, listH);
            _host.Size = _list.Size;
            Size = new Size(w, listH + 2);

            Show(anchor, new Point(0, anchor.Height));
            if (_list.Items.Count > 0)
                _list.Focus();
        }

        private void List_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? SupeyTheme.ListSelected : SupeyTheme.ListBody;
            Color fore = selected ? SupeyTheme.ListSelectedText : SupeyTheme.ListText;
            using (var b = new SolidBrush(back))
                e.Graphics.FillRectangle(b, e.Bounds);
            string text = _list.Items[e.Index]?.ToString() ?? string.Empty;
            var textRect = new Rectangle(e.Bounds.X + RowPadX, e.Bounds.Y, e.Bounds.Width - RowPadX - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, _list.Font, textRect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void List_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int idx = _list.IndexFromPoint(e.Location);
            if (idx < 0) return;
            _pickedIndex = idx;
            Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && _list.SelectedIndex >= 0)
            {
                _pickedIndex = _list.SelectedIndex;
                Close(ToolStripDropDownCloseReason.ItemClicked);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Close(ToolStripDropDownCloseReason.Keyboard);
                e.Handled = true;
            }
        }
    }
}
