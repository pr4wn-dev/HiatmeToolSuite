using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal class WRDrivers
    {
        public string text { get; set; }
        public string value { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName { get; set; }


        public void BreakName()
        {
            string fullname = text;

            string first = fullname.Split(' ')[0]; //First word
            first = first.ToLower();
            FirstName = (char.ToUpper(first[0]) + first.Substring(1)).Replace(" ", "");

            string last = fullname.Split(' ')[fullname.Split(' ').Length - 1]; //Last word
            last = last.ToLower();
            LastName = (char.ToUpper(last[0]) + last.Substring(1)).Replace(" ","");

            FullName = FirstName + " " + LastName;
        }

    }
}
