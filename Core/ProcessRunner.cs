using System.Diagnostics;
using System.IO;
using System.Text;

namespace EricGameLauncher;

public static class ProcessRunner
{
    public static void Run(string path, bool admin)
    {
        if (string.IsNullOrEmpty(path)) return;

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
                else if (path.Contains("://"))
                {
                    psi.FileName = path;
                }
                else
                {
                    var (filePath, arguments) = SplitPath(path);
                    psi.FileName = filePath;

                    try
                    {
                        string? dir = Path.GetDirectoryName(filePath);
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
                }
                catch (Exception ex)
                {
                    LogService.Write("App", $"RunProcess start exception file={psi.FileName} args={psi.Arguments}", ex);
                    throw;
                }
            }
            catch (Exception ex) { LogService.Write("App", "RunProcess failed", ex); }
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

        int lastSpaceIndex = input.LastIndexOf(' ');
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
                currentIndex = input.LastIndexOf(' ', currentIndex - 1);
            }
        }

        return (Environment.ExpandEnvironmentVariables(input), string.Empty);
    }
}
