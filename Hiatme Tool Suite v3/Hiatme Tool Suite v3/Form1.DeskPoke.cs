using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private List<SchedulePresenceEntry> _schedActOthers = new List<SchedulePresenceEntry>();
        private DeskPokeOverlay _deskPokeOverlay;
        private ContextMenuStrip _deskPokeMenu;

        private void InitDeskPoke()
        {
            if (_deskPokeOverlay != null) return;
            _deskPokeOverlay = new DeskPokeOverlay { Visible = false };
            Controls.Add(_deskPokeOverlay);
            Resize += (_, __) =>
            {
                if (_deskPokeOverlay != null && _deskPokeOverlay.Visible)
                    _deskPokeOverlay.Bounds = ClientRectangle;
            };
            if (_schedActFeed != null)
                _schedActFeed.PokesArrived += OnDeskPokesArrived;
        }

        private void OnDeskPokesArrived(List<DeskPokeMessage> pokes)
        {
            if (pokes == null || pokes.Count == 0) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => OnDeskPokesArrived(pokes))); return; }
            var last = pokes[pokes.Count - 1];
            if (last == null || string.IsNullOrWhiteSpace(last.Emoji)) return;
            EnsureDeskPokeOverlay();
            _deskPokeOverlay.ShowPoke(last.Emoji, last.Dispatcher);
        }

        private void EnsureDeskPokeOverlay()
        {
            if (_deskPokeOverlay != null && !_deskPokeOverlay.IsDisposed) return;
            InitDeskPoke();
        }

        private void ShowDeskPokeMenu(Control anchor, Point clientPt)
        {
            if (anchor == null) return;
            if (_deskPokeMenu != null)
            {
                _deskPokeMenu.Dispose();
                _deskPokeMenu = null;
            }
            var menu = new ContextMenuStrip { Renderer = new DarkContextMenuRenderer() };
            var others = _schedActOthers ?? new List<SchedulePresenceEntry>();
            if (others.Count == 0)
            {
                var empty = new ToolStripMenuItem("Nobody else is online") { Enabled = false };
                menu.Items.Add(empty);
            }
            else
            {
                Image thumb = DeskPokeImages.MenuThumb("middle-finger");
                foreach (var o in others)
                {
                    if (o == null || string.IsNullOrWhiteSpace(o.ClientId)) continue;
                    string name = string.IsNullOrWhiteSpace(o.Dispatcher) ? "?" : o.Dispatcher.Trim();
                    var person = new ToolStripMenuItem(name);
                    string target = o.ClientId;
                    string label = name;
                    var send = new ToolStripMenuItem("Send middle finger", thumb, async (_, __) =>
                    {
                        await SendDeskPokeAsync(target, "middle-finger", label).ConfigureAwait(true);
                    });
                    person.DropDownItems.Add(send);
                    menu.Items.Add(person);
                }
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Test on my own screen", DeskPokeImages.MenuThumb("middle-finger"), (_, __) =>
            {
                EnsureDeskPokeOverlay();
                _deskPokeOverlay.ShowPoke("middle-finger", "you (test)");
            });
            menu.Items.Add("Open activity log", null, (_, __) => _ = OpenScheduleActivityDrawerAsync(-1));
            _deskPokeMenu = menu;
            menu.Show(anchor, clientPt);
        }

        private async System.Threading.Tasks.Task SendDeskPokeAsync(string targetClientId, string emoji, string who)
        {
            if (_schedActFeed == null || string.IsNullOrWhiteSpace(targetClientId)) return;
            try
            {
                bool ok = await _schedActFeed.SendPokeAsync(targetClientId, emoji).ConfigureAwait(true);
                if (!ok)
                    SetScheduleBuilderStatus("Could not send that — is the panel up?");
                else
                    SetScheduleBuilderStatus("Sent to " + (who ?? "them") + ".");
            }
            catch { }
        }
    }

    internal sealed class DeskPokeOverlay : Control
    {
        private readonly Timer _life = new Timer { Interval = 4500 };
        private Image _img;
        private Bitmap _bgSnap;
        private string _from = "";
        private static readonly Font FromFont = new Font("Segoe UI Semibold", 14f);

        public DeskPokeOverlay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            Dock = DockStyle.None;
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            TabStop = false;
            Cursor = Cursors.Hand;
            _life.Tick += (_, __) => HidePoke();
        }

        public void ShowPoke(string emojiId, string fromDispatcher)
        {
            _img = DeskPokeImages.Get(emojiId);
            _from = (fromDispatcher ?? "").Trim();
            if (Parent != null)
            {
                Bounds = Parent.ClientRectangle;
                CaptureParentSnapshot();
            }
            Visible = true;
            BringToFront();
            _life.Stop();
            _life.Start();
            Invalidate();
        }

        private void CaptureParentSnapshot()
        {
            try
            {
                if (Parent == null) return;
                var rect = Parent.ClientRectangle;
                if (rect.Width <= 0 || rect.Height <= 0) return;

                if (_bgSnap != null)
                {
                    _bgSnap.Dispose();
                    _bgSnap = null;
                }
                _bgSnap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
                var pt = Parent.PointToScreen(Point.Empty);
                using (var g = Graphics.FromImage(_bgSnap))
                    g.CopyFromScreen(pt, Point.Empty, rect.Size, CopyPixelOperation.SourceCopy);
            }
            catch
            {
                if (_bgSnap != null)
                {
                    _bgSnap.Dispose();
                    _bgSnap = null;
                }
            }
        }

        public void HidePoke()
        {
            _life.Stop();
            Visible = false;
        }

        protected override void OnClick(EventArgs e)
        {
            HidePoke();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingMode = CompositingMode.SourceOver;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            if (_bgSnap != null)
                g.DrawImageUnscaled(_bgSnap, 0, 0);

            if (_img != null)
            {
                int side = Math.Max(180, (int)(Math.Min(Width, Height) * 0.62));
                var dest = new Rectangle((Width - side) / 2, (Height - side) / 2 - 12, side, side);
                // Prevent dark fringe when scaling images with transparent edges.
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(_img, dest, 0, 0, _img.Width, _img.Height, GraphicsUnit.Pixel, ia);
                }
            }

            if (!string.IsNullOrEmpty(_from))
            {
                string caption = "from " + _from;
                var sz = g.MeasureString(caption, FromFont);
                float x = (Width - sz.Width) / 2f;
                float y = Height * 0.82f;
                using (var b = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                    g.DrawString(caption, FromFont, b, x, y);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _life.Dispose();
                if (_bgSnap != null)
                {
                    _bgSnap.Dispose();
                    _bgSnap = null;
                }
            }
            base.Dispose(disposing);
        }
    }

    internal static class DeskPokeImages
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Image> Cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Image> Thumbs = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public static Image Get(string emojiId)
        {
            string id = (emojiId ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id)) return null;
            lock (Sync)
            {
                Image hit;
                if (Cache.TryGetValue(id, out hit)) return hit;
                hit = LoadPng(id + ".png");
                if (hit != null) Cache[id] = hit;
                return hit;
            }
        }

        public static Image MenuThumb(string emojiId)
        {
            string id = (emojiId ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id)) return null;
            lock (Sync)
            {
                Image hit;
                if (Thumbs.TryGetValue(id, out hit)) return hit;
                var src = Get(id);
                if (src == null) return null;
                var bmp = new Bitmap(18, 18, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    g.DrawImage(src, new Rectangle(0, 0, 18, 18));
                }
                Thumbs[id] = bmp;
                return bmp;
            }
        }

        private static Image LoadPng(string fileName)
        {
            string[] folders =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "desk-pokes"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "Resources", "desk-pokes"),
            };
            foreach (string folder in folders)
            {
                string path = Path.Combine(folder, fileName);
                if (!File.Exists(path)) continue;
                try
                {
                    using (var stream = File.OpenRead(path))
                        return new Bitmap(stream);
                }
                catch { }
            }

            Assembly assembly = Assembly.GetExecutingAssembly();
            string suffix = ".Resources.desk-pokes." + fileName;
            string resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return null;
            try
            {
                    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null) return null;
                        return new Bitmap(stream);
                    }
            }
            catch
            {
                return null;
            }
        }
    }
}
