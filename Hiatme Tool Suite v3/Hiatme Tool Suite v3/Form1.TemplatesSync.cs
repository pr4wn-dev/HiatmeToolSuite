using System;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        /// <summary>Upload weekday template folders to the panel for regulars/reporting.</summary>
        internal void SyncDeskTemplatesToPanel()
        {
            try
            {
                var settings = HiatmeAiSettings.LoadNoProbe();
                if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                    return;
                if (!settings.ShouldUploadDeskTemplates())
                    return;
                HiatmeAiClient.SyncDeskTemplatesFireAndForget(settings);
            }
            catch { }
        }
    }
}
