using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace WinSereno.Localization
{
    // Bind the canonical value AND the catalog revision. Switching language updates
    // existing controls without replacing their DataContext, commands, selection or data.
    public static class LocalizationPresentation
    {
        private static bool initialized;
        private static readonly DependencyProperty WrappedProperty = DependencyProperty.RegisterAttached(
            "Wrapped", typeof(bool), typeof(LocalizationPresentation), new PropertyMetadata(false));
        private static readonly PresentationConverter converter = new PresentationConverter();
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }
        private static void OnWindowLoaded(object sender, RoutedEventArgs args)
        {
            var window = (Window)sender;
            if ((bool)window.GetValue(WrappedProperty)) return;
            Visit(window);
            bool scanning = false;
            EventHandler updated = (s, e) =>
            {
                if (scanning) return;
                scanning = true;
                try { Visit(window); } finally { scanning = false; }
            };
            // Deferred templates (diagnostic cards, refreshed adapters and activity rows)
            // are discovered when WPF lays them out. Existing nodes are never rebound.
            window.LayoutUpdated += updated;
            window.Closed += (s, e) => window.LayoutUpdated -= updated;
        }
        private static void Visit(DependencyObject element)
        {
            Localize(element);
            if (element is TextBlock)
                // Updating a Run binding mutates WPF's text container, including its
                // collection version. Walk a snapshot, not the live InlineCollection.
                foreach (Inline inline in ((TextBlock)element).Inlines.ToArray()) VisitInline(inline);
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++) Visit(System.Windows.Media.VisualTreeHelper.GetChild(element, i));
        }
        private static void VisitInline(Inline inline)
        {
            Localize(inline);
            if (inline is Span) foreach (Inline child in ((Span)inline).Inlines.ToArray()) VisitInline(child);
        }
        private static void Localize(DependencyObject element)
        {
            if (element == null || (bool)element.GetValue(WrappedProperty)) return;
            element.SetValue(WrappedProperty, true);
            if (element is Run) { Wrap(element, Run.TextProperty); return; }
            if (element is TextBlock) Wrap(element, TextBlock.TextProperty);
            if (element is TextBox) Wrap(element, TextBox.TextProperty);
            if (element is ContentControl) Wrap(element, ContentControl.ContentProperty);
            if (element is HeaderedContentControl) Wrap(element, HeaderedContentControl.HeaderProperty);
            Wrap(element, FrameworkElement.ToolTipProperty);
            Wrap(element, System.Windows.Automation.AutomationProperties.NameProperty);
        }
        private static void Wrap(DependencyObject element, DependencyProperty property)
        {
            var original = BindingOperations.GetBinding(element, property);
            if (original == null) return; // Literal XAML uses DynamicResource directly.
            string path = original.Path?.Path ?? "";
            var context = (element as FrameworkElement)?.DataContext ?? (element as FrameworkContentElement)?.DataContext;
            if (context is WinSereno.Models.NetworkAdapterInformation &&
                (path == "Name" || path == "Description" || path == "CardName" || path == "IPv4" || path == "Gateway" || path == "Dns")) return;
            if (context is WinSereno.ViewModels.DiskViewModel && path == "Name") return;
            if (context is WinSereno.Models.RestartAdapter && (path == "Name" || path == "Description" || path == "DisplayName")) return;
            if (context is WinSereno.Models.TcpIpAdapterConfiguration && (path == "Name" || path == "Description" || path == "Heading")) return;
            var information = context as WinSereno.ViewModels.InformationCardViewModel;
            bool hardwareValue = information != null && path == "Value" &&
                (information.Block == WinSereno.Models.InformationBlock.Cpu ||
                 information.Block == WinSereno.Models.InformationBlock.Gpu ||
                 information.Block == WinSereno.Models.InformationBlock.Windows);
            // Native output, commands, identities and paths must never be translated.
            if (path.Contains("StdOut") || path.Contains("StdErr") || path.Contains("LastRelevantLine") ||
                path.Contains("WindowsDescription") || path.Contains("ExactCommand") || path.EndsWith("Arguments") ||
                path.EndsWith("Path") || path.EndsWith("Provider") || path.EndsWith("ProductVersion") ||
                (element is TextBox && path == "Text")) return;
            if (original.Mode == BindingMode.TwoWay || (element is TextBox && !((TextBox)element).IsReadOnly)) return;
            var translated = new MultiBinding { Mode = BindingMode.OneWay, Converter = converter,
                ConverterParameter = new PresentationFormat { Format = original.StringFormat, HardwareValue = hardwareValue } };
            translated.Bindings.Add(original);
            translated.Bindings.Add(new Binding(nameof(LocalizationService.Revision)) { Source = LocalizationService.Current });
            BindingOperations.SetBinding(element, property, translated);
        }
        private sealed class PresentationFormat
        {
            public string Format;
            public bool HardwareValue;
        }
        private sealed class PresentationConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values.Length == 0 || values[0] == DependencyProperty.UnsetValue) return DependencyProperty.UnsetValue;
                object value = values[0];
                var presentation = (PresentationFormat)parameter;
                if (presentation.HardwareValue && value is string &&
                    (string)value != LocalizationService.Source("Text.Checking") &&
                    (string)value != LocalizationService.Source("Text.CouldNotRetrieveInformation") &&
                    (string)value != LocalizationService.Source("Text.Unavailable") &&
                    (string)value != LocalizationService.Source("Text.NameUnavailable") &&
                    (string)value != LocalizationService.Source("Text.ModelUnavailable")) return value;
                if (value is string) value = LocalizationService.Current.Present((string)value);
                if (presentation.Format != null && value != null)
                    return string.Format(culture, LocalizationService.Current.Present(presentation.Format), value);
                return value is string ? LocalizationService.Current.Present((string)value) : value;
            }
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }
    }
}
