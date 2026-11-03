namespace EricGameLauncher;

internal enum ExitSaveDecision { Stay, Retry, Discard }

internal static class ExitSaveController
{
    internal static async Task<bool> ConfirmAsync(Action save, Func<Exception, Task<ExitSaveDecision>> choose)
    {
        while (true)
        {
            try { save(); return true; }
            catch (Exception ex)
            {
                LogService.Write("App", "Exit save failed; awaiting decision", ex);
                var decision = await choose(ex);
                if (decision == ExitSaveDecision.Discard) return true;
                if (decision != ExitSaveDecision.Retry) return false;
            }
        }
    }
}
