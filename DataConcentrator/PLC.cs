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
        private readonly Dictionary<string, List<ITag>> scannedTagsByAddress = new Dictionary<string, List<ITag>>();
        private readonly object syncRoot = new object();
        private volatile bool _running = true;

        public event Action<AlarmInfo> AlarmRaised;

        public List<ITag> IOElements { get; set; }

        public PLC(IPLCSimulatorManager plc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _plc.StartPLCSimulator();
            IOElements = new List<ITag>();
        }

        public void StopSimulator()
        {
            _running = false;

            List<ITag> tagsSnapshot;
            List<Thread> threadsSnapshot;

            lock (syncRoot)
            {
                tagsSnapshot = IOElements.ToList();
                threadsSnapshot = scanThreads.Values.ToList();
            }

            // Give threads time to exit gracefully
            foreach (var tag in tagsSnapshot)
            {
                if (tag is AnalogInput analogInput)
                {
                    analogInput.StopScan();
                }
                else if (tag is DigitalInput digitalInput)
                {
                    digitalInput.StopScan();
                }
            }

            // Wait for scan threads to finish
            foreach (var thread in threadsSnapshot)
            {
                try
                {
                    thread?.Join(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // ignore join exceptions
                }
            }

            try
            {
                _plc.StopPLCSimulator();
            }
            catch
            {
                // ignore simulator stop exceptions
            }
        }

        public bool AddInput(ITag tag)
        {
            if (tag == null)
            {
                return false;
            }

            Thread threadToStart = null;

            lock (syncRoot)
            {
                IOElements.Add(tag);

                if (tag is AnalogInput analogInput)
                {
                    analogInput.AlarmRaised += OnAlarmRaised;
                    analogInput.StartScan();
                }
                else if (tag is DigitalInput digitalInput)
                {
                    digitalInput.StartScan();
                }

                var address = tag.Address ?? string.Empty;
                if (!scannedTagsByAddress.TryGetValue(address, out var tagList))
                {
                    tagList = new List<ITag>();
                    scannedTagsByAddress[address] = tagList;
                }

                tagList.Add(tag);

                if (!scanThreads.ContainsKey(address))
                {
                    threadToStart = new Thread(() => ScanAddress(address))
                    {
                        IsBackground = true,
                        Name = $"Scan-{address}"
                    };

                    scanThreads[address] = threadToStart;
                }
            }

            if (threadToStart != null)
            {
                threadToStart.Start();
            }

            return true;
        }

        public bool RemoveInput(ITag tag)
        {
            if (tag == null)
            {
                return false;
            }

            lock (syncRoot)
            {
                IOElements.Remove(tag);

                if (tag is AnalogInput analogInput)
                {
                    analogInput.AlarmRaised -= OnAlarmRaised;
                    analogInput.StopScan();
                }
                else if (tag is DigitalInput digitalInput)
                {
                    digitalInput.StopScan();
                }

                var address = tag.Address ?? string.Empty;
                if (scannedTagsByAddress.TryGetValue(address, out var tagList))
                {
                    tagList.Remove(tag);
                    if (tagList.Count == 0)
                    {
                        scannedTagsByAddress.Remove(address);
                        scanThreads.Remove(address);
                    }
                }
            }

            return true;
        }

        private void ScanAddress(string address)
        {
            while (_running)
            {
                List<ITag> tags;

                lock (syncRoot)
                {
                    if (!scannedTagsByAddress.TryGetValue(address, out var tagList))
                    {
                        break;
                    }

                    if (tagList.Count == 0)
                    {
                        break;
                    }

                    tags = tagList.ToList();
                }

                double? analogValue = null;
                try
                {
                    analogValue = _plc.GetAnalogValue(address);
                }
                catch
                {
                    // failure reading value should not crash the scanner thread
                }

                foreach (var tag in tags)
                {
                    if (tag is AnalogInput analogInput)
                    {
                        if (!analogInput.ScanOn)
                        {
                            continue;
                        }

                        if (analogValue.HasValue)
                        {
                            analogInput.UpdateValue(analogValue.Value);
                        }
                    }
                    else if (tag is DigitalInput digitalInput)
                    {
                        if (!digitalInput.ScanOn)
                        {
                            continue;
                        }

                        if (analogValue.HasValue)
                        {
                            digitalInput.UpdateState(analogValue.Value != 0);
                        }
                    }
                }

                Thread.Sleep(TimeSpan.FromSeconds(0.5));
            }
        }

        private void OnAlarmRaised(AnalogInput source, AlarmInfo alarmInfo)
        {
            AlarmRaised?.Invoke(alarmInfo);
        }
    }

}
