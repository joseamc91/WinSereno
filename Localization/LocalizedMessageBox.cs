using System.Windows;

namespace WinSereno.Localization
{
    internal static class LocalizedMessageBox
    {
        public static MessageBoxResult Show(string message, string caption) => MessageBox.Show(
            LocalizationService.Current.Present(message), LocalizationService.Current.Present(caption));
        public static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton buttons,
            MessageBoxImage image, MessageBoxResult defaultResult = MessageBoxResult.None) => MessageBox.Show(owner,
                LocalizationService.Current.Present(message), LocalizationService.Current.Present(caption), buttons, image, defaultResult);
    }
}
