using System.Diagnostics;
using System.IO;
using System.Text;

namespace EricGameLauncher;

public sealed record LaunchResult(bool Success, string? Error = null)
{
    public void EnsureSuccess() { if (!Success) throw new InvalidOperationException(Error ?? Text.T("Launch_Failed")); }
}

public static class ProcessRunner
{
    public static string? FileTarget(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase) || command.Contains("://", StringComparison.Ordinal)) return null;
        string file = SplitPath(command).filePath;
        if (File.Exists(file) || Path.IsPathRooted(file)) return file;
        foreach (string directory in new[] { Environment.CurrentDirectory, Environment.SystemDirectory }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim('"'), file);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return file;
    }
    public static LaunchResult Run(string path, bool admin)
    {
        if (string.IsNullOrEmpty(path)) return new(false, Text.T("Launch_Failed"));

        using (LogService.StartOperation("App", $"RunProcess {path}"))
        {
            try
            {
                LogService.Write("App", $"RunProcess Start admin={admin} path={path}");
                path = Environment.ExpandEnvironmentVariables(path);

                var psi = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                if (path.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (admin)
                    {
                        string psScript = $"Start-Process '{path.Replace("'", "''")}' -Verb RunAs";
                        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));
                        psi.FileName = "powershell.exe";
                        psi.Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}";
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                    }
                    else
                    {
                        psi.FileName = "explorer.exe";
                        psi.Arguments = $"\"{path.Replace("\"", "\\\"")}\"";
                    }
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(path, @"^[a-zA-Z][a-zA-Z0-9+.-]*://"))
                {
                    psi.FileName = path;
                }
                else
                {
                    var (filePath, arguments) = SplitPath(path);
                    psi.FileName = filePath;

                    try
                    {
                        string? dir = filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? null : Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
                    }
                    catch (Exception ex) { LogService.Write("App", "Swallowed exception", ex); }

                    if (!string.IsNullOrEmpty(arguments))
                    {
                        psi.Arguments = arguments;
                    }
                    if (admin) psi.Verb = "runas";
                }

                Process? process = null;
                try
                {
                    process = Process.Start(psi);
                    if (process != null)
                        LogService.Write("App", $"RunProcess started pid={process.Id} file={psi.FileName} args={psi.Arguments}");
                    else
                        LogService.Write("App", $"RunProcess start returned null for file={psi.FileName} args={psi.Arguments}");
                    process?.Dispose();
                    return new(true);
                }
                catch (Exception ex)
                {
                    LogService.Write("App", $"RunProcess start exception file={psi.FileName} args={psi.Arguments}", ex);
                    throw;
                }
            }
            catch (Exception ex) { LogService.Write("App", "RunProcess failed", ex); return new(false, Text.T("Launch_Failed") + " " + ex.Message); }
        }
    }

    public static (string filePath, string arguments) SplitPath(string input)
    {
        try { LogService.Write("App", $"SplitPath called inputLen={(input == null ? 0 : input.Length)}"); } catch { }
        if (string.IsNullOrWhiteSpace(input))
            return (string.Empty, string.Empty);

        input = input.Trim();

        if (input.StartsWith('"'))
        {
            int endQuote = input.IndexOf('"', 1);
            if (endQuote > 0)
            {
                string filePath = input.Substring(1, endQuote - 1);
                filePath = Environment.ExpandEnvironmentVariables(filePath);
                string arguments = endQuote < input.Length - 1 ? input.Substring(endQuote + 1).Trim() : string.Empty;
                return (filePath, arguments);
            }
        }

        string expandedInput = Environment.ExpandEnvironmentVariables(input);
        if (File.Exists(expandedInput)) return (expandedInput, string.Empty);

        int lastSpaceIndex = input.LastIndexOfAny([' ', '\t']);
        if (lastSpaceIndex > 0)
        {
            int currentIndex = lastSpaceIndex;
            while (currentIndex > 0)
            {
                string potentialPath = input.Substring(0, currentIndex);
                string expandedPath = Environment.ExpandEnvironmentVariables(potentialPath);
                if (File.Exists(expandedPath))
                {
                    string arguments = input.Substring(currentIndex + 1).Trim();
                    return (expandedPath, arguments);
                }
                currentIndex = input.LastIndexOfAny([' ', '\t'], currentIndex - 1);
            }
        }

        int separator = input.IndexOfAny([' ', '\t']);
        if (separator > 0)
        {
            string command = Environment.ExpandEnvironmentVariables(input[..separator]);
            if (command.IndexOfAny(['\\', '/', ':']) < 0)
                return (command, input[(separator + 1)..].Trim());
        }
        return (expandedInput, string.Empty);
    }
}
