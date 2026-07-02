using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private System.Windows.Forms.Panel _tripScoutSearchHost;
        private System.Windows.Forms.Panel _tripScoutActionsHost;
        private System.Windows.Forms.Panel _tripScoutHeaderHost;
        private System.Windows.Forms.Panel _tripScoutActivitySep;
        private System.Windows.Forms.Panel _tripScoutSimulateSep;
        private SupeyCard _tripScoutHeaderCard;

        private const int TripScoutTbPadH = 18;
        private const int TripScoutTbPadV = 12;
        internal const int TripScoutTbHeaderY = 10;
        internal const int TripScoutTbHeaderRowH = 42;
        private const int TripScoutTbHeaderGap = 10;
        internal const int TripScoutTbRowY = TripScoutTbHeaderY + TripScoutTbHeaderRowH + TripScoutTbHeaderGap;
        private const int TripScoutTbRowH = 46;
        internal const int TripScoutToolbarH = TripScoutTbRowY + TripScoutTbRowH + (TripScoutTbPadV * 2);
        private const int TripScoutTbRowGap = 10;
        private const int TripScoutTbTitleW = 118;
        private const int TripScoutTbHeaderTextGap = 10;
        private const int TripScoutTbCardPadH = 18;
        private const int TripScoutTbCardPadV = 8;
        private const int TripScoutTbCtrlH = 30;
        private const int TripScoutTbLabelGap = 8;
        private const int TripScoutTbItemGap = 8;
        private const int TripScoutTbSectionGap = 10;
        private const int TripScoutTbSepW = 1;
        private const int TripScoutTbSepH = TripScoutTbRowH - (TripScoutTbCardPadV * 2);
        private const int TripScoutTbDateW = 214;
        private const int TripScoutTbLoadW = 90;
        private const int TripScoutTbActionBtnW = 102;

        private static System.Windows.Forms.Panel MakeTripScoutToolbarSeparator(string name)
        {
            return new System.Windows.Forms.Panel
            {
                Name = name,
                Width = TripScoutTbSepW,
                Height = TripScoutTbSepH,
                BackColor = SupeyTheme.BorderSubtle,
            };
        }

        private static SupeyCard MakeTripScoutToolbarChromeCard(string name)
        {
            return new SupeyCard
            {
                Name = name,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
            };
        }

        private void EnsureTripScoutHeaderCard()
        {
            if (_tripScoutToolbarPanel == null || _tripScoutToolbarPanel.IsDisposed)
                return;
            if (_tripScoutHeaderCard != null && !_tripScoutHeaderCard.IsDisposed)
                return;

            _tripScoutHeaderCard = MakeTripScoutToolbarChromeCard("tripScoutHeaderCard");
            _tripScoutHeaderHost = new System.Windows.Forms.Panel
            {
                Name = "tripScoutHeaderHost",
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };
            _tripScoutHeaderCard.Controls.Add(_tripScoutHeaderHost);
            _tripScoutToolbarPanel.Controls.Add(_tripScoutHeaderCard);

            if (_tripScoutToolbarTitle != null)
                _tripScoutHeaderHost.Controls.Add(_tripScoutToolbarTitle);
            if (_tripScoutToolbarSubtitle != null)
                _tripScoutHeaderHost.Controls.Add(_tripScoutToolbarSubtitle);
        }

        private void EnsureTripScoutToolbarControlParents()
        {
            if (_tripScoutSearchHost == null || _tripScoutActionsHost == null || _tripScoutHeaderHost == null)
                return;

            ReparentTripScoutToolbarControl(_tripScoutToolbarTitle, _tripScoutHeaderHost);
            ReparentTripScoutToolbarControl(_tripScoutToolbarSubtitle, _tripScoutHeaderHost);
            ReparentTripScoutToolbarControl(_tripScoutSearchLabel, _tripScoutSearchHost);
            ReparentTripScoutToolbarControl(tssearchbox, _tripScoutSearchHost);
            ReparentTripScoutToolbarControl(_tripScoutDateLabel, _tripScoutActionsHost);
            ReparentTripScoutToolbarControl(tsdatepicker, _tripScoutActionsHost);
            ReparentTripScoutToolbarControl(tsloadbtn, _tripScoutActionsHost);
        }

        private static void ReparentTripScoutToolbarControl(Control control, Control host)
        {
            if (control == null || control.IsDisposed || host == null || host.IsDisposed)
                return;
            if (ReferenceEquals(control.Parent, host))
                return;

            control.Parent?.Controls.Remove(control);
            host.Controls.Add(control);
        }

        private static int MeasureTripScoutButtonWidth(SupeyButton btn)
        {
            if (btn == null || btn.IsDisposed)
                return 0;

            int textW = TextRenderer.MeasureText(btn.Text, btn.Font).Width;
            return Math.Max(TripScoutTbActionBtnW, textW + 20);
        }

        private void StyleTripScoutToolbarControls()
        {
            if (_tripScoutSearchLabel != null)
            {
                _tripScoutSearchLabel.Font = SupeyTheme.CaptionFont;
                _tripScoutSearchLabel.ForeColor = SupeyTheme.TextSecondary;
                _tripScoutSearchLabel.BackColor = Color.Transparent;
            }

            if (_tripScoutDateLabel != null)
            {
                _tripScoutDateLabel.Font = SupeyTheme.CaptionFont;
                _tripScoutDateLabel.ForeColor = SupeyTheme.TextSecondary;
                _tripScoutDateLabel.BackColor = Color.Transparent;
            }

            if (tssearchbox != null && !tssearchbox.IsDisposed)
            {
                tssearchbox.UseTallSize = false;
                tssearchbox.UseToolbarSize = true;
                tssearchbox.Height = TripScoutTbCtrlH;
                tssearchbox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }

            if (tsdatepicker != null && !tsdatepicker.IsDisposed)
            {
                tsdatepicker.UseToolbarSize = true;
                tsdatepicker.Size = new Size(TripScoutTbDateW, TripScoutTbCtrlH);
                tsdatepicker.BorderColor = SupeyTheme.BorderSubtle;
                tsdatepicker.BorderSize = 1;
                tsdatepicker.Font = SupeyTheme.BodyFont;
                tsdatepicker.SkinColor = SupeyTheme.Surface;
                tsdatepicker.TextColor = SupeyTheme.TextPrimary;
                tsdatepicker.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }

            if (tsloadbtn != null && !tsloadbtn.IsDisposed)
            {
                tsloadbtn.AutoSize = false;
                tsloadbtn.Size = new Size(TripScoutTbLoadW, TripScoutTbCtrlH);
                tsloadbtn.HighEmphasis = true;
                tsloadbtn.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }

            StyleTripScoutActivityButtons();
        }

        private void StyleTripScoutActivityButtons()
        {
            ApplyTripScoutActionButtonStyle(_tripScoutChangesBtn);
            ApplyTripScoutActionButtonStyle(_tripScoutWillCallsBtn);
            ApplyTripScoutActionButtonStyle(_tripScoutTestChangeBtn);
        }

        private static void ApplyTripScoutActionButtonStyle(SupeyButton btn)
        {
            if (btn == null || btn.IsDisposed)
                return;

            btn.AutoSize = false;
            btn.Height = TripScoutTbCtrlH;
            btn.Margin = Padding.Empty;
        }

        private void LayoutTripScoutToolbarControls()
        {
            if (_tripScoutToolbarPanel == null || _tripScoutToolbarPanel.IsDisposed || tssearchbox == null)
                return;

            StyleTripScoutToolbarControls();

            int padL = _tripScoutToolbarPanel.Padding.Left;
            int padR = _tripScoutToolbarPanel.Padding.Right;
            int clientW = _tripScoutToolbarPanel.ClientSize.Width;

            LayoutTripScoutLiveBell();
            int liveCardReserve = TripScoutLiveToolbarCardReservedWidth() + TripScoutTbRowGap;

            LayoutTripScoutHeaderCard(padL, padR, clientW, liveCardReserve);
            LayoutTripScoutToolbarRowCards(padL, padR, clientW);

            _tripScoutHeaderCard?.BringToFront();
            _tripScoutSearchCard?.BringToFront();
            _tripScoutActionsCard?.BringToFront();
            _tripScoutLiveToolbarCard?.BringToFront();
        }

        private void LayoutTripScoutHeaderCard(int padL, int padR, int clientW, int liveCardReserve)
        {
            if (_tripScoutHeaderCard == null || _tripScoutHeaderCard.IsDisposed)
                return;

            int headerW = Math.Max(180, clientW - padL - padR - liveCardReserve);
            _tripScoutHeaderCard.SetBounds(padL, TripScoutTbHeaderY, headerW, TripScoutTbHeaderRowH);
            LayoutTripScoutHeaderHost(headerW);
        }

        private void LayoutTripScoutHeaderHost(int hostW)
        {
            if (_tripScoutHeaderHost == null)
                return;

            int x = TripScoutTbCardPadH;
            int hostH = TripScoutTbHeaderRowH;
            int textY = TripScoutTbCardPadV;
            int textH = hostH - (TripScoutTbCardPadV * 2);

            if (_tripScoutToolbarTitle != null)
            {
                _tripScoutToolbarTitle.Font = SupeyTheme.SubHeaderFont;
                _tripScoutToolbarTitle.ForeColor = SupeyTheme.TextPrimary;
                _tripScoutToolbarTitle.BackColor = Color.Transparent;
                _tripScoutToolbarTitle.TextAlign = ContentAlignment.MiddleLeft;
                _tripScoutToolbarTitle.SetBounds(x, textY, TripScoutTbTitleW, textH);
                x += TripScoutTbTitleW + TripScoutTbHeaderTextGap;
            }

            if (_tripScoutToolbarSubtitle != null)
            {
                _tripScoutToolbarSubtitle.Font = SupeyTheme.CaptionFont;
                _tripScoutToolbarSubtitle.ForeColor = SupeyTheme.TextSecondary;
                _tripScoutToolbarSubtitle.BackColor = Color.Transparent;
                _tripScoutToolbarSubtitle.TextAlign = ContentAlignment.MiddleLeft;
                _tripScoutToolbarSubtitle.SetBounds(
                    x,
                    textY,
                    Math.Max(80, hostW - x - TripScoutTbCardPadH),
                    textH);
            }
        }

        private int MeasureTripScoutActionsContentWidth()
        {
            int w = TripScoutTbCardPadH * 2;
            w += MeasureTripScoutLabelWidth(_tripScoutDateLabel) + TripScoutTbLabelGap;
            w += TripScoutTbDateW + TripScoutTbItemGap;
            w += TripScoutTbLoadW;

            if (_tripScoutChangesBtn != null && !_tripScoutChangesBtn.IsDisposed)
            {
                w += TripScoutTbSectionGap + TripScoutTbSepW + TripScoutTbSectionGap;
                w += MeasureTripScoutButtonWidth(_tripScoutChangesBtn) + TripScoutTbItemGap;
                w += MeasureTripScoutButtonWidth(_tripScoutWillCallsBtn);
            }

            if (_tripScoutTestChangeBtn != null && !_tripScoutTestChangeBtn.IsDisposed)
            {
                w += TripScoutTbSectionGap + TripScoutTbSepW + TripScoutTbSectionGap;
                w += MeasureTripScoutButtonWidth(_tripScoutTestChangeBtn);
            }

            return w;
        }

        private static int MeasureTripScoutLabelWidth(Label label)
        {
            if (label == null || label.IsDisposed)
                return 0;
            return label.PreferredWidth;
        }

        private static int TripScoutToolbarCardControlY(int controlHeight) =>
            TripScoutTbCardPadV + Math.Max(0, (TripScoutTbRowH - (TripScoutTbCardPadV * 2) - controlHeight) / 2);

        private void LayoutTripScoutToolbarRowCards(int padL, int padR, int clientW)
        {
            int actionsW = MeasureTripScoutActionsContentWidth();
            if (_tripScoutActionsCard != null && !_tripScoutActionsCard.IsDisposed)
            {
                _tripScoutActionsCard.SetBounds(
                    Math.Max(padL, clientW - padR - actionsW),
                    TripScoutTbRowY,
                    actionsW,
                    TripScoutTbRowH);
                LayoutTripScoutActionsHost(actionsW);
            }

            int searchW = Math.Max(240, clientW - padL - padR - actionsW - TripScoutTbRowGap);
            if (_tripScoutSearchCard != null && !_tripScoutSearchCard.IsDisposed)
            {
                _tripScoutSearchCard.SetBounds(padL, TripScoutTbRowY, searchW, TripScoutTbRowH);
                LayoutTripScoutSearchHost(searchW);
            }

            _tripScoutSearchCard?.BringToFront();
            _tripScoutActionsCard?.BringToFront();
        }

        private void LayoutTripScoutSearchHost(int hostW)
        {
            if (_tripScoutSearchHost == null || tssearchbox == null)
                return;

            int ctrlY = TripScoutToolbarCardControlY(TripScoutTbCtrlH);
            int x = TripScoutTbCardPadH;

            if (_tripScoutSearchLabel != null)
            {
                int labelY = ctrlY + (TripScoutTbCtrlH - _tripScoutSearchLabel.PreferredHeight) / 2;
                _tripScoutSearchLabel.SetBounds(x, labelY, MeasureTripScoutLabelWidth(_tripScoutSearchLabel), _tripScoutSearchLabel.PreferredHeight);
                x += MeasureTripScoutLabelWidth(_tripScoutSearchLabel) + TripScoutTbLabelGap;
            }

            int searchW = Math.Max(120, hostW - x - TripScoutTbCardPadH);
            tssearchbox.SetBounds(x, ctrlY, searchW, TripScoutTbCtrlH);
        }

        private void LayoutTripScoutActionsHost(int hostW)
        {
            if (_tripScoutActionsHost == null)
                return;

            int ctrlY = TripScoutToolbarCardControlY(TripScoutTbCtrlH);
            int sepY = TripScoutToolbarCardControlY(TripScoutTbSepH);
            int x = TripScoutTbCardPadH;

            if (_tripScoutDateLabel != null)
            {
                int labelY = ctrlY + (TripScoutTbCtrlH - _tripScoutDateLabel.PreferredHeight) / 2;
                _tripScoutDateLabel.SetBounds(x, labelY, MeasureTripScoutLabelWidth(_tripScoutDateLabel), _tripScoutDateLabel.PreferredHeight);
                x += MeasureTripScoutLabelWidth(_tripScoutDateLabel) + TripScoutTbLabelGap;
            }

            if (tsdatepicker != null && !tsdatepicker.IsDisposed)
            {
                int dateH = tsdatepicker.Height > 0 ? tsdatepicker.Height : TripScoutTbCtrlH;
                int dateY = TripScoutToolbarCardControlY(dateH);
                tsdatepicker.SetBounds(x, dateY, TripScoutTbDateW, dateH);
                x += TripScoutTbDateW + TripScoutTbItemGap;
            }

            if (tsloadbtn != null && !tsloadbtn.IsDisposed)
            {
                tsloadbtn.SetBounds(x, ctrlY, TripScoutTbLoadW, TripScoutTbCtrlH);
                x += TripScoutTbLoadW;
            }

            if (_tripScoutChangesBtn != null && !_tripScoutChangesBtn.IsDisposed)
            {
                x += TripScoutTbSectionGap;
                PlaceTripScoutToolbarSeparator(_tripScoutActivitySep, x, sepY);
                x += TripScoutTbSepW + TripScoutTbSectionGap;

                int changesW = MeasureTripScoutButtonWidth(_tripScoutChangesBtn);
                _tripScoutChangesBtn.SetBounds(x, ctrlY, changesW, TripScoutTbCtrlH);
                x += changesW + TripScoutTbItemGap;

                int willCallsW = MeasureTripScoutButtonWidth(_tripScoutWillCallsBtn);
                _tripScoutWillCallsBtn?.SetBounds(x, ctrlY, willCallsW, TripScoutTbCtrlH);
                x += willCallsW;
            }

            if (_tripScoutTestChangeBtn != null && !_tripScoutTestChangeBtn.IsDisposed)
            {
                x += TripScoutTbSectionGap;
                PlaceTripScoutToolbarSeparator(_tripScoutSimulateSep, x, sepY);
                x += TripScoutTbSepW + TripScoutTbSectionGap;
                int testW = MeasureTripScoutButtonWidth(_tripScoutTestChangeBtn);
                _tripScoutTestChangeBtn.SetBounds(x, ctrlY, testW, TripScoutTbCtrlH);
            }
        }

        private static void PlaceTripScoutToolbarSeparator(System.Windows.Forms.Panel sep, int x, int y)
        {
            if (sep == null || sep.IsDisposed)
                return;
            sep.SetBounds(x, y, TripScoutTbSepW, TripScoutTbSepH);
        }
    }
}
