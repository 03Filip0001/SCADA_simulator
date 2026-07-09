using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Contracts
{
    public interface IDigitalOutput : INotifyPropertyChanged, IOutputCommon, ITag
    {
        double CurrentValue { get; }
    }
}
