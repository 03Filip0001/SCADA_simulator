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
        public string DialogResultName { get; set; }
        public string DialogResultAddress { get; set; }
        public string DialogResultDescription { get; set; }
        public double DialogResultLowLimit { get; set; }
        public double DialogResultHighLimit { get; set; }
        public string DialogResultAlarmMessage { get; set; }
        public bool DialogResultHasAlarmSettings { get; set; }
        public IAnalogInput DialogResultAlarmTarget { get; set; }

        // public IAlarm DialogResultAlarm {get; set;}
        public AddWindow()
            : this(Enumerable.Empty<ITag>())
        {
        }

        public AddWindow(IEnumerable<ITag> existingTags)
        {
            InitializeComponent();

            var dropdownOptions = new List<string> { "None", "AI", "AO", "DI", "DO", "Alarm" };

            Dropdown.ItemsSource = dropdownOptions;
            Dropdown.SelectedIndex = 0;

            var alarmTargets = existingTags.OfType<IAnalogInput>().ToList();
            AlarmTargetDropdown.ItemsSource = alarmTargets;
            if (alarmTargets.Any())
            {
                AlarmTargetDropdown.SelectedIndex = 0;
            }

            panelAlarm.Visibility = Visibility.Collapsed;
            panelIO.Visibility = Visibility.Collapsed;

        }

        public void Dropdown_Changed(object sender, RoutedEventArgs e)
        {
            ComboBox dropdown = this.FindName("Dropdown") as ComboBox;
            StackPanel panelIO = this.FindName("panelIO") as StackPanel;
            StackPanel panelAlarm = this.FindName("panelAlarm") as StackPanel;
            
            if (dropdown?.SelectedItem == null) return;
            
            var item = dropdown.SelectedItem;

            Debug.WriteLine(item.ToString());
            Debug.WriteLine(item.GetType());

            if (item.ToString() == "Alarm")
            {
                if (panelIO != null) panelIO.Visibility = Visibility.Collapsed;
                if (panelAlarm != null) panelAlarm.Visibility = Visibility.Visible;
            }
            else if (Enum.IsDefined(typeof(Tag_Type), item))
            {
                if (panelIO != null) panelIO.Visibility = Visibility.Visible;
                if (panelAlarm != null) panelAlarm.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (panelIO != null) panelIO.Visibility = Visibility.Collapsed;
                if (panelAlarm != null) panelAlarm.Visibility = Visibility.Collapsed;
            }
        }

        public void Button_Create(object sender, RoutedEventArgs e)
        {
            this.DialogResultType = Dropdown?.SelectedItem?.ToString() ?? "AI";

            TextBox textboxName = this.FindName("TextBoxName") as TextBox;
            TextBox textboxAddress = this.FindName("TextBoxAddress") as TextBox;
            TextBox textboxDescription = this.FindName("TextBoxDescription") as TextBox;
            TextBox textboxLowLimit = this.FindName("TextBoxLowLimit") as TextBox;
            TextBox textboxHighLimit = this.FindName("TextBoxHighLimit") as TextBox;
            TextBox textboxAlarmMessage = this.FindName("TextBoxAlarmMessage") as TextBox;

            this.DialogResultName = textboxName?.Text ?? "";
            this.DialogResultAddress = textboxAddress?.Text ?? "";
            this.DialogResultDescription = textboxDescription?.Text ?? "";
            this.DialogResultAlarmMessage = textboxAlarmMessage?.Text ?? string.Empty;
            this.DialogResultAlarmTarget = AlarmTargetDropdown?.SelectedItem as IAnalogInput;
            this.DialogResultHasAlarmSettings = this.DialogResultType == "Alarm";

            if (this.DialogResultType == "Alarm" && this.DialogResultAlarmTarget == null)
            {
                MessageBox.Show("Select an existing analog input tag for the alarm.", "Add Alarm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (double.TryParse(textboxLowLimit?.Text, out var lowLimit))
            {
                this.DialogResultLowLimit = lowLimit;
            }
            else
            {
                this.DialogResultLowLimit = 0;
            }

            if (double.TryParse(textboxHighLimit?.Text, out var highLimit))
            {
                this.DialogResultHighLimit = highLimit;
            }
            else
            {
                this.DialogResultHighLimit = 100;
            }

            this.DialogResult = true;
        }

        public void Button_Cancel(object sender, RoutedEventArgs e)
        {
            this.DialogResult= false;
            this.Close();
        }
    }
}
