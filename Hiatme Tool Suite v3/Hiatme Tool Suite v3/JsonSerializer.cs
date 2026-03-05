using Hiatme_Tools;
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
            if (jsonstr != null)
            {
                WRBatchData wrBatchData = JsonConvert.DeserializeObject<WRBatchData>(jsonstr);
                return wrBatchData;
            }
            return null;
        }
        public string DeserializeDriverJSONString(string jsonstr)
        {
            if (jsonstr != null)
            {
                WRDriverInfo wrDriverData = JsonConvert.DeserializeObject<WRDriverInfo>(jsonstr);
                if (wrDriverData != null)
                {
                    string driverName = wrDriverData.ColumnValue;
                    return driverName;
                }
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
