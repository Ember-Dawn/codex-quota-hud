namespace CodexQuotaHud;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainHudForm());
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ERROR", "unhandled exception", ex);
            throw;
        }
        finally
        {
            DebugLogger.Shutdown();
        }
    }
}
