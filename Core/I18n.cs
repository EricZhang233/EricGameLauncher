using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace EricGameLauncher;

public static class I18n
{
    private static Dictionary<string, string> _strings = new();
    private static Dictionary<string, Dictionary<string, string>>? _allTranslations = null;
    private static string _currentLanguage = "Zh-CN";

    public static string CurrentLanguage => _currentLanguage;

    public static event Action? LanguageChanged;

    public static void Load(string langCode)
    {
        var sw = Stopwatch.StartNew();
        LogService.Write("App", $"I18n Load Start: targetLang={langCode}");
        _currentLanguage = langCode;

        if (_allTranslations == null)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("EricGameLauncher.i18n.json");
                if (stream == null) return;

                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                _allTranslations = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
            }
            catch (Exception ex) { LogService.Write("App", $"I18n Load failed for lang={langCode}: Exception={ex.Message}"); }
        }

        if (_allTranslations != null && _allTranslations.TryGetValue(langCode, out var dict))
        {
            _strings = dict;
        }

        LanguageChanged?.Invoke();
        LogService.Write("App", $"I18n Load End: dictSize={_allTranslations?.Count ?? 0}, duration={sw.ElapsedMilliseconds}ms");
    }

    public static string T(string key)
    {
        return _strings.TryGetValue(key, out var value) ? value : key;
    }

    public static List<string> GetAvailableLanguages()
    {
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

    public static string DetectSystemLanguage()
    {
        try
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            string name = culture.Name.ToLowerInvariant();
            string lang = culture.TwoLetterISOLanguageName.ToLowerInvariant();

            if (lang == "zh")
            {
                bool isTraditional = name.Contains("tw") || name.Contains("hk") ||
                                        name.Contains("mo") || name.Contains("hant");
                return isTraditional ? "Zh-TW" : "Zh-CN";
            }

            return lang switch
            {
                "ja" => "JA",
                "ko" => "KO",
                "fr" => "FR",
                "de" => "DE",
                "es" => "ES",
                _ => "EN",
            };
        }
        catch { return "EN"; }
    }

    public static string GetDisplayName(string langCode)
    {
        string nativeName = langCode;
        if (_allTranslations != null &&
            _allTranslations.TryGetValue(langCode, out var dict) &&
            dict.TryGetValue("_LangName", out var native) &&
            !string.IsNullOrEmpty(native))
            nativeName = native;

        string localizedKey = "LangName_" + langCode;
        if (_strings.TryGetValue(localizedKey, out var localized) &&
            !string.IsNullOrEmpty(localized) &&
            localized != nativeName)
            return $"{nativeName} ({localized})";

        return nativeName;
    }
}
