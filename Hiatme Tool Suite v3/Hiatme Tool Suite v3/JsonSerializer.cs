using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    internal class JsonSerializer
    {  
        //deserialize jsonstring into a dynamic object
        public WRBatchData DeserializeJSONString(string jsonstr)
        {
            if (string.IsNullOrWhiteSpace(jsonstr))
                return null;
            var trimmed = jsonstr.TrimStart();
            if (trimmed.StartsWith("<", StringComparison.Ordinal))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<WRBatchData>(jsonstr);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        public string DeserializeDriverJSONString(string jsonstr)
        {
            if (string.IsNullOrWhiteSpace(jsonstr))
                return null;
            try
            {
                WRDriverInfo wrDriverData = JsonConvert.DeserializeObject<WRDriverInfo>(jsonstr);
                if (wrDriverData != null)
                    return wrDriverData.ColumnValue;
            }
            catch (JsonException)
            {
            }
            return null;
        }
    }
    public class WRBatchData
    {
        public dynamic values { get; set; }
        public string totalRecords { get; set; }
    }
    public class WRDriverInfo
    {
        public string ColumnValue { get; set; }
    }
}
