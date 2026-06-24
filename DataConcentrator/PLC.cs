using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Contracts;

namespace DataConcentrator
{
    public class PLC
    {
        private readonly IPLCSimulatorManager _plc;

        public static Dictionary<string, Thread> tagThreads = new Dictionary<string, Thread>();

        public PLC(IPLCSimulatorManager plc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _plc.StartPLCSimulator();
        }

        public void StopSimulator()
        {
            _plc.Abort();
        }
         
    }
}
