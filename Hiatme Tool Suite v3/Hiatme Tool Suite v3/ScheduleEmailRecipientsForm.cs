using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class ScheduleEmailRecipientEntry
    {
        public string TabName { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }

        public bool CanSend => !string.IsNullOrWhiteSpace(Email);
    }

    /// <summary>Pick which drivers receive the full schedule workbook before Gmail send.</summary>
    internal partial class ScheduleEmailRecipientsForm : MaterialForm
    {
        private static Color ListBg => SupeyTheme.ListBody;
        private static Color ListSelected => SupeyTheme.ListSelected;
        private static Color ListText => SupeyTheme.ListText;
        private static Color ListSelectedText => SupeyTheme.ListSelectedText;
        private static Color ListTextDim => Color.FromArgb(150, 150, 150);

        private readonly List<ScheduleEmailRecipientEntry> _allRecipients;
        private bool _suppressRecipientCheckEvents;

        private Label _headerLbl;
        private Label _contextLbl;
        private Label _summaryLbl;
        private SupeyListView _driverList;
        private MaterialButton _checkAllBtn;
        private MaterialButton _checkNoneBtn;
        private DarkOnAccentMaterialButton _sendBtn;
        private MaterialButton _cancelBtn;

        public IList<ScheduleEmailRecipientEntry> SelectedRecipients { get; private set; }
            = new List<ScheduleEmailRecipientEntry>();

        public ScheduleEmailRecipientsForm(
            IEnumerable<ScheduleEmailRecipientEntry> recipients,
            DateTime serviceDate,
            string fromAddress)
        {
            _allRecipients = (recipients ?? Array.Empty<ScheduleEmailRecipientEntry>())
                .Where(r => r != null)
                .ToList();

            try
            {
                var mgr = MaterialSkinManager.Instance;
                mgr.AddFormToManage(this);
                mgr.Theme = MaterialSkinManager.Themes.DARK;
                mgr.ColorScheme = new ColorScheme(
                    Primary.Grey900, Primary.Grey800, Primary.BlueGrey500, Accent.Lime700, TextShade.WHITE);
            }
            catch { }

            BuildUi(serviceDate, fromAddress);
            SupeyListViewHelpers.EnableDoubleBufferRecursively(this);
            SupeyDarkScrollBars.Apply(this);
            PopulateList();
            UpdateSummary();
        }

        private void BuildUi(DateTime serviceDate, string fromAddress)
        {
            Text = "Email schedules";
            ClientSize = new Size(640, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(33, 33, 33);

            _headerLbl = new Label
            {
                Text = "Choose drivers to email",
                Location = new Point(20, 78),
                Size = new Size(600, 24),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI Semibold", 11f),
                BackColor = Color.Transparent,
            };

            string from = (fromAddress ?? "").Trim();
            _contextLbl = new Label
            {
                Text = "Schedule for " + serviceDate.ToString("MMMM d, yyyy")
                    + " · full workbook (.xlsx, all tabs)"
                    + (from.Length > 0 ? " · From: " + from : ""),
                Location = new Point(20, 104),
                Size = new Size(600, 36),
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.Transparent,
            };

            _checkAllBtn = MakeTextButton("CHECK ALL", new Point(20, 148), new Size(100, 32));
            _checkAllBtn.Click += (s, e) => SetAllChecks(true);

            _checkNoneBtn = MakeTextButton("CHECK NONE", new Point(128, 148), new Size(110, 32));
            _checkNoneBtn.Click += (s, e) => SetAllChecks(false);

            _driverList = new SupeyListView
            {
                Location = new Point(20, 188),
                Size = new Size(600, 260),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = ListBg,
                ForeColor = ListText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Archivo Medium", 9.75f),
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                CheckBoxes = true,
                View = View.Details,
                OwnerDraw = true,
                UseCompatibleStateImageBehavior = false,
            };
            _driverList.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "Send", Width = 52 },
                new ColumnHeader { Text = "Driver", Width = 220 },
                new ColumnHeader { Text = "Email", Width = 300 },
            });
            _driverList.DrawColumnHeader += OnDrawColumnHeader;
            _driverList.DrawItem += OnDrawItem;
            _driverList.DrawSubItem += OnDrawSubItem;
            _driverList.ItemChecked += (s, e) => OnItemChecked(e.Item);
            _driverList.ItemCheck += OnItemCheck;

            _summaryLbl = new Label
            {
                Location = new Point(20, 456),
                Size = new Size(400, 20),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.Transparent,
            };

            _cancelBtn = MakeTextButton("CANCEL", new Point(424, 472), new Size(96, 36));
            _cancelBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancelBtn.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _sendBtn = new DarkOnAccentMaterialButton
            {
                Text = "SEND",
                Location = new Point(524, 472),
                Size = new Size(96, 36),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Type = MaterialButton.MaterialButtonType.Contained,
                Density = MaterialButton.MaterialButtonDensity.Default,
                UseAccentColor = true,
            };
            _sendBtn.Click += (s, e) => OnSendClicked();

            Controls.Add(_headerLbl);
            Controls.Add(_contextLbl);
            Controls.Add(_checkAllBtn);
            Controls.Add(_checkNoneBtn);
            Controls.Add(_driverList);
            Controls.Add(_summaryLbl);
            Controls.Add(_cancelBtn);
            Controls.Add(_sendBtn);

            try
            {
                ListViewSorter.Attach(_driverList);
                ListViewMinWidthEnforcer.Attach(_driverList);
                ListViewHeaderEmptyAreaPainter.Attach(_driverList);
            }
            catch { }
        }

        private static MaterialButton MakeTextButton(string text, Point location, Size size)
        {
            return new MaterialButton
            {
                Text = text,
                Location = location,
                AutoSize = false,
                Size = size,
                Type = MaterialButton.MaterialButtonType.Text,
                Density = MaterialButton.MaterialButtonDensity.Default,
                UseAccentColor = false,
                NoAccentTextColor = Color.Gainsboro,
            };
        }

        private void PopulateList()
        {
            _suppressRecipientCheckEvents = true;
            _driverList.BeginUpdate();
            try
            {
                _driverList.Items.Clear();
                foreach (var entry in _allRecipients)
                {
                    if (entry == null)
                        continue;

                    string email = entry.CanSend ? entry.Email.Trim() : "(no email on roster)";
                    var item = new ListViewItem(new[] { "", entry.DisplayName ?? entry.TabName ?? "", email })
                    {
                        Tag = entry,
                        Checked = entry.CanSend,
                    };
                    _driverList.Items.Add(item);
                }
            }
            finally
            {
                _driverList.EndUpdate();
                _suppressRecipientCheckEvents = false;
            }

            ListViewMinWidthEnforcer.ScheduleRecompute(_driverList);
            UpdateSummary();
            UpdateSendEnabled();
        }

        private void SetAllChecks(bool check)
        {
            _suppressRecipientCheckEvents = true;
            _driverList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in _driverList.Items)
                {
                    if (item == null)
                        continue;

                    var entry = item.Tag as ScheduleEmailRecipientEntry;
                    if (entry != null && entry.CanSend)
                        item.Checked = check;
                }
            }
            finally
            {
                _driverList.EndUpdate();
                _suppressRecipientCheckEvents = false;
            }

            UpdateSummary();
            UpdateSendEnabled();
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressRecipientCheckEvents)
                return;
            if (e.Index < 0 || e.Index >= _driverList.Items.Count)
                return;

            var item = _driverList.Items[e.Index];
            if (item == null)
                return;

            var entry = item.Tag as ScheduleEmailRecipientEntry;
            if (entry != null && !entry.CanSend)
                e.NewValue = CheckState.Unchecked;
        }

        private void OnItemChecked(ListViewItem item)
        {
            if (item == null || _suppressRecipientCheckEvents)
                return;

            var entry = item.Tag as ScheduleEmailRecipientEntry;
            if (entry != null && !entry.CanSend && item.Checked)
            {
                _suppressRecipientCheckEvents = true;
                try { item.Checked = false; }
                finally { _suppressRecipientCheckEvents = false; }
            }

            UpdateSummary();
            UpdateSendEnabled();
        }

        private void UpdateSummary()
        {
            if (_summaryLbl == null)
                return;

            int canSend = _allRecipients.Count(entry => entry != null && entry.CanSend);
            int selected = 0;
            foreach (ListViewItem item in _driverList.Items)
            {
                if (item != null && item.Checked)
                    selected++;
            }

            int missing = _allRecipients.Count(entry => entry == null || !entry.CanSend);

            _summaryLbl.Text = selected + " selected"
                + (canSend > 0 ? " · " + canSend + " with email" : "")
                + (missing > 0 ? " · " + missing + " skipped (no email)" : "");
        }

        private void UpdateSendEnabled()
        {
            if (_sendBtn == null)
                return;

            bool any = false;
            foreach (ListViewItem item in _driverList.Items)
            {
                if (item != null && item.Checked)
                {
                    any = true;
                    break;
                }
            }

            _sendBtn.Enabled = any;
        }

        private void OnSendClicked()
        {
            var picks = new List<ScheduleEmailRecipientEntry>();
            foreach (ListViewItem item in _driverList.Items)
            {
                if (item == null || !item.Checked)
                    continue;
                var r = item.Tag as ScheduleEmailRecipientEntry;
                if (r != null && r.CanSend)
                    picks.Add(r);
            }

            if (picks.Count == 0)
                return;

            SelectedRecipients = picks;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            SupeyListViewHelpers.DrawColumnHeader(e);
        }

        private void OnDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            SupeyListViewHelpers.SuppressDefaultDrawItem(e);
        }

        private void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item != null && e.Item.Selected;
            var entry = e.Item?.Tag as ScheduleEmailRecipientEntry;
            bool dim = entry != null && !entry.CanSend;

            SupeyListViewHelpers.DrawSubItemCellBackground(
                e, selected ? ListSelected : ListBg);

            if (e.ColumnIndex == 0)
            {
                bool canCheck = entry != null && entry.CanSend;
                SupeyListViewHelpers.DrawModernCheckbox(
                    e.Graphics, e.Bounds, canCheck && e.Item != null && e.Item.Checked, selected);
                SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds);
                return;
            }

            Color fg = selected ? ListSelectedText : (dim ? ListTextDim : ListText);
            var bounds = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 12, e.Bounds.Height);
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", _driverList.Font, bounds, fg, flags);
            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds);
        }
    }
}
