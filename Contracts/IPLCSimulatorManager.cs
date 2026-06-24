using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Contracts
{
    public interface IPLCSimulatorManager
    {
        void StartPLCSimulator();
        double GetAnalogValue(string address);
        void SetAnalogValue(string address, double value);
        void SetDigitalValue(string address, double value);
        void Abort();
    }
}
