using System.Windows;
using WinSereno.Models;
using WinSereno.Services;

namespace WinSereno.Views
{
    public sealed class DialogService : IDialogService, ITcpIpResetDialogs, IPreferencesDialogs
    {
        public bool ConfirmResetPreferences()
        {
            var window = new Window { Title = WinSereno.Localization.LocalizationService.Current.Get("Text.ResetPreferences"), Width = 520, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow };
            var layout = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
            layout.Children.Add(new System.Windows.Controls.TextBlock { Text = WinSereno.Localization.LocalizationService.Current.Get("Text.WinserenoSVisualPreferencesWillBeRestoredLogs"), TextWrapping = TextWrapping.Wrap });
            var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            buttons.Children.Add(new System.Windows.Controls.Button { Content = WinSereno.Localization.LocalizationService.Current.Get("Text.Cancel"), IsCancel = true });
            var reset = new System.Windows.Controls.Button { Content = WinSereno.Localization.LocalizationService.Current.Get("Text.Reset") };
            reset.Click += (s, e) => window.DialogResult = true;
            buttons.Children.Add(reset); layout.Children.Add(buttons); window.Content = layout;
            return window.ShowDialog() == true;
        }
        public bool ConfirmTcpIpReset(TcpIpResetSnapshot snapshot) => new TcpIpResetWarningWindow(snapshot) { Owner = Application.Current.MainWindow }.ShowDialog() == true;
        public RestartAdapter SelectRestartAdapter(System.Collections.Generic.IReadOnlyList<RestartAdapter> adapters)
        {
            var window = new Window { Title = WinSereno.Localization.LocalizationService.Current.Get("Text.SelectAdapter"), Width = 640, Height = 240, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow };
            var layout = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
            layout.Children.Add(new System.Windows.Controls.TextBlock { Text = WinSereno.Localization.LocalizationService.Current.Get("Text.SelectTheConnectedPhysicalAdapterYouWantTo"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
            var choice = new System.Windows.Controls.ComboBox { ItemsSource = adapters, DisplayMemberPath = nameof(RestartAdapter.DisplayName), SelectedIndex = -1 };
            layout.Children.Add(choice);
            var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            var cancel = new System.Windows.Controls.Button { Content = WinSereno.Localization.LocalizationService.Current.Get("Text.Cancel"), IsCancel = true };
            var select = new System.Windows.Controls.Button { Content = WinSereno.Localization.LocalizationService.Current.Get("Text.Continue"), IsEnabled = false };
            choice.SelectionChanged += (s, e) => select.IsEnabled = choice.SelectedItem is RestartAdapter;
            select.Click += (s, e) => window.DialogResult = true;
            buttons.Children.Add(cancel); buttons.Children.Add(select); layout.Children.Add(buttons); window.Content = layout;
            return window.ShowDialog() == true ? choice.SelectedItem as RestartAdapter : null;
        }
        public bool ConfirmTask(MaintenanceTask task) => new TaskConfirmationWindow(task) { Owner = Application.Current.MainWindow }.ShowDialog() == true;
        public void ShowDiagnosticDetails(DiagnosticResult result) => new DiagnosticDetailsWindow(result) { Owner = Application.Current.MainWindow }.ShowDialog();
        public void ShowOutput(TaskProgress progress)
        {
            // Current output stays live; historical details bind to their independent snapshot.
            var main = Application.Current.MainWindow.DataContext as ViewModels.MainViewModel;
            object context = main != null && ReferenceEquals(main.Progress, progress) ? (object)main :
                new { TaskName = progress.CurrentTask?.Name, Progress = progress };
            new TaskOutputWindow(context) { Owner = Application.Current.MainWindow }.ShowDialog();
        }
        public void ShowMessage(string message) => WinSereno.Localization.LocalizedMessageBox.Show(Application.Current.MainWindow, message, WinSereno.Localization.LocalizationService.Current.Get("Text.Winsereno"), MessageBoxButton.OK, MessageBoxImage.Information);
        public bool ConfirmCancelAndClose() => WinSereno.Localization.LocalizedMessageBox.Show(Application.Current.MainWindow,
            WinSereno.Localization.LocalizationService.Current.Get("Text.ACancellableOperationIsRunningDoYouWant"), WinSereno.Localization.LocalizationService.Current.Get("Text.CloseApplication"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
