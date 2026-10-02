using System.Windows;
namespace WinSereno.Views
{
    public partial class TaskOutputWindow : Window
    {
        public TaskOutputWindow(object viewModel) { InitializeComponent(); DataContext = viewModel; }
    }
}
