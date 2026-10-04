using System.Windows.Controls;
namespace WinSereno.Views
{
    public partial class DiagnosticCard : UserControl
    {
        public DiagnosticCard() { WinSereno.Localization.LocalizationPresentation.Initialize(); InitializeComponent(); }
        private void Header_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            // En anchos reducidos los botones pasan debajo del título; el estado conserva su borde derecho.
            bool narrow = e.NewSize.Width < 600;
            Grid.SetColumnSpan(ResultName, narrow ? 2 : 1);
            Grid.SetColumn(HeaderActions, narrow ? 0 : 1);
            Grid.SetRow(HeaderActions, narrow ? 1 : 0);
            Grid.SetColumnSpan(HeaderActions, narrow ? 2 : 1);
        }
    }
}
