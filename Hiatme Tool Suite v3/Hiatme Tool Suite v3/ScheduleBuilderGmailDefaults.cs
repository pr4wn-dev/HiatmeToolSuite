using System;
using System.IO;
using Hiatme_Tool_Suite_v3.Properties;
using Newtonsoft.Json.Linq;
namespace Hiatme_Tool_Suite_v3
{
    /// Shared office Gmail — baked into the app deploy. Other users never log in or see 2-step prompts.
    /// You set this once (Google App Password for the shared account); everyone else just sends mail.
    internal static class ScheduleBuilderGmailDefaults
    {
        public static string ConfigPath =>
            Path.Combine(AppContext.BaseDirectory ?? "", "hiatme_config", "gmail_default.json");

        public static bool IsConfigured()
        {
            return TryGet(out _, out _);
        }

        public static bool TryGet(out string address, out string appPassword)
        {
            address = string.Empty;
            appPassword = string.Empty;

            try
            {
                string path = ConfigPath;
                if (!File.Exists(path))
                    return false;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                var root = JObject.Parse(json);
                address = (root["address"] ?? root["email"] ?? root["username"])?.ToString() ?? string.Empty;
                appPassword = (root["appPassword"] ?? root["app_password"] ?? root["password"])?.ToString() ?? string.Empty;
                ScheduleBuilderGmailMailer.NormalizeCredentials(ref address, ref appPassword);

                return address.Length > 0 && appPassword.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Human-readable hint for the login UI (address only, never the password).</summary>
        public static string DescribeForUi()
        {
            if (!TryGet(out string address, out _))
                return "Not configured — edit hiatme_config\\gmail_default.json";

            return address;
        }

        /// <summary>
        /// When the deploy bundle includes hiatme_config/gmail_default.json, prefer office Gmail
        /// unless the user already saved personal Gmail credentials.
        /// </summary>
        public static void ApplyBundledOfficePreferenceIfAvailable()
        {
            if (!IsConfigured())
                return;

            var settings = Settings.Default;
            bool hasPersonal = !string.IsNullOrWhiteSpace(settings.gmailUserName)
                && !string.IsNullOrWhiteSpace(settings.gmailUserPass);
            if (hasPersonal && !settings.gmailUseOfficeDefault)
                return;

            if (!settings.gmailUseOfficeDefault)
            {
                settings.gmailUseOfficeDefault = true;
                settings.Save();
            }
        }
    }
}
