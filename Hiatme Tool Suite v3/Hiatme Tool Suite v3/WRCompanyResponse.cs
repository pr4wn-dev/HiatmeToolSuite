using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Hiatme_Tools
{
    public class WRCompanyResponse
    {
        public string status { get; set; }
        [JsonProperty("companyInfo")]
        public WRCompanyInfo companyinfo { get; set; }
        
    }
}
