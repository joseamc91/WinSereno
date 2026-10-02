using System.Windows;
using WinSereno.Models;
using WinSereno.ViewModels;
namespace WinSereno.Views
{
    public partial class TaskConfirmationWindow : Window
    {
        public TaskConfirmationWindow(MaintenanceTask task) { InitializeComponent(); DataContext = new TaskConfirmationViewModel(task); }
        private void OnExecute(object sender, RoutedEventArgs e) { DialogResult = true; }
    }
}
