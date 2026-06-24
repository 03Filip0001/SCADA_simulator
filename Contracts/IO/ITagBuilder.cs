using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts
{
    public interface ITagBuilder
    {
        IAnalogInput CreateAnalogInput(string address);
        IAnalogOutput CreateAnalogOutput(string address);

        IDigitalInput CreateDigitalInput(string address);
        IDigitalOutput CreateDigitalOutput(string address);
    }
}
