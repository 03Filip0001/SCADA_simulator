using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Contracts
{
    public interface IAnalogInput : INotifyPropertyChanged, IInputCommon, IAnalogCommon, ITag
    {
        double Deadband { get; set; }
        double CurrentValue { get; }
        bool AlarmActive { get; }
        bool AlarmAcknowledged { get; }
        string AlarmMessage { get; }
        void AcknowledgeAlarm();
    }
}
