using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Contracts;
using DataConcentrator.Model;

namespace DataConcentrator
{
    public class PLC
    {
        private readonly IPLCSimulatorManager _plc;
        // One scan thread per input tag so each tag can be polled at its own
        // ScanTime (the spec requires per-tag scan periods).
        private readonly Dictionary<ITag, Thread> scanThreads = new Dictionary<ITag, Thread>();
        private readonly object syncRoot = new object();
        private volatile bool _running = true;

        public event Action<AlarmInfo> AlarmRaised;
        public event Action<string> AlarmCleared;

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
                if (!IOElements.Contains(tag))
                {
                    IOElements.Add(tag);
                }

                if (tag is AnalogInput analogInput)
                {
                    analogInput.AlarmRaised += OnAlarmRaised;
                    analogInput.AlarmCleared += OnAlarmCleared;
                    analogInput.StartScan();
                }
                else if (tag is DigitalInput digitalInput)
                {
                    digitalInput.StartScan();
                }

                if (!scanThreads.ContainsKey(tag))
                {
                    threadToStart = new Thread(() => ScanTag(tag))
                    {
                        IsBackground = true,
                        Name = $"Scan-{tag.Address}"
                    };

                    scanThreads[tag] = threadToStart;
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
                    analogInput.AlarmCleared -= OnAlarmCleared;
                    analogInput.StopScan();
                }
                else if (tag is DigitalInput digitalInput)
                {
                    digitalInput.StopScan();
                }

                // Removing the tag from the dictionary makes its scan thread
                // notice on its next iteration that it is no longer registered
                // and exit.
                scanThreads.Remove(tag);
            }

            return true;
        }

        // Writes a value into an output tag: pushes it to the PLC Simulator and
        // updates the tag so the GUI reflects it. Returns false with a reason
        // when the tag is not an output.
        public bool WriteOutput(ITag tag, double value, out string errorMessage)
        {
            errorMessage = null;

            if (tag == null)
            {
                errorMessage = "No tag selected.";
                return false;
            }

            try
            {
                if (tag is AnalogOutput analogOutput)
                {
                    _plc.SetAnalogValue(tag.Address, value);
                    analogOutput.ApplyWrite(value);
                    return true;
                }

                if (tag is DigitalOutput digitalOutput)
                {
                    double normalized = value != 0 ? 1 : 0;
                    _plc.SetDigitalValue(tag.Address, normalized);
                    digitalOutput.ApplyWrite(normalized);
                    return true;
                }

                errorMessage = "Selected tag is not an output tag.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                SystemLogger.LogError($"Failed to write value to output '{tag.Name}'.", ex);
                return false;
            }
        }

        private void ScanTag(ITag tag)
        {
            while (_running)
            {
                lock (syncRoot)
                {
                    if (!scanThreads.ContainsKey(tag))
                    {
                        break;
                    }
                }

                double scanSeconds = 0.5;

                try
                {
                    if (tag is AnalogInput analogInput)
                    {
                        scanSeconds = analogInput.ScanTime > 0 ? analogInput.ScanTime : 0.5;
                        if (analogInput.ScanOn)
                        {
                            analogInput.UpdateValue(_plc.GetAnalogValue(tag.Address));
                        }
                    }
                    else if (tag is DigitalInput digitalInput)
                    {
                        scanSeconds = digitalInput.ScanTime > 0 ? digitalInput.ScanTime : 0.5;
                        if (digitalInput.ScanOn)
                        {
                            digitalInput.UpdateState(_plc.GetAnalogValue(tag.Address) != 0);
                        }
                    }
                    else
                    {
                        // Not an input tag; nothing to scan.
                        break;
                    }
                }
                catch (Exception ex)
                {
                    // failure reading value should not crash the scanner thread
                    SystemLogger.LogError($"PLC communication failed for tag '{tag.Name}'.", ex);
                }

                Thread.Sleep(TimeSpan.FromSeconds(scanSeconds > 0 ? scanSeconds : 0.5));
            }
        }

        private void OnAlarmRaised(AnalogInput source, AlarmInfo alarmInfo)
        {
            AlarmRaised?.Invoke(alarmInfo);
        }

        private void OnAlarmCleared(AnalogInput source)
        {
            AlarmCleared?.Invoke(source.Name);
        }
    }
}
