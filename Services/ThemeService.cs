using System;
using System.Windows;

namespace WinSereno.Services
{
    public sealed class ThemeService
    {
        private ResourceDictionary current;
        private readonly Func<string> systemTheme;
        public string EffectiveTheme { get; private set; }
        public ThemeService() : this(ReadSystemTheme) { }
        internal ThemeService(Func<string> systemTheme) { this.systemTheme = systemTheme ?? throw new ArgumentNullException(nameof(systemTheme)); }
        private static string ReadSystemTheme()
        {
            try
            {
                var value = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);
                return value is int && (int)value == 0 ? "Dark" : "Light";
            }
            catch (System.Security.SecurityException) { return "Light"; }
            catch (UnauthorizedAccessException) { return "Light"; }
            catch (System.IO.IOException) { return "Light"; }
        }
        public void Apply(string theme)
        {
            string resolved = theme;
            if (theme == "System") { try { resolved = systemTheme(); } catch (Exception) { resolved = "Light"; } }
            EffectiveTheme = resolved == "Dark" ? "Dark" : "Light";
            var next = new ResourceDictionary { Source = new Uri("Themes/" + EffectiveTheme + ".xaml", UriKind.Relative) };
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            if (current != null) dictionaries.Remove(current);
            dictionaries.Add(next);
            current = next;
        }
    }
}
