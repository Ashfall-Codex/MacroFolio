using System.Globalization;
using System.Resources;

namespace MacroFolio.Localization;

public static class Loc
{
    public const string FrenchCode = "fr";
    public const string EnglishCode = "en";

    private const string FallbackLanguage = EnglishCode;

    public static readonly IReadOnlyList<KeyValuePair<string, string>> AvailableLanguages = new[]
    {
        new KeyValuePair<string, string>(FrenchCode, "Français"),
        new KeyValuePair<string, string>(EnglishCode, "English"),
    };

    private static readonly ResourceManager ResourceManager =
        new("MacroFolio.Localization.Strings", typeof(Loc).Assembly);

    private static CultureInfo currentCulture = CultureInfo.GetCultureInfo(FrenchCode);

    public static string CurrentLanguage => currentCulture.TwoLetterISOLanguageName;

    public static void SetLanguage(string? languageCode)
    {
        var code = Normalize(languageCode);
        currentCulture = CultureInfo.GetCultureInfo(code);
    }

    /// <summary>Resolve a key for the current language, falling back to English then the key itself.</summary>
    public static string Get(string key)
    {
        var value = ResourceManager.GetString(key, currentCulture);
        if (!string.IsNullOrEmpty(value)) return value;

        value = ResourceManager.GetString(key, CultureInfo.GetCultureInfo(FallbackLanguage));
        return string.IsNullOrEmpty(value) ? key : value;
    }

    private static string Normalize(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return FallbackLanguage;
        var two = languageCode.Trim().ToLowerInvariant();
        if (two.Length > 2) two = two[..2];
        return two is FrenchCode or EnglishCode ? two : FallbackLanguage;
    }
}
