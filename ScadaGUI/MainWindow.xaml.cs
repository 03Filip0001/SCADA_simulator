using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Data;
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
            }

            alarmRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            alarmRefreshTimer.Tick += (sender, args) => SyncActiveAlarmsFromTags();
            alarmRefreshTimer.Start();

            DataContext = this;
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

            TestanalogInput = builder.CreateAnalogInput("ADDR001");
            TestanalogInput.Name = "Analog Tag 1";
            TestanalogInput.Address = "ADDR001";
            TestanalogInput.Type = Tag_Type.AI;

            tag = builder.CreateDigitalInput("ADDR009");
            tag.Name = "Digital Tag 1";
            tag.Address = "ADDR009";
            tag.Type = Tag_Type.DI;

            IOElements.Add(TestanalogInput);
            IOElements.Add(tag);
            SaveAllTags();
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
            if (!(SelectedTag is AnalogInput analogInput))
            {
                MessageBox.Show("Select an analog input tag to delete its alarm.", "Delete Alarm", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!analogInput.AlarmEnabled)
            {
                MessageBox.Show("The selected analog input does not have an alarm configured.", "Delete Alarm", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            analogInput.ClearAlarm();
            RemoveActiveAlarmsForTag(analogInput.Name);
            DeleteAlarmConfiguration(analogInput.Name);
            SystemLogger.Log($"Alarm deleted for tag: {analogInput.Name}");
            SyncActiveAlarmsFromTags();
            OnPropertyChanged(nameof(SelectedTag));
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
                MessageBox.Show("Select a valid tag type.", "Create Tag", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ITag newTag = CreateTagByType(tagType, addwindow.DialogResultAddress);
            if (newTag == null)
            {
                return;
            }

            ApplyTagValues(newTag, addwindow.DialogResultName, addwindow.DialogResultAddress, addwindow.DialogResultDescription, tagType);
            IOElements.Add(newTag);
            RegisterInputIfNeeded(newTag);
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

            ApplyTagValues(tagToUpdate, addwindow.DialogResultName, addwindow.DialogResultAddress, addwindow.DialogResultDescription, tagToUpdate.Type);

            if (addwindow.DialogResultHasAlarmSettings && tagToUpdate is AnalogInput alarmTarget)
            {
                alarmTarget.ConfigureAlarm(
                    addwindow.DialogResultLowLimit,
                    addwindow.DialogResultHighLimit,
                    addwindow.DialogResultAlarmMessage,
                    addwindow.DialogResultAlarmName,
                    addwindow.DialogResultAlarmType,
                    addwindow.DialogResultAlarmPriority);
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

        private void ApplyTagValues(ITag tag, string name, string address, string description, Tag_Type tagType)
        {
            tag.Name = name;
            tag.Address = address;
            tag.Description = description ?? string.Empty;
            tag.Type = tagType;
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
                    existingAlarm.AlarmName = displayAlarm.AlarmName;
                    existingAlarm.AlarmType = displayAlarm.AlarmType;
                    existingAlarm.Priority = displayAlarm.Priority;
                    existingAlarm.Message = displayAlarm.Message;
                    existingAlarm.Timestamp = displayAlarm.Timestamp;
                    CollectionViewSource.GetDefaultView(ActiveAlarms).Refresh();
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
                RemoveActiveAlarmsForTag(tagName);
                var sourceTag = IOElements.OfType<AnalogInput>().FirstOrDefault(tag => tag.Name == tagName);
                if (sourceTag != null)
                {
                    SaveAlarmConfiguration(sourceTag);
                }

                SyncActiveAlarmsFromTags();
            }));
        }

        private void SyncActiveAlarmsFromTags()
        {
            var activeInputs = IOElements
                .OfType<AnalogInput>()
                .Where(tag => tag.AlarmEnabled && tag.AlarmActive)
                .ToList();

            var activeNames = new HashSet<string>(activeInputs.Select(tag => tag.Name));
            foreach (var staleAlarm in ActiveAlarms.Where(alarm => !activeNames.Contains(alarm.TagName)).ToList())
            {
                ActiveAlarms.Remove(staleAlarm);
            }

            foreach (var activeInput in activeInputs)
            {
                var alarmInfo = ActiveAlarms.FirstOrDefault(alarm => alarm.TagName == activeInput.Name);
                if (alarmInfo == null)
                {
                    ActiveAlarms.Add(new AlarmInfo
                    {
                        TagName = activeInput.Name,
                        Address = activeInput.Address,
                        TriggeredValue = activeInput.CurrentValue,
                        LowLimit = activeInput.LowLimit,
                        HighLimit = activeInput.HighLimit,
                        IsAcknowledged = activeInput.AlarmAcknowledged,
                        AlarmName = activeInput.AlarmName,
                        AlarmType = activeInput.AlarmType,
                        Priority = activeInput.AlarmPriority,
                        Message = activeInput.AlarmMessage,
                        Timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    alarmInfo.Address = activeInput.Address;
                    alarmInfo.TriggeredValue = activeInput.CurrentValue;
                    alarmInfo.LowLimit = activeInput.LowLimit;
                    alarmInfo.HighLimit = activeInput.HighLimit;
                    alarmInfo.IsAcknowledged = activeInput.AlarmAcknowledged;
                    alarmInfo.AlarmName = activeInput.AlarmName;
                    alarmInfo.AlarmType = activeInput.AlarmType;
                    alarmInfo.Priority = activeInput.AlarmPriority;
                    alarmInfo.Message = activeInput.AlarmMessage;
                }
            }

            CollectionViewSource.GetDefaultView(ActiveAlarms).Refresh();
            UpdateAcknowledgeButtonState();
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
            SystemLogger.Log($"Persistence warning: {errorMessage}");
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            alarmRefreshTimer?.Stop();

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
