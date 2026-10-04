using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinSereno.Services;
using WinSereno.ViewModels;

namespace WinSereno.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel viewModel;
        private readonly IDialogService dialogs;
        private bool waitingForCancellation;
        public MainWindow(MainViewModel viewModel, IDialogService dialogs)
        {
            WinSereno.Localization.LocalizationPresentation.Initialize(); InitializeComponent();
            TaskExecutionHost.Content = new TaskExecutionPanel();
            this.viewModel = viewModel; this.dialogs = dialogs;
            DataContext = viewModel;
            viewModel.RepairNavigationRequested += (sender, args) => PageScroll.ScrollToTop();
            Closing += OnClosing;
            Loaded += async (sender, args) => await viewModel.Home.RefreshAsync();
            Closed += (sender, args) => viewModel.Home.Stop();
        }
        private void OnNavigationKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var item = Keyboard.FocusedElement as ListBoxItem;
            var navigation = item?.DataContext as NavigationItem;
            if (navigation == null || !((ListBox)sender).Items.Contains(navigation)) return;
            viewModel.NavigateCommand.Execute(navigation.Section);
            e.Handled = true;
        }
        private void OnDiagnosticHostLoaded(object sender, RoutedEventArgs e)
        {
            var host = (ContentControl)sender;
            if (host.Content == null) host.Content = new DiagnosticCard();
        }
        private void ToastLayer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Canvas does not reserve page height. Clamp the overlay to the actual main area, not the window/sidebar.
            TaskExecutionHost.Width = System.Math.Max(0, System.Math.Min(500, e.NewSize.Width));
            TaskExecutionHost.MaxHeight = System.Math.Max(0, e.NewSize.Height);
        }
        private async void OnClosing(object sender, CancelEventArgs e)
        {
            if (!viewModel.Operations.IsActive) return;
            e.Cancel = true;
            if (waitingForCancellation) return;
            if (!viewModel.Operations.CanBeCancelled)
            {
                dialogs.ShowMessage("Hay una operación no cancelable en ejecución. Espera a que termine antes de cerrar la aplicación.");
                return;
            }
            if (!dialogs.ConfirmCancelAndClose()) return;
            waitingForCancellation = true;
            viewModel.Operations.RequestCancellation();
            await viewModel.Operations.WaitForIdleAsync();
            waitingForCancellation = false;
            Close();
        }
    }
}
