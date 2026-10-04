using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace WinSereno.Localization
{
    public sealed class LanguageChoice
    {
        public string Code { get; }
        public string Name { get; }
        public LanguageChoice(string code, string name) { Code = code; Name = name; }
        public override string ToString() => Name;
    }

    // Canonical messages are deliberately independent of UI culture. Existing parsers,
    // IPC messages, logs and stored results retain their semantics when the UI switches.
    // Only the presentation boundary translates those messages; native output is opaque.
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Current { get; } = new LocalizationService();
        public static IReadOnlyList<LanguageChoice> Languages { get; } = Array.AsReadOnly(new[] {
            new LanguageChoice("es", "Español"), new LanguageChoice("en", "English") });
        private readonly Dictionary<string, ResourceDictionary> catalogs = new Dictionary<string, ResourceDictionary>();
        private readonly object catalogLock = new object();
        private readonly Dictionary<string, string> sourceKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private Regex phrases;
        private ResourceDictionary active;
        public string Language { get; private set; } = "es";
        public int Revision { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;
        public static string Normalize(string language) => Languages.Any(l => l.Code == language) ? language : "es";
        private ResourceDictionary Catalog(string language)
        {
            lock (catalogLock)
            {
                if (!catalogs.TryGetValue(language, out var catalog))
                {
                    catalog = (ResourceDictionary)Application.LoadComponent(new Uri("/WinSereno;component/Localization/Strings." + language + ".xaml", UriKind.Relative));
                    catalogs.Add(language, catalog);
                }
                return catalog;
            }
        }
        public static string Source(string key) => (string)Current.Catalog("es")[key];
        public string Get(string key) => (string)Catalog(Language)[key];
        public void Apply(string language)
        {
            language = Normalize(language);
            var next = Catalog(language);
            if (Application.Current != null)
            {
                var dictionaries = Application.Current.Resources.MergedDictionaries;
                if (active != null) dictionaries.Remove(active);
                dictionaries.Add(next); active = next;
            }
            Language = language; Revision++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
        }
        public string Present(string canonical)
        {
            if (string.IsNullOrEmpty(canonical) || Language == "es") return canonical;
            if (phrases == null)
            {
                foreach (string key in Catalog("es").Keys)
                {
                    string value = (string)Catalog("es")[key];
                    if (!sourceKeys.ContainsKey(value)) sourceKeys.Add(value, key);
                    if (!sourceKeys.ContainsKey(value.ToUpperInvariant())) sourceKeys.Add(value.ToUpperInvariant(), key);
                }
                // Longer complete messages take precedence over their reusable fragments.
                phrases = new Regex(string.Join("|", sourceKeys.Keys.Where(s => s.Length > 3)
                    .OrderByDescending(s => s.Length).Select(s =>
                        (char.IsLetterOrDigit(s[0]) ? "(?<![\\p{L}\\p{N}])" : "") + Regex.Escape(s) +
                        (char.IsLetterOrDigit(s[s.Length - 1]) ? "(?![\\p{L}\\p{N}])" : ""))), RegexOptions.CultureInvariant);
            }
            if (sourceKeys.TryGetValue(canonical, out var exact)) return MatchCase(canonical, Get(exact));
            return phrases.Replace(canonical, match => MatchCase(match.Value, Get(sourceKeys[match.Value])));
        }
        private static string MatchCase(string source, string translation) => source.Any(char.IsLetter) &&
            source.All(c => !char.IsLetter(c) || char.IsUpper(c)) ? translation.ToUpperInvariant() : translation;
        public string PresentWithNative(string canonical, IEnumerable<string> nativeSegments)
        {
            if (string.IsNullOrEmpty(canonical)) return canonical;
            var segments = nativeSegments.Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s.Length).ToArray();
            if (segments.Length == 0) return Present(canonical);
            var result = new System.Text.StringBuilder(); int offset = 0;
            while (offset < canonical.Length)
            {
                string found = null; int next = canonical.Length;
                foreach (string segment in segments)
                {
                    int index = canonical.IndexOf(segment, offset, StringComparison.Ordinal);
                    if (index >= 0 && index < next) { next = index; found = segment; }
                }
                result.Append(Present(canonical.Substring(offset, next - offset)));
                if (found == null) break;
                result.Append(found); offset = next + found.Length;
            }
            return result.ToString();
        }
        public string PresentWithNativeRanges(string canonical, IEnumerable<WinSereno.Models.NativeTextRange> ranges)
        {
            var result = new System.Text.StringBuilder(); int offset = 0;
            foreach (var range in ranges.OrderBy(r => r.Offset))
            {
                if (range.Offset < offset || range.Length < 0 || range.Offset > canonical.Length - range.Length)
                    throw new ArgumentException("Invalid native output range.", nameof(ranges));
                result.Append(Present(canonical.Substring(offset, range.Offset - offset)));
                result.Append(canonical, range.Offset, range.Length);
                offset = range.Offset + range.Length;
            }
            result.Append(Present(canonical.Substring(offset)));
            return result.ToString();
        }
    }
}
