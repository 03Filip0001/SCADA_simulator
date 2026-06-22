using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataConcentrator
{
    // napraviti AnalogInput, AnalogOuput, DigitalInput i 
    // DigitalOutput klase koje nasledjuju Tag klasu

    public enum Tag_Type
    {
        AI,
        AO,
        DI,
        DO
    }

    public class Tag : INotifyPropertyChanged
    {
       
        private string name;

        private string description;

        private Tag_Type type;

        private string address;


        #region Properties

        [Key]
        public string Name
        {
            get { return name; }
            set
            {
                name = value;
                OnPropertyChanged("Name");
            }
        }

        public string Description
        {
            get { return description; }
            set
            {
                description = value;
                OnPropertyChanged("Description");
            }
        }

        public Tag_Type Type
        {
            get { return  type; }
            set
            {
                type = value;
                OnPropertyChanged("Type");
            }
        }

        public string Address
        {
            get { return address; }
            set
            {
                address = value;
                OnPropertyChanged("Address");
            }
        }

        #endregion

        #region INotifyPropertyChanged Members

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        #endregion
    }
}
