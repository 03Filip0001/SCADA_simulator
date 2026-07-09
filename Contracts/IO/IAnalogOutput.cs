using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Contracts
{
    public interface IAnalogOutput : INotifyPropertyChanged, IAnalogCommon, IOutputCommon, ITag
    {
        double CurrentValue { get; }
    }
}
