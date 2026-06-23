using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Hiatme_Tool_Suite_v3
{
    public class RJDatePicker : DateTimePicker
    {
        // ── Win32 plumbing to dark-theme the popup MonthCalendar ──────────────────
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int DTM_FIRST = 0x1000;
        private const int DTM_GETMONTHCAL = DTM_FIRST + 8;

        //Fields
        //-> Appearance
        private Color skinColor = Color.MediumSlateBlue;
        private Color textColor = Color.White;
        private Color borderColor = Color.PaleVioletRed;
        private int borderSize = 0;
        private bool _themeHooked;

        //-> Other Values
        private bool droppedDown = false;
        private Image calendarIcon = Properties.Resources.calendarWhite;
        private RectangleF iconButtonArea;
        private const int calendarIconWidth = 34;
        private const int arrowIconWidth = 17;

        // Custom themed calendar popup that replaces the un-themable native MonthCalendar.
        private SupeyCalendarPopup _popup;
        private DateTime _lastPopupClosed = DateTime.MinValue;
        private const int WM_LBUTTONDOWN = 0x0201;

        //Properties
        public Color SkinColor
        {
            get { return skinColor; }
            set
            {
                skinColor = value;
                if (skinColor.GetBrightness() >= 0.6F)
                    calendarIcon = Properties.Resources.calendarDark;
                else calendarIcon = Properties.Resources.calendarWhite;
                this.Invalidate();
            }
        }

        public Color TextColor
        {
            get { return textColor; }
            set
            {
                textColor = value;
                this.Invalidate();
            }
        }

        public Color BorderColor
        {
            get { return borderColor; }
            set
            {
                borderColor = value;
                this.Invalidate();
            }
        }

        public int BorderSize
        {
            get { return borderSize; }
            set
            {
                borderSize = value;
                this.Invalidate();
            }
        }

        //Constructor
        public RJDatePicker()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.MinimumSize = new Size(0, 35);
            this.Font = new Font(this.Font.Name, 9.5F);
            ApplyTheme();
            SupeyThemeManager.ThemeChanged += OnSupeyThemeChanged;
        }

        /// <summary>Pull the closed-face + popup-calendar colors from the active Supey theme.</summary>
        public void ApplyTheme()
        {
            skinColor = SupeyTheme.SurfaceElevated;
            textColor = SupeyTheme.TextPrimary;
            borderColor = SupeyTheme.BorderSubtle;
            calendarIcon = skinColor.GetBrightness() >= 0.6F
                ? Properties.Resources.calendarDark
                : Properties.Resources.calendarWhite;

            // These map onto the popup MonthCalendar once visual styles are disabled for it.
            CalendarMonthBackground = SupeyTheme.Surface;
            CalendarForeColor = SupeyTheme.TextPrimary;
            CalendarTitleBackColor = SupeyTheme.SurfaceHeader;
            CalendarTitleForeColor = SupeyTheme.TextPrimary;
            CalendarTrailingForeColor = SupeyTheme.TextMuted;
            Font = SupeyTheme.BodyFont;
            this.Invalidate();
        }

        private void OnSupeyThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ApplyTheme();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnSupeyThemeChanged;
                _popup?.Dispose();
            }
            base.Dispose(disposing);
        }

        //Overridden methods

        /// <summary>
        /// Swallow the native left-click (which would open the gray Win32 calendar) and show our own
        /// fully-themed <see cref="SupeyCalendarPopup"/> instead.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDOWN && !DesignMode)
            {
                ToggleCustomPopup();
                return;
            }
            base.WndProc(ref m);
        }

        private void ToggleCustomPopup()
        {
            // If the click is what just closed the popup, don't immediately reopen it.
            if ((DateTime.Now - _lastPopupClosed).TotalMilliseconds < 250) return;
            if (_popup != null && _popup.Visible) { _popup.Close(); return; }

            if (_popup == null)
            {
                _popup = new SupeyCalendarPopup();
                _popup.DateSelected += d =>
                {
                    this.Value = d;
                    droppedDown = false;
                    this.Invalidate();
                };
                _popup.Closed += (s, e) => { _lastPopupClosed = DateTime.Now; droppedDown = false; Invalidate(); };
            }

            droppedDown = true;
            Invalidate();
            _popup.ShowBelow(this, this.Value);
        }
        protected override void OnCloseUp(EventArgs eventargs)
        {
            base.OnCloseUp(eventargs);
            droppedDown = false;
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            e.Handled = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (Graphics graphics = this.CreateGraphics())
            using (Pen penBorder = new Pen(borderColor, borderSize))
            using (SolidBrush skinBrush = new SolidBrush(skinColor))
            using (SolidBrush openIconBrush = new SolidBrush(Color.FromArgb(50, 64, 64, 64)))
            using (SolidBrush textBrush = new SolidBrush(textColor))
            using (StringFormat textFormat = new StringFormat())
            {
                RectangleF clientArea = new RectangleF(0, 0, this.Width - 0.5F, this.Height - 0.5F);
                RectangleF iconArea = new RectangleF(clientArea.Width - calendarIconWidth, 0, calendarIconWidth, clientArea.Height);
                penBorder.Alignment = PenAlignment.Inset;
                textFormat.LineAlignment = StringAlignment.Center;

                //Draw surface
                graphics.FillRectangle(skinBrush, clientArea);
                //Draw text
                graphics.DrawString("   " + this.Text, this.Font, textBrush, clientArea, textFormat);
                //Draw open calendar icon highlight
                if (droppedDown == true) graphics.FillRectangle(openIconBrush, iconArea);
                //Draw border 
                if (borderSize >= 1) graphics.DrawRectangle(penBorder, clientArea.X, clientArea.Y, clientArea.Width, clientArea.Height);
                //Draw icon
                graphics.DrawImage(calendarIcon, this.Width - calendarIcon.Width - 9, (this.Height - calendarIcon.Height) / 2);

            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Designer-serialized SkinColor/TextColor/BorderColor run after the ctor; re-assert the
            // live theme here so every picker matches the active preset instead of the baked grays.
            if (!_themeHooked)
            {
                _themeHooked = true;
                ApplyTheme();
            }
            int iconWidth = GetIconButtonWidth();
            iconButtonArea = new RectangleF(this.Width - iconWidth, 0, iconWidth, this.Height);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (iconButtonArea.Contains(e.Location))
                this.Cursor = Cursors.Hand;
            else this.Cursor = Cursors.Default;
        }

        //Private methods
        private int GetIconButtonWidth()
        {
            int textWidh = TextRenderer.MeasureText(this.Text, this.Font).Width;
            if (textWidh <= this.Width - (calendarIconWidth + 20))
                return calendarIconWidth;
            else return arrowIconWidth;
        }
    }
}
