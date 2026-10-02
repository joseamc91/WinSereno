using System;
using System.Windows;

namespace WinSereno.Services
{
    public sealed class ThemeService
    {
        private ResourceDictionary current;
        public void Apply(string theme)
        {
            var next = new ResourceDictionary { Source = new Uri("Themes/" + (theme == "Dark" ? "Dark" : "Light") + ".xaml", UriKind.Relative) };
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            if (current != null) dictionaries.Remove(current);
            dictionaries.Add(next);
            current = next;
        }
    }
}
