using System;
using System.Drawing;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class TimeCorrectionLoadModeForm : MaterialForm
    {
        private const int DialogWidth = 520;
        private const int TextWidth = 430;

        private static readonly Font BodyFont = new Font("Segoe UI", 9.75F);
        private static readonly Font TitleFont = new Font("Segoe UI Semibold", 11F);
        private static readonly Font HintFont = new Font("Segoe UI", 9F);

        private readonly RadioButton _rbStandard;
        private readonly RadioButton _rbModivcareRed;
        private readonly RadioButton _rbLenient;
        private readonly RadioButton _rbDataOnly;
        private readonly RadioButton[] _modeRadios;

        public TimeCorrectionLoadMode SelectedMode { get; private set; } = TimeCorrectionLoadMode.StandardScoreboard;

        public TimeCorrectionLoadModeForm()
        {
            Text = "Time Correction — load mode";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(DialogWidth, 500);
            MinimumSize = new Size(DialogWidth, 500);
            MaximumSize = new Size(DialogWidth, 500);
            BackColor = DarkContextMenuRenderer.Background;

            try
            {
                var mgr = MaterialSkinManager.Instance;
                mgr.AddFormToManage(this);
                mgr.Theme = MaterialSkinManager.Themes.DARK;
                mgr.ColorScheme = new ColorScheme(Primary.Grey900, Primary.Grey800, Primary.BlueGrey500,
                    Accent.Lime700, TextShade.WHITE);
            }
            catch
            {
            }

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = DarkContextMenuRenderer.Background,
            };

            var footerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 20, 12),
                BackColor = DarkContextMenuRenderer.Background,
            };

            var loadBtn = new DarkOnAccentMaterialButton
            {
                Text = "LOAD BATCH",
                AutoSize = false,
                Type = MaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(132, 36),
                DialogResult = DialogResult.OK,
            };
            loadBtn.Click += (s, e) => CommitSelection();

            var cancelBtn = new MaterialButton
            {
                Text = "CANCEL",
                AutoSize = false,
                Type = MaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = Color.Gainsboro,
                Size = new Size(96, 36),
                Margin = new Padding(0, 0, 8, 0),
                DialogResult = DialogResult.Cancel,
            };

            footerButtons.Controls.Add(loadBtn);
            footerButtons.Controls.Add(cancelBtn);
            footer.Controls.Add(footerButtons);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = DarkContextMenuRenderer.Background,
                Padding = new Padding(24, 76, 24, 12),
            };

            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                AutoSize = false,
                BackColor = DarkContextMenuRenderer.Background,
            };

            stack.Controls.Add(CreateHeaderLabel("How should this batch be evaluated?", TitleFont, Color.Gainsboro, 8));
            stack.Controls.Add(CreateHeaderLabel(
                "Driver and vehicle eligibility uses lists from one sample trip opened on Modivcare.",
                HintFont, Color.Silver, 14));

            _rbStandard = AddOptionRow(stack,
                "Standard — full scoreboard PU/DO timing (usual run).", true);
            _rbModivcareRed = AddOptionRow(stack,
                "Portal red only — fix only trips Modivcare shows in red (standard timing); blue rows are left alone.",
                false);
            _rbLenient = AddOptionRow(stack,
                "Lenient — light timing on blue rows; portal-red rows still get full scoreboard fixes.",
                false);
            _rbDataOnly = AddOptionRow(stack,
                "Data only — driver/vehicle on blue rows; portal-red rows still get scoreboard timing.",
                false);

            _modeRadios = new[] { _rbStandard, _rbModivcareRed, _rbLenient, _rbDataOnly };
            foreach (RadioButton rb in _modeRadios)
                rb.CheckedChanged += ModeRadio_CheckedChanged;

            body.Controls.Add(stack);

            AcceptButton = loadBtn;
            CancelButton = cancelBtn;

            Controls.Add(body);
            Controls.Add(footer);
        }

        private static Label CreateHeaderLabel(string text, Font font, Color color, int bottomMargin)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                MaximumSize = new Size(TextWidth, 0),
                Margin = new Padding(0, 0, 0, bottomMargin),
                BackColor = DarkContextMenuRenderer.Background,
            };
        }

        private RadioButton AddOptionRow(FlowLayoutPanel parent, string text, bool selected)
        {
            var row = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Width = TextWidth + 26,
                Margin = new Padding(0, 0, 0, 10),
                BackColor = DarkContextMenuRenderer.Background,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TextWidth));

            var rb = new RadioButton
            {
                AutoSize = true,
                Checked = selected,
                TabStop = true,
                Margin = new Padding(0, 3, 0, 0),
                BackColor = DarkContextMenuRenderer.Background,
            };

            var caption = new Label
            {
                Text = text,
                Font = BodyFont,
                ForeColor = Color.Gainsboro,
                AutoSize = true,
                MaximumSize = new Size(TextWidth, 0),
                Margin = new Padding(0),
                BackColor = DarkContextMenuRenderer.Background,
                Cursor = Cursors.Hand,
            };

            caption.Click += (s, e) => SelectModeRadio(rb);

            row.Controls.Add(rb, 0, 0);
            row.Controls.Add(caption, 1, 0);
            parent.Controls.Add(row);
            return rb;
        }

        private void ModeRadio_CheckedChanged(object sender, EventArgs e)
        {
            var rb = sender as RadioButton;
            if (rb == null || !rb.Checked)
                return;
            SelectModeRadio(rb, commit: true);
        }

        /// <summary>
        /// WinForms only auto-groups radios that share the same parent; ours sit in separate row panels.
        /// </summary>
        private void SelectModeRadio(RadioButton selected, bool commit = false)
        {
            foreach (RadioButton rb in _modeRadios)
            {
                if (rb == selected)
                    continue;
                if (rb.Checked)
                    rb.Checked = false;
            }
            if (!selected.Checked)
                selected.Checked = true;
            if (commit)
                CommitSelection();
        }

        private void CommitSelection()
        {
            if (_rbDataOnly.Checked)
                SelectedMode = TimeCorrectionLoadMode.DataOnly;
            else if (_rbLenient.Checked)
                SelectedMode = TimeCorrectionLoadMode.Lenient;
            else if (_rbModivcareRed.Checked)
                SelectedMode = TimeCorrectionLoadMode.ModivcareRedOnly;
            else
                SelectedMode = TimeCorrectionLoadMode.StandardScoreboard;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
                CommitSelection();
            base.OnFormClosing(e);
        }
    }
}
