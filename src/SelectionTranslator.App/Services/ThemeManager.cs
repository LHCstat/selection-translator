using System.Windows;
using Microsoft.Win32;

namespace SelectionTranslator.App.Services;

public static class ThemeManager
{
    private static readonly string[] ThemeFiles = { "Themes/Light.xaml", "Themes/Dark.xaml" };

    public static void Apply(string theme)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        var file = IsDark(theme) ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        var source = new Uri(
            $"pack://application:,,,/SelectionTranslator.App;component/{file}",
            UriKind.Absolute);

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null &&
            ThemeFiles.Any(f => d.Source.OriginalString.EndsWith(f, StringComparison.OrdinalIgnoreCase)));

        var replacement = new ResourceDictionary { Source = source };

        if (existing is null)
            dictionaries.Insert(0, replacement);
        else
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
    }

    private static bool IsDark(string theme) => theme switch
    {
        "dark" => true,
        "light" => false,
        _ => IsSystemDark()
    };

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}