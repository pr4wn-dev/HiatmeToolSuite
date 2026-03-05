using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Web.UI.WebControls;

namespace Hiatme_Tool_Suite_v3
{
    public class MCLoginHandler : INotifyPropertyChanged
    {
        public WebProxy Proxy { get; set; }
        public CookieContainer CookieJar { get; set; }
        public HttpClientHandler Handler { get; set; }
        public HttpClient Client { get; set; }
        public string UserAgent { get; set; }
        public string AWSALB { get; set; }
        public string ContentType { get; set; }
        public string ViewStateToken { get; set; }
        public string ViewStateGeneratorToken { get; set; }
        public string EventValidationToken { get; set; }
        public string EventArguement { get; set; }
        public bool IntentionalLogout { get; set; }
        public MCLoginHandler()
        {
            Proxy = new WebProxy();
            CookieJar = new CookieContainer();
            Handler = new HttpClientHandler();
            Handler.CookieContainer = CookieJar;
            Client = new HttpClient(Handler);
            UserAgent = "";
            AWSALB = "";
            ContentType = "";
            Connected = false;
            ViewStateToken = "";
            ViewStateGeneratorToken = "";
            EventValidationToken = "";
            IntentionalLogout = false;
        }



        //Functions
        public async Task<bool> Login(string user, string pass)
        {
            IntentionalLogout = false;
            //grab validation tokens
            UpdateTripActualsHeaders("https://transportationco.logisticare.com/ProcessATMBatches.aspx");
            await FindTokens();
            string username = user;
            string password = pass;

            try
            {
                var formContent = new FormUrlEncodedContent(new[]
                {
                            new KeyValuePair<string, string>("__LASTFOCUS", ""),
                            new KeyValuePair<string, string>("__EVENTTARGET", ""),
                            new KeyValuePair<string, string>("__EVENTARGUMENT", ""),
                            new KeyValuePair<string, string>("__VIEWSTATE", ViewStateToken),
                            new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", ViewStateGeneratorToken),
                            new KeyValuePair<string, string>("__EVENTVALIDATION", EventValidationToken),
                            new KeyValuePair<string, string>("ctl00$cphMainContent$txtUserName", username),
                            new KeyValuePair<string, string>("ctl00$cphMainContent$txtPassword", password),
                            new KeyValuePair<string, string>("ctl00$cphMainContent$btnLogin", "Login"),
                            });

                var res = await Client.PostAsync("https://transportationco.logisticare.com/login.aspx", formContent);
                var response = await res.Content.ReadAsStringAsync();
                try
                {
                    res.EnsureSuccessStatusCode();
                    string responseUri = res.RequestMessage.RequestUri.ToString();
                   // Console.WriteLine("Location: " + responseUri);
                    if (responseUri.Contains("login.aspx"))
                    {
                        IntentionalLogout = true;
                        Connected = false;
                        MessageBox.Show("Invalid login credentials.");
                        return false;
                    }
                }
                catch
                {
                    //Console.WriteLine("There was a problem retrieving location.");
                    Connected = false;
                    return false;
                }
            }
            catch
            {
                Connected = false;
                return false;
            }
            //MessageBox.Show("Modivcare login success!");
            Connected = true;
            return true;
        }
        public async Task<bool> Logout()
        {
            try
            {
                IntentionalLogout = true;
                HttpResponseMessage resmsg = Client.GetAsync("https://transportationco.logisticare.com/Login.aspx?Logout=true").Result;
                var response = await resmsg.Content.ReadAsStringAsync();
            }
            catch (NullReferenceException e)
            {
                Connected = false;
                return false;
            }
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

                    Console.WriteLine("Modivcare: Waiting for connection..");
                    System.Threading.Thread.Sleep(1000);
                }
                //Console.WriteLine("connected");
            });

        }



        //update headers and tokens
        public void UpdateTripActualsHeaders(string referer)//update headers to retrieve batches of calls
        {
            var _UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.99 Safari/537.36";
            string decodedsec = "\"Not ? A_Brand\";v=\"8\", \"Chromium\";v=\"108\", \"Google Chrome\";v=\"108\"";
            Client.DefaultRequestHeaders.Clear();
            Client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            Client.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
            Client.DefaultRequestHeaders.Add("sec-ch-ua", decodedsec);
            Client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            Client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            Client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            Client.DefaultRequestHeaders.Add("Origin", "https://transportationco.logisticare.com");
            Client.DefaultRequestHeaders.Add("User-Agent", _UserAgent);
            Client.DefaultRequestHeaders.Remove("Accept");
            Client.DefaultRequestHeaders.Add("Accept", "text/html, application/xhtml+xml, application/xml; q=0.9, image/avif, image/webp, image/apng, */*; q=0.8, application/signed-exchange; v=b3; q=0.9");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            Client.DefaultRequestHeaders.Remove("Referer");
            Client.DefaultRequestHeaders.Add("Referer", referer);
            Client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");
            Client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        }
        public async Task FindTokens()
        {
            try
            {
                HttpResponseMessage resmsg = Client.GetAsync("https://transportationco.logisticare.com/").Result;
                var response = await resmsg.Content.ReadAsStringAsync();
                GrabTokens(response);
            }
            // Filter by InnerException.
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // Handle timeout.
                //Console.WriteLine("Timed out: " + ex.Message);
            }
            catch (TaskCanceledException ex)
            {
                // Handle cancellation.
                //Console.WriteLine("Canceled: " + ex.Message);
            }
        }
        public void GrabTokens(string resp)
        {
            string response = resp;
            try
            {
                //get first part of string for view state token
                string decoded_view_state_begin = HttpUtility.UrlDecode("id=\"__VIEWSTATE\" value=\"");
                string decoded_view_state_end = HttpUtility.UrlDecode("VIEWSTATEGENERATOR");
                int pvsFrom = response.IndexOf(decoded_view_state_begin) + decoded_view_state_begin.Length;
                int pvsTo = response.IndexOf(decoded_view_state_end);
                String _pre_view_state_token = response.Substring(pvsFrom, pvsTo - pvsFrom);

                //get second part of string for view state token

                string view_state_begin = HttpUtility.UrlDecode("");
                string view_state_end = HttpUtility.UrlDecode("\" />");
                int vsFrom = _pre_view_state_token.IndexOf(view_state_begin) + view_state_begin.Length;
                int vsTo = _pre_view_state_token.IndexOf(view_state_end);
                String _view_state_token = _pre_view_state_token.Substring(vsFrom, vsTo - vsFrom);

                ViewStateToken = _view_state_token;
                 //Console.WriteLine("ViewState token: " + ViewStateToken);
            }
            catch
            {
                //Console.WriteLine("ViewState Token Error!");
            }

            try
            {
                //get first part of string for view state generator token
                string decoded_view_state_generator_begin = HttpUtility.UrlDecode("id=\"__VIEWSTATEGENERATOR\" value=\"");
                string decoded_view_state_generator_end = HttpUtility.UrlDecode("__EVENTVALIDATION");
                int pvsgFrom = response.IndexOf(decoded_view_state_generator_begin) + decoded_view_state_generator_begin.Length;
                int pvsgTo = response.IndexOf(decoded_view_state_generator_end);
                String _pre_view_state_generator_token = response.Substring(pvsgFrom, pvsgTo - pvsgFrom);

                //get second part of string for view state generator token

                string view_state_generator_begin = HttpUtility.UrlDecode("");
                string view_state_generator_end = HttpUtility.UrlDecode("\" />");
                int vsgFrom = _pre_view_state_generator_token.IndexOf(view_state_generator_begin) + view_state_generator_begin.Length;
                int vsgTo = _pre_view_state_generator_token.IndexOf(view_state_generator_end);
                String _view_state_generator_token = _pre_view_state_generator_token.Substring(vsgFrom, vsgTo - vsgFrom);

                ViewStateGeneratorToken = _view_state_generator_token;
                //  Console.WriteLine("ViewStateGenerator token: " + ViewStateGeneratorToken);
            }
            catch
            {
                //Console.WriteLine("ViewStateGenerator Token Error!");
            }

            try
            {
                //get first part of string for event validation token
                string decoded_event_validation_begin = HttpUtility.UrlDecode("id=\"__EVENTVALIDATION\" value=\"");
                string decoded_event_validation_end = HttpUtility.UrlDecode("wrapper");
                int pevFrom = response.IndexOf(decoded_event_validation_begin) + decoded_event_validation_begin.Length;
                int pevTo = response.IndexOf(decoded_event_validation_end);
                String _pre_event_validation_token = response.Substring(pevFrom, pevTo - pevFrom);

                //get second part of string for view state token
                string event_validation_begin = HttpUtility.UrlDecode("");
                string event_validation_end = HttpUtility.UrlDecode("\" />");
                int evFrom = _pre_event_validation_token.IndexOf(event_validation_begin) + event_validation_begin.Length;
                int evTo = _pre_event_validation_token.IndexOf(event_validation_end);
                String _event_validation_token = _pre_event_validation_token.Substring(evFrom, evTo - evFrom);

                EventValidationToken = _event_validation_token;
                //  Console.WriteLine("EventValidation token: " + EventValidationToken);
            }
            catch
            {
                //Console.WriteLine("EventValidation Token Error!");
            }

        }
        public void GrabTokensSeptViewState(string resp)
        {
            string response = resp;
            try
            {
                //get first part of string for view state token
                string decoded_view_state_begin = HttpUtility.UrlDecode("id=\"__VIEWSTATE\" value=\"");
                string decoded_view_state_end = HttpUtility.UrlDecode("VIEWSTATEGENERATOR");
                int pvsFrom = response.IndexOf(decoded_view_state_begin) + decoded_view_state_begin.Length;
                int pvsTo = response.IndexOf(decoded_view_state_end);
                String _pre_view_state_token = response.Substring(pvsFrom, pvsTo - pvsFrom);

                //get second part of string for view state token

                string view_state_begin = HttpUtility.UrlDecode("");
                string view_state_end = HttpUtility.UrlDecode("\" />");
                int vsFrom = _pre_view_state_token.IndexOf(view_state_begin) + view_state_begin.Length;
                int vsTo = _pre_view_state_token.IndexOf(view_state_end);
                String _view_state_token = _pre_view_state_token.Substring(vsFrom, vsTo - vsFrom);

                //ViewStateToken = _view_state_token;
                //Console.WriteLine("ViewState token: " + ViewStateToken);
            }
            catch
            {
                //Console.WriteLine("ViewState Token Error!");
            }

            try
            {
                //get first part of string for view state generator token
                string decoded_view_state_generator_begin = HttpUtility.UrlDecode("id=\"__VIEWSTATEGENERATOR\" value=\"");
                string decoded_view_state_generator_end = HttpUtility.UrlDecode("__EVENTVALIDATION");
                int pvsgFrom = response.IndexOf(decoded_view_state_generator_begin) + decoded_view_state_generator_begin.Length;
                int pvsgTo = response.IndexOf(decoded_view_state_generator_end);
                String _pre_view_state_generator_token = response.Substring(pvsgFrom, pvsgTo - pvsgFrom);

                //get second part of string for view state generator token

                string view_state_generator_begin = HttpUtility.UrlDecode("");
                string view_state_generator_end = HttpUtility.UrlDecode("\" />");
                int vsgFrom = _pre_view_state_generator_token.IndexOf(view_state_generator_begin) + view_state_generator_begin.Length;
                int vsgTo = _pre_view_state_generator_token.IndexOf(view_state_generator_end);
                String _view_state_generator_token = _pre_view_state_generator_token.Substring(vsgFrom, vsgTo - vsgFrom);

                ViewStateGeneratorToken = _view_state_generator_token;
                //  Console.WriteLine("ViewStateGenerator token: " + ViewStateGeneratorToken);
            }
            catch
            {
                //Console.WriteLine("ViewStateGenerator Token Error!");
            }

            try
            {
                //get first part of string for event validation token
                string decoded_event_validation_begin = HttpUtility.UrlDecode("id=\"__EVENTVALIDATION\" value=\"");
                string decoded_event_validation_end = HttpUtility.UrlDecode("wrapper");
                int pevFrom = response.IndexOf(decoded_event_validation_begin) + decoded_event_validation_begin.Length;
                int pevTo = response.IndexOf(decoded_event_validation_end);
                String _pre_event_validation_token = response.Substring(pevFrom, pevTo - pevFrom);

                //get second part of string for view state token
                string event_validation_begin = HttpUtility.UrlDecode("");
                string event_validation_end = HttpUtility.UrlDecode("\" />");
                int evFrom = _pre_event_validation_token.IndexOf(event_validation_begin) + event_validation_begin.Length;
                int evTo = _pre_event_validation_token.IndexOf(event_validation_end);
                String _event_validation_token = _pre_event_validation_token.Substring(evFrom, evTo - evFrom);

                EventValidationToken = _event_validation_token;
                //  Console.WriteLine("EventValidation token: " + EventValidationToken);
            }
            catch
            {
                //Console.WriteLine("EventValidation Token Error!");
            }
        }
        public void GrabViewStateGeneratorToken(string resp)
        {
            string response = resp;

            try
            {
                //get first part of string for view state generator token
                string decoded_view_state_generator_begin = HttpUtility.UrlDecode("id=\"__VIEWSTATEGENERATOR\" value=\"");
                string decoded_view_state_generator_end = HttpUtility.UrlDecode("__SCROLLPOSITIONX");
                int pvsgFrom = response.IndexOf(decoded_view_state_generator_begin) + decoded_view_state_generator_begin.Length;
                int pvsgTo = response.IndexOf(decoded_view_state_generator_end);
                String _pre_view_state_generator_token = response.Substring(pvsgFrom, pvsgTo - pvsgFrom);

                //get second part of string for view state generator token
                string view_state_generator_begin = HttpUtility.UrlDecode("");
                string view_state_generator_end = HttpUtility.UrlDecode("\" />");
                int vsgFrom = _pre_view_state_generator_token.IndexOf(view_state_generator_begin) + view_state_generator_begin.Length;
                int vsgTo = _pre_view_state_generator_token.IndexOf(view_state_generator_end);
                String _view_state_generator_token = _pre_view_state_generator_token.Substring(vsgFrom, vsgTo - vsgFrom);

                ViewStateGeneratorToken = _view_state_generator_token;
                //Console.WriteLine("ViewStateGenerator token: " + ViewStateGeneratorToken);
            }
            catch
            {
                //Console.WriteLine("ViewStateGenerator Token Error!");
            }
        }
        public void GrabViewStateToken(string resp)
        {
            string response = resp;
            try
            {
                //get first part of string for view state token
                string decoded_view_state_begin = HttpUtility.UrlDecode("id=\"__VIEWSTATE\" value=\"");
                string decoded_view_state_end = HttpUtility.UrlDecode("VIEWSTATEGENERATOR");
                int pvsFrom = response.IndexOf(decoded_view_state_begin) + decoded_view_state_begin.Length;
                int pvsTo = response.IndexOf(decoded_view_state_end);
                String _pre_view_state_token = response.Substring(pvsFrom, pvsTo - pvsFrom);

                //get second part of string for view state token

                string view_state_begin = HttpUtility.UrlDecode("");
                string view_state_end = HttpUtility.UrlDecode("\" />");
                int vsFrom = _pre_view_state_token.IndexOf(view_state_begin) + view_state_begin.Length;
                int vsTo = _pre_view_state_token.IndexOf(view_state_end);
                String _view_state_token = _pre_view_state_token.Substring(vsFrom, vsTo - vsFrom);

                ViewStateToken = _view_state_token;
                // Console.WriteLine("ViewState token: " + ViewStateToken);
            }
            catch
            {
                //Console.WriteLine("ViewState Token Error!");
            }
        }

        //Event handlers
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
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
