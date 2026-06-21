using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Theme-driven Material-style toggle — the Supey replacement for MaterialSwitch.
    /// Derives from <see cref="Control"/> (not <see cref="CheckBox"/>) so WinForms does not leave
    /// a native checkbox chrome ghost at stale coordinates when reparented or moved.
    /// </summary>
    internal class SupeySwitch : Control
    {
        private const int TrackW = 36;
        private const int TrackH = 14;
        private const int Radius = TrackH / 2;
        private const int Thumb = 20;
        private const int TrackX = Thumb / 2 - Radius;

        private readonly Timer _anim;
        private float _t;
        private bool _hovered;
        private bool _checked;

        public SupeySwitch()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.StandardClick
                | ControlStyles.StandardDoubleClick, true);
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            Font = SupeyTheme.BodyFont;
            Cursor = Cursors.Hand;
            AutoSize = false;
            TabStop = true;
            Size = new Size(200, 26);
            _t = 0f;
            _anim = new Timer { Interval = 15 };
            _anim.Tick += Animate;
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool UseVisualStyleBackColor { get; set; } = true;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                if (!_anim.Enabled) _t = value ? 1f : 0f;
                OnCheckedChanged(EventArgs.Empty);
                CheckedChanged?.Invoke(this, EventArgs.Empty);
                if (!_anim.Enabled) _anim.Start();
                Invalidate();
            }
        }

        public event EventHandler CheckedChanged;

        // ── Designer-compat no-ops ────────────────────────────────────────────────
        public int Depth { get; set; }
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;
        public Point MouseLocation { get; set; } = new Point(-1, -1);
        public bool Ripple { get; set; } = true;

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ForeColor = SupeyTheme.TextPrimary;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                _anim?.Stop();
                _anim?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected virtual void OnCheckedChanged(EventArgs e) { }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                Checked = !Checked;
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int textW = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
            int w = TrackX + TrackW + 12 + textW + 6;
            int h = Math.Max(26, Thumb + 6);
            return new Size(w, h);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (AutoSize) Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }

        private void Animate(object sender, EventArgs e)
        {
            float target = Checked ? 1f : 0f;
            const float step = 0.14f;
            if (_t < target) _t = Math.Min(target, _t + step);
            else if (_t > target) _t = Math.Max(target, _t - step);
            Invalidate();
            if (Math.Abs(_t - target) < 0.001f) { _t = target; _anim.Stop(); }
        }

        private float Eased => _t * _t * (3f - 2f * _t);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            float p = Eased;
            int trackY = (Height - TrackH) / 2;
            var track = new Rectangle(TrackX, trackY, TrackW, TrackH);

            Color trackColor = Blend(SupeyTheme.SurfaceElevated, SupeyTheme.AccentPrimary, p);
            using (var path = Rounded(track, Radius))
            using (var b = new SolidBrush(trackColor))
                g.FillPath(b, path);
            if (p < 0.5f)
                using (var path = Rounded(track, Radius))
                using (var pen = new Pen(SupeyTheme.BorderSubtle))
                    g.DrawPath(pen, path);

            int cxBegin = TrackX + Radius;
            int cxEnd = TrackX + TrackW - Radius;
            int cx = (int)(cxBegin + (cxEnd - cxBegin) * p);
            int cy = trackY + TrackH / 2;
            var thumb = new Rectangle(cx - Thumb / 2, cy - Thumb / 2, Thumb, Thumb);

            if (_hovered && Enabled)
            {
                int d = Thumb + 12;
                using (var b = new SolidBrush(Color.FromArgb(38, SupeyTheme.AccentPrimary)))
                    g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
            }

            using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            {
                g.FillEllipse(sh, thumb.X - 1, thumb.Y + 1, thumb.Width + 2, thumb.Height + 2);
                g.FillEllipse(sh, thumb.X, thumb.Y + 1, thumb.Width, thumb.Height + 1);
            }

            Color thumbColor = Blend(SupeyTheme.TextSecondary, SupeyTheme.AccentPrimary, p);
            using (var b = new SolidBrush(thumbColor))
                g.FillEllipse(b, thumb);

            if (!string.IsNullOrEmpty(Text))
            {
                var textRect = new Rectangle(TrackX + TrackW + 12, 0, Width - (TrackX + TrackW + 12), Height);
                TextRenderer.DrawText(g, Text, Font, textRect, ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }
    }
}
