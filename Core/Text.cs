using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EricGameLauncher;

public static class Text
{
    private static Dictionary<string, string> _strings = new();
    private static Dictionary<string, Dictionary<string, string>>? _allTranslations = null;
    private static Dictionary<string, string>? _cliStrings = null;
    private static string _currentLanguage = "Zh-CN";
    private static int _lookupCount = 0;
    private static int _missCount = 0;
    private static HashSet<string> _missedKeys = new();

    public static string CurrentLanguage => _currentLanguage;

    public static event Action? LanguageChanged;

    public static void Load(string langCode)
    {
        using (LogService.StartOperation("App", "Text_Load"))
        {
            var sw = Stopwatch.StartNew();
            LogService.Write("App", $"Text Load Start: targetLang={langCode}");
            _currentLanguage = langCode;
            _lookupCount = 0;
            _missCount = 0;
            _missedKeys.Clear();

        if (_allTranslations == null)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("EricGameLauncher.text.yaml");
                if (stream == null) return;

                using var reader = new StreamReader(stream);
                string yaml = reader.ReadToEnd();
                var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                    .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                    .Build();
                _allTranslations = deserializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(yaml);

                if (_allTranslations != null && _allTranslations.TryGetValue("CLI", out var cliDict))
                {
                    _cliStrings = cliDict;
                    _allTranslations.Remove("CLI");
                }
            }
            catch (Exception ex) { LogService.Write("App", $"Text Load failed for lang={langCode}", ex); }
        }

        if (_allTranslations != null && _allTranslations.TryGetValue(langCode, out var dict))
        {
            _strings = dict;
        }

            LanguageChanged?.Invoke();
            LogService.Write("App", $"Text Load End: dictSize={_allTranslations?.Count ?? 0}, duration={sw.ElapsedMilliseconds}ms");
        }
    }

    public static string T(string key)
    {
        _lookupCount++;
        if (_strings.TryGetValue(key, out var value)) return value;
        _missCount++;
        _missedKeys.Add(key);
        return key;
    }

    public static string Cli(string key)
    {
        if (_cliStrings != null && _cliStrings.TryGetValue(key, out var value)) return value;
        return key;
    }

    public static void FlushSummary()
    {
        if (_lookupCount == 0) return;
        if (_missCount > 0)
        {
            LogService.Write("App", $"Text Summary: {_lookupCount} lookups, {_missCount} MISSING keys: [{string.Join(", ", _missedKeys)}]");
        }
        else
        {
            LogService.Write("App", $"Text Summary: {_lookupCount} lookups, all resolved (lang={_currentLanguage})");
        }
        _lookupCount = 0;
        _missCount = 0;
        _missedKeys.Clear();
    }

    public static List<string> GetAvailableLanguages()
    {
        try { LogService.Write("App", "GetAvailableLanguages called"); } catch { }
        if (_allTranslations == null)
        {
            Load(_currentLanguage);
        }

        if (_allTranslations != null)
        {
            return new List<string>(_allTranslations.Keys);
        }

        return new List<string> { "Zh-CN", "EN" };
    }

    private static string? _cachedSystemLanguage = null;

    public static string DetectSystemLanguage()
    {
        if (_cachedSystemLanguage != null) return _cachedSystemLanguage;
        try
        {
            LogService.Write("App", "DetectSystemLanguage called");
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            _cachedSystemLanguage = culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ? "Zh-CN" : "EN";
            LogService.Write("App", $"DetectSystemLanguage result={_cachedSystemLanguage}");
            return _cachedSystemLanguage;
        }
        catch { _cachedSystemLanguage = "EN"; return _cachedSystemLanguage; }
    }

    public static string GetDisplayName(string langCode)
    {
        try { LogService.Write("App", $"GetDisplayName called langCode={langCode}"); } catch { }
        string nativeName = langCode;
        if (_allTranslations != null &&
            _allTranslations.TryGetValue(langCode, out var dict) &&
            dict.TryGetValue("_LangName", out var native) &&
            !string.IsNullOrEmpty(native))
            nativeName = native;

        string localizedKey = "LangName_" + langCode;
        if (_strings.TryGetValue(localizedKey, out var localized) &&
            !string.IsNullOrEmpty(localized) &&
            localized != localizedKey)
            return localized;

        return nativeName;
    }
}
