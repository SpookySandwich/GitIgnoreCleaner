using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;
using GitIgnoreCleaner.Models;

namespace GitIgnoreCleaner.Services;

public static class LocalizationService
{
    public const string SystemDefaultTag = "";
    public const string DefaultLanguageTag = "en-US";
    private static ResourceLoader? _resourceLoader;
    private static readonly IReadOnlyList<LanguageOption> LanguageOptions =
    [
        new(SystemDefaultTag, string.Empty),
        new("en-US", "English (United States)"),
        new("zh-Hans", "简体中文"),
        new("ja-JP", "日本語"),
        new("ru-RU", "Русский")
    ];

    private static readonly Dictionary<string, string> SupportedLanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en-US",
        ["en-US"] = "en-US",
        ["zh"] = "zh-Hans",
        ["zh-CN"] = "zh-Hans",
        ["zh-SG"] = "zh-Hans",
        ["zh-Hans"] = "zh-Hans",
        ["ja"] = "ja-JP",
        ["ja-JP"] = "ja-JP",
        ["ru"] = "ru-RU",
        ["ru-RU"] = "ru-RU"
    };

    public static IReadOnlyList<LanguageOption> GetAvailableLanguages()
    {
        return LanguageOptions
            .Select(option => option.Tag == SystemDefaultTag
                ? option with { DisplayName = GetString("SettingsLanguageSystemDefault") }
                : option)
            .ToList();
    }

    public static void Initialize()
    {
        ApplicationLanguages.PrimaryLanguageOverride = LoadSavedLanguageTag() ?? ResolveSystemLanguageTag();
    }

    public static string GetSavedLanguageSelectionTag()
    {
        return LoadSavedLanguageTag() ?? SystemDefaultTag;
    }

    public static void SavePreferredLanguage(string? tag)
    {
        if (!TryNormalizeSupportedLanguageTag(tag, out var normalizedTag))
        {
            normalizedTag = null;
        }

        AppSettingsService.SavePreferredLanguageTag(normalizedTag);
    }

    public static string GetString(string key)
    {
        var value = (_resourceLoader ??= new ResourceLoader()).GetString(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentUICulture, GetString(key), args);
    }

    public static string GetVersionLabel(string version)
    {
        return Format("SettingsVersionFormat", version);
    }

    private static string? LoadSavedLanguageTag()
    {
        try
        {
            var rawTag = AppSettingsService.GetPreferredLanguageTag();
            return TryNormalizeSupportedLanguageTag(rawTag, out var normalizedTag) ? normalizedTag : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveSystemLanguageTag()
    {
        return TryNormalizeSupportedLanguageTag(CultureInfo.CurrentUICulture.Name, out var normalizedTag)
            ? normalizedTag
            : DefaultLanguageTag;
    }

    private static bool TryNormalizeSupportedLanguageTag(string? tag, out string normalizedTag)
    {
        normalizedTag = string.Empty;

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var trimmed = tag.Trim();
        if (SupportedLanguageMap.TryGetValue(trimmed, out var mappedTag) && mappedTag is not null)
        {
            normalizedTag = mappedTag;
            return true;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(trimmed);
            if (SupportedLanguageMap.TryGetValue(culture.Name, out mappedTag) && mappedTag is not null)
            {
                normalizedTag = mappedTag;
                return true;
            }

            if (SupportedLanguageMap.TryGetValue(culture.TwoLetterISOLanguageName, out mappedTag) && mappedTag is not null)
            {
                normalizedTag = mappedTag;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }
}
