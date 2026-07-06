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
        private bool alarmEvaluationPending;
        private string alarmMessage;
        private readonly List<AnalogInputHistoryRecord> history = new List<AnalogInputHistoryRecord>();

        public double ScanTime { get; set; }
        public bool ScanOn { get; set; }

        public double LowLimit { get; set; }
        public double HighLimit { get; set; }
        public string Units { get; set; }

        public double Deadband { get; set; }
        public double Hysteresis { get; set; }
        public bool AlarmEnabled { get; set; }
        public string AlarmName { get; set; }
        public string AlarmType { get; set; }
        public int AlarmPriority { get; set; }

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
            set
            {
                if (alarmMessage != value)
                {
                    alarmMessage = value;
                    OnPropertyChanged(nameof(AlarmMessage));
                }
            }
        }

        public override double CurrentValue
        {
            get => currentValue;
            protected set
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
        public event Action<AnalogInput> AlarmCleared;
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
            AlarmEnabled = false;
            AlarmName = string.Empty;
            AlarmType = string.Empty;
            AlarmPriority = 0;
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

        public void ConfigureAlarm(double lowLimit, double highLimit, string message)
        {
            ConfigureAlarm(lowLimit, highLimit, message, string.Empty, string.Empty, 0);
        }

        public void ConfigureAlarm(double lowLimit, double highLimit, string message, string alarmName, string alarmType, int alarmPriority)
        {
            LowLimit = lowLimit;
            HighLimit = highLimit;
            AlarmMessage = message ?? string.Empty;
            AlarmName = alarmName ?? string.Empty;
            AlarmType = alarmType ?? string.Empty;
            AlarmPriority = alarmPriority;
            AlarmEnabled = true;
            alarmEvaluationPending = true;
            OnPropertyChanged(nameof(AlarmEnabled));
            OnPropertyChanged(nameof(AlarmName));
            OnPropertyChanged(nameof(AlarmType));
            OnPropertyChanged(nameof(AlarmPriority));
        }

        public void ClearAlarm()
        {
            bool wasActive = AlarmActive;

            AlarmEnabled = false;
            AlarmActive = false;
            AlarmAcknowledged = false;
            AlarmName = string.Empty;
            AlarmType = string.Empty;
            AlarmPriority = 0;
            AlarmMessage = string.Empty;
            alarmEvaluationPending = false;
            OnPropertyChanged(nameof(AlarmEnabled));
            OnPropertyChanged(nameof(AlarmName));
            OnPropertyChanged(nameof(AlarmType));
            OnPropertyChanged(nameof(AlarmPriority));

            if (wasActive)
            {
                AlarmCleared?.Invoke(this);
            }
        }

        internal void UpdateValue(double newValue)
        {
            if (Math.Abs(CurrentValue - newValue) <= Deadband && !alarmEvaluationPending)
            {
                return;
            }

            if (Math.Abs(CurrentValue - newValue) > Deadband)
            {
                CurrentValue = newValue;
                AddHistoryRecord(newValue);
            }

            alarmEvaluationPending = false;
            EvaluateAlarm(CurrentValue);
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
            if (!AlarmEnabled)
            {
                return;
            }

            bool isAbove = value > HighLimit;
            bool isBelow = value < LowLimit;
            bool shouldBeActive = isAbove || isBelow;

            if (shouldBeActive)
            {
                bool wasActive = AlarmActive;
                var configuredMessage = AlarmMessage;

                AlarmActive = true;
                if (!wasActive)
                {
                    AlarmAcknowledged = false;
                }

                AlarmMessage = string.IsNullOrWhiteSpace(configuredMessage)
                    ? isAbove
                        ? $"Alarm triggered: {value:F2} > HighLimit ({HighLimit:F2})"
                        : $"Alarm triggered: {value:F2} < LowLimit ({LowLimit:F2})"
                    : configuredMessage;

                var alarmInfo = new AlarmInfo
                {
                    TagName = Name,
                    Address = Address,
                    TriggeredValue = value,
                    LowLimit = LowLimit,
                    HighLimit = HighLimit,
                    IsAcknowledged = AlarmAcknowledged,
                    AlarmName = AlarmName,
                    AlarmType = AlarmType,
                    Priority = AlarmPriority,
                    Message = AlarmMessage,
                    Timestamp = DateTime.UtcNow
                };

                if (!wasActive)
                {
                    TryPersistAlarm(alarmInfo);
                    AlarmRaised?.Invoke(this, alarmInfo);
                }

                return;
            }

            if (AlarmActive && IsInsideClearRange(value))
            {
                AlarmActive = false;
                AlarmAcknowledged = false;
                AlarmMessage = string.Empty;
                AlarmCleared?.Invoke(this);
            }
        }

        private bool IsInsideClearRange(double value)
        {
            return value >= LowLimit + Hysteresis && value <= HighLimit - Hysteresis;
        }

        private void TryPersistAlarm(AlarmInfo alarmInfo)
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

            lock (ContextClass.SyncRoot)
            {
                try
                {
                    ContextClass.Instance.AlarmRecords.Add(record);
                    ContextClass.Instance.SaveChanges();
                    alarmInfo.Id = record.Id;
                }
                catch
                {
                    alarmInfo.Id = 0;
                }
            }
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
        public string AlarmName { get; set; }
        public string AlarmType { get; set; }
        public int Priority { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
