using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace FfxiMacros.App.Localization;

/// <summary>The languages the interface is written in.</summary>
public enum Language
{
    English,
    French,
}

/// <summary>
/// The interface language, and the lookup every label goes through.
/// </summary>
/// <remarks>
/// <para>
/// The strings live in plain C# dictionaries rather than in <c>.resx</c> satellite assemblies:
/// there are barely a hundred of them, they can be read without an Avalonia application running —
/// which is what lets the tests check both languages agree — and nothing has to survive trimming.
/// </para>
/// <para>
/// XAML still binds them with <c>{DynamicResource}</c>. <see cref="Apply"/> pours the table into a
/// resource dictionary merged into the application, so switching language re-resolves every label
/// on screen without rebuilding the window. Anything a view model composed itself is refreshed by
/// listening to <see cref="Changed"/>.
/// </para>
/// </remarks>
public static class Loc
{
    private static ResourceDictionary? _installed;

    /// <summary>The language in use. English, until a saved setting or the user says otherwise.</summary>
    public static Language Current { get; private set; } = Language.English;

    /// <summary>Raised after <see cref="Current"/> changes, for text that code composed.</summary>
    public static event Action? Changed;

    /// <summary>The two-letter code stored in the settings file.</summary>
    public static string Code => CodeOf(Current);

    public static string CodeOf(Language language) => language == Language.French ? "fr" : "en";

    /// <summary>Reads a stored code; anything unrecognised falls back to English.</summary>
    public static Language Parse(string? code) =>
        string.Equals(code?.Trim(), "fr", StringComparison.OrdinalIgnoreCase) ? Language.French : Language.English;

    /// <summary>Switches the interface, refreshing both the XAML labels and the composed text.</summary>
    public static void Apply(Language language)
    {
        Current = language;
        Install(Table);
        Changed?.Invoke();
    }

    /// <summary>The text for a key, or the key itself when it is missing — never an exception.</summary>
    public static string T(string key) => Table.TryGetValue(key, out string? text) ? text : key;

    /// <summary>The text for a key, with <c>{0}</c>… filled in.</summary>
    public static string T(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, T(key), arguments);

    /// <summary>The whole table for a language, for the tests that compare the two.</summary>
    public static IReadOnlyDictionary<string, string> TableFor(Language language) =>
        language == Language.French ? Strings.French : Strings.English;

    private static IReadOnlyDictionary<string, string> Table => TableFor(Current);

    /// <summary>
    /// Replaces the merged dictionary that holds the labels. The old one is removed rather than
    /// overwritten, so the two languages can never end up layered on top of each other.
    /// </summary>
    private static void Install(IReadOnlyDictionary<string, string> table)
    {
        if (Application.Current is not { } application)
            return;

        var dictionary = new ResourceDictionary();
        foreach (var entry in table)
            dictionary[entry.Key] = entry.Value;

        var merged = application.Resources.MergedDictionaries;
        if (_installed is not null)
            merged.Remove(_installed);

        merged.Add(dictionary);
        _installed = dictionary;
    }
}
