using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private BillingDaysPill _billingDaysPill;
        private BillingDaysPopup _billingDaysPopup;
        private System.Windows.Forms.Timer _billingDaysTimer;
        private List<HiatmeAiClient.BillingOpenDay> _billingOpenDays =
            new List<HiatmeAiClient.BillingOpenDay>();
        private bool _billingDaysKnown;
        private int _billingDaysPoll;

        private void InstallBillingDaysPill()
        {
            if (_billingDaysPill != null)
                return;
            _billingDaysPill = new BillingDaysPill();
            _billingDaysPill.Click += (_, __) =>
            {
                DismissBillingStar();
                ToggleBillingDaysPopup();
            };
            _billingDaysPill.LayoutChanged += (_, __) => RepositionSchedulePresencePill();
            Controls.Add(_billingDaysPill);
            _billingDaysPill.SetCount(0, known: false);
            FormClosing += (_, __) =>
            {
                try { _billingDaysTimer?.Stop(); } catch { }
                try { _billingDaysTimer?.Dispose(); } catch { }
                try { _billingDaysPopup?.Close(); } catch { }
                HideBillingStar(false);
            };
            _billingDaysTimer = new System.Windows.Forms.Timer { Interval = 120000 };
            _billingDaysTimer.Tick += (_, __) => { _ = RefreshBillingDaysAsync(); };
            _billingDaysTimer.Start();
            RepositionBillingDaysPill();
            _ = RefreshBillingDaysAsync();
        }

        private void RepositionBillingDaysPill()
        {
            if (_billingDaysPill == null || _billingDaysPill.IsDisposed)
                return;
            int x = 16;
            int y = Math.Max(0, (ChromeTitleHeight - _billingDaysPill.Height) / 2);
            if (_schedActPresencePill != null && !_schedActPresencePill.IsDisposed && _schedActPresencePill.Visible)
            {
                x = _schedActPresencePill.Right + 8;
                y = _schedActPresencePill.Top + (_schedActPresencePill.Height - _billingDaysPill.Height) / 2;
            }
            var theme = TitleBarThemeButtonBounds;
            var ai = TitleBarAiButtonBounds;
            int right = ClientSize.Width - (46 * 3) - 8;
            if (!ai.IsEmpty)
                right = ai.Left;
            if (!theme.IsEmpty)
                right = Math.Min(right, theme.Left);
            int limit = Math.Max(8, right - 8 - _billingDaysPill.Width);
            if (x > limit)
                x = limit;
            var want = new Point(Math.Max(8, x), Math.Max(0, y));
            if (_billingDaysPill.Location != want)
                _billingDaysPill.Location = want;
            _billingDaysPill.BringToFront();
            TrackBillingStar();
        }

        private async System.Threading.Tasks.Task RefreshBillingDaysAsync()
        {
            int poll = ++_billingDaysPoll;
            HiatmeAiClient.BillingOpenDays result = null;
            try
            {
                result = await HiatmeAiClient.GetBillingOpenDaysAsync(HiatmeAiSettings.LoadNoProbe())
                    .ConfigureAwait(false);
            }
            catch
            {
                result = null;
            }
            if (IsDisposed || poll != _billingDaysPoll)
                return;
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => ApplyBillingDays(result)));
                    return;
                }
            }
            catch
            {
                return;
            }
            ApplyBillingDays(result);
        }

        private void ApplyBillingDays(HiatmeAiClient.BillingOpenDays result)
        {
            if (IsDisposed || _billingDaysPill == null || _billingDaysPill.IsDisposed)
                return;
            _billingOpenDays = result != null && result.Ok && result.Days != null
                ? result.Days
                : new List<HiatmeAiClient.BillingOpenDay>();
            _billingDaysKnown = result != null && result.Ok;
            int count = _billingDaysKnown ? result.Count : 0;
            if (_billingDaysKnown && count == 0)
                count = _billingOpenDays.Count;
            _billingDaysPill.SetCount(count, _billingDaysKnown);
            if (_billingDaysPopup != null && !_billingDaysPopup.IsDisposed)
                _billingDaysPopup.ShowDays(_billingOpenDays, _billingDaysKnown);
            SyncBillingStar(count);
        }

        private LateDriversFreeEdge _billingStar;

        private bool _billingStarDismissed;

        private void SyncBillingStar(int count)
        {
            if (!_billingDaysKnown || count <= 0)
                _billingStarDismissed = false;
            bool show = _billingDaysKnown && count > 0 && !_billingStarDismissed
                && _billingDaysPill != null && !_billingDaysPill.IsDisposed && _billingDaysPill.Visible;
            if (!show)
            {
                HideBillingStar();
                return;
            }
            if (_billingStar == null || _billingStar.IsDisposed)
            {
                _billingStar = new LateDriversFreeEdge();
                _billingStar.UseAcrossSuite();
                _billingStar.UseMark(LateDriversEdgeMark.Dollar);
                _billingStar.Click += (_, __) =>
                {
                    _billingStarDismissed = true;
                    BeginInvoke(new Action(() =>
                    {
                        HideBillingStar();
                        ToggleBillingDaysPopup();
                    }));
                };
            }
            TrackBillingStar();
        }

        private void HideBillingStar(bool smoke = true)
        {
            if (_billingStar == null)
                return;
            var star = _billingStar;
            _billingStar = null;
            try
            {
                if (smoke)
                    star.ReleaseWithSmoke();
                else
                    star.Dispose();
            }
            catch { }
        }

        private void DismissBillingStar()
        {
            _billingStarDismissed = true;
            HideBillingStar();
        }

        private void TrackBillingStar()
        {
            if (_billingStar == null || _billingStar.IsDisposed
                || _billingDaysPill == null || _billingDaysPill.IsDisposed || !_billingDaysPill.Visible)
                return;
            var pill = _billingDaysPill;
            _billingStar.HostOn(this);
            int x = pill.Left + (pill.Width - _billingStar.Width) / 2;
            int y = pill.Bottom + 22;
            int bottomLimit = ClientSize.Height - _billingStar.Height - 8;
            if (y > bottomLimit)
                y = Math.Max(pill.Bottom, bottomLimit);
            var track = new Point(pill.Left + pill.Width / 2, pill.Top + pill.Height / 2);
            _billingStar.FlyTo(new Point(x, y), track, false, 0);
        }

        private void KeepBillingStarVisible()
        {
            if (_billingStar == null || _billingStar.IsDisposed)
                return;
            _billingStar.UseAcrossSuite();
            _billingStar.RestackSprite();
        }

        private void ToggleBillingDaysPopup()
        {
            if (_billingDaysPopup != null && !_billingDaysPopup.IsDisposed)
            {
                _billingDaysPopup.Close();
                _billingDaysPopup = null;
                return;
            }
            if (_billingDaysPill == null || _billingDaysPill.IsDisposed)
                return;
            _billingDaysPopup = new BillingDaysPopup();
            _billingDaysPopup.ShowDays(_billingOpenDays, _billingDaysKnown);
            _billingDaysPopup.DayChosen += (_, iso) =>
            {
                string chosen = iso;
                BeginInvoke(new Action(() => OpenBillingForDay(chosen)));
            };
            bool armed = false;
            _billingDaysPopup.Shown += (_, __) =>
            {
                try { _billingDaysPopup?.BeginInvoke(new Action(() => armed = true)); }
                catch { armed = true; }
            };
            _billingDaysPopup.Deactivate += (_, __) =>
            {
                if (!armed)
                    return;
                try { _billingDaysPopup?.Close(); } catch { }
                _billingDaysPopup = null;
            };
            _billingDaysPopup.Show(this);
            Point at = _billingDaysPill.PointToScreen(new Point(0, _billingDaysPill.Height + 6));
            Rectangle screen = Screen.FromControl(this).WorkingArea;
            if (at.X + _billingDaysPopup.Width > screen.Right)
                at.X = Math.Max(screen.Left, screen.Right - _billingDaysPopup.Width);
            if (at.Y + _billingDaysPopup.Height > screen.Bottom)
                at.Y = Math.Max(screen.Top, _billingDaysPill.PointToScreen(Point.Empty).Y - _billingDaysPopup.Height - 6);
            _billingDaysPopup.Location = at;
        }

        private void OpenBillingForDay(string iso)
        {
            if (_billingDaysPopup != null && !_billingDaysPopup.IsDisposed)
            {
                try { _billingDaysPopup.Close(); } catch { }
                _billingDaysPopup = null;
            }
            if (!DateTime.TryParseExact(
                    (iso ?? "").Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime day))
                return;
            if (hiatmeTabControl != null && tabPage2 != null && !tabPage2.IsDisposed)
                hiatmeTabControl.SelectedTab = tabPage2;
            if (rjDatePicker1 != null && !rjDatePicker1.IsDisposed)
                rjDatePicker1.Value = day;
            _billingSubmitPending = false;
            billloadButton_Click(this, EventArgs.Empty);
        }
    }

    internal sealed class BillingDaysPill : Control
    {
        private static readonly Font PillFont = new Font("Segoe UI Semibold", 8.5f);
        private static readonly Font MicroFont = new Font("Segoe UI", 7.5f);
        private const string Tag = "BILLING";
        private readonly ToolTip _tip = new ToolTip { ShowAlways = true, InitialDelay = 400 };
        private int _count;
        private bool _known;

        public event EventHandler LayoutChanged;

        public BillingDaysPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            BackColor = SupeyTheme.SurfaceHeader;
            Cursor = Cursors.Hand;
            Height = 26;
            Width = 10;
            TabStop = false;
            Relayout();
        }

        public void SetCount(int count, bool known)
        {
            count = Math.Max(0, count);
            if (count == _count && known == _known && Width > 10)
            {
                Invalidate();
                return;
            }
            _count = count;
            _known = known;
            Relayout();
        }

        private string SummaryText()
        {
            if (!_known)
                return "CHECKING";
            if (_count == 1)
                return "1 DAY";
            return _count.ToString(CultureInfo.InvariantCulture) + " DAYS";
        }

        private void Relayout()
        {
            int w = MeasureWidth();
            if (Width != w)
                Width = w;
            _tip.SetToolTip(this, TipText());
            Invalidate();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        private int MeasureWidth()
        {
            using (var g = CreateGraphics())
            {
                var fmt = StringFormat.GenericTypographic;
                float x = 8f;
                x += 10f;
                float tagW = Math.Max(52f, g.MeasureString(Tag, MicroFont, PointF.Empty, fmt).Width + 12f);
                x += tagW + 8f;
                x += g.MeasureString(SummaryText(), PillFont, PointF.Empty, fmt).Width;
                return Math.Max(10, (int)Math.Ceiling(x) + 10);
            }
        }

        private string TipText()
        {
            if (!_known)
                return "Waiting for the daily billing check.";
            if (_count == 0)
                return "No billable trips in the past 3 months.";
            if (_count == 1)
                return "1 day still has trips ready to bill. Click for the list.";
            return _count.ToString(CultureInfo.InvariantCulture)
                + " days still have trips ready to bill. Click for the list.";
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (var b = new SolidBrush(SupeyTheme.SurfaceHeader))
                pevent.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var fmt = StringFormat.GenericTypographic;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            bool live = _known && _count > 0;
            Color hazard = SupeyTheme.WarnText;
            using (var fill = new SolidBrush(SupeyTheme.SurfaceHeader))
            using (var path = Rounded(r, Height / 2f))
                g.FillPath(fill, path);
            using (var pen = new Pen(SupeyTheme.Divider))
            using (var path = Rounded(r, Height / 2f))
                g.DrawPath(pen, path);

            float x = 8f;
            var dot = new RectangleF(x, (Height - 8f) / 2f, 8f, 8f);
            if (!live)
            {
                using (var pen = new Pen(SupeyTheme.TextMuted, 1.2f))
                    g.DrawEllipse(pen, dot);
            }
            else
            {
                using (var b = new SolidBrush(hazard))
                    g.FillEllipse(b, dot);
            }
            x += 12f;

            float tagW = Math.Max(52f, g.MeasureString(Tag, MicroFont, PointF.Empty, fmt).Width + 12f);
            var tagRect = new RectangleF(x, (Height - 14f) / 2f, tagW, 14f);
            using (var bg = new SolidBrush(live ? hazard : SupeyTheme.TextMuted))
            using (var path = Rounded(tagRect, tagRect.Height / 2f))
                g.FillPath(bg, path);
            var tagSz = g.MeasureString(Tag, MicroFont, PointF.Empty, fmt);
            using (var br = new SolidBrush(live ? SupeyTheme.Surface : Color.White))
                g.DrawString(Tag, MicroFont, br,
                    tagRect.X + (tagRect.Width - tagSz.Width) / 2f,
                    tagRect.Y + (tagRect.Height - tagSz.Height) / 2f, fmt);
            x += tagW + 8f;

            string summary = SummaryText();
            var sumSize = g.MeasureString(summary, PillFont, PointF.Empty, fmt);
            using (var br = new SolidBrush(live ? SupeyTheme.TextSecondary : SupeyTheme.TextMuted))
                g.DrawString(summary, PillFont, br, x, (Height - sumSize.Height) / 2f, fmt);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _tip.Dispose();
            base.Dispose(disposing);
        }

        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class BillingDaysPopup : Form
    {
        private const int PadX = 12;
        private const int PadY = 10;
        private const int RowH = 34;
        private const int HeaderH = 28;
        private readonly List<Row> _rows = new List<Row>();
        private int _hover = -1;
        private int _scroll;
        private string _header = "Past 3 months";

        private struct Row
        {
            public string Iso;
            public string When;
            public string Count;
            public bool Quiet;
        }

        public BillingDaysPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = SupeyTheme.SurfaceElevated;
            Font = SupeyTheme.BodyFont;
            Width = 280;
            Height = 72;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        public void ShowDays(List<HiatmeAiClient.BillingOpenDay> days, bool known)
        {
            _rows.Clear();
            if (!known)
            {
                _header = "Billing";
                _rows.Add(new Row { When = "Waiting for the daily check", Count = "", Quiet = true });
            }
            else if (days == null || days.Count == 0)
            {
                _header = "Past 3 months";
                _rows.Add(new Row { When = "Nothing left to bill", Count = "", Quiet = true });
            }
            else
            {
                _header = days.Count == 1 ? "1 billable day" : days.Count + " billable days";
                foreach (HiatmeAiClient.BillingOpenDay day in days)
                {
                    int n = day == null ? 0 : Math.Max(0, day.Billable);
                    _rows.Add(new Row
                    {
                        Iso = day == null ? "" : (day.Date ?? "").Trim(),
                        When = FormatDay(day == null ? "" : day.Date),
                        Count = n == 1 ? "1 trip" : n.ToString(CultureInfo.InvariantCulture) + " trips",
                        Quiet = false,
                    });
                }
            }
            int rows = Math.Min(8, Math.Max(1, _rows.Count));
            _scroll = 0;
            Height = PadY + HeaderH + rows * RowH + PadY;
            Width = MeasureWidth();
            ApplyRound();
            Invalidate();
        }

        private int MeasureWidth()
        {
            int w = 240;
            using (var g = CreateGraphics())
            {
                var fmt = StringFormat.GenericTypographic;
                float header = g.MeasureString(_header, SupeyTheme.CaptionFont, PointF.Empty, fmt).Width;
                w = Math.Max(w, (int)Math.Ceiling(header) + PadX * 2);
                foreach (Row row in _rows)
                {
                    float when = g.MeasureString(row.When, SupeyTheme.BodyFont, PointF.Empty, fmt).Width;
                    float count = string.IsNullOrEmpty(row.Count)
                        ? 0f
                        : g.MeasureString(row.Count, SupeyTheme.CaptionFont, PointF.Empty, fmt).Width + 16f;
                    w = Math.Max(w, (int)Math.Ceiling(when + count) + PadX * 2 + 28);
                }
            }
            return Math.Min(420, w);
        }

        private void ApplyRound()
        {
            var path = new GraphicsPath();
            int r = 10;
            var rect = new Rectangle(0, 0, Width, Height);
            int d = r * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d - 1, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d - 1, rect.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }

        public event EventHandler<string> DayChosen;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int next = RowAt(e.Y);
            if (next == _hover)
                return;
            _hover = next;
            Cursor = next >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;
            int index = RowAt(e.Y);
            if (index < 0)
                return;
            string iso = _rows[index].Iso;
            if (string.IsNullOrWhiteSpace(iso))
                return;
            DayChosen?.Invoke(this, iso);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover < 0)
                return;
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, _rows.Count - 8);
            int next = _scroll + (e.Delta > 0 ? -1 : 1);
            if (next < 0)
                next = 0;
            if (next > max)
                next = max;
            if (next == _scroll)
                return;
            _scroll = next;
            _hover = -1;
            Invalidate();
        }

        private int RowAt(int y)
        {
            int top = PadY + HeaderH;
            if (y < top)
                return -1;
            int index = _scroll + (y - top) / RowH;
            if (index < 0 || index >= _rows.Count || _rows[index].Quiet)
                return -1;
            if (index >= _scroll + 8)
                return -1;
            return index;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(SupeyTheme.SurfaceElevated);
            var fmt = StringFormat.GenericTypographic;
            using (var br = new SolidBrush(SupeyTheme.TextMuted))
                g.DrawString(_header, SupeyTheme.CaptionFont, br, PadX, PadY + 4, fmt);

            int y = PadY + HeaderH;
            int last = Math.Min(_rows.Count, _scroll + 8);
            for (int i = _scroll; i < last; i++)
            {
                Row row = _rows[i];
                var band = new Rectangle(6, y + 2, Width - 12, RowH - 4);
                if (i == _hover)
                {
                    using (var path = Rounded(band, 6))
                    using (var fill = new SolidBrush(SupeyTheme.AccentPrimary))
                        g.FillPath(fill, path);
                }
                Color ink = i == _hover
                    ? SupeyTheme.OnAccentText
                    : (row.Quiet ? SupeyTheme.TextMuted : SupeyTheme.TextPrimary);
                var whenSize = g.MeasureString(row.When, SupeyTheme.BodyFont, PointF.Empty, fmt);
                using (var br = new SolidBrush(ink))
                    g.DrawString(row.When, SupeyTheme.BodyFont, br, PadX, y + (RowH - whenSize.Height) / 2f, fmt);
                if (!string.IsNullOrEmpty(row.Count))
                {
                    var countSize = g.MeasureString(row.Count, SupeyTheme.CaptionFont, PointF.Empty, fmt);
                    float chipW = Math.Max(54f, countSize.Width + 14f);
                    var chip = new RectangleF(Width - PadX - chipW, y + (RowH - 18f) / 2f, chipW, 18f);
                    Color chipFill = i == _hover ? Color.FromArgb(48, 255, 255, 255) : SupeyTheme.SurfaceHeader;
                    Color chipInk = i == _hover ? SupeyTheme.OnAccentText : SupeyTheme.TextSecondary;
                    using (var path = Rounded(chip, 9f))
                    using (var fill = new SolidBrush(chipFill))
                        g.FillPath(fill, path);
                    using (var br = new SolidBrush(chipInk))
                        g.DrawString(row.Count, SupeyTheme.CaptionFont, br,
                            chip.X + (chip.Width - countSize.Width) / 2f,
                            chip.Y + (chip.Height - countSize.Height) / 2f, fmt);
                }
                y += RowH;
            }

            using (var pen = new Pen(SupeyTheme.BorderSubtle))
                g.DrawPath(pen, Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10f));
        }

        private static string FormatDay(string iso)
        {
            if (DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
                return day.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
            return string.IsNullOrWhiteSpace(iso) ? "Unknown day" : iso;
        }

        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
