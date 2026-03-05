using System;
using System.Collections.Generic;
using System.Web;
using System.Net.Http;
using System.Net;
using Hiatme_Tools;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Windows.Forms;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Security.Policy;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    public class WRLoginHandler : INotifyPropertyChanged
    {
        public static WRCompanyResponse WRCompanyResponse { get; set; }
        public static WebProxy Proxy { get; set; }
        public static CookieContainer CookieJar { get; set; }
        public static HttpClientHandler Handler { get; set; }
        public HttpClient Client { get; set; }
        public static string UserAgent { get; set; }
        public static string AWSALB { get; set; }
        public static string ContentType { get; set; }
        public string _CsrfToken { get; set; }
        public string SESSION { get; set; }
        public bool IntentionalLogout { get; set; }
        public WRLoginHandler()
        {
            WRCompanyResponse = new WRCompanyResponse();
            Proxy = new WebProxy();
            CookieJar = new CookieContainer();
            Handler = new HttpClientHandler();
            Handler.CookieContainer = CookieJar;
            Handler.AutomaticDecompression = DecompressionMethods.GZip;
            Client = new HttpClient(Handler);
            Client.Timeout = TimeSpan.FromSeconds(60);
            UserAgent = "";
            AWSALB = "";
            ContentType = "";
            Connected = false;
            _CsrfToken = "";
            SESSION = "";
            IntentionalLogout = false;
        }
        public async Task<bool> GetCompanyInfo(string code, string user, string pass)
        {
            string companycode = code;
            string username = user;
            string password = pass;

            try
            {
                Client.DefaultRequestHeaders.Clear();
                HttpResponseMessage resmsg = Client.GetAsync("https://appreg.app.wellryde.com/appregister/companyinfo/" + companycode).Result;
                var response = await resmsg.Content.ReadAsStringAsync();

                //deserialize the object into company info object
                WRCompanyResponse = JsonConvert.DeserializeObject<WRCompanyResponse>(response);

                if (WRCompanyResponse.companyinfo == null)
                {
                    //Console.WriteLine("There was a problem returning info of company code.");
                    MessageBox.Show("Invalid company code!");
                    //Connected = false;
                    return false;
                }
                else
                {
                    Console.WriteLine(WRCompanyResponse.companyinfo.CompanyName);
                    Console.WriteLine(WRCompanyResponse.companyinfo.ContactName);
                    Console.WriteLine(WRCompanyResponse.companyinfo.Email);
                    return await Login(companycode, username, password);
                }

            }
            catch (JsonReaderException e)
            {
                MessageBox.Show("Incorrect login information!");
                //Console.WriteLine($"{e.Message}");
            }
            //Proceed to login after info grab completes.
            return false;
        }
        public async Task<bool> Login(string code, string user, string pass)
        {
            IntentionalLogout = false;
            string companycode = code;
            string username = user;
            string password = pass;

            try
            {
                var _UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.99 Safari/537.36";
                string _ContentType = "application/x-www-form-urlencoded";
                Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(_ContentType));
                Client.DefaultRequestHeaders.Add("User-Agent", _UserAgent);

                var formContent = new FormUrlEncodedContent(new[]{
             new KeyValuePair<string, string>("deviceFingerPrint", ""),
             new KeyValuePair<string, string>("_logincsrf", ""),
             new KeyValuePair<string, string>("geoLocationVal", "false"),
             new KeyValuePair<string, string>("userCompany", companycode),
             new KeyValuePair<string, string>("j_username", username),
             new KeyValuePair<string, string>("j_password", password),
             new KeyValuePair<string, string>("userLat", ""),
             new KeyValuePair<string, string>("userLong", ""),
             new KeyValuePair<string, string>("_csrf", ""),
             });

                var res = await Client.PostAsync("https://portal.app.wellryde.com/portal/j_spring_security_check", formContent);
                var response = await res.Content.ReadAsStringAsync();
                try
                {
                    res.EnsureSuccessStatusCode();
                    string responseUri = res.RequestMessage.RequestUri.ToString();

                    Uri uri = new Uri("https://portal.app.wellryde.com/portal/j_spring_security_check");
                    IEnumerable<Cookie> responseCookies = CookieJar.GetCookies(uri).Cast<Cookie>();
                    foreach (Cookie cookie in responseCookies)
                    {
                        if (cookie.Name == "SESSION")
                        {
                            SESSION = cookie.Value;
                            //Console.WriteLine(cookie.Name + ": " + cookie.Value);
                        }
                    }


                    //Console.WriteLine("Location: " + responseUri);
                    if (responseUri.Contains("error=t"))
                    {
                        MessageBox.Show("Incorrect Login Information!");
                        IntentionalLogout = true;
                        Connected = false;
                        return false;
                    }
                }
                catch
                {
                    //Console.WriteLine("There was a problem retrieving location.");
                    Connected = false;
                    return false;
                }
                _CsrfToken = GrabCSRFToken(response);
            }
            catch
            {
                //Invalid email and/or password
                Console.WriteLine("There was a problem logging in.");
                Connected = false;
                return false;
            }

            UpdateHandlerHeaders();
            Connected = true;
            IntentionalLogout = false;
            return true;
        }
        public async Task<bool> Logout()
        {
            
            try
            {
                HttpResponseMessage resmsg = Client.GetAsync("https://portal.app.wellryde.com/portal/j_spring_security_logout").Result;
                var response = await resmsg.Content.ReadAsStringAsync();
            }
            catch (NullReferenceException e)
            {
                Connected = false;
                return false;
            }
            IntentionalLogout = true;
            Connected = false;
            return true;
        }

        public async Task ResetConnection()
        {
            this.IntentionalLogout = false;
            this.Connected = false;
            await Task.Run(() => {
                while (!this.Connected)
                {
                    // operation
                   
                    Console.WriteLine("Wellryde: Waiting for connection..");
                    System.Threading.Thread.Sleep(1000);
                }
                //Console.WriteLine("connected");
            });

        }
        //Update tokens and headers
        public string GrabCSRFToken(string resp)
        {
            try
            {
                string response = resp;

                string decodedfilterValues = HttpUtility.UrlDecode("<meta name=\"_csrf\" content=\"");
                string decodedfilterValues2 = HttpUtility.UrlDecode("\" /><link rel=");

                int startPoint = response.IndexOf(decodedfilterValues) + decodedfilterValues.Length;
                int endPOint = response.LastIndexOf(decodedfilterValues2);

                var _csrf = response.Substring(startPoint, endPOint - startPoint);
                //Console.WriteLine("crsf token: " + _csrf);
                return _csrf;
            }
            catch (Exception ex)
            {
                //Console.WriteLine(ex.ToString());
                return null;
            }
        }
        private void UpdateHandlerHeaders()//update headers to retrieve batches of calls
        {
            try {
                Client.DefaultRequestHeaders.Clear(); //JUST ADDED!!!
                string decodedsec = HttpUtility.UrlDecode("%22Google%20Chrome%22%3Bv=%22107%22%2C%20%22Chromium%22%3Bv=%22107%22%2C%20%22Not=A?Brand%22%3B%20v=%2224%22");
            Client.DefaultRequestHeaders.Add("sec-ch-ua", decodedsec);
            Client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            Client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            Client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "Windows");
            Client.DefaultRequestHeaders.Add("Origin", "https://portal.app.wellryde.com");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            Client.DefaultRequestHeaders.Add("Referer", "https://portal.app.wellryde.com/portal/nu");
            Client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            Client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            Client.DefaultRequestHeaders.Remove("Accept");
            Client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            }
            catch (System.FormatException e)
            {
                //Console.WriteLine("wrLoginHandler: UpdateHandlerHeaders():" + e.Message);
            }
        }

        //Event handlers
        public event PropertyChangedEventHandler PropertyChanged;
        bool connected;
        public bool Connected
        {
            get { return connected; }
            set
            {
                connected = value;
                OnPropertyChanged(nameof(Connected));
            }
        }    
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }

    }
}
