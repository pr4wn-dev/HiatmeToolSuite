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

        public static async Task TestConnectionAsync(
            string fromAddress,
            string password,
            CancellationToken cancellationToken = default)
        {
            fromAddress = (fromAddress ?? "").Trim();
            password = password ?? "";

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var msg = new MailMessage(fromAddress, fromAddress))
                {
                    msg.Subject = "Hiatme Tool Suite — Gmail test";
                    msg.Body =
                        "If you received this message, Gmail credentials are working for Schedule Builder email.";
                    msg.IsBodyHtml = false;
                    using (var client = CreateClient(fromAddress, password))
                        client.Send(msg);
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
            fromAddress = (fromAddress ?? "").Trim();
            toAddress = (toAddress ?? "").Trim();
            password = password ?? "";

            if (!File.Exists(attachmentPath))
                throw new FileNotFoundException("Schedule attachment was not found.", attachmentPath);

            string dateLabel = serviceDate.ToString("dddd, MMMM d, yyyy");
            string driver = string.IsNullOrWhiteSpace(driverDisplayName) ? "Driver" : driverDisplayName.Trim();
            string subject = "Schedule for " + serviceDate.ToString("MMMM d, yyyy") + " — " + driver;
            string body =
                "Hello " + driver + ",\r\n\r\n"
                + "Attached is the full schedule workbook for " + dateLabel + " (all driver tabs).\r\n\r\n"
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
                    using (var client = CreateClient(fromAddress, password))
                        client.Send(msg);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private static SmtpClient CreateClient(string user, string pass)
        {
            return new SmtpClient(SmtpHost, SmtpPort)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(user, pass),
                Timeout = 120000,
            };
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
