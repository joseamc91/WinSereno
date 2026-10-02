using System.Globalization;
using System.Text;
using WinSereno.Models;

namespace WinSereno.ViewModels
{
    public sealed class DiagnosticDetailsViewModel
    {
        public string Name { get; }
        public string Text { get; }
        public DiagnosticDetailsViewModel(DiagnosticResult result)
        {
            Name = result.Name;
            var text = new StringBuilder(result.StatusLabel + " · " + result.Summary + "\n\n");
            text.AppendLine(result.DetailedDescription);
            if (!string.IsNullOrWhiteSpace(result.TechnicalDetails)) text.AppendLine(result.TechnicalDetails);
            foreach (var item in result.Events)
            {
                text.AppendLine("\n────────────────────────────────────────");
                text.AppendLine((item.Time?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "Fecha no disponible") + " · " + item.Provider + " · ID " + item.EventId + " · " + item.Level);
                text.AppendLine("Interpretación: " + item.Interpretation);
                text.AppendLine("Descripción original de Windows:\n" + item.WindowsDescription);
            }
            text.AppendLine("\nDuración de la comprobación: " + result.Duration.TotalSeconds.ToString("0.00", CultureInfo.CurrentCulture) + " s");
            Text = text.ToString();
        }
    }
}
