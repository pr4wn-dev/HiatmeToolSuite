using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Theme-driven check box — the Supey replacement for MaterialCheckbox. Derives from
    /// <see cref="Control"/> (not <see cref="CheckBox"/>) so WinForms does not leave a native
    /// checkbox chrome ghost when reparented, resized, or owner-painted.
    /// </summary>
    internal class SupeyCheckbox : Control
    {
        private const int BoxSize = 18;
        private const int BoxTextGap = 8;
        private const int TextPadRight = 6;

        private CheckState _checkState = CheckState.Unchecked;

        public SupeyCheckbox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.StandardClick
                | ControlStyles.StandardDoubleClick, true);
            BackColor = SupeyTheme.SurfaceBase;
            ForeColor = SupeyTheme.TextPrimary;
            Font = SupeyTheme.BodyFont;
            Cursor = Cursors.Hand;
            AutoSize = false;
            TabStop = true;
            Size = new Size(160, 24);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool UseVisualStyleBackColor { get; set; } = true;

        public bool Checked
        {
            get => _checkState == CheckState.Checked;
            set => CheckState = value ? CheckState.Checked : CheckState.Unchecked;
        }

        public CheckState CheckState
        {
            get => _checkState;
            set
            {
                if (_checkState == value) return;
                _checkState = value;
                OnCheckedChanged(EventArgs.Empty);
                OnCheckStateChanged(EventArgs.Empty);
                CheckedChanged?.Invoke(this, EventArgs.Empty);
                CheckStateChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public event EventHandler CheckedChanged;
        public event EventHandler CheckStateChanged;

        // ── Designer-compat no-ops ────────────────────────────────────────────────
        public int Depth { get; set; }
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;
        public Point MouseLocation { get; set; } = new Point(-1, -1);
        public bool Ripple { get; set; }
        /// <summary>Accepted for Designer compatibility (MaterialCheckbox.ReadOnly); unused.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>Preferred width for the current label at the control height.</summary>
        public int PreferredWidth(int height = 0)
        {
            int h = height > 0 ? height : Height;
            int textW = string.IsNullOrEmpty(Text)
                ? 0
                : TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, h),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            return BoxSize + BoxTextGap + textW + TextPadRight;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ForeColor = SupeyTheme.TextPrimary;
            SyncSurfaceBackColor();
            Invalidate();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            SyncSurfaceBackColor();
        }

        private void SyncSurfaceBackColor()
        {
            if (Parent != null && !Parent.IsDisposed)
                BackColor = Parent.BackColor;
            else
                BackColor = SupeyTheme.SurfaceBase;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected virtual void OnCheckedChanged(EventArgs e) { }
        protected virtual void OnCheckStateChanged(EventArgs e) { }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled && !ReadOnly)
                Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && !ReadOnly && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                Checked = !Checked;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            int boxY = (Height - BoxSize) / 2;
            var box = new Rectangle(0, boxY, BoxSize, BoxSize);

            using (var fill = new SolidBrush(Checked ? SupeyTheme.AccentPrimary : SupeyTheme.SurfaceElevated))
                g.FillRectangle(fill, box);
            using (var pen = new Pen(Checked ? SupeyTheme.AccentPrimary : SupeyTheme.BorderSubtle))
                g.DrawRectangle(pen, box);

            if (Checked)
            {
                using (var pen = new Pen(SupeyTheme.OnAccentText, 2f))
                {
                    g.DrawLines(pen, new[]
                    {
                        new Point(box.Left + 4, box.Top + 9),
                        new Point(box.Left + 7, box.Top + 13),
                        new Point(box.Left + 14, box.Top + 5),
                    });
                }
            }

            if (!string.IsNullOrEmpty(Text))
            {
                var textRect = new RectangleF(BoxSize + BoxTextGap, 0, Width - BoxSize - BoxTextGap, Height);
                using (var brush = new SolidBrush(ForeColor))
                {
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Near,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.NoWrap,
                    };
                    g.DrawString(Text, Font, brush, textRect, sf);
                }
            }
        }
    }
}
