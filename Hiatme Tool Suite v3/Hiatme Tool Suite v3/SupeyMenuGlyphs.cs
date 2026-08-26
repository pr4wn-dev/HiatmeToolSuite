using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// 16×16 menu glyphs authored on a fixed 12×12 inner box so every icon has the same visual weight.
    /// Drawn in white; <see cref="DarkContextMenuRenderer"/> tints at paint time.
    /// </summary>
    internal static class SupeyMenuGlyphs
    {
        private const int Canvas = 16;
        private const float Inset = 2f;
        private const float W = Canvas - (Inset * 2f);
        private const float Stroke = 1.25f;

        private static readonly Color Ink = Color.White;

        private static readonly Dictionary<string, Bitmap> Cache =
            new Dictionary<string, Bitmap>(StringComparer.Ordinal);

        public static Bitmap Map => Get("map", DrawMap);
        public static Bitmap Person => Get("person", DrawPerson);
        public static Bitmap Move => Get("move", DrawMove);
        public static Bitmap Cut => Get("cut", DrawCut);
        public static Bitmap Paste => Get("paste", DrawPaste);
        public static Bitmap InsertAbove => Get("insUp", g => DrawInsert(g, up: true));
        public static Bitmap InsertBelow => Get("insDn", g => DrawInsert(g, up: false));
        public static Bitmap Broom => Get("broom", DrawBroom);
        public static Bitmap Trash => Get("trash", DrawTrash);
        public static Bitmap Undo => Get("undo", g => DrawArcArrow(g, redo: false));
        public static Bitmap Redo => Get("redo", g => DrawArcArrow(g, redo: true));
        public static Bitmap Reroute => Get("reroute", DrawReroute);
        public static Bitmap Tray => Get("tray", DrawTray);
        public static Bitmap CircleX => Get("circX", DrawCircleX);
        public static Bitmap CircleCheck => Get("circOk", DrawCircleCheck);
        public static Bitmap Note => Get("note", DrawNote);
        public static Bitmap Pencil => Get("pencil", DrawPencil);
        public static Bitmap Rows => Get("rows", DrawRows);
        public static Bitmap RowAbove => Get("rowUp", g => DrawRowInsert(g, up: true));
        public static Bitmap RowBelow => Get("rowDn", g => DrawRowInsert(g, up: false));
        public static Bitmap Palette => Get("palette", DrawPalette);
        public static Bitmap Revert => Get("revert", DrawRevert);
        public static Bitmap Sort => Get("sort", DrawSort);
        public static Bitmap Mail => Get("mail", DrawMail);
        public static Bitmap Home => Get("home", DrawHome);
        public static Bitmap Ban => Get("ban", DrawBan);

        private static Bitmap Get(string key, Action<Graphics> draw)
        {
            Bitmap cached;
            if (Cache.TryGetValue(key, out cached))
                return cached;

            var bmp = new Bitmap(Canvas, Canvas, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                draw(g);
            }

            Cache[key] = bmp;
            return bmp;
        }

        private static Pen Pen() =>
            new Pen(Ink, Stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        private static Pen ArrowPen() =>
            new Pen(Ink, Stroke)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
                CustomEndCap = new AdjustableArrowCap(2.2f, 2.6f, true),
            };

        private static RectangleF Box() => new RectangleF(Inset, Inset, W, W);

        private static void DrawMap(Graphics g)
        {
            using (var p = Pen())
            {
                float cx = Inset + W * 0.5f;
                g.DrawEllipse(p, cx - 2.2f, Inset + 1.5f, 4.4f, 4.4f);
                g.DrawLine(p, cx, Inset + 5.8f, cx, Inset + W - 1f);
                g.DrawLines(p, new[]
                {
                    new PointF(cx, Inset + W - 1f),
                    new PointF(cx - 2.4f, Inset + W - 3.6f),
                    new PointF(cx + 2.4f, Inset + W - 3.6f),
                });
            }
        }

        private static void DrawPerson(Graphics g)
        {
            using (var p = Pen())
            {
                float cx = Inset + W * 0.5f;
                g.DrawEllipse(p, cx - 2f, Inset + 1.2f, 4f, 4f);
                g.DrawArc(p, Inset + 1.5f, Inset + 4.5f, W - 3f, W - 4f, 200, 140);
            }
        }

        private static void DrawMove(Graphics g)
        {
            using (var p = ArrowPen())
            {
                float lx = Inset + W * 0.32f;
                float rx = Inset + W * 0.68f;
                g.DrawLine(p, lx, Inset + W - 2f, lx, Inset + 2.5f);
                g.DrawLine(p, rx, Inset + 2.5f, rx, Inset + W - 2f);
            }
        }

        private static void DrawCut(Graphics g)
        {
            using (var p = Pen())
            {
                g.DrawLine(p, Inset + 2f, Inset + 1.5f, Inset + W - 2f, Inset + W - 2f);
                g.DrawLine(p, Inset + W - 2f, Inset + 1.5f, Inset + 2f, Inset + W - 2f);
                g.DrawEllipse(p, Inset + 0.5f, Inset + W - 4.5f, 3f, 3f);
                g.DrawEllipse(p, Inset + W - 3.5f, Inset + W - 4.5f, 3f, 3f);
            }
        }

        private static void DrawPaste(Graphics g)
        {
            using (var p = Pen())
            {
                var r = Box();
                g.DrawRectangle(p, r.X + 1f, r.Y + 2.5f, r.Width - 2f, r.Height - 3.5f);
                g.DrawRectangle(p, r.X + 3.5f, r.Y, 5f, 3f);
                g.DrawLine(p, r.X + 3f, r.Y + 6.5f, r.Right - 3f, r.Y + 6.5f);
                g.DrawLine(p, r.X + 3f, r.Y + 9f, r.Right - 4f, r.Y + 9f);
            }
        }

        private static void DrawInsert(Graphics g, bool up)
        {
            using (var p = Pen())
            using (var a = ArrowPen())
            {
                float cx = Inset + W * 0.5f;
                float line = up ? Inset + 3.5f : Inset + W - 3.5f;
                g.DrawLine(p, Inset + 1f, line, Inset + W - 1f, line);
                if (up)
                    g.DrawLine(a, cx, Inset + W - 2.5f, cx, Inset + 5f);
                else
                    g.DrawLine(a, cx, Inset + 2.5f, cx, Inset + W - 5f);
            }
        }

        private static void DrawBroom(Graphics g)
        {
            using (var p = Pen())
            {
                g.DrawLine(p, Inset + W - 1.5f, Inset + 1.5f, Inset + 4f, Inset + 7f);
                g.DrawLine(p, Inset + 2f, Inset + 8.5f, Inset + 7.5f, Inset + 8.5f);
                g.DrawLines(p, new[]
                {
                    new PointF(Inset + 2f, Inset + 8.5f),
                    new PointF(Inset + 1f, Inset + W - 1f),
                    new PointF(Inset + 9f, Inset + W - 1f),
                    new PointF(Inset + 7.5f, Inset + 8.5f),
                });
            }
        }

        private static void DrawTrash(Graphics g)
        {
            using (var p = Pen())
            {
                var r = Box();
                g.DrawLine(p, r.X, r.Y + 3f, r.Right, r.Y + 3f);
                g.DrawLine(p, r.X + 2.5f, r.Y + 3f, r.X + 3f, r.Y + 1f);
                g.DrawLine(p, r.Right - 3f, r.Y + 1f, r.Right - 2.5f, r.Y + 3f);
                g.DrawLine(p, r.X + 3f, r.Y + 1f, r.Right - 3f, r.Y + 1f);
                g.DrawLine(p, r.X + 2f, r.Y + 3f, r.X + 2.8f, r.Bottom - 1f);
                g.DrawLine(p, r.Right - 2f, r.Y + 3f, r.Right - 2.8f, r.Bottom - 1f);
                g.DrawLine(p, r.X + 2.8f, r.Bottom - 1f, r.Right - 2.8f, r.Bottom - 1f);
            }
        }

        private static void DrawArcArrow(Graphics g, bool redo)
        {
            using (var p = ArrowPen())
            {
                var r = Box();
                if (redo)
                    g.DrawArc(p, r.X, r.Y + 1f, r.Width, r.Height - 2f, 300, -210);
                else
                    g.DrawArc(p, r.X, r.Y + 1f, r.Width, r.Height - 2f, 240, 210);
            }
        }

        private static void DrawReroute(Graphics g)
        {
            using (var p = Pen())
            using (var a = ArrowPen())
            {
                var r = Box();
                g.DrawLine(p, r.X, r.Bottom - 1.5f, r.X + 3.5f, r.Bottom - 1.5f);
                g.DrawArc(p, r.X + 1f, r.Y + 1f, r.Width - 3f, r.Height - 3f, 90, 90);
                g.DrawLine(a, r.X + r.Width * 0.45f, r.Y + 2f, r.Right - 1f, r.Y + 2f);
            }
        }

        private static void DrawTray(Graphics g)
        {
            using (var p = Pen())
            using (var a = ArrowPen())
            {
                float cx = Inset + W * 0.5f;
                g.DrawLine(a, cx, Inset + 5f, cx, Inset + 1f);
                g.DrawLine(p, Inset + 1f, Inset + 5.5f, Inset + W - 1f, Inset + 5.5f);
                g.DrawLine(p, Inset + 1f, Inset + 5.5f, Inset + 1f, Inset + W - 1f);
                g.DrawLine(p, Inset + W - 1f, Inset + 5.5f, Inset + W - 1f, Inset + W - 1f);
                g.DrawLine(p, Inset + 1f, Inset + W - 1f, Inset + W - 1f, Inset + W - 1f);
            }
        }

        private static void DrawCircleX(Graphics g)
        {
            using (var p = Pen())
            {
                var r = Box();
                g.DrawEllipse(p, r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
                g.DrawLine(p, r.X + 3.5f, r.Y + 3.5f, r.Right - 3.5f, r.Bottom - 3.5f);
                g.DrawLine(p, r.Right - 3.5f, r.Y + 3.5f, r.X + 3.5f, r.Bottom - 3.5f);
            }
        }

        private static void DrawCircleCheck(Graphics g)
        {
            using (var p = Pen())
            {
                var r = Box();
                g.DrawEllipse(p, r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
                g.DrawLines(p, new[]
                {
                    new PointF(r.X + 3f, r.Y + r.Height * 0.52f),
                    new PointF(r.X + r.Width * 0.42f, r.Bottom - 3.5f),
                    new PointF(r.Right - 2.5f, r.Y + 3.5f),
                });
            }
        }

        private static void DrawNote(Graphics g)
        {
            using (var p = Pen())
            {
                var r = Box();
                g.DrawLines(p, new[]
                {
                    new PointF(r.X + 1f, r.Y + 1f), new PointF(r.Right - 1f, r.Y + 1f),
                    new PointF(r.Right - 1f, r.Y + r.Height * 0.62f),
                    new PointF(r.X + r.Width * 0.55f, r.Bottom - 1f),
                    new PointF(r.X + 1f, r.Bottom - 1f), new PointF(r.X + 1f, r.Y + 1f),
                });
                g.DrawLine(p, r.X + 3f, r.Y + 4f, r.Right - 3f, r.Y + 4f);
                g.DrawLine(p, r.X + 3f, r.Y + 7f, r.Right - 3f, r.Y + 7f);
            }
        }

        private static void DrawPencil(Graphics g)
        {
            using (var p = Pen())
            {
                g.DrawLines(p, new[]
                {
                    new PointF(Inset + 1f, Inset + W - 1f),
                    new PointF(Inset + 4f, Inset + W - 4f),
                    new PointF(Inset + W - 2f, Inset + 2f),
                    new PointF(Inset + W - 1f, Inset + 4f),
                    new PointF(Inset + 4f, Inset + W - 1f),
                });
            }
        }

        private static void DrawRows(Graphics g)
        {
            using (var p = Pen())
            {
                float y1 = Inset + 2.5f;
                float y2 = Inset + W * 0.5f;
                float y3 = Inset + W - 2.5f;
                g.DrawLine(p, Inset + 1f, y1, Inset + W - 1f, y1);
                g.DrawLine(p, Inset + 1f, y2, Inset + W - 1f, y2);
                g.DrawLine(p, Inset + 1f, y3, Inset + W - 1f, y3);
            }
        }

        private static void DrawRowInsert(Graphics g, bool up)
        {
            using (var p = Pen())
            using (var dotted = new Pen(Ink, Stroke) { DashStyle = DashStyle.Dot })
            {
                float solid = up ? Inset + W * 0.62f : Inset + 2.5f;
                float dash = up ? Inset + 2.5f : Inset + W * 0.62f;
                g.DrawLine(p, Inset + 1f, solid, Inset + W - 1f, solid);
                g.DrawLine(p, Inset + 1f, solid + 2.8f, Inset + W - 1f, solid + 2.8f);
                g.DrawLine(dotted, Inset + 1f, dash, Inset + W - 1f, dash);
            }
        }

        private static void DrawPalette(Graphics g)
        {
            using (var p = Pen())
            {
                var r = Box();
                g.DrawArc(p, r.X, r.Y, r.Width, r.Height, 130, 280);
                using (var dot = new SolidBrush(Ink))
                {
                    g.FillEllipse(dot, r.X + 2.5f, r.Y + 4f, 1.6f, 1.6f);
                    g.FillEllipse(dot, r.X + r.Width * 0.42f, r.Y + 2.5f, 1.6f, 1.6f);
                    g.FillEllipse(dot, r.X + r.Width * 0.65f, r.Y + 5f, 1.6f, 1.6f);
                }
            }
        }

        private static void DrawRevert(Graphics g)
        {
            using (var p = ArrowPen())
            {
                var r = Box();
                g.DrawArc(p, r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f, 250, 200);
            }
        }

        private static void DrawSort(Graphics g)
        {
            using (var p = Pen())
            using (var a = ArrowPen())
            {
                g.DrawLine(p, Inset + 1f, Inset + 2.5f, Inset + W * 0.62f, Inset + 2.5f);
                g.DrawLine(p, Inset + 1f, Inset + W * 0.5f, Inset + W * 0.48f, Inset + W * 0.5f);
                g.DrawLine(p, Inset + 1f, Inset + W - 2.5f, Inset + W * 0.35f, Inset + W - 2.5f);
                g.DrawLine(a, Inset + W - 2f, Inset + 2.5f, Inset + W - 2f, Inset + W - 2f);
            }
        }

        private static void DrawMail(Graphics g)
        {
            using (var p = Pen())
            {
                var r = Box();
                g.DrawRectangle(p, r.X + 0.5f, r.Y + 2f, r.Width - 1f, r.Height - 4f);
                g.DrawLines(p, new[]
                {
                    new PointF(r.X + 0.5f, r.Y + 2f),
                    new PointF(r.X + r.Width * 0.5f, r.Y + r.Height * 0.48f),
                    new PointF(r.Right - 0.5f, r.Y + 2f),
                });
            }
        }

        private static void DrawHome(Graphics g)
        {
            using (var p = Pen())
            {
                var r = Box();
                float cx = r.X + r.Width * 0.5f;
                g.DrawLines(p, new[]
                {
                    new PointF(r.X + 1f, r.Y + r.Height * 0.45f),
                    new PointF(cx, r.Y + 1.5f),
                    new PointF(r.Right - 1f, r.Y + r.Height * 0.45f),
                });
                g.DrawLines(p, new[]
                {
                    new PointF(r.X + 2.5f, r.Y + r.Height * 0.4f),
                    new PointF(r.X + 2.5f, r.Bottom - 1f),
                    new PointF(r.Right - 2.5f, r.Bottom - 1f),
                    new PointF(r.Right - 2.5f, r.Y + r.Height * 0.4f),
                });
            }
        }

        private static void DrawBan(Graphics g)
        {
            using (var p = Pen())
            {
                var r = Box();
                g.DrawEllipse(p, r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
                g.DrawLine(p, r.X + 3f, r.Bottom - 3f, r.Right - 3f, r.Y + 3f);
            }
        }
    }
}
