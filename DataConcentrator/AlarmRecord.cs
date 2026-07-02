using System;

namespace DataConcentrator
{
    public class AlarmRecord
    {
        public int Id { get; set; }
        public string TagName { get; set; }
        public string Address { get; set; }
        public double TriggeredValue { get; set; }
        public double LowLimit { get; set; }
        public double HighLimit { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
