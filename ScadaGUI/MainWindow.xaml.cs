using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Entity;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

using Contracts;

namespace ScadaGUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public IAnalogInput TestanalogInput { get; set; }
        public ITag tag { get; set; }
        public ITagBuilder tagBuilder { get; set; }

        public ObservableCollection<ITag> IOElements { get; set; }

        public MainWindow(ITagBuilder builder)
        {
            InitializeComponent();
            IOElements = new ObservableCollection<ITag>();
            tagBuilder = builder;

            TestanalogInput = builder.CreateAnalogInput("ADDR001");
            tag = builder.CreateDigitalInput("ADDR005");
            tag.Type = Tag_Type.DI;

            TestanalogInput.Type = Tag_Type.AI;
            TestanalogInput.Name = "Test";

            IOElements.Add(TestanalogInput);
            IOElements.Add(tag);

            //foreach (AnalogInput ai in ...)
            //{
            //    ai.AlarmActivated += OnAlarmActivated;
            //    ai.StartScan();
            //}

            this.DataContext = this;
        }

        private void Button_AddTag(object sender, RoutedEventArgs e)
        {
            AddWindow addwindow = new AddWindow();
            addwindow.ShowDialog();

            if (addwindow.DialogResult.GetValueOrDefault())
            {
                Debug.WriteLine(addwindow.DialogResultType);

                tag = tagBuilder.CreateDigitalOutput("HAHA");
                tag.Name = "Novi Tag";
                tag.Type = Tag_Type.DO;

                IOElements.Add(tag);
            }
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
        //    //abort input threads
        //    foreach(AnalogInput ai in ContextClass.Instance.AnalogInputs)
        //    {
        //        ai.StopScan();
        //    }
        //    foreach(DigitalInput di in ContextClass.Instance.DigitalInputs)
        //    {
        //        di.StopScan();
        //    }

        //    //abort simulator threads
        //    if (PLC.Instance != null)
        //    {
        //        PLC.Instance.Abort();
        //    }

        //    ContextClass.Instance.SaveChanges();
        //    ContextClass.Instance.Dispose();
        }

        //static void OnAlarmActivated(string alarmName)
        //{
        //    Application.Current.Dispatcher.BeginInvoke(
        //    DispatcherPriority.Background,
        //        new Action(() =>
        //        {
        //            ActivatedAlarm alarm = new ActivatedAlarm(ContextClass.Instance.Alarms.Find(alarmName));
        //            ContextClass.Instance.ActivatedAlarms.Add(alarm);
        //            ContextClass.Instance.SaveChanges();
        //        }));

        //}

    }
}
