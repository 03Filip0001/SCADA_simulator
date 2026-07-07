using Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataConcentrator.Model
{
    internal class DigitalInput : Tag, IDigitalInput
    {
        private bool currentState;

        public double ScanTime { get; set; }
        public bool ScanOn { get; set; }

        public bool CurrentState
        {
            get => currentState;
            private set
            {
                if (currentState != value)
                {
                    currentState = value;
                    OnPropertyChanged(nameof(CurrentState));
                }
            }
        }

        public DigitalInput(string address)
        {
            Address = address;
            ScanTime = 1.0;
            ScanOn = true;
            CurrentState = false;
        }

        internal void UpdateState(bool newState)
        {
            CurrentState = newState;
        }

        internal void RestoreCurrentState(bool currentState)
        {
            CurrentState = currentState;
        }

        public void StartScan() => ScanOn = true;

        public void StopScan() => ScanOn = false;
    }
}
