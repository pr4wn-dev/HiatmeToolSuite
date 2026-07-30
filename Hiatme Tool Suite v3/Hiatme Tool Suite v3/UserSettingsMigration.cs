using System;
using System.Collections.Generic;
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

                bool recovered = false;
                if (CredentialsEmpty(settings))
                    recovered = TryRecoverFromSiblingUserConfigs(settings);

                settings.LastRunAssemblyVersion = current;
                settings.Save();

                // Tell the AI server what happened. Credentials silently vanishing on
                // update is the single worst failure for a desk, and it only shows up
                // on machines we cannot inspect — so make it visible.
                TryReportOutcome(last, current, recovered, CredentialsEmpty(settings));
            }
            catch
            {
                // Never block startup if migration fails.
            }
        }

        private static void TryReportOutcome(
            string previous, string current, bool recovered, bool stillEmpty)
        {
            try
            {
                HiatmeEventReporter.Report(
                    stillEmpty ? "settings_migration_failed" : "settings_migration",
                    "UserSettingsMigration",
                    stillEmpty
                        ? "Credentials empty after upgrade from " + (previous ?? "")
                        : "Settings carried forward to " + current,
                    null,
                    new Newtonsoft.Json.Linq.JObject
                    {
                        ["previous_version"] = previous ?? "",
                        ["current_version"] = current ?? "",
                        ["recovered_from_sibling"] = recovered,
                        ["credentials_empty"] = stillEmpty,
                    });
            }
            catch
            {
                /* telemetry must never disrupt startup */
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
        /// pull creds from an older version folder that still has data.
        ///
        /// The per-version folder sits under an evidence-hash folder derived from the exe path, so a desk
        /// that ends up running from a new location gets a brand-new hash folder with no history and
        /// Settings.Upgrade() finds nothing. Scan every hash folder for this app, not just the current one,
        /// or those desks silently lose their saved logins on update.
        /// </summary>
        private static bool TryRecoverFromSiblingUserConfigs(Settings settings)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
            if (config == null || string.IsNullOrEmpty(config.FilePath))
                return false;

            string currentVersionDir = Path.GetDirectoryName(config.FilePath);
            string versionsRoot = Path.GetDirectoryName(currentVersionDir);
            if (string.IsNullOrEmpty(versionsRoot) || !Directory.Exists(versionsRoot))
                return false;

            foreach (string dir in EnumerateCandidateVersionDirs(versionsRoot, currentVersionDir))
            {
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
                return true;
            }

            return false;
        }

        /// <summary>
        /// Every version folder that could hold saved creds, best candidate first: newest real version
        /// wins, then most recently written. Ordering by folder name is wrong — as strings "4.0.0.9"
        /// sorts above "4.0.0.21", which would restore months-old credentials over yesterday's.
        /// </summary>
        private static IEnumerable<string> EnumerateCandidateVersionDirs(
            string versionsRoot, string currentVersionDir)
        {
            var dirs = new List<string>();

            // Sibling versions under the current exe-path hash.
            dirs.AddRange(SafeGetDirectories(versionsRoot));

            // Same app, different exe path (updater moved / reinstalled elsewhere).
            string appRoot = Path.GetDirectoryName(versionsRoot);
            if (!string.IsNullOrEmpty(appRoot) && Directory.Exists(appRoot))
            {
                foreach (string hashDir in SafeGetDirectories(appRoot))
                {
                    if (string.Equals(hashDir, versionsRoot, StringComparison.OrdinalIgnoreCase))
                        continue;
                    dirs.AddRange(SafeGetDirectories(hashDir));
                }
            }

            return dirs
                .Where(d => !string.Equals(d, currentVersionDir, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(d => ParseVersionOrZero(Path.GetFileName(d)))
                .ThenByDescending(SafeLastWriteUtc);
        }

        private static string[] SafeGetDirectories(string path)
        {
            try
            {
                return Directory.Exists(path) ? Directory.GetDirectories(path) : new string[0];
            }
            catch
            {
                return new string[0];
            }
        }

        private static Version ParseVersionOrZero(string name)
        {
            Version v;
            return Version.TryParse(name, out v) ? v : new Version(0, 0, 0, 0);
        }

        private static DateTime SafeLastWriteUtc(string dir)
        {
            try
            {
                string cfg = Path.Combine(dir, "user.config");
                return File.Exists(cfg) ? File.GetLastWriteTimeUtc(cfg) : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
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
