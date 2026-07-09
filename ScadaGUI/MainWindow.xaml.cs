using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Contracts;
using DataConcentrator;
using DataConcentrator.Model;
using DataConcentrator.Persistence;
using PLCSimulator;

namespace ScadaGUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private ITag selectedTag;
        private AlarmInfo selectedAlarm;
        private string searchText;
        private string selectedTagTypeFilter;

        private readonly DataConcentrator.PLC _plc;
        private readonly DispatcherTimer alarmRefreshTimer;
        private DateTime lastRuntimePersist = DateTime.MinValue;
        private bool persistenceWarningShown;
        private bool suppressAlarmAudioEvents;

        public IAnalogInput TestanalogInput { get; set; }
        public ITag tag { get; set; }
        public ITagBuilder tagBuilder { get; set; }
        public ObservableCollection<ITag> IOElements { get; set; }
        public ICollectionView FilteredIOElements { get; set; }
        public ObservableCollection<AlarmInfo> ActiveAlarms { get; set; }
        public List<string> TagFilterOptions { get; set; }

        public ITag SelectedTag
        {
            get => selectedTag;
            set
            {
                if (selectedTag == value) return;
                selectedTag = value;
                OnPropertyChanged(nameof(SelectedTag));
            }
        }

        public AlarmInfo SelectedAlarm
        {
            get => selectedAlarm;
            set
            {
                if (selectedAlarm == value) return;
                selectedAlarm = value;
                OnPropertyChanged(nameof(SelectedAlarm));
            }
        }

        public string SearchText
        {
            get => searchText;
            set
            {
                if (searchText == value) return;
                searchText = value;
                OnPropertyChanged(nameof(SearchText));
                FilteredIOElements?.Refresh();
            }
        }

        public string SelectedTagTypeFilter
        {
            get => selectedTagTypeFilter;
            set
            {
                if (selectedTagTypeFilter == value) return;
                selectedTagTypeFilter = value;
                OnPropertyChanged(nameof(SelectedTagTypeFilter));
                FilteredIOElements?.Refresh();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow(ITagBuilder builder)
        {
            PersistenceService.Initialize(out _);
            ThemeManager.Initialize();
            InitializeComponent();
            UpdateThemeToggleButtonContent();
            SystemLogger.Log("Application startup.");

            IOElements = new ObservableCollection<ITag>();
            ActiveAlarms = new ObservableCollection<AlarmInfo>();
            TagFilterOptions = new List<string> { "All", "AI", "AO", "DI", "DO" };
            SelectedTagTypeFilter = "All";

            tagBuilder = builder;
            _plc = new DataConcentrator.PLC(new PLCSimulatorManager());

            LoadPersistedTagsOrDefaults(builder);

            FilteredIOElements = CollectionViewSource.GetDefaultView(IOElements);
            FilteredIOElements.Filter = FilterTag;

            SelectedTag = IOElements.FirstOrDefault();

            _plc.AlarmRaised += OnAlarmRaised;
            _plc.AlarmCleared += OnAlarmCleared;
            foreach (var element in IOElements)
            {
                RegisterInputIfNeeded(element);
                PushRestoredOutputValue(element);
            }

            alarmRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            alarmRefreshTimer.Tick += (sender, args) => SyncActiveAlarmsFromTags();
            alarmRefreshTimer.Start();
            SyncActiveAlarmsFromTags();

            DataContext = this;
            UpdateSelectedTagPanels();
            InitializeAlarmAudio();
        }

        // F1: loads the persisted alarm sound + volume, applies them to the
        // sound service and reflects them in the picker/slider.
        private void InitializeAlarmAudio()
        {
            AlarmSoundCombo.ItemsSource = AlarmSoundService.AvailableSounds;

            string savedSound = PersistenceService.LoadSetting("AlarmSound", out _);
            if (string.IsNullOrWhiteSpace(savedSound) || !AlarmSoundService.AvailableSounds.Contains(savedSound))
            {
                savedSound = AlarmSoundService.AvailableSounds[0];
            }

            double savedVolume = 80;
            string savedVolumeText = PersistenceService.LoadSetting("AlarmVolume", out _);
            if (!string.IsNullOrWhiteSpace(savedVolumeText)
                && double.TryParse(savedVolumeText, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVolume))
            {
                savedVolume = parsedVolume;
            }

            AlarmSoundService.Configure(savedSound, savedVolume);

            suppressAlarmAudioEvents = true;
            AlarmSoundCombo.SelectedItem = savedSound;
            AlarmVolumeSlider.Value = savedVolume;
            AlarmVolumeLabel.Text = $"{(int)savedVolume}%";
            suppressAlarmAudioEvents = false;
        }

        private void AlarmSound_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (suppressAlarmAudioEvents)
            {
                return;
            }

            if (AlarmSoundCombo.SelectedItem is string sound)
            {
                AlarmSoundService.SetSound(sound);
                PersistenceService.SaveSetting("AlarmSound", sound, out _);
                SystemLogger.Log($"Alarm sound set to: {sound}");
            }
        }

        private void AlarmVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AlarmVolumeLabel != null)
            {
                AlarmVolumeLabel.Text = $"{(int)e.NewValue}%";
            }

            if (suppressAlarmAudioEvents)
            {
                return;
            }

            AlarmSoundService.SetVolume(e.NewValue);
            PersistenceService.SaveSetting("AlarmVolume", ((int)e.NewValue).ToString(CultureInfo.InvariantCulture), out _);
        }

        // On startup output tags restore their last written value; push it back
        // to the PLC Simulator so the simulated address matches (CP-3/CP-4).
        private void PushRestoredOutputValue(ITag tag)
        {
            if (tag is IAnalogOutput analogOutput)
            {
                _plc.WriteOutput(tag, analogOutput.CurrentValue, out _);
            }
            else if (tag is IDigitalOutput digitalOutput)
            {
                _plc.WriteOutput(tag, digitalOutput.CurrentValue, out _);
            }
        }

        private void LoadPersistedTagsOrDefaults(ITagBuilder builder)
        {
            if (!PersistenceService.Initialize(out string initializeError))
            {
                ShowPersistenceWarning(initializeError);
            }

            var persistedTags = PersistenceService.LoadTags(builder, out string loadError);
            if (!string.IsNullOrWhiteSpace(loadError))
            {
                ShowPersistenceWarning(loadError);
            }

            foreach (var persistedTag in persistedTags)
            {
                IOElements.Add(persistedTag);
            }

            if (IOElements.Any())
            {
                return;
            }
        }

        private bool FilterTag(object obj)
        {
            if (!(obj is ITag tag))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var lower = SearchText.ToLowerInvariant();
                if (!(tag.Name?.ToLowerInvariant().Contains(lower) == true
                      || tag.Address?.ToLowerInvariant().Contains(lower) == true
                      || tag.Description?.ToLowerInvariant().Contains(lower) == true))
                {
                    return false;
                }
            }

            if (SelectedTagTypeFilter == "All")
            {
                return true;
            }

            return tag.Type.ToString() == SelectedTagTypeFilter;
        }

        private void Button_AddTag(object sender, RoutedEventArgs e)
        {
            AddWindow addwindow = new AddWindow(IOElements);
            addwindow.ShowDialog();

            if (addwindow.DialogResult.GetValueOrDefault())
            {
                try
                {
                    CreateFromDialog(addwindow);
                }
                catch (Exception ex)
                {
                    SystemLogger.LogError("Tag creation failed.", ex);
                    MessageBox.Show($"Unable to create tag: {ex.Message}", "Create Tag", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Button_UpdateTag(object sender, RoutedEventArgs e)
        {
            if (SelectedTag == null)
            {
                MessageBox.Show("Select a tag to update.", "Update Tag", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AddWindow addwindow = new AddWindow(IOElements, SelectedTag);
            addwindow.ShowDialog();

            if (addwindow.DialogResult.GetValueOrDefault())
            {
                try
                {
                    UpdateFromDialog(SelectedTag, addwindow);
                }
                catch (Exception ex)
                {
                    SystemLogger.LogError("Tag update failed.", ex);
                    MessageBox.Show($"Unable to update tag: {ex.Message}", "Update Tag", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Button_ViewTag(object sender, RoutedEventArgs e)
        {
            if (SelectedTag == null)
            {
                MessageBox.Show("Select a tag to view.", "View Tag", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string details = $"Name: {SelectedTag.Name}\nAddress: {SelectedTag.Address}\nType: {SelectedTag.Type}\nDescription: {SelectedTag.Description}";
            if (SelectedTag is AnalogInput analogInput)
            {
                details += $"\nAssigned alarm: {(analogInput.AlarmEnabled ? "Yes" : "No")}";
                if (analogInput.AlarmEnabled)
                {
                    details += $"\nAlarm name: {analogInput.AlarmName}\nAlarm type: {analogInput.AlarmType}\nPriority: {analogInput.AlarmPriority}\nAlarm active: {analogInput.AlarmActive}\nLow limit: {analogInput.LowLimit}\nHigh limit: {analogInput.HighLimit}";
                }
            }

            MessageBox.Show(details, "Tag Details", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // CP-9: lists the alarms attached to the selected analog input with
        // their current state.
        private void Button_ShowDetails(object sender, RoutedEventArgs e)
        {
            if (!(SelectedTag is AnalogInput analogInput))
            {
                MessageBox.Show("Select an analog input tag to view its alarms.", "Alarm Details", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!analogInput.AlarmEnabled)
            {
                MessageBox.Show($"'{analogInput.Name}' has no alarms configured.", "Alarm Details", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string state = analogInput.AlarmActive
                ? (analogInput.AlarmAcknowledged ? "Acknowledged" : "Active")
                : (analogInput.AlarmHasOccurred ? "Unacknowledged (returned to normal)" : "Inactive");

            string details =
                $"Alarms for {analogInput.Name}:\n\n" +
                $"- {analogInput.AlarmName}\n" +
                $"   Type: {analogInput.AlarmType}\n" +
                $"   Low limit: {analogInput.LowLimit}\n" +
                $"   High limit: {analogInput.HighLimit}\n" +
                $"   Priority: {analogInput.AlarmPriority}\n" +
                $"   State: {state}\n" +
                $"   Message: {analogInput.AlarmMessage}";

            MessageBox.Show(details, "Alarm Details", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Button_DeleteTag(object sender, RoutedEventArgs e)
        {
            if (SelectedTag == null)
            {
                MessageBox.Show("Select a tag to delete.", "Delete Tag", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Delete tag '{SelectedTag.Name}'?", "Delete Tag", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                DeleteTag(SelectedTag);
            }
            catch (Exception ex)
            {
                SystemLogger.LogError("Tag deletion failed.", ex);
                MessageBox.Show($"Unable to delete tag: {ex.Message}", "Delete Tag", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Button_DeleteAlarm(object sender, RoutedEventArgs e)
        {
            var selectedAlarms = ActiveAlarmsGrid.SelectedItems.OfType<AlarmInfo>().ToList();

            if (selectedAlarms.Any())
            {
                foreach (var alarm in selectedAlarms)
                {
                    var alarmTag = IOElements.OfType<AnalogInput>().FirstOrDefault(tag => tag.Name == alarm.TagName);
                    if (alarmTag != null && alarmTag.AlarmEnabled)
                    {
                        DeleteAlarmFor(alarmTag);
                    }
                }

                SyncActiveAlarmsFromTags();
                OnPropertyChanged(nameof(SelectedTag));
                return;
            }

            if (!(SelectedTag is AnalogInput analogInput))
            {
                MessageBox.Show("Select an analog input tag or an alarm to delete its alarm.", "Delete Alarm", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!analogInput.AlarmEnabled)
            {
                MessageBox.Show("The selected analog input does not have an alarm configured.", "Delete Alarm", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DeleteAlarmFor(analogInput);
            SyncActiveAlarmsFromTags();
            OnPropertyChanged(nameof(SelectedTag));
        }

        private void DeleteAlarmFor(AnalogInput analogInput)
        {
            analogInput.ClearAlarm();
            RemoveActiveAlarmsForTag(analogInput.Name);
            DeleteAlarmConfiguration(analogInput.Name);
            SystemLogger.Log($"Alarm deleted for tag: {analogInput.Name}");
        }

        private void CreateFromDialog(AddWindow addwindow)
        {
            string type = addwindow.DialogResultType;

            if (type == "None")
            {
                return;
            }

            if (type == "Alarm")
            {
                if (addwindow.DialogResultAlarmTarget is AnalogInput alarmTarget)
                {
                    alarmTarget.ConfigureAlarm(
                        addwindow.DialogResultLowLimit,
                        addwindow.DialogResultHighLimit,
                        addwindow.DialogResultAlarmMessage,
                        addwindow.DialogResultAlarmName,
                        addwindow.DialogResultAlarmType,
                        addwindow.DialogResultAlarmPriority);
                    SaveAlarmConfiguration(alarmTarget);
                    SystemLogger.Log($"Alarm created for tag: {alarmTarget.Name}");
                    SyncActiveAlarmsFromTags();
                }

                return;
            }

            if (!Enum.TryParse(type, out Tag_Type tagType))
            {
                SystemLogger.LogError($"Failed to create tag: invalid tag type '{type}'.");
                MessageBox.Show("Select a valid tag type.", "Create Tag", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ITag newTag = CreateTagByType(tagType, addwindow.DialogResultAddress);
            if (newTag == null)
            {
                return;
            }

            ApplyTagValues(newTag, addwindow.DialogResultName, addwindow.DialogResultAddress, addwindow.DialogResultDescription, addwindow.DialogResultUnits, tagType);
            ApplyExtendedTagValues(newTag, addwindow);
            IOElements.Add(newTag);
            RegisterInputIfNeeded(newTag);

            // Output tags push their initial value to the PLC on creation.
            if (newTag is IOutputCommon outputCommon)
            {
                WriteOutputValue(newTag, outputCommon.InitialValue, persist: false);
            }

            SaveTag(newTag);
            SystemLogger.Log($"Tag created: {newTag.Name}");
            FilteredIOElements.Refresh();
            SelectedTag = newTag;
        }

        private void UpdateFromDialog(ITag tagToUpdate, AddWindow addwindow)
        {
            string oldName = tagToUpdate.Name;
            bool isInput = tagToUpdate.Type == Tag_Type.AI || tagToUpdate.Type == Tag_Type.DI;

            if (isInput)
            {
                _plc.RemoveInput(tagToUpdate);
            }

            ApplyTagValues(tagToUpdate, addwindow.DialogResultName, addwindow.DialogResultAddress, addwindow.DialogResultDescription, addwindow.DialogResultUnits, tagToUpdate.Type);
            ApplyExtendedTagValues(tagToUpdate, addwindow);

            if (addwindow.DialogResultHasAlarmSettings && tagToUpdate is AnalogInput alarmTarget)
            {
                alarmTarget.ConfigureAlarm(
                    addwindow.DialogResultLowLimit,
                    addwindow.DialogResultHighLimit,
                    addwindow.DialogResultAlarmMessage,
                    addwindow.DialogResultAlarmName,
                    addwindow.DialogResultAlarmType,
                    addwindow.DialogResultAlarmPriority);
                SystemLogger.Log($"Alarm updated for tag: {alarmTarget.Name}");
            }

            if (isInput)
            {
                _plc.AddInput(tagToUpdate);
            }

            SaveTag(tagToUpdate, oldName);
            SystemLogger.Log($"Tag updated: {oldName} -> {tagToUpdate.Name}");

            if (!string.Equals(oldName, tagToUpdate.Name, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var alarm in ActiveAlarms.Where(a => a.TagName == oldName))
                {
                    alarm.TagName = tagToUpdate.Name;
                }
            }

            FilteredIOElements.Refresh();
            SyncActiveAlarmsFromTags();
            OnPropertyChanged(nameof(SelectedTag));
        }

        private ITag CreateTagByType(Tag_Type tagType, string address)
        {
            switch (tagType)
            {
                case Tag_Type.AI:
                    return tagBuilder.CreateAnalogInput(address);
                case Tag_Type.AO:
                    return tagBuilder.CreateAnalogOutput(address);
                case Tag_Type.DI:
                    return tagBuilder.CreateDigitalInput(address);
                case Tag_Type.DO:
                    return tagBuilder.CreateDigitalOutput(address);
                default:
                    return null;
            }
        }

        private void ApplyTagValues(ITag tag, string name, string address, string description, string units, Tag_Type tagType)
        {
            tag.Name = name;
            tag.Address = address;
            tag.Description = description ?? string.Empty;
            tag.Type = tagType;

            if (tag is IAnalogCommon analogCommon)
            {
                analogCommon.Units = string.IsNullOrWhiteSpace(units) ? analogCommon.Units : units;
            }
        }

        // Applies the per-type fields captured by AddWindow (CP-2): scan
        // settings for inputs, limits for analog tags, deadband/hysteresis for
        // AI, initial value for outputs.
        private void ApplyExtendedTagValues(ITag tag, AddWindow addwindow)
        {
            if (tag is IInputCommon input && (tag.Type == Tag_Type.AI || tag.Type == Tag_Type.DI))
            {
                input.ScanTime = addwindow.DialogResultScanTime;
                input.ScanOn = addwindow.DialogResultScanOn;
            }

            if (addwindow.DialogResultHasLimits && tag is IAnalogCommon analogCommon)
            {
                analogCommon.LowLimit = addwindow.DialogResultIoLowLimit;
                analogCommon.HighLimit = addwindow.DialogResultIoHighLimit;
            }

            if (tag is AnalogInput analogInput)
            {
                analogInput.Deadband = addwindow.DialogResultDeadband;
                analogInput.Hysteresis = addwindow.DialogResultHysteresis;
            }

            if (tag is IOutputCommon output && (tag.Type == Tag_Type.AO || tag.Type == Tag_Type.DO))
            {
                output.InitialValue = addwindow.DialogResultInitialValue;
            }
        }

        // Writes a value to an output tag through the PLC (never touches the
        // simulator directly), reflects it in the grid, persists it and logs it.
        private void WriteOutputValue(ITag tag, double value, bool persist = true)
        {
            if (!_plc.WriteOutput(tag, value, out string error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MessageBox.Show(error, "Write Output", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return;
            }

            SystemLogger.Log($"Value written to output '{tag.Name}': {value}");

            if (persist)
            {
                SaveTag(tag);
            }

            UpdateSelectedTagPanels();
        }

        private void RegisterInputIfNeeded(ITag tag)
        {
            if (tag.Type == Tag_Type.AI || tag.Type == Tag_Type.DI)
            {
                _plc.AddInput(tag);
            }
        }

        private void DeleteTag(ITag tagToDelete)
        {
            if (tagToDelete.Type == Tag_Type.AI || tagToDelete.Type == Tag_Type.DI)
            {
                _plc.RemoveInput(tagToDelete);
            }

            IOElements.Remove(tagToDelete);
            RemoveActiveAlarmsForTag(tagToDelete.Name);
            DeletePersistedTag(tagToDelete.Name);
            SystemLogger.Log($"Tag deleted: {tagToDelete.Name}");
            FilteredIOElements.Refresh();
            SelectedTag = IOElements.FirstOrDefault();
            SyncActiveAlarmsFromTags();
        }

        private void RemoveActiveAlarmsForTag(string tagName)
        {
            var alarmsToRemove = ActiveAlarms.Where(alarm => alarm.TagName == tagName).ToList();
            foreach (var alarm in alarmsToRemove)
            {
                ActiveAlarms.Remove(alarm);
            }

            if (SelectedAlarm != null && SelectedAlarm.TagName == tagName)
            {
                SelectedAlarm = null;
            }

            UpdateAcknowledgeButtonState();
        }

        private void Button_ToggleTheme(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
            UpdateThemeToggleButtonContent();
        }

        private void UpdateThemeToggleButtonContent()
        {
            if (ThemeToggleButton != null)
            {
                ThemeToggleButton.Content = ThemeManager.CurrentTheme == AppTheme.Dark ? "Switch to Light" : "Switch to Dark";
            }
        }

        // CP-10: writes a .txt report of every AI history record whose value was
        // within (HighLimit + LowLimit)/2 +/- 5, grouped per tag.
        private void Button_GenerateReport(object sender, RoutedEventArgs e)
        {
            try
            {
                var analogInputs = IOElements.OfType<AnalogInput>().ToList();
                var builder = new System.Text.StringBuilder();
                builder.AppendLine("SCADA Analog Input Report");
                builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                builder.AppendLine();

                int totalMatches = 0;

                foreach (var analogInput in analogInputs)
                {
                    double midpoint = (analogInput.HighLimit + analogInput.LowLimit) / 2.0;
                    double lowerBound = midpoint - 5;
                    double upperBound = midpoint + 5;

                    var history = PersistenceService.GetHistory(analogInput.Name, out string historyError);
                    if (!string.IsNullOrWhiteSpace(historyError))
                    {
                        ShowPersistenceWarning(historyError);
                    }

                    var matches = history
                        .Where(record => record.Value >= lowerBound && record.Value <= upperBound)
                        .ToList();

                    builder.AppendLine($"Tag: {analogInput.Name} (Address {analogInput.Address}) - window {lowerBound:F2}..{upperBound:F2} [midpoint {midpoint:F2}]");
                    if (matches.Count == 0)
                    {
                        builder.AppendLine("  (no matching records)");
                    }
                    else
                    {
                        foreach (var record in matches)
                        {
                            builder.AppendLine($"  {record.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {record.Value:F2}");
                        }
                    }

                    builder.AppendLine();
                    totalMatches += matches.Count;
                }

                string fileName = $"Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                System.IO.File.WriteAllText(path, builder.ToString());

                SystemLogger.Log($"Report generated: {path} ({totalMatches} matching records).");
                MessageBox.Show(
                    $"Report saved to:\n{path}\n\n{totalMatches} matching record(s).",
                    "Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SystemLogger.LogError("Report generation failed.", ex);
                MessageBox.Show($"Unable to generate report: {ex.Message}", "Report", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Button_ShowHistory(object sender, RoutedEventArgs e)
        {
            if (SelectedTag == null)
            {
                MessageBox.Show("Select a tag first to open its history.", "History", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var historyWindow = new HistoryWindow(SelectedTag);
            historyWindow.Owner = this;
            historyWindow.ShowDialog();
        }

        private void TagsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateSelectedTagPanels();
        }

        // Shows the write control for the selected output tag and the scan
        // toggle for the selected input tag (CP-4, CP-5); hides them otherwise.
        private void UpdateSelectedTagPanels()
        {
            if (WritePanelAnalog == null)
            {
                return;
            }

            var tag = SelectedTag;

            WritePanelAnalog.Visibility = tag is IAnalogOutput ? Visibility.Visible : Visibility.Collapsed;
            WritePanelDigital.Visibility = tag is IDigitalOutput ? Visibility.Visible : Visibility.Collapsed;

            bool isInput = tag != null && (tag.Type == Tag_Type.AI || tag.Type == Tag_Type.DI);
            ScanTogglePanel.Visibility = isInput ? Visibility.Visible : Visibility.Collapsed;

            if (tag is IAnalogOutput analogOutput)
            {
                WriteValueBox.Text = analogOutput.CurrentValue.ToString();
            }

            if (tag is IDigitalOutput digitalOutput)
            {
                DigitalStateCheck.IsChecked = digitalOutput.CurrentValue != 0;
            }

            if (tag is IInputCommon input)
            {
                ScanOnCheck.IsChecked = input.ScanOn;
            }
        }

        private void Button_WriteAnalog(object sender, RoutedEventArgs e)
        {
            if (!(SelectedTag is IAnalogOutput analogOutput))
            {
                return;
            }

            if (!double.TryParse(WriteValueBox.Text, out double value))
            {
                MessageBox.Show("Enter a valid number to write.", "Write Output", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (value < analogOutput.LowLimit || value > analogOutput.HighLimit)
            {
                MessageBox.Show(
                    $"Value must be within the limits [{analogOutput.LowLimit} .. {analogOutput.HighLimit}].",
                    "Write Output",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            WriteOutputValue(SelectedTag, value);
        }

        private void CheckBox_WriteDigital(object sender, RoutedEventArgs e)
        {
            if (!(SelectedTag is IDigitalOutput))
            {
                return;
            }

            double value = DigitalStateCheck.IsChecked == true ? 1 : 0;
            WriteOutputValue(SelectedTag, value);
        }

        private void CheckBox_ToggleScan(object sender, RoutedEventArgs e)
        {
            if (!(SelectedTag is IInputCommon input))
            {
                return;
            }

            input.ScanOn = ScanOnCheck.IsChecked == true;
            SaveTag(SelectedTag);
            SystemLogger.Log($"Scan {(input.ScanOn ? "enabled" : "disabled")} for tag: {SelectedTag.Name}");
        }

        private void Button_AcknowledgeAlarm(object sender, RoutedEventArgs e)
        {
            var selectedAlarms = ActiveAlarmsGrid.SelectedItems
                .OfType<AlarmInfo>()
                .ToList();

            if (!selectedAlarms.Any())
            {
                UpdateAcknowledgeButtonState();
                return;
            }

            foreach (var alarm in selectedAlarms)
            {
                if (alarm.IsAcknowledged)
                {
                    continue;
                }

                var matchingTag = IOElements
                    .OfType<IAnalogInput>()
                    .FirstOrDefault(t => t.Name == alarm.TagName);

                if (matchingTag != null)
                {
                    matchingTag.AcknowledgeAlarm();
                    alarm.IsAcknowledged = true;
                    if (matchingTag is AnalogInput analogInput)
                    {
                        SaveAlarmConfiguration(analogInput);
                        SystemLogger.Log($"Alarm acknowledged: {analogInput.Name}");
                    }
                }
            }

            SyncActiveAlarmsFromTags();
            UpdateAcknowledgeButtonState();
        }

        private void ActiveAlarmsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateAcknowledgeButtonState();
        }

        private void UpdateAcknowledgeButtonState()
        {
            if (AcknowledgeAlarmButton != null && ActiveAlarmsGrid != null)
            {
                AcknowledgeAlarmButton.IsEnabled = ActiveAlarmsGrid.SelectedItems.Count > 0;
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ActiveAlarmsGrid == null || ActiveAlarmsGrid.SelectedItems.Count == 0)
            {
                return;
            }

            var clickedElement = e.OriginalSource as DependencyObject;

            if (IsDescendantOf(clickedElement, ActiveAlarmsGrid)
                || IsDescendantOf(clickedElement, AcknowledgeAlarmButton)
                || IsDescendantOf(clickedElement, DeleteAlarmButton))
            {
                return;
            }

            ActiveAlarmsGrid.UnselectAll();
        }

        private static bool IsDescendantOf(DependencyObject element, DependencyObject ancestor)
        {
            while (element != null)
            {
                if (element == ancestor)
                {
                    return true;
                }

                element = (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            return false;
        }

        private void OnAlarmRaised(AlarmInfo alarmInfo)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                AlarmInfo displayAlarm = alarmInfo;
                string readError = null;
                if (alarmInfo.Id != 0
                    && PersistenceService.TryGetActivatedAlarm(alarmInfo.Id, out AlarmInfo databaseAlarm, out readError))
                {
                    databaseAlarm.Address = alarmInfo.Address;
                    databaseAlarm.TriggeredValue = alarmInfo.TriggeredValue;
                    displayAlarm = databaseAlarm;
                }
                else if (!string.IsNullOrWhiteSpace(readError))
                {
                    ShowPersistenceWarning(readError);
                }

                // A raised alarm is always active by definition, and by definition
                // has now occurred at least once.
                displayAlarm.IsActive = true;
                displayAlarm.HasOccurred = true;

                var existingAlarm = ActiveAlarms.FirstOrDefault(alarm => alarm.TagName == displayAlarm.TagName);
                if (existingAlarm == null)
                {
                    ActiveAlarms.Add(displayAlarm);
                }
                else
                {
                    existingAlarm.Id = displayAlarm.Id;
                    existingAlarm.AlarmDefinitionId = displayAlarm.AlarmDefinitionId;
                    existingAlarm.Address = displayAlarm.Address;
                    existingAlarm.TriggeredValue = displayAlarm.TriggeredValue;
                    existingAlarm.LowLimit = displayAlarm.LowLimit;
                    existingAlarm.HighLimit = displayAlarm.HighLimit;
                    existingAlarm.IsAcknowledged = displayAlarm.IsAcknowledged;
                    existingAlarm.IsActive = true;
                    existingAlarm.HasOccurred = true;
                    existingAlarm.AlarmName = displayAlarm.AlarmName;
                    existingAlarm.AlarmType = displayAlarm.AlarmType;
                    existingAlarm.Priority = displayAlarm.Priority;
                    existingAlarm.Message = displayAlarm.Message;
                    existingAlarm.Timestamp = displayAlarm.Timestamp;
                }

                var sourceTag = IOElements.OfType<AnalogInput>().FirstOrDefault(tag => tag.Name == displayAlarm.TagName);
                if (sourceTag != null)
                {
                    SaveAlarmConfiguration(sourceTag);
                }

                SystemLogger.Log($"Alarm event displayed for tag: {displayAlarm.TagName}");

                SyncActiveAlarmsFromTags();
            }));
        }

        private void OnAlarmCleared(string tagName)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // The row for this tag stays in the Alarms panel; its color and
                // Ack checkbox just update to reflect the now-inactive state,
                // which SyncActiveAlarmsFromTags is responsible for.
                var sourceTag = IOElements.OfType<AnalogInput>().FirstOrDefault(tag => tag.Name == tagName);
                if (sourceTag != null)
                {
                    SaveAlarmConfiguration(sourceTag);
                }

                SystemLogger.Log($"Alarm returned to normal: {tagName}");

                SyncActiveAlarmsFromTags();
            }));
        }

        private void SyncActiveAlarmsFromTags()
        {
            // The Alarms panel shows one permanent row per currently configured
            // alarm -- it is an overview of every alarm in the system, not just
            // ones that have fired. Rows are added/removed only as alarms are
            // configured/deleted; the alarm's Active/Acknowledged/Inactive state
            // is instead reflected purely through row coloring (see MainWindow.xaml).
            var alarmEnabledInputs = IOElements
                .OfType<AnalogInput>()
                .Where(tag => tag.AlarmEnabled)
                .ToList();

            foreach (var input in alarmEnabledInputs)
            {
                var alarmInfo = ActiveAlarms.FirstOrDefault(alarm => alarm.TagName == input.Name);

                if (alarmInfo == null)
                {
                    ActiveAlarms.Add(new AlarmInfo
                    {
                        TagName = input.Name,
                        Address = input.Address,
                        TriggeredValue = input.CurrentValue,
                        LowLimit = input.LowLimit,
                        HighLimit = input.HighLimit,
                        IsAcknowledged = input.AlarmAcknowledged,
                        IsActive = input.AlarmActive,
                        HasOccurred = input.AlarmHasOccurred,
                        AlarmName = input.AlarmName,
                        AlarmType = input.AlarmType,
                        Priority = input.AlarmPriority,
                        Message = input.AlarmMessage,
                        Timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    bool wasActive = alarmInfo.IsActive;

                    alarmInfo.Address = input.Address;
                    alarmInfo.TriggeredValue = input.CurrentValue;
                    alarmInfo.LowLimit = input.LowLimit;
                    alarmInfo.HighLimit = input.HighLimit;
                    alarmInfo.IsAcknowledged = input.AlarmAcknowledged;
                    alarmInfo.IsActive = input.AlarmActive;
                    alarmInfo.HasOccurred = input.AlarmHasOccurred;
                    alarmInfo.AlarmName = input.AlarmName;
                    alarmInfo.AlarmType = input.AlarmType;
                    alarmInfo.Priority = input.AlarmPriority;
                    alarmInfo.Message = input.AlarmMessage;

                    if (input.AlarmActive && !wasActive)
                    {
                        // A new occurrence of this alarm condition.
                        alarmInfo.Timestamp = DateTime.UtcNow;
                    }
                }
            }

            var enabledNames = new HashSet<string>(alarmEnabledInputs.Select(input => input.Name));
            foreach (var alarm in ActiveAlarms.Where(alarm => !enabledNames.Contains(alarm.TagName)).ToList())
            {
                ActiveAlarms.Remove(alarm);
                if (SelectedAlarm == alarm)
                {
                    SelectedAlarm = null;
                }
            }

            UpdateAcknowledgeButtonState();
            AlarmSoundService.UpdateState(alarmEnabledInputs.Any(tag => tag.AlarmActive && !tag.AlarmAcknowledged));
            SaveRuntimeStateIfDue();
        }

        private void SaveRuntimeStateIfDue()
        {
            if ((DateTime.UtcNow - lastRuntimePersist).TotalSeconds < 5)
            {
                return;
            }

            lastRuntimePersist = DateTime.UtcNow;
            SaveAllTags(false);
        }

        private void SaveTag(ITag tag, string oldName = null)
        {
            if (!PersistenceService.SaveTag(tag, out string errorMessage, oldName))
            {
                ShowPersistenceWarning(errorMessage);
            }
        }

        private void SaveAlarmConfiguration(AnalogInput tag)
        {
            if (!PersistenceService.SaveAlarm(tag, out string errorMessage))
            {
                ShowPersistenceWarning(errorMessage);
            }
        }

        private void DeletePersistedTag(string tagName)
        {
            if (!PersistenceService.DeleteTag(tagName, out string errorMessage))
            {
                ShowPersistenceWarning(errorMessage);
            }
        }

        private void DeleteAlarmConfiguration(string tagName)
        {
            if (!PersistenceService.DeleteAlarm(tagName, out string errorMessage))
            {
                ShowPersistenceWarning(errorMessage);
            }
        }

        private void SaveAllTags(bool showWarning = true)
        {
            if (!PersistenceService.SaveAll(IOElements, out string errorMessage) && showWarning)
            {
                ShowPersistenceWarning(errorMessage);
            }
        }

        private void ShowPersistenceWarning(string errorMessage)
        {
            if (persistenceWarningShown || string.IsNullOrWhiteSpace(errorMessage))
            {
                return;
            }

            persistenceWarningShown = true;
            MessageBox.Show(
                $"Persistent storage is unavailable: {errorMessage}",
                "Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SystemLogger.LogWarning($"Persistence warning: {errorMessage}");
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            alarmRefreshTimer?.Stop();
            AlarmSoundService.UpdateState(false);

            // Stop all scanning threads
            foreach (var tag in IOElements)
            {
                if (tag is IInputCommon input)
                {
                    input.StopScan();
                }
            }

            // Stop PLC simulator
            if (_plc != null)
            {
                _plc.StopSimulator();
            }

            SaveAllTags(false);
            SystemLogger.Log("Application shutdown.");

        }
    }
}
