using System.Windows;
using WinSereno.Models;
using WinSereno.ViewModels;

namespace WinSereno.Views
{
    public partial class DiagnosticDetailsWindow : Window
    {
        public DiagnosticDetailsWindow(DiagnosticResult result) { WinSereno.Localization.LocalizationPresentation.Initialize(); InitializeComponent(); var model = new DiagnosticDetailsViewModel(result); DataContext = model; Closed += (s, e) => model.Dispose(); }
    }
}
