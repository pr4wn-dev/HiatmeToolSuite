using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Hiatme_Tools
{
    public class WRCompanyInfo
    {
        public string CompanyID { get; set; }
        public string CompanyCode { get; set; }
        public string CompanyName { get; set; }
        public string ContactName { get; set; }
        public string Email { get; set; }
        /// <summary>Matches portal login page AJAX (<c>companyInfo.geolocation</c>).</summary>
        [JsonProperty("geolocation")]
        public string Geolocation { get; set; }

    }
}
