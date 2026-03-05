using MaterialSkin;
using MaterialSkin.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Update
{
    public partial class Form1 : MaterialForm
    {
        readonly MaterialSkinManager materialSkinManager;

        public Form1()
        {
            InitializeComponent();
            materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.EnforceBackcolorOnAllComponents = false;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.DARK;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.Grey900, Primary.Grey800, Primary.BlueGrey500, Accent.Lime700, TextShade.WHITE);

            CheckForUpdates();
        }

        private int GetBuildNumber()
        {
                return Properties.Settings.Default.Build;
        }


        private async void CheckForUpdates()
        {
            WebClient client = new WebClient();
            string filename = "Hamsters.txt";
            Uri uri = new Uri("https://hiatme.com/updates/" + filename);

            client.DownloadFileCompleted += (sender, e) => Console.WriteLine("Finished");
            client.DownloadFileAsync(uri, "Hamsters.txt");
        }




    }
    }
