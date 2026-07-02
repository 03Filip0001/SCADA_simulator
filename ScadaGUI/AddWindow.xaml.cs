using Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ScadaGUI
{
    /// <summary>
    /// Interaction logic for AddWindow.xaml
    /// </summary>
    public partial class AddWindow : Window
    {

        public string DialogResultType { get; set; }
        public ITag DialogResultTag { get; set; }

        // public IAlarm DialogResultAlarm {get; set;}
        public AddWindow()
        {
            InitializeComponent();

            List<object> dropdownOptions = new List<object>();

            dropdownOptions.Add("None");
            var enumTypes = Enum.GetValues(typeof(Tag_Type));
            foreach (var enumType in enumTypes)
            {
                dropdownOptions.Add(enumType.ToString());
            }
            dropdownOptions.Add("Alarm");

            Dropdown.ItemsSource = dropdownOptions;
            Dropdown.SelectedIndex = 0;

            panelAlarm.Visibility = Visibility.Collapsed;
            panelIO.Visibility = Visibility.Collapsed;

        }

        public void Dropdown_Changed(object sender, RoutedEventArgs e)
        {
            var item = Dropdown.SelectedItem;

            Debug.WriteLine(item.ToString());
            Debug.WriteLine(item.GetType());

            if(Enum.IsDefined(typeof(Tag_Type), item))
            {
                panelIO.Visibility= Visibility.Visible;
                panelAlarm.Visibility = Visibility.Collapsed;
            } else if (string.Compare("Alarm", item.ToString()) == 0) { 
                panelAlarm.Visibility= Visibility.Visible;
                panelIO.Visibility= Visibility.Collapsed;
            }
            else
            {
                panelIO.Visibility= Visibility.Collapsed;
                panelAlarm.Visibility = Visibility.Collapsed;
            }
        }

        public void Button_Create(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.DialogResultType = Dropdown.SelectedItem.ToString();

        }

        public void Button_Cancel(object sender, RoutedEventArgs e)
        {
            this.DialogResult= false;
            this.Close();
        }
    }
}
