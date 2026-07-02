using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Sends Schedule Builder driver workbooks via Gmail SMTP (smtp.gmail.com:587).</summary>
    internal static class ScheduleBuilderGmailMailer
    {
        public const string SmtpHost = "smtp.gmail.com";
        public const int SmtpPort = 587;

        /// <summary>Trim address; strip spaces from Google App Passwords pasted as "abcd efgh …".</summary>
        internal static void NormalizeCredentials(ref string fromAddress, ref string password)
        {
            fromAddress = (fromAddress ?? "").Trim();
            password = (password ?? "").Trim();
            if (password.IndexOf(' ') >= 0)
                password = password.Replace(" ", "");
        }

        public static async Task TestConnectionAsync(
            string fromAddress,
            string password,
            CancellationToken cancellationToken = default)
        {
            NormalizeCredentials(ref fromAddress, ref password);
            if (fromAddress.Length == 0 || password.Length == 0)
                throw new InvalidOperationException("Gmail address and password (or App Password) are required.");

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var msg = new MailMessage(fromAddress, fromAddress))
                {
                    msg.Subject = "Hiatme Tool Suite — Gmail test";
                    msg.Body =
                        "If you received this message, Gmail credentials are working for Schedule Builder email.";
                    msg.IsBodyHtml = false;
                    SendMessage(fromAddress, password, msg);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        public static async Task SendDriverScheduleAsync(
            string fromAddress,
            string password,
            string toAddress,
            string driverDisplayName,
            DateTime serviceDate,
            string attachmentPath,
            CancellationToken cancellationToken = default)
        {
            NormalizeCredentials(ref fromAddress, ref password);
            toAddress = (toAddress ?? "").Trim();

            if (!File.Exists(attachmentPath))
                throw new FileNotFoundException("Schedule attachment was not found.", attachmentPath);

            string dateLabel = serviceDate.ToString("dddd, MMMM d, yyyy");
            string driver = string.IsNullOrWhiteSpace(driverDisplayName) ? "Driver" : driverDisplayName.Trim();
            string subject = "Schedule for " + serviceDate.ToString("MMMM d, yyyy") + " — " + driver;
            string body =
                "Hello " + driver + ",\r\n\r\n"
                + "Attached is the full schedule workbook for " + dateLabel + " (all driver tabs).\r\n\r\n"
                + "Not seeing schedule emails? Check your Spam folder, mark this message \"Not spam\", "
                + "and add " + fromAddress + " to your contacts so future schedules go to your inbox.\r\n\r\n"
                + "— Sent from Hiatme Tool Suite Schedule Builder";

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var msg = new MailMessage(fromAddress, toAddress))
                {
                    msg.Subject = subject;
                    msg.Body = body;
                    msg.IsBodyHtml = false;
                    msg.Attachments.Add(new Attachment(attachmentPath));
                    SendMessage(fromAddress, password, msg);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private static void SendMessage(string user, string pass, MailMessage msg)
        {
            try
            {
                using (var client = CreateClient(user, pass))
                    client.Send(msg);
            }
            catch (SmtpException ex)
            {
                throw new InvalidOperationException(DescribeSmtpFailure(ex), ex);
            }
        }

        private static string DescribeSmtpFailure(SmtpException ex)
        {
            string raw = (ex.Message ?? "").Trim();
            string lower = raw.ToLowerInvariant();
            if (lower.Contains("5.7.0") || lower.Contains("authentication required")
                || lower.Contains("not authenticated") || lower.Contains("535"))
            {
                return "Gmail rejected the login (5.7.0 Authentication Required).\r\n\r\n"
                    + "Google no longer accepts your normal Gmail password for SMTP.\r\n"
                    + "Use a Google App Password:\r\n"
                    + "  1. Google Account → Security → turn on 2-Step Verification\r\n"
                    + "  2. Security → App passwords → create one for Mail\r\n"
                    + "  3. Paste the 16-character App Password here (spaces are OK)\r\n\r\n"
                    + "Server: " + raw;
            }

            return string.IsNullOrEmpty(raw) ? "Gmail SMTP send failed." : raw;
        }

        private static SmtpClient CreateClient(string user, string pass)
        {
            EnsureTls();
            return new SmtpClient(SmtpHost, SmtpPort)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(user, pass),
                Timeout = 120000,
            };
        }

        private static void EnsureTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
                /* best effort */
            }
        }

        internal static string SanitizeFileNamePart(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Driver";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = raw.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }
            return new string(chars).Trim();
        }
    }
}
