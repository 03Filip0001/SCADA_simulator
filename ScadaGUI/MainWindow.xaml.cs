using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Contracts;
using DataConcentrator;
using DataConcentrator.Model;
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
            InitializeComponent();

            IOElements = new ObservableCollection<ITag>();
            ActiveAlarms = new ObservableCollection<AlarmInfo>();
            TagFilterOptions = new List<string> { "All", "AI", "AO", "DI", "DO" };
            SelectedTagTypeFilter = "All";

            tagBuilder = builder;
            _plc = new DataConcentrator.PLC(new PLCSimulatorManager());

            TestanalogInput = builder.CreateAnalogInput("ADDR001");
            TestanalogInput.Name = "Analog Tag 1";
            TestanalogInput.Type = Tag_Type.AI;

            tag = builder.CreateDigitalInput("ADDR009");
            tag.Name = "Digital Tag 1";
            tag.Type = Tag_Type.DI;

            IOElements.Add(TestanalogInput);
            IOElements.Add(tag);

            FilteredIOElements = CollectionViewSource.GetDefaultView(IOElements);
            FilteredIOElements.Filter = FilterTag;

            SelectedTag = IOElements.FirstOrDefault();

            _plc.AlarmRaised += OnAlarmRaised;
            _plc.AddInput(TestanalogInput);
            _plc.AddInput(tag);

            DataContext = this;
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
            AddWindow addwindow = new AddWindow();
            addwindow.ShowDialog();

            if (addwindow.DialogResult.GetValueOrDefault())
            {
                string type = addwindow.DialogResultType;
                string name = addwindow.DialogResultName;
                string address = addwindow.DialogResultAddress;
                string description = addwindow.DialogResultDescription;

                ITag newTag = null;
                Tag_Type tagType = Tag_Type.AI;

                // Određujem tip taga
                if (Enum.TryParse(type, out tagType))
                {
                    switch (tagType)
                    {
                        case Tag_Type.AI:
                            newTag = tagBuilder.CreateAnalogInput(address ?? "NEW_ADDR");
                            break;
                        case Tag_Type.AO:
                            newTag = tagBuilder.CreateAnalogOutput(address ?? "NEW_ADDR");
                            break;
                        case Tag_Type.DI:
                            newTag = tagBuilder.CreateDigitalInput(address ?? "NEW_ADDR");
                            break;
                        case Tag_Type.DO:
                            newTag = tagBuilder.CreateDigitalOutput(address ?? "NEW_ADDR");
                            break;
                    }

                    if (newTag != null)
                    {
                        // Postavljam svojstva
                        newTag.Name = name ?? "New Tag";
                        newTag.Address = address ?? "NEW_ADDR";
                        newTag.Description = description ?? "";
                        newTag.Type = tagType;

                        if (newTag is AnalogInput analogInput && addwindow.DialogResultHasAlarmSettings)
                        {
                            analogInput.LowLimit = addwindow.DialogResultLowLimit;
                            analogInput.HighLimit = addwindow.DialogResultHighLimit;
                            analogInput.AlarmMessage = addwindow.DialogResultAlarmMessage ?? string.Empty;
                        }

                        // Dodajem tag u kolekciju
                        IOElements.Add(newTag);
                        FilteredIOElements.Refresh();

                        // Ako je ulazni tag, dodajem ga u PLC za skeniranje
                        if (tagType == Tag_Type.AI || tagType == Tag_Type.DI)
                        {
                            _plc.AddInput(newTag);
                        }
                    }
                }
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
            if (SelectedAlarm == null)
            {
                MessageBox.Show("Please select an alarm to acknowledge.", "Acknowledge Alarm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var matchingTag = IOElements.OfType<IAnalogInput>().FirstOrDefault(t => t.Name == SelectedAlarm.TagName);
            if (matchingTag != null)
            {
                matchingTag.AcknowledgeAlarm();
                SelectedAlarm.IsAcknowledged = true;
                CollectionViewSource.GetDefaultView(ActiveAlarms).Refresh();
            }
        }

        private void OnAlarmRaised(AlarmInfo alarmInfo)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dbRecord = ContextClass.Instance.AlarmRecords.Find(alarmInfo.Id);
                if (dbRecord != null)
                {
                    alarmInfo.Message = dbRecord.Message;
                }

                ActiveAlarms.Add(alarmInfo);
            }));
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
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

            // Close database context if needed
            try
            {
                ContextClass.Instance.SaveChanges();
                ContextClass.Instance.Dispose();
            }
            catch
            {
                // Ignore any database errors during shutdown
            }
        }
    }
}
