using Contracts;
using DataConcentrator;
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
        // The fixed set of I/O addresses the PLC Simulator exposes (ADDR001-ADDR016;
        // see PLCSimulatorManager). Kept here rather than modifying that project.
        private static readonly HashSet<string> ValidPlcAddresses = new HashSet<string>(StringComparer.Ordinal)
        {
            "ADDR001", "ADDR002", "ADDR003", "ADDR004",
            "ADDR005", "ADDR006", "ADDR007", "ADDR008",
            "ADDR009", "ADDR010", "ADDR011", "ADDR012", "ADDR013",
            "ADDR014", "ADDR015", "ADDR016"
        };

        private readonly List<ITag> existingTags;
        private readonly ITag tagToEdit;

        public string DialogResultType { get; set; }
        public string DialogResultName { get; set; }
        public string DialogResultAddress { get; set; }
        public string DialogResultDescription { get; set; }
        public double DialogResultLowLimit { get; set; }
        public double DialogResultHighLimit { get; set; }
        public string DialogResultAlarmName { get; set; }
        public string DialogResultAlarmType { get; set; }
        public int DialogResultAlarmPriority { get; set; }
        public string DialogResultAlarmMessage { get; set; }
        public bool DialogResultHasAlarmSettings { get; set; }
        public IAnalogInput DialogResultAlarmTarget { get; set; }

        // public IAlarm DialogResultAlarm {get; set;}
        public AddWindow()
            : this(Enumerable.Empty<ITag>())
        {
        }

        public AddWindow(IEnumerable<ITag> existingTags)
            : this(existingTags, null)
        {
        }

        public AddWindow(IEnumerable<ITag> existingTags, ITag tagToEdit)
        {
            InitializeComponent();

            this.existingTags = existingTags?.ToList() ?? new List<ITag>();
            this.tagToEdit = tagToEdit;

            var dropdownOptions = new List<string> { "None", "AI", "AO", "DI", "DO", "Alarm" };

            Dropdown.ItemsSource = dropdownOptions;
            Dropdown.SelectedIndex = 0;

            var alarmTargets = this.existingTags.OfType<IAnalogInput>().ToList();
            AlarmTargetDropdown.ItemsSource = alarmTargets;
            if (alarmTargets.Any())
            {
                AlarmTargetDropdown.SelectedIndex = 0;
            }

            panelAlarm.Visibility = Visibility.Collapsed;
            panelIO.Visibility = Visibility.Collapsed;

            if (tagToEdit != null)
            {
                Title = "Update Tag";
                CreateButton.Content = "Update";
                Dropdown.SelectedItem = tagToEdit.Type.ToString();
                Dropdown.IsEnabled = false;
                TextBoxName.Text = tagToEdit.Name;
                TextBoxAddress.Text = tagToEdit.Address;
                TextBoxDescription.Text = tagToEdit.Description;
                panelIO.Visibility = Visibility.Visible;

                if (tagToEdit is DataConcentrator.Model.AnalogInput analogInput && analogInput.AlarmEnabled)
                {
                    panelAlarm.Visibility = Visibility.Visible;
                    AlarmTargetDropdown.SelectedItem = tagToEdit;
                    AlarmTargetDropdown.IsEnabled = false;
                    TextBoxAlarmName.Text = analogInput.AlarmName;
                    TextBoxAlarmType.Text = analogInput.AlarmType;
                    TextBoxAlarmPriority.Text = analogInput.AlarmPriority.ToString();
                    TextBoxLowLimit.Text = analogInput.LowLimit.ToString();
                    TextBoxHighLimit.Text = analogInput.HighLimit.ToString();
                    TextBoxAlarmMessage.Text = analogInput.AlarmMessage;
                }
            }

            UpdateCreateButtonState();
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

            UpdateCreateButtonState();
        }

        private void UpdateCreateButtonState()
        {
            if (CreateButton != null)
            {
                CreateButton.IsEnabled = Dropdown?.SelectedItem?.ToString() != "None";
            }
        }

        private void LogValidationFailure(string reason)
        {
            SystemLogger.LogError($"Failed to {(tagToEdit != null ? "update" : "create")} tag: {reason}");
        }

        public void Button_Create(object sender, RoutedEventArgs e)
        {
            this.DialogResultType = Dropdown?.SelectedItem?.ToString() ?? "AI";

            TextBox textboxName = this.FindName("TextBoxName") as TextBox;
            TextBox textboxAddress = this.FindName("TextBoxAddress") as TextBox;
            TextBox textboxDescription = this.FindName("TextBoxDescription") as TextBox;
            TextBox textboxAlarmName = this.FindName("TextBoxAlarmName") as TextBox;
            TextBox textboxAlarmType = this.FindName("TextBoxAlarmType") as TextBox;
            TextBox textboxAlarmPriority = this.FindName("TextBoxAlarmPriority") as TextBox;
            TextBox textboxLowLimit = this.FindName("TextBoxLowLimit") as TextBox;
            TextBox textboxHighLimit = this.FindName("TextBoxHighLimit") as TextBox;
            TextBox textboxAlarmMessage = this.FindName("TextBoxAlarmMessage") as TextBox;
            double lowLimit = 0;
            double highLimit = 0;
            int alarmPriority = 0;

            this.DialogResultName = textboxName?.Text?.Trim() ?? "";
            this.DialogResultAddress = textboxAddress?.Text?.Trim() ?? "";
            this.DialogResultDescription = textboxDescription?.Text ?? "";
            this.DialogResultAlarmName = textboxAlarmName?.Text?.Trim() ?? string.Empty;
            this.DialogResultAlarmType = textboxAlarmType?.Text?.Trim() ?? string.Empty;
            this.DialogResultAlarmMessage = textboxAlarmMessage?.Text ?? string.Empty;
            this.DialogResultAlarmTarget = AlarmTargetDropdown?.SelectedItem as IAnalogInput;
            this.DialogResultHasAlarmSettings = this.DialogResultType == "Alarm"
                || (tagToEdit is DataConcentrator.Model.AnalogInput editedAnalogInput && editedAnalogInput.AlarmEnabled);

            if (this.DialogResultType == "None")
            {
                this.DialogResult = true;
                return;
            }

            if (this.DialogResultHasAlarmSettings && this.DialogResultAlarmTarget == null)
            {
                LogValidationFailure("no analog input tag selected for the alarm");
                MessageBox.Show("Select an existing analog input tag for the alarm.", "Add Alarm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (this.DialogResultType != "Alarm")
            {
                if (string.IsNullOrWhiteSpace(this.DialogResultName))
                {
                    LogValidationFailure("tag name is required");
                    MessageBox.Show("Tag name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(this.DialogResultAddress))
                {
                    LogValidationFailure("tag address is required");
                    MessageBox.Show("Tag address is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!ValidPlcAddresses.Contains(this.DialogResultAddress))
                {
                    LogValidationFailure($"'{this.DialogResultAddress}' is not a valid PLC Simulator address");
                    MessageBox.Show($"'{this.DialogResultAddress}' is not a valid PLC Simulator address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool duplicateName = existingTags.Any(tag =>
                    !ReferenceEquals(tag, tagToEdit)
                    && string.Equals(tag.Name, this.DialogResultName, StringComparison.OrdinalIgnoreCase));

                if (duplicateName)
                {
                    LogValidationFailure($"duplicate tag name '{this.DialogResultName}'");
                    MessageBox.Show("A tag with this name already exists.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (this.DialogResultHasAlarmSettings && string.IsNullOrWhiteSpace(this.DialogResultAlarmName))
            {
                LogValidationFailure("alarm name is required");
                MessageBox.Show("Alarm name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (this.DialogResultHasAlarmSettings && string.IsNullOrWhiteSpace(this.DialogResultAlarmType))
            {
                LogValidationFailure("alarm type is required");
                MessageBox.Show("Alarm type is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (this.DialogResultHasAlarmSettings && !int.TryParse(textboxAlarmPriority?.Text, out alarmPriority))
            {
                LogValidationFailure("alarm priority must be a valid whole number");
                MessageBox.Show("Alarm priority must be a valid whole number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (this.DialogResultHasAlarmSettings && alarmPriority < 0)
            {
                LogValidationFailure("alarm priority cannot be negative");
                MessageBox.Show("Alarm priority cannot be negative.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (this.DialogResultHasAlarmSettings && !double.TryParse(textboxLowLimit?.Text, out lowLimit))
            {
                LogValidationFailure("alarm low limit must be a valid number");
                MessageBox.Show("Alarm low limit must be a valid number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (this.DialogResultHasAlarmSettings && !double.TryParse(textboxHighLimit?.Text, out highLimit))
            {
                LogValidationFailure("alarm high limit must be a valid number");
                MessageBox.Show("Alarm high limit must be a valid number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (this.DialogResultHasAlarmSettings && highLimit <= lowLimit)
            {
                LogValidationFailure("alarm high limit must be greater than the low limit");
                MessageBox.Show("Alarm high limit must be greater than the low limit.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (this.DialogResultHasAlarmSettings)
            {
                this.DialogResultLowLimit = lowLimit;
                this.DialogResultHighLimit = highLimit;
                this.DialogResultAlarmPriority = alarmPriority;
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
