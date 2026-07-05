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

        // public IAlarm DialogResultAlarm {get; set;}
        public AddWindow()
        {
            InitializeComponent();

            var dropdownOptions = Enum.GetValues(typeof(Tag_Type))
                .Cast<Tag_Type>()
                .Select(t => t.ToString())
                .ToList();

            Dropdown.ItemsSource = dropdownOptions;
            Dropdown.SelectedIndex = 0;

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

            if (Enum.IsDefined(typeof(Tag_Type), item))
            {
                if (panelIO != null) panelIO.Visibility = Visibility.Visible;
                if (panelAlarm != null) panelAlarm.Visibility = Visibility.Collapsed;

                var alarmGroup = this.FindName("AlarmSettingsGroup") as UIElement;
                if (item.ToString() == Tag_Type.AI.ToString())
                {
                    if (alarmGroup != null) alarmGroup.Visibility = Visibility.Visible;
                }
                else
                {
                    if (alarmGroup != null) alarmGroup.Visibility = Visibility.Collapsed;
                }
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
            UIElement alarmGroup = this.FindName("AlarmSettingsGroup") as UIElement;

            this.DialogResultName = textboxName?.Text ?? "";
            this.DialogResultAddress = textboxAddress?.Text ?? "";
            this.DialogResultDescription = textboxDescription?.Text ?? "";
            this.DialogResultAlarmMessage = textboxAlarmMessage?.Text ?? string.Empty;
            this.DialogResultHasAlarmSettings = alarmGroup?.Visibility == Visibility.Visible;

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
