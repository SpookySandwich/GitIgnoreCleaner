using System.Text.Json;

namespace GitIgnoreCleaner.Services;

public sealed class AppSettings
{
    public string? PreferredLanguageTag { get; set; }
}

public static class AppSettingsService
{
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static AppSettings? _cachedSettings;

    public static string? GetPreferredLanguageTag()
    {
        lock (SyncRoot)
        {
            return Clone(LoadSettings()).PreferredLanguageTag;
        }
    }

    public static void SavePreferredLanguageTag(string? tag)
    {
        UpdateSettings(settings => settings.PreferredLanguageTag = NormalizeOptionalValue(tag));
    }

    private static void UpdateSettings(Action<AppSettings> update)
    {
        lock (SyncRoot)
        {
            var settings = Clone(LoadSettings());
            update(settings);
            SaveSettings(settings);
            _cachedSettings = settings;
        }
    }

    private static AppSettings LoadSettings()
    {
        if (_cachedSettings != null)
        {
            return _cachedSettings;
        }

        var settings = LoadSettingsFromDisk() ?? LoadLegacySettings() ?? new AppSettings();
        _cachedSettings = settings;
        return settings;
    }

    private static AppSettings? LoadSettingsFromDisk()
    {
        try
        {
            var settingsPath = GetSettingsPath();
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            using var stream = File.OpenRead(settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(stream);
        }
        catch
        {
            return null;
        }
    }

    private static AppSettings? LoadLegacySettings()
    {
        try
        {
            var legacyLanguagePath = GetLegacyLanguagePreferencePath();
            if (!File.Exists(legacyLanguagePath))
            {
                return null;
            }

            var tag = File.ReadAllText(legacyLanguagePath).Trim();
            return new AppSettings
            {
                PreferredLanguageTag = NormalizeOptionalValue(tag)
            };
        }
        catch
        {
            return null;
        }
    }

    private static void SaveSettings(AppSettings settings)
    {
        var settingsDirectory = Path.GetDirectoryName(GetSettingsPath());
        if (!string.IsNullOrWhiteSpace(settingsDirectory))
        {
            Directory.CreateDirectory(settingsDirectory);
        }

        var tempPath = GetSettingsPath() + ".tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
            }

            File.Move(tempPath, GetSettingsPath(), overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }

            throw;
        }

        TryDeleteLegacyLanguagePreference();
    }

    private static void TryDeleteLegacyLanguagePreference()
    {
        try
        {
            var legacyPath = GetLegacyLanguagePreferencePath();
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
        catch
        {
            // Best-effort migration cleanup only.
        }
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitIgnoreCleaner",
            "settings",
            "settings.json");
    }

    private static string GetLegacyLanguagePreferencePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitIgnoreCleaner",
            "settings",
            "language.txt");
    }

    private static AppSettings Clone(AppSettings settings)
    {
        return new AppSettings
        {
            PreferredLanguageTag = settings.PreferredLanguageTag
        };
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
