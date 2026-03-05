using MaterialSkin.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal class ReportCard
    {
        private List<SubjectGrade> gradeList { get; set; }
        private TabPage tabPage {  get; set; }
        private SoundPlayer player { get; set; }
        private Button autoassbutton { get; set; }

        MaterialCard mainScreenBG;
        Panel reportCardBG;
        Panel gradeChartBG;
        TableLayoutPanel gradeChart;
        MaterialButton continuebtn;

        public ReportCard(TabPage formtabpage, Button autoassbtn)
        {
            tabPage = formtabpage;
            player = new SoundPlayer();
            autoassbutton = autoassbtn;
        }
        public async void StartReport(List<SubjectGrade> grades)
        {
            gradeList = grades;
            InitializeControls();

            ShowScreen();
            ShowReportCard();

            player.Stream = Properties.Resources.drumroll;
            player.Play();

            await Task.Delay(3300);

            await ShowGrades();
        }
        private void InitializeControls()
        {
            LoadMainScreenControls();
            LoadReportCardControls();
            LoadGradeChartControls();
            
        }
        private void ShowScreen()
        {
            mainScreenBG.Visible = true;
        }
        private void ShowReportCard()
        {
            reportCardBG.Visible = true;
        }
        private void LoadMainScreenControls()
        {
            mainScreenBG = new MaterialCard();
            mainScreenBG.Visible = false;
            mainScreenBG.Parent = tabPage;
            mainScreenBG.BringToFront();
            mainScreenBG.Dock = DockStyle.Fill;
        }
        private void LoadReportCardControls()
        {
            //create report card body
            reportCardBG = new Panel();
            reportCardBG.Visible = false;
            reportCardBG.Parent = mainScreenBG;
            reportCardBG.BackColor = Color.FromArgb(70, 70, 70);
            reportCardBG.Dock = DockStyle.None;
            reportCardBG.Anchor = AnchorStyles.None;
            reportCardBG.Size = new Size(1100, 700);
            reportCardBG.BorderStyle = BorderStyle.FixedSingle;
            reportCardBG.Location = new Point((mainScreenBG.Width / 2) - (reportCardBG.Width / 2), (mainScreenBG.Height / 2) - (reportCardBG.Height / 2));

            //create title
            Label reportcardTitle = new Label();
            reportcardTitle.Visible = true;
            reportcardTitle.Parent = reportCardBG;
            reportcardTitle.ForeColor = Color.WhiteSmoke;
            reportcardTitle.Text = "REPORT CARD";
            reportcardTitle.Font = new Font("monospace", 40, FontStyle.Underline);
            reportcardTitle.Location = new Point((reportCardBG.Width / 2) - 210, 20);
            reportcardTitle.AutoSize = true;

            //create sub title
            Label reportcardSubTitle = new Label();
            reportcardSubTitle.Visible = true;
            reportcardSubTitle.Parent = reportCardBG;
            reportcardSubTitle.ForeColor = Color.WhiteSmoke;
            reportcardSubTitle.Text = "BADGERS SCHOOL OF FUCKERY";
            reportcardSubTitle.Font = new Font("monospace", 12, FontStyle.Bold);
            reportcardSubTitle.Location = new Point((reportCardBG.Width / 2) - 146, 84);
            reportcardSubTitle.AutoSize = true;

            //create student title
            Label InstuctorTitle = new Label();
            InstuctorTitle.Visible = true;
            InstuctorTitle.Parent = reportCardBG;
            InstuctorTitle.ForeColor = Color.WhiteSmoke;
            InstuctorTitle.Text = "Headmaster:";
            InstuctorTitle.Font = new Font("monospace", 34, FontStyle.Bold);
            InstuctorTitle.Location = new Point(40, 150);
            InstuctorTitle.AutoSize = true;

            //create student title
            Label InstructorName = new Label();
            InstructorName.Visible = true;
            InstructorName.Parent = reportCardBG;
            InstructorName.ForeColor = Color.WhiteSmoke;
            InstructorName.Text = " The Badger                           ";
            InstructorName.Font = new Font("Freestyle Script", 50, FontStyle.Underline);
            InstructorName.Location = new Point(330, 130);
            InstructorName.AutoSize = true;

            //create subject title
            Label SubjectTitle = new Label();
            SubjectTitle.Visible = true;
            SubjectTitle.Parent = reportCardBG;
            SubjectTitle.ForeColor = Color.WhiteSmoke;
            SubjectTitle.Text = "SUBJECT";
            SubjectTitle.Font = new Font("monospace", 34, FontStyle.Bold);
            SubjectTitle.Location = new Point(40, 224);
            SubjectTitle.AutoSize = true;

            //create subject title
            Label closingTitle = new Label();
            closingTitle.Visible = true;
            closingTitle.Parent = reportCardBG;
            closingTitle.ForeColor = Color.WhiteSmoke;
            closingTitle.Text = "* FAILED SUBJECTS MUST BE CORRECTED BEFORE ASSIGNING TRIPS!";
            closingTitle.Font = new Font("monospace", 10, FontStyle.Bold);
            closingTitle.Location = new Point((reportCardBG.Width / 2) - 260, 670);
            closingTitle.AutoSize = true;
        }
        List<Label> gradeLabelArray;
        private void LoadGradeChartControls()
        {
            gradeChartBG = new Panel();
            gradeChartBG.Visible = true;
            gradeChartBG.Parent = reportCardBG;
            gradeChartBG.BackColor = Color.FromArgb(70, 70, 70);
            gradeChartBG.Dock = DockStyle.None;
            gradeChartBG.Anchor = AnchorStyles.None;
            gradeChartBG.Size = new System.Drawing.Size(1000, 340);
            gradeChartBG.Location = new Point(49, 280);

            gradeChart = new TableLayoutPanel
            {
                Location = new Point(40, 300),
                Dock = DockStyle.Fill,
                Parent = gradeChartBG,
                Name = "gradeChart",
                ColumnCount = 3,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                RowCount = 5,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                GrowStyle = System.Windows.Forms.TableLayoutPanelGrowStyle.AddRows
            };

            for (int i = 0; i < gradeChart.ColumnCount; i++)
            {
                gradeChart.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
                gradeChart.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
                gradeChart.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            }

            for (int i = 0; i < gradeChart.RowCount; i++)
            {
                gradeChart.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }

            foreach (SubjectGrade subject in gradeList)
            {
                Label gradeSubject = new Label();
                gradeSubject.Visible = true;
                gradeSubject.Parent = reportCardBG;
                gradeSubject.ForeColor = Color.WhiteSmoke;
                gradeSubject.Dock = DockStyle.Fill;
                gradeSubject.Name = "SubjectLabel";
                gradeSubject.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
                gradeSubject.Text = "  " + subject.Subject;
                gradeSubject.Font = new Font("monospace", 26, FontStyle.Regular);

                gradeChart.SetColumnSpan(gradeSubject, 1);
                gradeChart.Controls.Add(gradeSubject);

                Label gradeNote = new Label();
                gradeNote.Visible = true;
                gradeNote.Parent = reportCardBG;
                gradeNote.ForeColor = Color.Red;
                gradeNote.Dock = DockStyle.Fill;
                gradeNote.Name = subject.Subject + "note";
                gradeNote.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
                gradeNote.Text = "  ";
                gradeNote.Font = new Font("Freestyle Script", 34, FontStyle.Bold);
                gradeChart.SetColumnSpan(gradeNote, 1);
                gradeChart.Controls.Add(gradeNote);


                Label gradeValue = new Label();
                gradeValue.Visible = true;
                gradeValue.Parent = reportCardBG;
                if (subject.Grade == "F")
                {
                    gradeValue.ForeColor = Color.Red;
                }
                else
                {
                    gradeValue.ForeColor = Color.LimeGreen;
                }
                gradeValue.Dock = DockStyle.Fill;
                gradeValue.Text = "";
                gradeValue.Name = subject.Subject;
                gradeValue.Font = new Font("Freestyle Script", 52, FontStyle.Bold);
                gradeValue.AutoSize = true;

                gradeChart.SetColumnSpan(gradeValue, 1);
                gradeChart.Controls.Add(gradeValue);

            }
        }
        private async Task ShowGrades()
        {
            bool failed = false;
            foreach (SubjectGrade sg in gradeList)
            {
                if (sg.Grade == "F")
                {
                    failed = true;
                }
                foreach (Label label in gradeChart.Controls)
                {
                    if (label.Name == sg.Subject)
                    {
                        await Task.Delay(1000);
                        label.Text = sg.Grade;
                        player.Stream = Properties.Resources.slam;
                        player.Play();
                    }
                } 
            }

            await Task.Delay(1000);
            ShowGNotes();

            PictureBox failpicture = new PictureBox();
            reportCardBG.Controls.Add(failpicture);
            failpicture.Name = "failedpicbox";
            failpicture.BringToFront();
            failpicture.SizeMode = PictureBoxSizeMode.StretchImage;
            failpicture.Anchor = AnchorStyles.Right;

            if (failed)
            {
                autoassbutton.Enabled = false;
                failpicture.Location = new Point(reportCardBG.Width - 290, 20);
                failpicture.Size = new System.Drawing.Size(250, 150);
                failpicture.Image = Properties.Resources.fail_stamp_7;
                player.Stream = Properties.Resources.haha;
                player.Play();
                await Task.Delay(1000);
                player.Stream = Properties.Resources.gummo;
                player.Play();
                //do failed animations
            }
            else
            {
                autoassbutton.Enabled = true;
                failpicture.Location = new Point(reportCardBG.Width - 200, 20);
                failpicture.Size = new System.Drawing.Size(150, 150);
                failpicture.Image = Properties.Resources.quality;
                player.Stream = Properties.Resources.cheer;
                player.Play();
                //do success animations
            }

            LoadContinueButton();
        }
        private void ShowGNotes()
        {
            foreach (SubjectGrade sg in gradeList)
            {
                foreach (Label label in gradeChart.Controls)
                {
                    if (label.Name == sg.Subject + "note")
                    {
                        label.Text = " " + sg.Notes;
                    }
                }
            }
        }
        private void LoadContinueButton()
        {
            continuebtn = new MaterialButton();
            continuebtn.Visible = true;
            continuebtn.Parent = reportCardBG;
            continuebtn.Dock = DockStyle.None;
            continuebtn.Anchor = AnchorStyles.None;
            continuebtn.Text = "CONTINUE";
            continuebtn.Location = new Point(950, 630);
            continuebtn.Click += new EventHandler(CloseMainScreen);
        }
        private void CloseMainScreen(object sender, EventArgs e)
        {
            mainScreenBG.Visible = false;
            mainScreenBG.Dispose();
        }

    }

    internal class SubjectGrade
    {
        public string Subject { get; set; }
        public string Grade { get; set; }
        public string Notes { get; set; }

    }
}
