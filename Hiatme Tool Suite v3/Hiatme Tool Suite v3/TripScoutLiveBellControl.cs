using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// WellRyde notification bell for Trip Scout live panel — shakes when will-calls are ready.
    /// </summary>
    internal sealed class TripScoutLiveBellControl : Control
    {
        private readonly Timer _shakeTimer;
        private int _shakeFrame;
        private bool _shaking;
        private int _badgeCount;

        public event EventHandler BellClicked;

        public TripScoutLiveBellControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            Size = new Size(32, 32);
            Cursor = Cursors.Hand;
            TabStop = false;
            AccessibleName = "WellRyde notifications";
            AccessibleRole = AccessibleRole.PushButton;

            _shakeTimer = new Timer { Interval = 60 };
            _shakeTimer.Tick += (_, __) =>
            {
                _shakeFrame = (_shakeFrame + 1) % 12;
                Invalidate();
            };

            Click += (_, __) => BellClicked?.Invoke(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _shakeTimer?.Dispose();
            base.Dispose(disposing);
        }

        public void SetNotificationState(int badgeCount, bool shouldShake)
        {
            _badgeCount = Math.Max(0, badgeCount);
            _shaking = shouldShake && _badgeCount > 0;
            if (_shaking)
            {
                if (!_shakeTimer.Enabled)
                {
                    _shakeFrame = 0;
                    _shakeTimer.Start();
                }
            }
            else
            {
                _shakeTimer.Stop();
                _shakeFrame = 0;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? SupeyTheme.SurfaceHeader);

            float angle = 0f;
            if (_shaking)
            {
                // Swing back and forth like a bell clapper.
                float[] swing = { -14f, 12f, -10f, 8f, -6f, 5f, -3f, 2f, 0f, 0f, 0f, 0f };
                angle = swing[_shakeFrame % swing.Length];
            }

            var center = new PointF(Width / 2f, Height / 2f + 1f);
            e.Graphics.TranslateTransform(center.X, center.Y);
            e.Graphics.RotateTransform(angle);
            e.Graphics.TranslateTransform(-center.X, -center.Y);

            Color bellColor = _badgeCount > 0
                ? Color.FromArgb(255, 220, 120)
                : SupeyTheme.TextSecondary;

            DrawBell(e.Graphics, center, bellColor);

            e.Graphics.ResetTransform();

            if (_badgeCount > 0)
                DrawBadge(e.Graphics, _badgeCount);
        }

        private static void DrawBell(Graphics g, PointF center, Color fill)
        {
            float w = 14f;
            float h = 15f;
            float cx = center.X;
            float top = center.Y - h * 0.55f;

            using (var path = new GraphicsPath())
            {
                path.AddBezier(
                    cx - w * 0.45f, top + h * 0.15f,
                    cx - w * 0.55f, top + h * 0.85f,
                    cx - w * 0.35f, top + h,
                    cx, top + h);
                path.AddBezier(
                    cx, top + h,
                    cx + w * 0.35f, top + h,
                    cx + w * 0.55f, top + h * 0.85f,
                    cx + w * 0.45f, top + h * 0.15f);
                path.CloseFigure();

                using (var brush = new SolidBrush(fill))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(180, 140, 60), 1.2f))
                    g.DrawPath(pen, path);
            }

            // Clapper
            using (var brush = new SolidBrush(Color.FromArgb(200, 160, 70)))
                g.FillEllipse(brush, cx - 2.5f, top + h + 1f, 5f, 5f);

            // Top knob
            using (var brush = new SolidBrush(fill))
                g.FillRectangle(brush, cx - 2f, top - 3f, 4f, 4f);
        }

        private void DrawBadge(Graphics g, int count)
        {
            string text = count > 9 ? "9+" : count.ToString();
            var rect = new Rectangle(ClientSize.Width - 16, 0, 16, 14);
            using (var brush = new SolidBrush(Color.FromArgb(220, 70, 60)))
            {
                g.FillEllipse(brush, rect.X, rect.Y, rect.Width, rect.Height);
            }
            TextRenderer.DrawText(
                g,
                text,
                new Font("Segoe UI", 7f, FontStyle.Bold),
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
