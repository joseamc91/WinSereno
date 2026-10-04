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
        // Presentation metadata only: these native blocks must remain opaque to localization.
        public IList<string> NativeOutputSegments { get; set; } = new List<string>();
        public IList<NativeTextRange> NativeOutputRanges { get; set; } = new List<NativeTextRange>();
        public string Recommendation { get; set; }
        public string TechnicalDetails { get; set; }
        public TimeSpan Duration { get; set; }
        public IList<DiagnosticEvent> Events { get; set; } = new List<DiagnosticEvent>();
        public bool HasDetails => !string.IsNullOrWhiteSpace(DetailedDescription) || !string.IsNullOrWhiteSpace(TechnicalDetails) || Events.Count > 0;
        public NavigationSection? NavigationTarget { get; set; }
        public string NavigationLabel { get; set; }
        public bool HasNavigation => NavigationTarget.HasValue;
        public string CardStatusLabel => StatusLabel.ToUpperInvariant();
        public string StatusLabel
        {
            get
            {
                switch (Status)
                {
                    case DiagnosticStatus.Healthy: return WinSereno.Localization.LocalizationService.Source("Text.Healthy");
                    case DiagnosticStatus.Attention: return WinSereno.Localization.LocalizationService.Source("Text.Attention");
                    case DiagnosticStatus.Error: return WinSereno.Localization.LocalizationService.Source("Text.Error");
                    default: return WinSereno.Localization.LocalizationService.Source("Text.NotChecked");
                }
            }
        }
    }
    public sealed class NativeTextRange
    {
        public int Offset { get; }
        public int Length { get; }
        public NativeTextRange(int offset, int length) { Offset = offset; Length = length; }
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
