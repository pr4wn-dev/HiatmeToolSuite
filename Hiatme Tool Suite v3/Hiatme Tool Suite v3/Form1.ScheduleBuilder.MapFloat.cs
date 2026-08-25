using System;
using System.Drawing;
using System.Windows.Forms;
using Hiatme_Tool_Suite_v3.Properties;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private Panel _fsMapSurface;
        private ScheduleBuilderMapFloatForm _fsMapFloatForm;
        private SupeyButton _fsMapFloatBtn;
        private SupeyButton _fsMapHideBtn;
        private SupeyButton _fsMapDockFloatChip;
        private bool _fsMapFloating = true;
        private bool _fsMapUiVisible = true;
        private bool _fsApplyingMapPresentation;
        private bool _fsMapFloatTopMost;

        private bool FsMapIsShownToUser()
        {
            if (!_fsMapUiVisible || _fsMap == null || _fsMap.IsDisposed)
                return false;
            if (_fsMapFloating)
                return _fsMapFloatForm != null && _fsMapFloatForm.Visible && !_fsMapFloatForm.IsDisposed;
            return _fsMainSplit != null && !_fsMainSplit.Panel1Collapsed;
        }

        private void ApplySavedFsMapPresentation()
        {
            try
            {
                _fsMapFloating = Settings.Default.FsMapFloating;
                _fsMapUiVisible = Settings.Default.FsMapUiVisible;
                _fsMapFloatTopMost = Settings.Default.FsMapFloatTopMost;
            }
            catch
            {
                _fsMapFloating = true;
                _fsMapUiVisible = true;
            }

            ApplyFsMapPresentation(persist: false);
        }

        private void ApplyFsMapPresentation(bool persist)
        {
            if (_fsMapSurface == null || _fsMainSplit == null)
                return;

            _fsApplyingMapPresentation = true;
            try
            {
                if (!_fsMapUiVisible)
                {
                    HideFsMapSurfaceCore();
                }
                else if (_fsMapFloating)
                {
                    ShowFsMapFloatingCore();
                }
                else
                {
                    ShowFsMapDockedCore();
                }
            }
            catch
            {
                // SplitContainer collapse / GMap reparent can throw after a dock.
                // Keep the map on screen instead of leaving it on a hidden window.
                if (_fsMapFloating && _fsMapUiVisible)
                {
                    try { ShowFsMapFloatingCore(); }
                    catch { try { ShowFsMapDockedCore(); } catch { } }
                }
            }
            finally
            {
                UpdateFsMapPresentationButtons();
                UpdateFsMapFloatCaption();
                SyncFsSettingsFloatMapCheck();
                _fsApplyingMapPresentation = false;
            }

            if (persist)
                PersistFsMapPresentation();
        }

        private void ShowFsMap()
        {
            _fsMapUiVisible = true;
            ApplyFsMapPresentation(persist: true);
            if (ScheduleOsrmGate.PreviewRoutingOk)
                ApplyFsMapDisplayFilter(autoFit: false);
        }

        private void HideFsMap()
        {
            PersistFsMapFloatBounds();
            _fsMapUiVisible = false;
            ApplyFsMapPresentation(persist: true);
        }

        private void ToggleFsMapVisible()
        {
            if (FsMapIsShownToUser())
                HideFsMap();
            else
                ShowFsMap();
        }

        private void SetFsMapFloating(bool floating)
        {
            PersistFsMapFloatBounds();
            _fsMapFloating = floating;
            _fsMapUiVisible = true;
            ApplyFsMapPresentation(persist: true);
            if (ScheduleOsrmGate.PreviewRoutingOk)
                ApplyFsMapDisplayFilter(autoFit: false);
        }

        private void ToggleFsMapFloating()
        {
            SetFsMapFloating(!(_fsMapUiVisible && _fsMapFloating));
        }

        private void SetFsMainSplitMapCollapsed(bool collapsed)
        {
            if (_fsMainSplit == null || _fsMainSplit.IsDisposed)
                return;

            try
            {
                if (collapsed)
                {
                    _fsMainSplit.Panel1MinSize = 0;
                    if (!_fsMainSplit.Panel1Collapsed)
                        _fsMainSplit.Panel1Collapsed = true;
                    return;
                }

                if (_fsMainSplit.Panel1Collapsed)
                    _fsMainSplit.Panel1Collapsed = false;
                if (_fsMainSplit.Panel1MinSize < 80)
                    _fsMainSplit.Panel1MinSize = 120;
            }
            catch
            {
                // SplitContainer rejects collapse when min-sizes don't fit the current height.
            }
        }

        private void ShowFsMapDockedCore()
        {
            EnsureFsMapFloatForm();
            if (_fsMapFloatForm != null && !_fsMapFloatForm.IsDisposed)
                _fsMapFloatForm.Hide();

            ReparentFsMapSurface(_fsMapWorkPanel);
            SetFsMainSplitMapCollapsed(false);
            RevealFsDockedMapSplit();
        }

        private void RevealFsDockedMapSplit()
        {
            if (_fsMainSplit == null || _fsMainSplit.Panel1Collapsed)
                return;

            int total = _fsMainSplit.Height;
            if (total < 200)
                return;

            int mapH = Math.Max(140, (int)(total * 0.42));
            int maxMap = total - _fsMainSplit.Panel2MinSize - _fsMainSplit.SplitterWidth;
            if (mapH > maxMap)
                mapH = Math.Max(_fsMainSplit.Panel1MinSize, maxMap);

            _applyingFsDefaultSplit = true;
            try
            {
                _fsMainSplit.SplitterDistance = mapH;
            }
            catch
            {
                // layout not ready yet
            }
            finally
            {
                _applyingFsDefaultSplit = false;
            }

            _fsDefaultSplitApplied = true;
            _fsUserAdjustedMainSplit = false;
        }

        private void ShowFsMapFloatingCore()
        {
            var form = EnsureFsMapFloatForm();
            if (form == null)
                return;

            RestoreFsMapFloatBounds(form);
            form.Pinned = _fsMapFloatTopMost;
            if (!form.IsHandleCreated)
                form.CreateControl();

            ReparentFsMapSurface(form.MapHost);
            SetFsMainSplitMapCollapsed(true);
            PresentFsMapFloatForm(form);
        }

        private void PresentFsMapFloatForm(ScheduleBuilderMapFloatForm form)
        {
            if (form == null || form.IsDisposed)
                return;

            if (form.Owner != this)
                form.Owner = this;

            if (form.WindowState == FormWindowState.Minimized)
                form.WindowState = FormWindowState.Normal;

            if (!form.Visible)
                form.Show(this);

            form.Visible = true;
            form.BringToFront();
            try { form.Activate(); } catch { }
            form.Update();
        }

        private void HideFsMapSurfaceCore()
        {
            PersistFsMapFloatBounds();
            if (_fsMapFloatForm != null && !_fsMapFloatForm.IsDisposed)
                _fsMapFloatForm.Hide();

            ReparentFsMapSurface(_fsMapWorkPanel);
            SetFsMainSplitMapCollapsed(true);
        }

        private ScheduleBuilderMapFloatForm EnsureFsMapFloatForm()
        {
            if (_fsMapFloatForm != null && !_fsMapFloatForm.IsDisposed)
                return _fsMapFloatForm;

            var form = new ScheduleBuilderMapFloatForm();
            form.DockRequested += () => SetFsMapFloating(false);
            form.HideRequested += HideFsMap;
            form.PinChanged += () =>
            {
                _fsMapFloatTopMost = form.Pinned;
                PersistFsMapPresentation();
            };
            form.Move += (s, e) => PersistFsMapFloatBounds();
            form.ResizeEnd += (s, e) => PersistFsMapFloatBounds();
            form.LiveResizeEnded += (s, e) => PersistFsMapFloatBounds();
            form.VisibleChanged += (s, e) =>
            {
                if (_fsApplyingMapPresentation)
                    return;
                if (form.Visible)
                    _fsMapFloatTopMost = form.Pinned;
            };
            _fsMapFloatForm = form;
            return form;
        }

        private void ReparentFsMapSurface(Control newParent)
        {
            if (_fsMapSurface == null || newParent == null || _fsMapSurface.IsDisposed)
                return;
            if (ReferenceEquals(_fsMapSurface.Parent, newParent))
                return;

            var old = _fsMapSurface.Parent;
            old?.SuspendLayout();
            newParent.SuspendLayout();
            try
            {
                old?.Controls.Remove(_fsMapSurface);
                _fsMapSurface.Dock = DockStyle.Fill;
                newParent.Controls.Add(_fsMapSurface);
                _fsMapSurface.BringToFront();
            }
            finally
            {
                newParent.ResumeLayout(true);
                old?.ResumeLayout(true);
            }

            try
            {
                _fsMap?.Invalidate(true);
            }
            catch
            {
                // GMap can throw if tiles are mid-reload during close.
            }
        }

        private void UpdateFsMapPresentationButtons()
        {
            if (_fsMapFloatBtn != null)
            {
                bool floatingShown = _fsMapUiVisible && _fsMapFloating;
                _fsMapFloatBtn.Text = floatingShown ? "Dock map" : "Float map";
                _fsMapFloatBtn.Kind = floatingShown
                    ? SupeyButton.Variant.Primary
                    : SupeyButton.Variant.Secondary;
            }

            if (_fsMapHideBtn != null)
            {
                bool shown = FsMapIsShownToUser();
                _fsMapHideBtn.Text = shown ? "Hide map" : "Show map";
            }

            UpdateFsDockedMapFloatChip();
        }

        private void EnsureFsDockedMapFloatChip()
        {
            if (_fsMapWorkPanel == null || _fsMapDockFloatChip != null)
                return;

            _fsMapDockFloatChip = new SupeyButton
            {
                Text = "Float",
                Kind = SupeyButton.Variant.Primary,
                Size = new Size(68, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _fsMapDockFloatChip.Click += (s, e) => SetFsMapFloating(true);
            _fsMapWorkPanel.Controls.Add(_fsMapDockFloatChip);
            _fsMapWorkPanel.Resize += (s, e) => PlaceFsDockedMapFloatChip();
            var tip = SupeyToolTip.Create(initialDelay: 250);
            tip.SetToolTip(_fsMapDockFloatChip, "Pop the map back into its own window.");
        }

        private void UpdateFsDockedMapFloatChip()
        {
            EnsureFsDockedMapFloatChip();
            if (_fsMapDockFloatChip == null)
                return;

            bool show = _fsMapUiVisible && !_fsMapFloating;
            _fsMapDockFloatChip.Visible = show;
            if (show)
                PlaceFsDockedMapFloatChip();
        }

        private void PlaceFsDockedMapFloatChip()
        {
            if (_fsMapDockFloatChip == null || _fsMapWorkPanel == null || !_fsMapDockFloatChip.Visible)
                return;

            _fsMapDockFloatChip.Location = new Point(
                Math.Max(8, _fsMapWorkPanel.ClientSize.Width - _fsMapDockFloatChip.Width - 10),
                10);
            _fsMapDockFloatChip.BringToFront();
        }

        private void UpdateFsMapFloatCaption()
        {
            _fsMapFloatForm?.SetDriverCaption(_fsActiveDriverTab);
        }

        private void PersistFsMapPresentation()
        {
            try
            {
                Settings.Default.FsMapFloating = _fsMapFloating;
                Settings.Default.FsMapUiVisible = _fsMapUiVisible;
                Settings.Default.FsMapFloatTopMost = _fsMapFloatForm?.Pinned ?? _fsMapFloatTopMost;
                PersistFsMapFloatBounds();
                Settings.Default.Save();
            }
            catch
            {
                // best effort
            }
        }

        private void PersistFsMapFloatBounds()
        {
            var form = _fsMapFloatForm;
            if (form == null || form.IsDisposed || !form.Visible || form.WindowState != FormWindowState.Normal)
                return;

            try
            {
                Settings.Default.FsMapFloatX = form.Left;
                Settings.Default.FsMapFloatY = form.Top;
                Settings.Default.FsMapFloatW = form.Width;
                Settings.Default.FsMapFloatH = form.Height;
                Settings.Default.FsMapFloatTopMost = form.Pinned;
            }
            catch
            {
                // best effort
            }
        }

        private void RestoreFsMapFloatBounds(ScheduleBuilderMapFloatForm form)
        {
            if (form == null)
                return;

            int x, y, w, h;
            try
            {
                x = Settings.Default.FsMapFloatX;
                y = Settings.Default.FsMapFloatY;
                w = Settings.Default.FsMapFloatW;
                h = Settings.Default.FsMapFloatH;
            }
            catch
            {
                PlaceFsMapFloatDefault(form);
                return;
            }

            var proposed = new Rectangle(x, y, w, h);
            if (w < 360 || h < 280 || !IsFsMapBoundsOnScreen(proposed))
            {
                PlaceFsMapFloatDefault(form);
                return;
            }

            form.Bounds = proposed;
        }

        private void PlaceFsMapFloatDefault(ScheduleBuilderMapFloatForm form)
        {
            var owner = Bounds;
            int w = 520;
            int h = 440;
            int x = owner.Right - w - 24;
            int y = owner.Top + 90;
            var wa = Screen.FromControl(this).WorkingArea;
            if (x + w > wa.Right)
                x = wa.Right - w - 16;
            if (y + h > wa.Bottom)
                y = wa.Bottom - h - 16;
            if (x < wa.Left)
                x = wa.Left + 16;
            if (y < wa.Top)
                y = wa.Top + 16;
            form.Bounds = new Rectangle(x, y, w, h);
        }

        private static bool IsFsMapBoundsOnScreen(Rectangle bounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(bounds))
                    return true;
            }

            return false;
        }

        private void ReleaseFsMapFloatForShutdown()
        {
            PersistFsMapPresentation();
            if (_fsMapFloatForm == null || _fsMapFloatForm.IsDisposed)
                return;

            ReparentFsMapSurface(_fsMapWorkPanel);
            _fsMapFloatForm.AllowClose();
            try
            {
                _fsMapFloatForm.Close();
            }
            catch
            {
                // ignore
            }

            _fsMapFloatForm = null;
        }
    }
}
