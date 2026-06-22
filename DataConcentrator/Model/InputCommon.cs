using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataConcentrator.Model
{
    internal interface InputCommon
    {
        double ScanTime { get; set; }
        bool ScanOn { get; set; }
    }
}
