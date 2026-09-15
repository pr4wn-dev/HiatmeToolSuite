using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Media;
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
                Image thumbMiddle = DeskPokeImages.MenuThumb("middle-finger");
                Image thumbCowboy = DeskPokeImages.MenuThumb("cowboy-kid-pointing");
                foreach (var o in others)
                {
                    if (o == null || string.IsNullOrWhiteSpace(o.ClientId)) continue;
                    string name = string.IsNullOrWhiteSpace(o.Dispatcher) ? "?" : o.Dispatcher.Trim();
                    var person = new ToolStripMenuItem(name);
                    string target = o.ClientId;
                    string label = name;
                    var sendMiddle = new ToolStripMenuItem("Send middle finger", thumbMiddle, async (_, __) =>
                    {
                        await SendDeskPokeAsync(target, "middle-finger", label).ConfigureAwait(true);
                    });
                    var sendCowboy = new ToolStripMenuItem("Send cowboy kid", thumbCowboy, async (_, __) =>
                    {
                        await SendDeskPokeAsync(target, "cowboy-kid-pointing", label).ConfigureAwait(true);
                    });
                    person.DropDownItems.Add(sendMiddle);
                    person.DropDownItems.Add(sendCowboy);
                    menu.Items.Add(person);
                }
            }
            menu.Items.Add(new ToolStripSeparator());
            var testSelf = new ToolStripMenuItem("Test on my own screen");
            string me = _schedActFeed != null ? _schedActFeed.DispatcherName : ScheduleActivityIdentity.DispatcherName();
            testSelf.DropDownItems.Add("Middle finger", DeskPokeImages.MenuThumb("middle-finger"), (_, __) =>
            {
                EnsureDeskPokeOverlay();
                _deskPokeOverlay.ShowPoke("middle-finger", me);
            });
            testSelf.DropDownItems.Add("Cowboy kid", DeskPokeImages.MenuThumb("cowboy-kid-pointing"), (_, __) =>
            {
                EnsureDeskPokeOverlay();
                _deskPokeOverlay.ShowPoke("cowboy-kid-pointing", me);
            });
            menu.Items.Add(testSelf);
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
        private readonly Timer _anim = new Timer { Interval = 33 };
        private readonly List<StarParticle> _stars = new List<StarParticle>();
        private readonly Random _rng = new Random();
        private Image _img;
        private Bitmap _bgSnap;
        private SoundPlayer _soundCheer;
        private MemoryStream _soundCheerStream;
        private SoundPlayer _soundGummo;
        private MemoryStream _soundGummoStream;
        private string _from = "";
        private string _emojiId = "";
        private DateTime _lastAnimAtUtc = DateTime.UtcNow;
        private static readonly Font CaptionFont = new Font("Segoe UI Semibold", 28f);
        private static readonly Color[] StarPalette =
        {
            Color.FromArgb(255, 255, 226, 94),
            Color.FromArgb(255, 255, 173, 74),
            Color.FromArgb(255, 255, 105, 173),
            Color.FromArgb(255, 124, 244, 255),
            Color.FromArgb(255, 190, 132, 255),
            Color.FromArgb(255, 145, 255, 169),
        };

        private sealed class StarParticle
        {
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public float Size;
            public float Angle;
            public float Spin;
            public float Age;
            public float Life;
            public Color Color;
        }

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
            InitCelebrationSounds();
            _life.Tick += (_, __) => HidePoke();
            _anim.Tick += (_, __) => TickAnim();
        }

        public void ShowPoke(string emojiId, string fromDispatcher)
        {
            _img = DeskPokeImages.Get(emojiId);
            _emojiId = (emojiId ?? "").Trim();
            _from = (fromDispatcher ?? "").Trim();
            if (Parent != null)
            {
                Bounds = Parent.ClientRectangle;
                CaptureParentSnapshot();
            }
            Visible = true;
            BringToFront();
            SeedStars();
            _lastAnimAtUtc = DateTime.UtcNow;
            _anim.Stop();
            _anim.Start();
            PlayCelebrationSound(emojiId);
            _life.Stop();
            _life.Start();
            Invalidate();
        }

        private void InitCelebrationSounds()
        {
            _soundCheer = CreatePlayerFromResource(Properties.Resources.cheer, out _soundCheerStream);
            _soundGummo = CreatePlayerFromResource(Properties.Resources.gummo, out _soundGummoStream);
            if (_soundCheer == null)
                _soundCheer = CreatePlayerFromPath("will-call-bell.wav");
        }

        private SoundPlayer CreatePlayerFromResource(UnmanagedMemoryStream src, out MemoryStream copy)
        {
            copy = null;
            if (src == null) return null;
            try
            {
                src.Position = 0;
                copy = new MemoryStream();
                src.CopyTo(copy);
                copy.Position = 0;
                var p = new SoundPlayer(copy);
                p.LoadAsync();
                return p;
            }
            catch { }
            return null;
        }

        private SoundPlayer CreatePlayerFromPath(string fileName)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", fileName);
                if (!File.Exists(path)) return null;
                var p = new SoundPlayer(path);
                p.LoadAsync();
                return p;
            }
            catch { }
            return null;
        }

        private void PlayCelebrationSound(string emojiId)
        {
            try
            {
                if (string.Equals((emojiId ?? "").Trim(), "cowboy-kid-pointing", StringComparison.OrdinalIgnoreCase))
                {
                    if (_soundGummo != null) { _soundGummo.Play(); return; }
                }
                else
                {
                    if (_soundCheer != null) { _soundCheer.Play(); return; }
                }
            }
            catch { }
            try { SystemSounds.Asterisk.Play(); } catch { }
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
            _anim.Stop();
            _stars.Clear();
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
                var dest = EmojiRect();
                // Prevent dark fringe when scaling images with transparent edges.
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(_img, dest, 0, 0, _img.Width, _img.Height, GraphicsUnit.Pixel, ia);
                }
            }
            PaintStars(g);
            PaintCaption(g);
        }

        private string CaptionText()
        {
            string who = string.IsNullOrWhiteSpace(_from) ? "Someone" : _from;
            if (string.Equals(_emojiId, "middle-finger", StringComparison.OrdinalIgnoreCase))
                return who + " says go fuck yourself";
            if (string.Equals(_emojiId, "cowboy-kid-pointing", StringComparison.OrdinalIgnoreCase))
                return who + " says you smell like a pile of bullshit";
            return who;
        }

        private void PaintCaption(Graphics g)
        {
            string caption = CaptionText();
            if (string.IsNullOrWhiteSpace(caption)) return;

            var dest = EmojiRect();
            var sz = g.MeasureString(caption, CaptionFont);
            float x = (Width - sz.Width) / 2f;
            float y = dest.Bottom + 8f;
            if (y + sz.Height > Height - 12f)
                y = Height - sz.Height - 12f;

            using (var path = new GraphicsPath())
            {
                path.AddString(caption, CaptionFont.FontFamily, (int)CaptionFont.Style,
                    g.DpiY * CaptionFont.Size / 72f, new PointF(x, y), StringFormat.GenericTypographic);
                using (var outline = new Pen(Color.FromArgb(220, 0, 0, 0), 6f) { LineJoin = LineJoin.Round })
                    g.DrawPath(outline, path);
                using (var fill = new SolidBrush(Color.White))
                    g.FillPath(fill, path);
            }
        }

        private Rectangle EmojiRect()
        {
            int side = Math.Max(180, (int)(Math.Min(Width, Height) * 0.62));
            return new Rectangle((Width - side) / 2, (Height - side) / 2 - 12, side, side);
        }

        private void SeedStars()
        {
            _stars.Clear();
            if (_img == null || Width <= 0 || Height <= 0)
                return;

            var dest = EmojiRect();
            float scale = Math.Max(0.85f, dest.Width / 300f);
            float ox = dest.Left + dest.Width * 0.5f;
            // Start burst from the lower hand/palm area (not the fingertip).
            float oy = dest.Top + dest.Height * 0.70f;

            // Two bursts: main celebratory fan upward + ring pop around the hand.
            for (int i = 0; i < 56; i++)
            {
                float a = Lerp(-2.4f, -0.74f, Next01());
                float speed = Lerp(280f, 700f, Next01()) * scale;
                _stars.Add(new StarParticle
                {
                    X = ox + Lerp(-26f, 26f, Next01()) * scale,
                    Y = oy + Lerp(-12f, 12f, Next01()) * scale,
                    Vx = (float)Math.Cos(a) * speed,
                    Vy = (float)Math.Sin(a) * speed,
                    Size = Lerp(11f, 27f, Next01()) * scale,
                    Angle = Lerp(0f, (float)Math.PI * 2f, Next01()),
                    Spin = Lerp(-7.8f, 7.8f, Next01()),
                    Age = 0f,
                    Life = Lerp(0.95f, 1.85f, Next01()),
                    Color = RandomStarColor(),
                });
            }
            for (int i = 0; i < 28; i++)
            {
                float a = Lerp(0f, (float)Math.PI * 2f, Next01());
                float speed = Lerp(200f, 420f, Next01()) * scale;
                _stars.Add(new StarParticle
                {
                    X = ox + Lerp(-44f, 44f, Next01()) * scale,
                    Y = oy + Lerp(-30f, 38f, Next01()) * scale,
                    Vx = (float)Math.Cos(a) * speed,
                    Vy = (float)Math.Sin(a) * speed,
                    Size = Lerp(9f, 20f, Next01()) * scale,
                    Angle = Lerp(0f, (float)Math.PI * 2f, Next01()),
                    Spin = Lerp(-8.4f, 8.4f, Next01()),
                    Age = 0f,
                    Life = Lerp(0.8f, 1.45f, Next01()),
                    Color = RandomStarColor(),
                });
            }
        }

        private void TickAnim()
        {
            if (!Visible)
            {
                _anim.Stop();
                return;
            }

            DateTime now = DateTime.UtcNow;
            float dt = (float)(now - _lastAnimAtUtc).TotalSeconds;
            _lastAnimAtUtc = now;
            if (dt <= 0f || dt > 0.2f) dt = 0.033f;

            for (int i = _stars.Count - 1; i >= 0; i--)
            {
                var p = _stars[i];
                p.Age += dt;
                if (p.Age >= p.Life || p.Y > Height + 36f || p.X < -40f || p.X > Width + 40f)
                {
                    _stars.RemoveAt(i);
                    continue;
                }
                p.Vy += 340f * dt;
                p.Vx *= 0.996f;
                p.Vy *= 0.996f;
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                p.Angle += p.Spin * dt;
            }

            if (_stars.Count == 0)
                _anim.Stop();
            Invalidate();
        }

        private void PaintStars(Graphics g)
        {
            if (_stars.Count == 0) return;

            foreach (var p in _stars)
            {
                float t = 1f - (p.Age / Math.Max(0.001f, p.Life));
                if (t <= 0f) continue;
                int alpha = (int)(255f * t * t);
                if (alpha <= 0) continue;

                float outer = Math.Max(4f, p.Size * (0.9f + (t * 0.6f)));
                float inner = outer * 0.45f;
                var c = Color.FromArgb(alpha, p.Color);
                using (var path = StarPath(new PointF(p.X, p.Y), outer, inner, 5, p.Angle))
                using (var b = new SolidBrush(c))
                    g.FillPath(b, path);
            }
        }

        private static GraphicsPath StarPath(PointF center, float outer, float inner, int points, float rotate)
        {
            var path = new GraphicsPath();
            int n = Math.Max(2, points) * 2;
            var pts = new PointF[n];
            float step = (float)Math.PI / points;
            for (int i = 0; i < n; i++)
            {
                float r = (i % 2 == 0) ? outer : inner;
                float a = rotate + (i * step);
                pts[i] = new PointF(
                    center.X + ((float)Math.Cos(a) * r),
                    center.Y + ((float)Math.Sin(a) * r));
            }
            path.AddPolygon(pts);
            return path;
        }

        private float Next01() => (float)_rng.NextDouble();
        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
        private Color RandomStarColor() => StarPalette[_rng.Next(StarPalette.Length)];

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _life.Dispose();
                _anim.Dispose();
                if (_bgSnap != null)
                {
                    _bgSnap.Dispose();
                    _bgSnap = null;
                }
                try
                {
                    _soundCheer?.Stop();
                    _soundCheer?.Dispose();
                    _soundGummo?.Stop();
                    _soundGummo?.Dispose();
                }
                catch { }
                _soundCheer = null;
                _soundGummo = null;
                _soundCheerStream?.Dispose();
                _soundCheerStream = null;
                _soundGummoStream?.Dispose();
                _soundGummoStream = null;
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
