internal static class UiHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) return 1;
        return UiFeatures.Run(args[0], args[1]);
    }
}
