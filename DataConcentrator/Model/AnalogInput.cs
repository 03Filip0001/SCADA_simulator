using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Contracts;
using DataConcentrator;
using DataConcentrator.Persistence;

namespace DataConcentrator.Model
{
    public class AnalogInput : Tag, IAnalogInput
    {
        private double currentValue;
        private bool alarmActive;
        private bool alarmAcknowledged;
        private bool alarmHasOccurred;
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
        public int AlarmDefinitionId { get; set; }

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

        // True once this alarm has fired at least once since it was configured.
        // Distinguishes a fresh, never-triggered alarm (shown as Inactive/green)
        // from one that fired and returned to normal but is still unacknowledged
        // (shown as still red until acknowledged).
        public bool AlarmHasOccurred
        {
            get => alarmHasOccurred;
            private set
            {
                if (alarmHasOccurred != value)
                {
                    alarmHasOccurred = value;
                    OnPropertyChanged(nameof(AlarmHasOccurred));
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
            AlarmDefinitionId = 0;
            AlarmMessage = string.Empty;
        }

        public void StartScan() => ScanOn = true;

        public void StopScan() => ScanOn = false;

        public void AcknowledgeAlarm()
        {
            AlarmAcknowledged = true;
        }

        public void RestoreCurrentValue(double value)
        {
            CurrentValue = value;
        }

        public void RestoreAlarmState(bool isActive, bool isAcknowledged)
        {
            AlarmActive = isActive;
            AlarmAcknowledged = isAcknowledged;
            AlarmHasOccurred = isActive || isAcknowledged;
            alarmEvaluationPending = true;
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
            AlarmHasOccurred = false;
            AlarmName = string.Empty;
            AlarmType = string.Empty;
            AlarmPriority = 0;
            AlarmDefinitionId = 0;
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

            PersistenceService.SaveHistoryRecord(Name, record, out _);
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
                    AlarmHasOccurred = true;
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
                    IsActive = true,
                    HasOccurred = AlarmHasOccurred,
                    AlarmDefinitionId = AlarmDefinitionId,
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
                // AlarmAcknowledged deliberately survives this transition: an
                // alarm that returns to normal without having been acknowledged
                // must stay red (not jump to green) until the user acknowledges
                // it. It only resets to false above when the alarm next
                // transitions from inactive to active (a new occurrence).
                AlarmActive = false;
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
            if (!PersistenceService.SaveAlarmEvent(alarmInfo, out _))
            {
                alarmInfo.Id = 0;
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

    public class AlarmInfo : INotifyPropertyChanged
    {
        private int id;
        private string tagName;
        private string address;
        private double triggeredValue;
        private double lowLimit;
        private double highLimit;
        private bool isAcknowledged;
        private bool isActive;
        private bool hasOccurred;
        private int alarmDefinitionId;
        private string alarmName;
        private string alarmType;
        private int priority;
        private string message;
        private DateTime timestamp;

        public int Id
        {
            get => id;
            set => SetField(ref id, value);
        }

        public string TagName
        {
            get => tagName;
            set => SetField(ref tagName, value);
        }

        public string Address
        {
            get => address;
            set => SetField(ref address, value);
        }

        public double TriggeredValue
        {
            get => triggeredValue;
            set => SetField(ref triggeredValue, value);
        }

        public double LowLimit
        {
            get => lowLimit;
            set => SetField(ref lowLimit, value);
        }

        public double HighLimit
        {
            get => highLimit;
            set => SetField(ref highLimit, value);
        }

        public bool IsAcknowledged
        {
            get => isAcknowledged;
            set
            {
                if (SetField(ref isAcknowledged, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowAcknowledged)));
                }
            }
        }

        public bool IsActive
        {
            get => isActive;
            set
            {
                if (SetField(ref isActive, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowAcknowledged)));
                }
            }
        }

        public bool ShowAcknowledged => isAcknowledged && isActive;

        public bool HasOccurred
        {
            get => hasOccurred;
            set => SetField(ref hasOccurred, value);
        }

        public int AlarmDefinitionId
        {
            get => alarmDefinitionId;
            set => SetField(ref alarmDefinitionId, value);
        }

        public string AlarmName
        {
            get => alarmName;
            set => SetField(ref alarmName, value);
        }

        public string AlarmType
        {
            get => alarmType;
            set => SetField(ref alarmType, value);
        }

        public int Priority
        {
            get => priority;
            set => SetField(ref priority, value);
        }

        public string Message
        {
            get => message;
            set => SetField(ref message, value);
        }

        public DateTime Timestamp
        {
            get => timestamp;
            set => SetField(ref timestamp, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
