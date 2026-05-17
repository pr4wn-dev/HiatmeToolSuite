using System.Drawing;
using System.Drawing.Drawing2D;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Generates the small ToolStrip icons (16×16 and 20×20) used by the Trip Scout right-click
    /// menu. Drawn at runtime with GDI+ so we don't have to ship more PNG resources for two glyphs.
    /// </summary>
    /// <remarks>
    /// Icons are cached after first request — menu items hold the same Bitmap reference for the
    /// lifetime of the app and we never re-allocate per right-click.
    /// </remarks>
    internal static class MenuIconFactory
    {
        private static Bitmap _assignIcon;
        private static Bitmap _unassignIcon;
        private static Bitmap _locateIcon;
        private static Bitmap _copyIcon;
        private static Bitmap _copyAllIcon;
        private static Bitmap _clearIcon;

        private const int IconSize = 20;
        private static readonly Color PersonColor = Color.FromArgb(220, 220, 220);
        private static readonly Color AssignBadge = Color.FromArgb(76, 175, 80);   // material green 500
        private static readonly Color UnassignBadge = Color.FromArgb(229, 57, 53); // material red 600
        private static readonly Color LocateBadge = Color.FromArgb(33, 150, 243);  // material blue 500
        private static readonly Color BadgeOutline = Color.FromArgb(35, 35, 35);
        private static readonly Color BadgeGlyph = Color.White;
        // Used by the Warnings list right-click menu (Supey Schedule tab).
        private static readonly Color CopyPaperFill = Color.FromArgb(225, 225, 225);
        private static readonly Color CopyPaperEdge = Color.FromArgb(35, 35, 35);
        private static readonly Color CopyPaperLine = Color.FromArgb(110, 110, 110);
        private static readonly Color TrashBody = Color.FromArgb(220, 220, 220);
        private static readonly Color TrashLid = Color.FromArgb(229, 57, 53); // red lid telegraphs "delete"

        public static Bitmap GetAssignIcon()
        {
            if (_assignIcon == null)
                _assignIcon = BuildIcon(isAssign: true);
            return _assignIcon;
        }

        public static Bitmap GetUnassignIcon()
        {
            if (_unassignIcon == null)
                _unassignIcon = BuildIcon(isAssign: false);
            return _unassignIcon;
        }

        /// <summary>Map pin (teardrop) glyph used by the "Locate driver on map" menu item.</summary>
        public static Bitmap GetLocateIcon()
        {
            if (_locateIcon == null)
                _locateIcon = BuildLocateIcon();
            return _locateIcon;
        }

        /// <summary>Single-document copy glyph for the "Copy selected" warning menu item.</summary>
        public static Bitmap GetCopyIcon()
        {
            if (_copyIcon == null)
                _copyIcon = BuildCopyIcon(stacked: false);
            return _copyIcon;
        }

        /// <summary>Two-document copy glyph for the "Copy all" warning menu item.</summary>
        public static Bitmap GetCopyAllIcon()
        {
            if (_copyAllIcon == null)
                _copyAllIcon = BuildCopyIcon(stacked: true);
            return _copyAllIcon;
        }

        /// <summary>Trash-can glyph (red lid) for the "Clear all warnings" menu item.</summary>
        public static Bitmap GetClearIcon()
        {
            if (_clearIcon == null)
                _clearIcon = BuildClearIcon();
            return _clearIcon;
        }

        private static Bitmap BuildCopyIcon(bool stacked)
        {
            var bmp = new Bitmap(IconSize, IconSize);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                using (var fill = new SolidBrush(CopyPaperFill))
                using (var edge = new Pen(CopyPaperEdge, 1.2f))
                using (var lines = new Pen(CopyPaperLine, 1f))
                {
                    if (stacked)
                    {
                        // Back document peeks out behind the front one, telegraphs "multiple".
                        var back = new Rectangle(2, 2, 11, 13);
                        g.FillRectangle(fill, back);
                        g.DrawRectangle(edge, back);
                    }
                    var front = new Rectangle(stacked ? 6 : 4, stacked ? 5 : 3, 11, 13);
                    g.FillRectangle(fill, front);
                    g.DrawRectangle(edge, front);
                    // Body lines suggesting text content.
                    int x = front.Left + 2;
                    int x2 = front.Right - 2;
                    for (int i = 0; i < 3; i++)
                    {
                        int y = front.Top + 3 + i * 3;
                        g.DrawLine(lines, x, y, x2, y);
                    }
                }
            }
            return bmp;
        }

        private static Bitmap BuildClearIcon()
        {
            var bmp = new Bitmap(IconSize, IconSize);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                using (var bodyBrush = new SolidBrush(TrashBody))
                using (var lidBrush = new SolidBrush(TrashLid))
                using (var edge = new Pen(BadgeOutline, 1.2f))
                using (var slot = new Pen(BadgeOutline, 1f))
                {
                    // Lid + handle.
                    var lid = new Rectangle(3, 4, 14, 2);
                    g.FillRectangle(lidBrush, lid);
                    g.DrawRectangle(edge, lid);
                    var handle = new Rectangle(8, 2, 4, 2);
                    g.FillRectangle(lidBrush, handle);
                    g.DrawRectangle(edge, handle);

                    // Body — slightly tapered.
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(new[]
                        {
                            new PointF(4f, 7f),
                            new PointF(16f, 7f),
                            new PointF(15f, 18f),
                            new PointF(5f, 18f),
                        });
                        g.FillPath(bodyBrush, path);
                        g.DrawPath(edge, path);
                    }
                    // Vertical bin slits.
                    g.DrawLine(slot, 8, 9, 8, 16);
                    g.DrawLine(slot, 10, 9, 10, 16);
                    g.DrawLine(slot, 12, 9, 12, 16);
                }
            }
            return bmp;
        }

        private static Bitmap BuildLocateIcon()
        {
            var bmp = new Bitmap(IconSize, IconSize);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                using (var path = new GraphicsPath())
                {
                    // Teardrop pin: round head + triangular tail to a single point.
                    var head = new Rectangle(4, 1, 12, 12);
                    path.AddEllipse(head);
                    path.AddPolygon(new[]
                    {
                        new PointF(6.0f, 11.0f),
                        new PointF(14.0f, 11.0f),
                        new PointF(10.0f, 19.0f),
                    });
                    using (var fill = new SolidBrush(LocateBadge))
                        g.FillPath(fill, path);
                    using (var outline = new Pen(BadgeOutline, 1.2f))
                        g.DrawPath(outline, path);
                }

                // Inner dot: white circle centered on the pin head.
                using (var inner = new SolidBrush(BadgeGlyph))
                    g.FillEllipse(inner, 8, 4, 5, 5);
            }
            return bmp;
        }

        private static Bitmap BuildIcon(bool isAssign)
        {
            var bmp = new Bitmap(IconSize, IconSize);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                DrawPerson(g);

                Color badgeFill = isAssign ? AssignBadge : UnassignBadge;
                DrawBadge(g, badgeFill);

                if (isAssign) DrawPlus(g);
                else DrawCross(g);
            }
            return bmp;
        }

        private static void DrawPerson(Graphics g)
        {
            using (var brush = new SolidBrush(PersonColor))
            {
                // Head: ~5px circle, upper-left so badge has room in lower-right.
                g.FillEllipse(brush, 3, 1, 7, 7);
                // Shoulders/body: half-ellipse cropped at the bottom of the icon.
                var bodyRect = new Rectangle(0, 9, 13, 14);
                g.FillEllipse(brush, bodyRect);
            }
        }

        private static void DrawBadge(Graphics g, Color fill)
        {
            using (var fillBrush = new SolidBrush(fill))
            using (var outlinePen = new Pen(BadgeOutline, 1.2f))
            {
                var badge = new Rectangle(11, 10, 9, 9);
                g.FillEllipse(fillBrush, badge);
                g.DrawEllipse(outlinePen, badge);
            }
        }

        private static void DrawPlus(Graphics g)
        {
            using (var pen = new Pen(BadgeGlyph, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                // Plus inside the badge centered around (15.5, 14.5).
                g.DrawLine(pen, 13.0f, 14.5f, 18.0f, 14.5f);
                g.DrawLine(pen, 15.5f, 12.0f, 15.5f, 17.0f);
            }
        }

        private static void DrawCross(Graphics g)
        {
            using (var pen = new Pen(BadgeGlyph, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                // X inside the badge centered around (15.5, 14.5).
                g.DrawLine(pen, 13.5f, 12.5f, 17.5f, 16.5f);
                g.DrawLine(pen, 17.5f, 12.5f, 13.5f, 16.5f);
            }
        }
    }
}
