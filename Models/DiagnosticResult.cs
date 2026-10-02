using System;
using System.Collections.Generic;

namespace WinSereno.Models
{
    public sealed class DiagnosticResult
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public DiagnosticStatus Status { get; set; }
        public string StatusCode => Status.ToString();
        public string DetailedDescription { get; set; }
        public string Recommendation { get; set; }
        public string TechnicalDetails { get; set; }
        public TimeSpan Duration { get; set; }
        public IList<DiagnosticEvent> Events { get; set; } = new List<DiagnosticEvent>();
        public bool HasDetails => !string.IsNullOrWhiteSpace(DetailedDescription) || !string.IsNullOrWhiteSpace(TechnicalDetails) || Events.Count > 0;
        public NavigationSection? NavigationTarget { get; set; }
        public string NavigationLabel { get; set; }
        public bool HasNavigation => NavigationTarget.HasValue;
        public string StatusLabel
        {
            get
            {
                switch (Status)
                {
                    case DiagnosticStatus.Healthy: return "Correcto";
                    case DiagnosticStatus.Attention: return "Atención";
                    case DiagnosticStatus.Error: return "Error";
                    default: return "No comprobado";
                }
            }
        }
    }
    public sealed class DiagnosticEvent
    {
        public DateTimeOffset? Time { get; set; }
        public string Provider { get; set; }
        public int EventId { get; set; }
        public string Level { get; set; }
        public string Interpretation { get; set; }
        public string WindowsDescription { get; set; }
    }
}
