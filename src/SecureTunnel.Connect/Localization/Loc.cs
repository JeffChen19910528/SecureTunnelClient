using System.ComponentModel;
using System.Globalization;
using System.IO;

namespace SecureTunnel.Connect.Localization;

/// <summary>
/// UI language switcher (English / Traditional Chinese). XAML binds to the
/// indexer through <see cref="LocExtension"/>; view models and services call
/// <see cref="T"/> and re-read on <see cref="LanguageChanged"/>. The default
/// is English so unit tests are deterministic; the app chooses the real
/// startup language through <see cref="InitializeFromEnvironment"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string English = "en";
    public const string TraditionalChinese = "zh-TW";

    private const string LanguageEnvironmentVariable = "SECURETUNNEL_UI_LANGUAGE";

    private string _language = English;

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public string Language
    {
        get => _language;
        set
        {
            var normalized = Normalize(value);
            if (normalized == _language)
            {
                return;
            }

            _language = normalized;
            // "Item[]" refreshes every indexer binding; "" refreshes all properties.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string this[string key] => Lookup(key);

    public static string T(string key) => Instance.Lookup(key);

    public static string T(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Instance.Lookup(key), args);

    /// <summary>Localized True/False (English keeps "True"/"False").</summary>
    public static string Bool(bool value) => T(value ? "Bool_True" : "Bool_False");

    public void Toggle() => Language = _language == English ? TraditionalChinese : English;

    /// <summary>
    /// Startup language: environment override, then the saved preference,
    /// then the OS UI language (Chinese systems start in Traditional Chinese).
    /// </summary>
    public void InitializeFromEnvironment()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(LanguageEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            Language = fromEnvironment;
            return;
        }

        var saved = LoadSavedLanguage();
        if (saved is not null)
        {
            Language = saved;
            return;
        }

        Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? TraditionalChinese : English;
    }

    public void SavePreference()
    {
        try
        {
            var path = PreferencePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, _language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A language preference is a convenience; never fail the UI over it.
        }
    }

    private static string? LoadSavedLanguage()
    {
        try
        {
            var path = PreferencePath();
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string PreferencePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SecureTunnel", "Connect", "ui-language.txt");

    private static string Normalize(string? value) =>
        value is not null && value.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? TraditionalChinese : English;

    private string Lookup(string key)
    {
        var table = _language == TraditionalChinese ? Strings.ZhTw : Strings.En;
        return table.TryGetValue(key, out var value) ? value
            : Strings.En.TryGetValue(key, out var fallback) ? fallback
            : key;
    }
}
