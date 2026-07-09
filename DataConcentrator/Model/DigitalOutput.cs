using Contracts;
using System;

namespace DataConcentrator.Model
{
    internal class DigitalOutput : Tag, IDigitalOutput
    {
        private bool currentState;

        // Output common interface
        public double InitialValue { get; set; }

        public bool CurrentState
        {
            get => currentState;
            private set
            {
                if (currentState != value)
                {
                    currentState = value;
                    OnPropertyChanged(nameof(CurrentState));
                    OnPropertyChanged(nameof(CurrentValue));
                }
            }
        }

        public override double CurrentValue
        {
            get => currentState ? 1.0 : 0.0;
            protected set { }
        }

        public DigitalOutput(string address)
        {
            Address = address;
            InitialValue = 0;
        }

        internal void ApplyWrite(double value) => CurrentState = value != 0;

        internal void RestoreCurrentState(bool state) => CurrentState = state;
    }
}
