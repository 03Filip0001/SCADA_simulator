using System.Configuration;
using System.Data;
using System.Windows;

using Contracts;

namespace SCADA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            ITagBuilder _builder = new DataConcentrator.Model.TagBuilder();

            ScadaGUI.MainWindow window = new ScadaGUI.MainWindow(_builder);
            window.Show();
        }
    }

}
