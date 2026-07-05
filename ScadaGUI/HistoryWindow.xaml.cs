using System.Windows;
using Contracts;

namespace ScadaGUI
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow(ITag selectedTag)
        {
            InitializeComponent();
            TitleText.Text = selectedTag != null
                ? $"History for {selectedTag.Name} ({selectedTag.Type})"
                : "Tag History";
        }

        private void Button_Close(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
