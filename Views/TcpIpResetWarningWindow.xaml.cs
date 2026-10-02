using System.Windows;
using WinSereno.Models;
using WinSereno.ViewModels;

namespace WinSereno.Views
{
    public partial class TcpIpResetWarningWindow : Window
    {
        public TcpIpResetWarningWindow(TcpIpResetSnapshot snapshot)
        {
            InitializeComponent(); DataContext = new TcpIpResetWarningViewModel(snapshot);
        }
        private void OnContinue(object sender, RoutedEventArgs e)
        {
            if (((TcpIpResetWarningViewModel)DataContext).CanContinue) DialogResult = true;
        }
        // Cancel and the title-bar X leave ShowDialog() != true; neither grants consent.
    }
}
