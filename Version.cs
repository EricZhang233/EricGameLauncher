namespace EricGameLauncher
{
    public static class AppVersion
    {
        public const string Version = "1.0.19.93";
        public static string DisplayVersion
        {
            get
            {
                try { LogService.Write("App", $"DisplayVersion accessed: {Version}"); } catch { }
                return $"Ver.{Version}";
            }
        }
    }
}