using Contracts;
using System;

namespace DataConcentrator.Model
{
    internal class AnalogOutput : Tag, IAnalogOutput
    {
        private double currentValue;

        // Analog common interface
        public double LowLimit { get; set; }
        public double HighLimit { get; set; }
        public string Units { get; set; }

        // Output common interface
        public double InitialValue { get; set; }

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

        public AnalogOutput(string address)
        {
            Address = address;
            Units = "units";
            LowLimit = 0;
            HighLimit = 100;
            InitialValue = 0;
        }

        // Applies a written value to the tag. The write-through to the PLC
        // Simulator is done by PLC.WriteOutput so the GUI never touches the
        // simulator directly.
        internal void ApplyWrite(double value) => CurrentValue = value;

        internal void RestoreCurrentValue(double value) => CurrentValue = value;
    }
}
