using System.Text.RegularExpressions;

namespace EricGameLauncher;

public static class SensitiveText
{
    public static string SettingValue(string key, string value) => key.Equals("githubtoken", StringComparison.OrdinalIgnoreCase) ? "[redacted]" : value;
    public static string Redact(string value)
    {
        string scrubbed = Regex.Replace(value, @"(?i)(githubToken[\""']?\s*[:=]\s*[\""']?)([^\s\""']+)", "$1[redacted]");
        return Regex.Replace(scrubbed, @"\b(?:ghp_|github_pat_)[A-Za-z0-9_]+", "[redacted]");
    }
}
