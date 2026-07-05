using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Contracts
{
    public interface IInputCommon
    {
        double ScanTime { get; set; }
        bool ScanOn { get; set; }
        void StartScan();
        void StopScan();
    }
}
