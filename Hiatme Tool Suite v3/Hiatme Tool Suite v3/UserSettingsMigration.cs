using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Hiatme_Tool_Suite_v3.Properties;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// User-scoped settings live in a versioned folder under %LOCALAPPDATA%.
    /// After an in-place update the assembly version changes, so .NET starts with empty creds
    /// unless we copy forward from the previous version's user.config via Settings.Upgrade().
    /// </summary>
    internal static class UserSettingsMigration
    {
        public static void ApplyAfterVersionChange()
        {
            try
            {
                string current = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                var settings = Settings.Default;
                string last = settings.LastRunAssemblyVersion ?? "";

                if (string.Equals(last, current, StringComparison.Ordinal))
                    return;

                settings.Upgrade();

                if (CredentialsEmpty(settings))
                    TryRecoverFromSiblingUserConfigs(settings);

                settings.LastRunAssemblyVersion = current;
                settings.Save();
            }
            catch
            {
                // Never block startup if migration fails.
            }
        }

        private static bool CredentialsEmpty(Settings settings)
        {
            return string.IsNullOrWhiteSpace(settings.wrUserName)
                && string.IsNullOrWhiteSpace(settings.mcUserName)
                && string.IsNullOrWhiteSpace(settings.hiatmeUserName)
                && string.IsNullOrWhiteSpace(settings.gmailUserName);
        }

        /// <summary>
        /// If a prior release already created a newer empty user.config (e.g. 3.0.1.4 without Upgrade),
        /// pull creds from an older sibling version folder that still has data.
        /// </summary>
        private static void TryRecoverFromSiblingUserConfigs(Settings settings)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
            if (config == null || string.IsNullOrEmpty(config.FilePath))
                return;

            string currentVersionDir = Path.GetDirectoryName(config.FilePath);
            string versionsRoot = Path.GetDirectoryName(currentVersionDir);
            if (string.IsNullOrEmpty(versionsRoot) || !Directory.Exists(versionsRoot))
                return;

            foreach (string dir in Directory.GetDirectories(versionsRoot).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(dir, currentVersionDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                string userConfig = Path.Combine(dir, "user.config");
                if (!File.Exists(userConfig))
                    continue;

                if (!TryReadCredentials(userConfig, out string wrCode, out string wrUser, out string wrPass,
                        out string mcUser, out string mcPass, out string hmUser, out string hmPass,
                        out string gmUser, out string gmPass))
                    continue;

                if (string.IsNullOrWhiteSpace(wrUser) && string.IsNullOrWhiteSpace(mcUser)
                    && string.IsNullOrWhiteSpace(hmUser) && string.IsNullOrWhiteSpace(gmUser))
                    continue;

                settings.wrCompanyCode = wrCode ?? string.Empty;
                settings.wrUserName = wrUser ?? string.Empty;
                settings.wrUserPass = wrPass ?? string.Empty;
                settings.mcUserName = mcUser ?? string.Empty;
                settings.mcUserPass = mcPass ?? string.Empty;
                settings.hiatmeUserName = hmUser ?? string.Empty;
                settings.hiatmeUserPass = hmPass ?? string.Empty;
                settings.gmailUserName = gmUser ?? string.Empty;
                settings.gmailUserPass = gmPass ?? string.Empty;
                return;
            }
        }

        private static bool TryReadCredentials(string userConfigPath,
            out string wrCode, out string wrUser, out string wrPass,
            out string mcUser, out string mcPass,
            out string hmUser, out string hmPass,
            out string gmUser, out string gmPass)
        {
            wrCode = wrUser = wrPass = mcUser = mcPass = hmUser = hmPass = gmUser = gmPass = string.Empty;

            try
            {
                var doc = XDocument.Load(userConfigPath);
                XElement settingsNode = doc.Descendants("Hiatme_Tool_Suite_v3.Properties.Settings").FirstOrDefault();
                if (settingsNode == null)
                    return false;

                wrCode = ReadSetting(settingsNode, "wrCompanyCode");
                wrUser = ReadSetting(settingsNode, "wrUserName");
                wrPass = ReadSetting(settingsNode, "wrUserPass");
                mcUser = ReadSetting(settingsNode, "mcUserName");
                mcPass = ReadSetting(settingsNode, "mcUserPass");
                hmUser = ReadSetting(settingsNode, "hiatmeUserName");
                hmPass = ReadSetting(settingsNode, "hiatmeUserPass");
                gmUser = ReadSetting(settingsNode, "gmailUserName");
                gmPass = ReadSetting(settingsNode, "gmailUserPass");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadSetting(XElement settingsNode, string name)
        {
            return settingsNode.Elements("setting")
                .FirstOrDefault(e => string.Equals((string)e.Attribute("name"), name, StringComparison.Ordinal))
                ?.Element("value")?.Value ?? string.Empty;
        }
    }
}
