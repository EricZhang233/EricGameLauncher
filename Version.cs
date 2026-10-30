namespace EricGameLauncher
{
    public static class AppVersion
    {
        public const string Version = "1.3.0";
        public static string DisplayVersion
        {
            get
            {
                return $"Ver.{Version}";
            }
        }
    }
}