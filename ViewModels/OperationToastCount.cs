namespace WinSereno.ViewModels
{
    // Structured presentation of diagnostic counters; never extracted from localized summaries.
    public sealed class OperationToastCount
    {
        public string StatusCode { get; }
        public string Text { get; }
        public string Separator { get; }
        public OperationToastCount(string statusCode, int count, bool first)
        {
            StatusCode = statusCode; Separator = first ? "" : WinSereno.Localization.LocalizationService.Source("Text.Separator");
            string label = statusCode == "Healthy" ? (count == 1 ? WinSereno.Localization.LocalizationService.Source("Text.Healthy") : WinSereno.Localization.LocalizationService.Source("Text.HealthyPlural887")) :
                statusCode == "Attention" ? WinSereno.Localization.LocalizationService.Source("Text.Attention") : statusCode == WinSereno.Localization.LocalizationService.Source("Text.Error") ? (count == 1 ? WinSereno.Localization.LocalizationService.Source("Text.Error") : WinSereno.Localization.LocalizationService.Source("Text.ErrorsPlural888")) :
                (count == 1 ? WinSereno.Localization.LocalizationService.Source("Text.NotChecked") : WinSereno.Localization.LocalizationService.Source("Text.NotChecked108"));
            Text = count + " " + label;
        }
    }
}
