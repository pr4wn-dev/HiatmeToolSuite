using Hiatme_Tools;
using System;
using System.Net.Sockets;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class livescreen : Form
    {
        Socket sock;
        public string ID = "";
        public livescreen(Socket sck, string id)
        {
            InitializeComponent();
            comboBox1.SelectedIndex = 4;
            sock = sck; ID = id;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex > -1)
            {
                try
                {
                    Form1.CommandSend("SCREENLIVEOPEN", $"[VERI]{comboBox1.SelectedItem}[VERI]{ID}[VERI][0x09]", sock);                   
                }
                catch (Exception) { }
                button1.Enabled = false;
                button2.Enabled = true;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (button1.Enabled == false)
            {
                try
                {
                    Form1.CommandSend("SCREENLIVECLOSE", "[VERI][0x09]", sock);
                }
                catch (Exception) { }
                button2.Enabled = false;
                button1.Enabled = true;
            }
        }

        private void livescreen_FormClosing(object sender, FormClosingEventArgs e)
        {
            button2.PerformClick();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (button1.Enabled == false)
            {
                try
                {
                    Form1.CommandSend("SCREENQUALITY", $"[VERI]{comboBox1.SelectedItem}[VERI][0x09]", sock);
                }
                catch (Exception) { }              
            }
        }
    }
}
