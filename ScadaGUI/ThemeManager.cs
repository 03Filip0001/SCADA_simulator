using System;
using System.Linq;
using System.Windows;
using DataConcentrator.Persistence;

namespace ScadaGUI
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    public static class ThemeManager
    {
        private const string LightThemeUri = "pack://application:,,,/ScadaGUI;component/Themes/LightTheme.xaml";
        private const string DarkThemeUri = "pack://application:,,,/ScadaGUI;component/Themes/DarkTheme.xaml";
        private const string ControlStylesUri = "pack://application:,,,/ScadaGUI;component/Themes/ControlStyles.xaml";

        private static bool controlStylesLoaded;

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

        public static void Initialize()
        {
            EnsureControlStylesLoaded();

            var savedTheme = PersistenceService.LoadTheme(out _);
            var theme = string.Equals(savedTheme, "Dark", StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark
                : AppTheme.Light;

            ApplyTheme(theme, persist: false);
        }

        public static void ToggleTheme()
        {
            ApplyTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        }

        public static void ApplyTheme(AppTheme theme, bool persist = true)
        {
            EnsureControlStylesLoaded();

            var themeDictionary = new ResourceDictionary
            {
                Source = new Uri(theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri, UriKind.Absolute)
            };

            var appResources = Application.Current.Resources;
            var existing = appResources.MergedDictionaries.FirstOrDefault(dictionary =>
                dictionary.Source != null
                && (dictionary.Source.OriginalString == LightThemeUri || dictionary.Source.OriginalString == DarkThemeUri));

            if (existing != null)
            {
                appResources.MergedDictionaries.Remove(existing);
            }

            appResources.MergedDictionaries.Add(themeDictionary);
            CurrentTheme = theme;

            if (persist)
            {
                PersistenceService.SaveTheme(theme.ToString(), out _);
            }
        }

        private static void EnsureControlStylesLoaded()
        {
            if (controlStylesLoaded)
            {
                return;
            }

            var appResources = Application.Current.Resources;
            bool alreadyPresent = appResources.MergedDictionaries.Any(dictionary =>
                dictionary.Source != null && dictionary.Source.OriginalString == ControlStylesUri);

            if (!alreadyPresent)
            {
                appResources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(ControlStylesUri, UriKind.Absolute)
                });
            }

            controlStylesLoaded = true;
        }
    }
}
