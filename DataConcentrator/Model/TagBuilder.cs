using Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataConcentrator.Model
{
    public class TagBuilder : ITagBuilder
    {
        public IAnalogInput CreateAnalogInput(string address) { return new AnalogInput(address); }
        public IAnalogOutput CreateAnalogOutput(string address) { return new AnalogOutput(address); }
        public IDigitalInput CreateDigitalInput(string address) {return new DigitalInput(address); }
        public IDigitalOutput CreateDigitalOutput(string address) {return new DigitalOutput(address);}
    }
}
