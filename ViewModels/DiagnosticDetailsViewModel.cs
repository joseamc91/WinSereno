using System.Globalization;
using System.Text;
using WinSereno.Models;

namespace WinSereno.ViewModels
{
    public sealed class DiagnosticDetailsViewModel : WinSereno.Infrastructure.ObservableObject, System.IDisposable
    {
        public string Name { get; }
        private readonly string canonicalText;
        private readonly System.Collections.Generic.List<NativeTextRange> nativeRanges = new System.Collections.Generic.List<NativeTextRange>();
        public string Text => WinSereno.Localization.LocalizationService.Current.PresentWithNativeRanges(canonicalText, nativeRanges);
        private void LanguageChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == "Revision") Raise(nameof(Text)); }
        public void Dispose() => WinSereno.Localization.LocalizationService.Current.PropertyChanged -= LanguageChanged;
        public DiagnosticDetailsViewModel(DiagnosticResult result)
        {
            Name = result.Name;
            var text = new StringBuilder(result.StatusLabel + WinSereno.Localization.LocalizationService.Source("Text.Separator") + result.Summary + "\n\n");
            int detailOffset = text.Length;
            foreach (var range in result.NativeOutputRanges) nativeRanges.Add(new NativeTextRange(detailOffset + range.Offset, range.Length));
            if (result.NativeOutputRanges.Count == 0 && result.DetailedDescription != null)
                foreach (string segment in result.NativeOutputSegments)
                {
                    if (string.IsNullOrEmpty(segment)) continue;
                    int index = result.DetailedDescription.IndexOf(segment, System.StringComparison.Ordinal);
                    if (index >= 0) nativeRanges.Add(new NativeTextRange(detailOffset + index, segment.Length));
                }
            text.AppendLine(result.DetailedDescription);
            if (!string.IsNullOrWhiteSpace(result.Recommendation)) text.AppendLine(WinSereno.Localization.LocalizationService.Source("Text.Recommendation") + result.Recommendation);
            if (!string.IsNullOrWhiteSpace(result.TechnicalDetails)) text.AppendLine(result.TechnicalDetails);
            foreach (var item in result.Events)
            {
                text.AppendLine("\n────────────────────────────────────────");
                text.AppendLine((item.Time?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? WinSereno.Localization.LocalizationService.Source("Text.DateUnavailable")) + WinSereno.Localization.LocalizationService.Source("Text.Separator") + item.Provider + " · ID " + item.EventId + WinSereno.Localization.LocalizationService.Source("Text.Separator") + item.Level);
                text.AppendLine(WinSereno.Localization.LocalizationService.Source("Text.Interpretation") + item.Interpretation);
                text.Append(WinSereno.Localization.LocalizationService.Source("Text.OriginalWindowsDescription"));
                if (!string.IsNullOrEmpty(item.WindowsDescription)) nativeRanges.Add(new NativeTextRange(text.Length, item.WindowsDescription.Length));
                text.AppendLine(item.WindowsDescription);
            }
            text.AppendLine(WinSereno.Localization.LocalizationService.Source("Text.CheckDuration") + result.Duration.TotalSeconds.ToString("0.00", CultureInfo.CurrentCulture) + " s");
            canonicalText = text.ToString();
            WinSereno.Localization.LocalizationService.Current.PropertyChanged += LanguageChanged;
        }
    }
}
