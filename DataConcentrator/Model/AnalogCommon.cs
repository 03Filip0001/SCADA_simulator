using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataConcentrator.Model
{
    internal interface AnalogCommon
    {
        double LowLimit { get; set; }
        double HighLimit { get; set; }
        string Units { get; set; }
    }
}
