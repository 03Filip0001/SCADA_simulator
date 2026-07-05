using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using DataConcentrator.Model;

namespace DataConcentrator
{
    public class PLC
    {
        private readonly IPLCSimulatorManager _plc;
        private readonly Dictionary<string, Thread> scanThreads = new Dictionary<string, Thread>();

        public List<ITag> IOElements { get; set; }

        public PLC(IPLCSimulatorManager plc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _plc.StartPLCSimulator();
            IOElements = new List<ITag>();
        }

        public void StopSimulator()
        {
            foreach (var thread in scanThreads.Values)
            {
                try
                {
                    thread?.Abort();
                }
                catch
                {
                    // ignore abort exceptions
                }
            }
            _plc.Abort();
        }

        public bool AddInput(ITag tag)
        {
            if (tag == null)
            {
                return false;
            }

            IOElements.Add(tag);

            if (tag is AnalogInput analogInput)
            {
                if (!scanThreads.ContainsKey(analogInput.Address))
                {
                    var thread = new Thread(() => ScanInput(analogInput))
                    {
                        IsBackground = true,
                        Name = $"AnalogScan-{analogInput.Address}"
                    };

                    scanThreads[analogInput.Address] = thread;
                    thread.Start();
                }
            }
            else if (tag is DigitalInput digitalInput)
            {
                if (!scanThreads.ContainsKey(digitalInput.Address))
                {
                    var thread = new Thread(() => ScanInput(digitalInput))
                    {
                        IsBackground = true,
                        Name = $"DigitalScan-{digitalInput.Address}"
                    };

                    scanThreads[digitalInput.Address] = thread;
                    thread.Start();
                }
            }

            return true;
        }

        private void ScanInput(ITag tag)
        {
            // Branch once to the appropriate loop for the concrete tag type
            if (tag is AnalogInput analogInput)
            {
                while (analogInput.ScanOn)
                {
                    try
                    {
                        double value = _plc.GetAnalogValue(analogInput.Address);
                        analogInput.UpdateValue(value);
                    }
                    catch
                    {
                        // failure reading value should not crash the scanner thread
                    }

                    var delaySeconds = Math.Max(0.2, analogInput.ScanTime);
                    Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
                }

                return;
            }

            if (tag is DigitalInput digitalInput)
            {
                while (digitalInput.ScanOn)
                {
                    try
                    {
                        double value = _plc.GetAnalogValue(digitalInput.Address);
                        digitalInput.UpdateState(value != 0);
                    }
                    catch
                    {
                        // failure reading value should not crash the scanner thread
                    }

                    var delaySeconds = Math.Max(0.2, digitalInput.ScanTime);
                    Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
                }
            }
        }
    }

}
