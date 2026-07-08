using System;
using System.Collections.Generic;

namespace DataConcentrator
{
    public class TagEntity
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public string Address { get; set; }
        public double? ScanTime { get; set; }
        public bool? ScanOn { get; set; }
        public double? LowLimit { get; set; }
        public double? HighLimit { get; set; }
        public string Units { get; set; }
        public double? InitialValue { get; set; }
        public double? Deadband { get; set; }
        public double? Hysteresis { get; set; }
        public double? CurrentValue { get; set; }
        public bool? CurrentState { get; set; }

        public virtual ICollection<AlarmEntity> Alarms { get; set; } = new List<AlarmEntity>();
    }

    public class AlarmEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string TagName { get; set; }
        public double BoundaryValue { get; set; }
        public string ActivationType { get; set; }
        public double LowLimit { get; set; }
        public double HighLimit { get; set; }
        public string Message { get; set; }
        public string State { get; set; }
        public int Priority { get; set; }
        public bool IsAcknowledged { get; set; }

        public virtual TagEntity Tag { get; set; }
    }

    public class ActivatedAlarmEntity
    {
        public int Id { get; set; }
        public int AlarmId { get; set; }
        public string TagName { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AnalogInputHistoryEntity
    {
        public int Id { get; set; }
        public string TagName { get; set; }
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
    }

    public class AppSettingEntity
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
