using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Login-safe dropdown: same look as <see cref="SupeyComboBox"/> but no native ComboBox HWND
    /// (WinForms ComboBox + UserPaint leaves a ghost chrome copy at stale coordinates).
    /// </summary>
    public sealed class SupeyDropDownField : Control
    {
        private const int LeftPadding = 16;
        private const int RightPadding = 12;
        private const int HintSmallY = 4;
        private const int HintSmallH = 18;
        private const int BottomPad = 3;
        private const int ActivationH = 2;
        private const int TallHeight = 58;
        private const int ShortHeight = 36;
        private const int ArrowInset = 12;
        private const int IconSize = 24;

        private readonly Timer _focusAnimTimer;
        private readonly ItemCollection _items;
        private string _hint = string.Empty;
        private bool _useTallSize = true;
        private bool _focused;
        private bool _menuOpen;
        private float _focusAnim;
        private int _lineY;
        private int _selectedIndex = -1;

        public SupeyDropDownField()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick,
                true);

            _items = new ItemCollection(this);
            Font = SupeyTheme.BodyFont;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(286, TallHeight);

            _focusAnimTimer = new Timer { Interval = 15 };
            _focusAnimTimer.Tick += FocusAnimTick;

            GotFocus += (_, __) => { _focused = true; StartFocusAnim(); Invalidate(); };
            LostFocus += (_, __) => { if (!_menuOpen) { _focused = false; StartFocusAnim(); Invalidate(); } };
            MouseEnter += (_, __) => Invalidate();
            MouseLeave += (_, __) => Invalidate();

            SupeyThemeManager.ThemeChanged += OnThemeChanged;
            ApplyHeightMetrics();
        }

        public event EventHandler SelectedIndexChanged;

        // Designer / MaterialComboBox shims (ignored where irrelevant)
        public int Depth { get; set; }
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;
        public bool AutoResize { get; set; }
        public bool UseAccent { get; set; } = true;
        public int StartIndex { get; set; }
        public bool FormattingEnabled { get; set; }
        public bool IntegralHeight { get; set; }
        public int DropDownHeight { get; set; }
        public int DropDownWidth { get; set; }
        public ComboBoxStyle DropDownStyle { get; set; } = ComboBoxStyle.DropDownList;
        public DrawMode DrawMode { get; set; } = DrawMode.OwnerDrawVariable;
        public int ItemHeight { get; set; }
        public int MaxDropDownItems { get; set; } = 8;

        public ItemCollection Items => _items;

        public bool AlignTextWithIconFields { get; set; }

        private int TextPad => AlignTextWithIconFields ? LeftPadding + IconSize : LeftPadding;

        public bool UseTallSize
        {
            get => _useTallSize;
            set { _useTallSize = value; ApplyHeightMetrics(); Invalidate(); }
        }

        public string Hint
        {
            get => _hint;
            set { _hint = value ?? string.Empty; Invalidate(); }
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int v = _items.Count == 0 ? -1 : Math.Max(-1, Math.Min(value, _items.Count - 1));
                if (_selectedIndex == v) return;
                _selectedIndex = v;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public object SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        public override string Text => GetItemText(SelectedItem);

        public string GetItemText(object item) => item?.ToString() ?? string.Empty;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                _focusAnimTimer?.Stop();
                _focusAnimTimer?.Dispose();
                if (_pickerPopup != null)
                {
                    _pickerPopup.ItemPicked -= PickerPopup_ItemPicked;
                    _pickerPopup.Closed -= PickerPopup_Closed;
                    _pickerPopup.Dispose();
                    _pickerPopup = null;
                }
            }
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyStartIndex();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyHeightMetrics();
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ApplyHeightMetrics();
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled && _items.Count > 0)
                ShowPickerMenu();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Down || e.KeyCode == Keys.F4)
            {
                e.Handled = true;
                if (Enabled && _items.Count > 0)
                    ShowPickerMenu();
            }
        }

        internal void OnItemsChanged()
        {
            if (_selectedIndex >= _items.Count)
                _selectedIndex = _items.Count > 0 ? 0 : -1;
            ApplyStartIndex();
            Invalidate();
        }

        private void ApplyStartIndex()
        {
            if (DesignMode || _items.Count == 0) return;
            if (_selectedIndex >= 0) return;
            if (StartIndex >= 0 && StartIndex < _items.Count)
                _selectedIndex = StartIndex;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            Invalidate();
        }

        private void StartFocusAnim()
        {
            if (!_focusAnimTimer.Enabled)
                _focusAnimTimer.Start();
        }

        private void FocusAnimTick(object sender, EventArgs e)
        {
            float target = (_focused || _menuOpen) ? 1f : 0f;
            const float step = 0.12f;
            if (_focusAnim < target) _focusAnim = Math.Min(target, _focusAnim + step);
            else if (_focusAnim > target) _focusAnim = Math.Max(target, _focusAnim - step);
            Invalidate();
            if (Math.Abs(_focusAnim - target) < 0.001f) { _focusAnim = target; _focusAnimTimer.Stop(); }
        }

        private void ApplyHeightMetrics()
        {
            int h = _useTallSize ? TallHeight : ShortHeight;
            if (Height != h)
                Height = h;
            _lineY = h - BottomPad;
            ItemHeight = _useTallSize ? 44 : 36;
            if (Width > 0)
                DropDownWidth = Width;
        }

        private SupeyFieldDropDownPopup _pickerPopup;

        private SupeyFieldDropDownPopup GetOrCreatePickerPopup()
        {
            if (_pickerPopup != null) return _pickerPopup;
            _pickerPopup = new SupeyFieldDropDownPopup();
            _pickerPopup.ItemPicked += PickerPopup_ItemPicked;
            _pickerPopup.Closed += PickerPopup_Closed;
            return _pickerPopup;
        }

        private void ShowPickerMenu()
        {
            _menuOpen = true;
            _focused = true;
            StartFocusAnim();
            Invalidate();

            var labels = new string[_items.Count];
            for (int i = 0; i < _items.Count; i++)
                labels[i] = GetItemText(_items[i]);

            int rowH = ItemHeight > 0 ? ItemHeight : (_useTallSize ? 44 : 36);
            int menuW = DropDownWidth > 0 ? DropDownWidth : Width;
            GetOrCreatePickerPopup().ShowBelow(this, labels, _selectedIndex, rowH, MaxDropDownItems, menuW);
        }

        private void PickerPopup_ItemPicked(int idx)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && idx >= 0 && idx < _items.Count)
                        SelectedIndex = idx;
                }));
            }
            else if (!IsDisposed && idx >= 0 && idx < _items.Count)
            {
                SelectedIndex = idx;
            }
        }

        private void PickerPopup_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            _menuOpen = false;
            _focused = Focused;
            StartFocusAnim();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(SupeyTheme.Surface);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(SupeyTheme.Surface))
                g.FillRectangle(bg, 0, 0, Width, _lineY);

            bool hasHint = !string.IsNullOrEmpty(_hint);
            bool hasValue = SelectedIndex >= 0 && !string.IsNullOrEmpty(Text);
            bool floatHint = hasHint && _useTallSize && hasValue;

            var hintRect = new Rectangle(TextPad, floatHint ? HintSmallY : 0, Width - TextPad - RightPadding - 20, floatHint ? HintSmallH : _lineY);

            using (var div = new SolidBrush(SupeyTheme.BorderSubtle))
                g.FillRectangle(div, 0, _lineY, Width, 1);

            if (_focusAnim > 0f)
            {
                int half = (int)(Width / 2f * _focusAnim);
                int cx = Width / 2;
                using (var acc = new SolidBrush(SupeyTheme.AccentPrimary))
                    g.FillRectangle(acc, cx - half, _lineY, half * 2, ActivationH);
            }

            if (floatHint)
            {
                Color hintColor = Blend(SupeyTheme.TextSecondary, SupeyTheme.AccentPrimary, _focusAnim);
                TextRenderer.DrawText(g, _hint, SupeyTheme.CaptionFont, hintRect, hintColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            if (hasValue)
            {
                var textRect = new Rectangle(
                    TextPad,
                    floatHint ? hintRect.Bottom - 2 : 0,
                    Width - TextPad - RightPadding - 16,
                    floatHint ? _lineY - (hintRect.Bottom - 2) : _lineY);
                TextRenderer.DrawText(g, Text, Font, textRect,
                    Enabled ? SupeyTheme.TextPrimary : SupeyTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else if (hasHint && !floatHint)
            {
                TextRenderer.DrawText(g, _hint, Font, hintRect, SupeyTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            DrawArrow(g);
        }

        private void DrawArrow(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cy = _lineY / 2;
            int ax = Width - ArrowInset;
            Color arrowColor = !Enabled ? SupeyTheme.TextMuted
                : (_menuOpen || _focused) ? SupeyTheme.AccentPrimary : SupeyTheme.TextSecondary;
            using (var brush = new SolidBrush(arrowColor))
            {
                var tri = new Point[]
                {
                    new Point(ax - 5, cy - 2),
                    new Point(ax + 5, cy - 2),
                    new Point(ax, cy + 3),
                };
                g.FillPolygon(brush, tri);
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        private static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public sealed class ItemCollection : IList
        {
            private readonly List<object> _inner = new List<object>();
            private readonly SupeyDropDownField _owner;

            internal ItemCollection(SupeyDropDownField owner) => _owner = owner;

            public void AddRange(object[] items)
            {
                if (items == null) return;
                _inner.AddRange(items);
                _owner.OnItemsChanged();
            }

            public int Count => _inner.Count;
            public bool IsReadOnly => false;
            public bool IsFixedSize => false;
            public object SyncRoot => _inner;
            public bool IsSynchronized => false;

            public object this[int index]
            {
                get => _inner[index];
                set { _inner[index] = value; _owner.OnItemsChanged(); }
            }

            public int Add(object value)
            {
                _inner.Add(value);
                _owner.OnItemsChanged();
                return _inner.Count - 1;
            }

            public void Clear()
            {
                _inner.Clear();
                _owner.OnItemsChanged();
            }

            public bool Contains(object value) => _inner.Contains(value);
            public void CopyTo(Array array, int index) => ((IList)_inner).CopyTo(array, index);
            public IEnumerator GetEnumerator() => _inner.GetEnumerator();
            public int IndexOf(object value) => _inner.IndexOf(value);
            public void Insert(int index, object value)
            {
                _inner.Insert(index, value);
                _owner.OnItemsChanged();
            }

            public void Remove(object value)
            {
                if (_inner.Remove(value))
                    _owner.OnItemsChanged();
            }

            public void RemoveAt(int index)
            {
                _inner.RemoveAt(index);
                _owner.OnItemsChanged();
            }
        }
    }
}
