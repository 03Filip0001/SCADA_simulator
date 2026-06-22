using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataConcentrator.Model
{
    public class AnalogInput : Tag, InputCommon, AnalogCommon
    {
        // Input common interface
        public double ScanTime { get; set; }
        public bool ScanOn { get; set; }

        // Analog common interface
        public double LowLimit {  get; set; }
        public double HighLimit { get; set; }
        public string Units { get; set; }

        // Analog input
        public double Deadband { get; set; }
        // DODAJ HISTEREZIS
        // DODAJ ALARMS
    }
}
