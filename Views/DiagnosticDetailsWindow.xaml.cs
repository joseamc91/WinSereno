using System.Windows;
using WinSereno.Models;
using WinSereno.ViewModels;

namespace WinSereno.Views
{
    public partial class DiagnosticDetailsWindow : Window
    {
        public DiagnosticDetailsWindow(DiagnosticResult result) { InitializeComponent(); DataContext = new DiagnosticDetailsViewModel(result); }
    }
}
