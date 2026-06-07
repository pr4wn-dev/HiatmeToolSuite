using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Owner-drawn WinForms tooltips that match <see cref="SupeyTheme"/> (dark surface, light text).
    /// Use <see cref="Create"/> instead of <c>new ToolTip()</c> anywhere in the Supey / Schedule Builder UI.
    /// </summary>
    internal static class SupeyToolTip
    {
        private static readonly Font TipFont = SupeyTheme.CaptionFont;
        private const int PadH = 10;
        private const int PadV = 8;
        private const int MaxWidth = 440;

        public static ToolTip Create(
            bool showAlways = true,
            int autoPopDelay = 10000,
            int initialDelay = 350,
            int reshowDelay = 200)
        {
            var tip = new ToolTip
            {
                AutoPopDelay = autoPopDelay,
                InitialDelay = initialDelay,
                ReshowDelay = reshowDelay,
                ShowAlways = showAlways,
                OwnerDraw = true,
            };
            tip.Draw += OnDraw;
            tip.Popup += OnPopup;
            return tip;
        }

        /// <summary>
        /// Replaces the built-in ListView item tooltip (light OS theme) with our dark owner-draw tip.
        /// </summary>
        public static ToolTip WireListViewItems(ListView listView)
        {
            if (listView == null)
                return null;

            var tip = Create();
            listView.ShowItemToolTips = false;
            listView.MouseMove += (s, e) =>
            {
                var hit = listView.HitTest(e.Location);
                string text = hit.Item?.ToolTipText;
                tip.SetToolTip(listView, string.IsNullOrWhiteSpace(text) ? "" : text);
            };
            return tip;
        }

        private static void OnDraw(object sender, DrawToolTipEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = e.Bounds;

            using (var back = new SolidBrush(SupeyTheme.SurfaceElevated))
                g.FillRectangle(back, bounds);

            using (var border = new Pen(SupeyTheme.BorderSubtle))
                g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

            var textRect = new Rectangle(
                bounds.X + PadH,
                bounds.Y + PadV / 2,
                bounds.Width - PadH * 2,
                bounds.Height - PadV);

            TextRenderer.DrawText(
                g,
                e.ToolTipText ?? "",
                TipFont,
                textRect,
                SupeyTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        private static void OnPopup(object sender, PopupEventArgs e)
        {
            var tip = sender as ToolTip;
            string text = tip?.GetToolTip(e.AssociatedControl);
            if (string.IsNullOrEmpty(text))
                return;

            Size sz = TextRenderer.MeasureText(
                text,
                TipFont,
                new Size(MaxWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.Left);

            e.ToolTipSize = new Size(
                Math.Max(48, sz.Width + PadH * 2),
                Math.Max(24, sz.Height + PadV));
        }
    }
}
