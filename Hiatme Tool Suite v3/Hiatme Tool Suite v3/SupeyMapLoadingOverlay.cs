using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Single-surface map loading veil — opaque map backdrop, one dot lit at a time, status text.
    /// No child controls (avoids WinForms transparency / z-order artifacts over GMap).
    /// </summary>
    internal sealed class SupeyMapLoadingOverlay : Control
    {
        public static readonly Color MapBackdrop = Color.FromArgb(30, 30, 30);

        private const int DotCount = 8;
        private const int SpinnerDiameter = 52;
        private static readonly Color DimDot = Color.FromArgb(72, 72, 72);
        private static readonly Color LitDot = Color.FromArgb(215, 218, 222);
        private static readonly Font MessageFont = new Font("Segoe UI", 9.5f);

        private readonly Timer _timer;
        private int _tick;
        private string _message = "Loading map data…";

        public SupeyMapLoadingOverlay()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw,
                true);

            BackColor = MapBackdrop;
            Visible = false;
            TabStop = false;

            _timer = new Timer { Interval = 130 };
            _timer.Tick += (s, e) =>
            {
                _tick = (_tick + 1) % DotCount;
                Invalidate();
            };
        }

        public string Message
        {
            get => _message;
            set
            {
                string text = string.IsNullOrWhiteSpace(value) ? _message : value.Trim();
                if (string.Equals(_message, text, StringComparison.Ordinal))
                    return;
                _message = text;
                Invalidate();
            }
        }

        public bool IsAnimating
        {
            get => _timer.Enabled;
            set
            {
                if (value)
                {
                    _tick = 0;
                    if (!IsDisposed)
                        _timer.Start();
                }
                else
                {
                    _timer.Stop();
                }

                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (var brush = new SolidBrush(MapBackdrop))
                pevent.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var backdrop = new SolidBrush(MapBackdrop))
                g.FillRectangle(backdrop, ClientRectangle);

            int cx = Width / 2;
            int cy = Height / 2;
            int textHeight = (int)Math.Ceiling(g.MeasureString(_message, MessageFont, Width).Height);
            int blockHeight = SpinnerDiameter + 10 + textHeight;
            int spinnerCy = cy - blockHeight / 2 + SpinnerDiameter / 2;

            DrawSpinner(g, cx, spinnerCy);

            var textRect = new RectangleF(0, spinnerCy + SpinnerDiameter / 2 + 10, Width, textHeight + 4);
            using (var brush = new SolidBrush(SupeyTheme.TextSecondary))
            {
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Near,
                    Trimming = StringTrimming.EllipsisCharacter,
                };
                g.DrawString(_message, MessageFont, brush, textRect, format);
            }
        }

        private void DrawSpinner(Graphics g, int cx, int cy)
        {
            float orbit = SpinnerDiameter / 2f - 8f;
            int active = _tick % DotCount;

            for (int i = 0; i < DotCount; i++)
            {
                bool lit = i == active;
                float dotSize = lit ? 7f : 5.5f;
                Color color = lit ? LitDot : DimDot;

                double angle = (i * (Math.PI * 2 / DotCount)) - (Math.PI / 2);
                float x = cx + (float)(Math.Cos(angle) * orbit);
                float y = cy + (float)(Math.Sin(angle) * orbit);

                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, x - dotSize / 2f, y - dotSize / 2f, dotSize, dotSize);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _timer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
