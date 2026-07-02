using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Contracts;
using DataConcentrator;

namespace DataConcentrator.Model
{
    public class AnalogInput : Tag, IAnalogInput
    {
        private double currentValue;
        private bool alarmActive;
        private bool alarmAcknowledged;
        private string alarmMessage;
        private readonly List<AnalogInputHistoryRecord> history = new List<AnalogInputHistoryRecord>();

        public double ScanTime { get; set; }
        public bool ScanOn { get; set; }

        public double LowLimit { get; set; }
        public double HighLimit { get; set; }
        public string Units { get; set; }

        public double Deadband { get; set; }
        public double Hysteresis { get; set; }

        public bool AlarmActive
        {
            get => alarmActive;
            private set
            {
                if (alarmActive != value)
                {
                    alarmActive = value;
                    OnPropertyChanged(nameof(AlarmActive));
                }
            }
        }

        public bool AlarmAcknowledged
        {
            get => alarmAcknowledged;
            private set
            {
                if (alarmAcknowledged != value)
                {
                    alarmAcknowledged = value;
                    OnPropertyChanged(nameof(AlarmAcknowledged));
                }
            }
        }

        public string AlarmMessage
        {
            get => alarmMessage;
            private set
            {
                if (alarmMessage != value)
                {
                    alarmMessage = value;
                    OnPropertyChanged(nameof(AlarmMessage));
                }
            }
        }

        public double CurrentValue
        {
            get => currentValue;
            private set
            {
                if (Math.Abs(currentValue - value) > double.Epsilon)
                {
                    currentValue = value;
                    OnPropertyChanged(nameof(CurrentValue));
                }
            }
        }

        public IReadOnlyList<AnalogInputHistoryRecord> History => history.AsReadOnly();

        public double AverageValue => history.Any() ? history.Average(record => record.Value) : 0;
        public double MinValue => history.Any() ? history.Min(record => record.Value) : 0;
        public double MaxValue => history.Any() ? history.Max(record => record.Value) : 0;

        public event Action<AnalogInput, AlarmInfo> AlarmRaised;
        public event Action<AnalogInput, AnalogInputHistoryRecord> HistoryRecorded;

        public AnalogInput(string address)
        {
            Address = address;
            ScanTime = 1.0;
            ScanOn = true;
            Units = "units";
            Deadband = 0.5;
            Hysteresis = 0.5;
            LowLimit = 0;
            HighLimit = 100;
            AlarmMessage = string.Empty;
        }

        public void StartScan() => ScanOn = true;

        public void StopScan() => ScanOn = false;

        public void AcknowledgeAlarm()
        {
            if (AlarmActive)
            {
                AlarmAcknowledged = true;
            }
        }

        internal void UpdateValue(double newValue)
        {
            if (Math.Abs(CurrentValue - newValue) <= Deadband)
            {
                return;
            }

            CurrentValue = newValue;
            AddHistoryRecord(newValue);
            EvaluateAlarm(newValue);
        }

        private void AddHistoryRecord(double newValue)
        {
            var record = new AnalogInputHistoryRecord
            {
                Timestamp = DateTime.UtcNow,
                Value = newValue,
                SourceAddress = Address,
                Units = Units
            };

            history.Add(record);
            HistoryRecorded?.Invoke(this, record);
            OnPropertyChanged(nameof(AverageValue));
            OnPropertyChanged(nameof(MinValue));
            OnPropertyChanged(nameof(MaxValue));
        }

        private void EvaluateAlarm(double value)
        {
            bool isAbove = value > HighLimit;
            bool isBelow = value < LowLimit;
            bool shouldBeActive = isAbove || isBelow;

            if (shouldBeActive)
            {
                AlarmActive = true;
                AlarmAcknowledged = false;
                AlarmMessage = isAbove
                    ? $"Alarm triggered: {value:F2} > HighLimit ({HighLimit:F2})"
                    : $"Alarm triggered: {value:F2} < LowLimit ({LowLimit:F2})";

                var alarmInfo = new AlarmInfo
                {
                    TagName = Name,
                    Address = Address,
                    TriggeredValue = value,
                    LowLimit = LowLimit,
                    HighLimit = HighLimit,
                    IsAcknowledged = AlarmAcknowledged,
                    Message = AlarmMessage,
                    Timestamp = DateTime.UtcNow
                };

                PersistAlarm(alarmInfo);
                AlarmRaised?.Invoke(this, alarmInfo);

                return;
            }

            if (AlarmActive && IsInsideClearRange(value))
            {
                AlarmActive = false;
                AlarmAcknowledged = false;
                AlarmMessage = string.Empty;
            }
        }

        private bool IsInsideClearRange(double value)
        {
            return value >= LowLimit + Hysteresis && value <= HighLimit - Hysteresis;
        }

        private void PersistAlarm(AlarmInfo alarmInfo)
        {
            var record = new DataConcentrator.AlarmRecord
            {
                TagName = alarmInfo.TagName,
                Address = alarmInfo.Address,
                TriggeredValue = alarmInfo.TriggeredValue,
                LowLimit = alarmInfo.LowLimit,
                HighLimit = alarmInfo.HighLimit,
                Message = alarmInfo.Message,
                Timestamp = alarmInfo.Timestamp
            };

            ContextClass.Instance.AlarmRecords.Add(record);
            ContextClass.Instance.SaveChanges();
            alarmInfo.Id = record.Id;
        }
    }

    public class AnalogInputHistoryRecord
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
        public string SourceAddress { get; set; }
        public string Units { get; set; }
    }

    public class AlarmInfo
    {
        public int Id { get; set; }
        public string TagName { get; set; }
        public string Address { get; set; }
        public double TriggeredValue { get; set; }
        public double LowLimit { get; set; }
        public double HighLimit { get; set; }
        public bool IsAcknowledged { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
