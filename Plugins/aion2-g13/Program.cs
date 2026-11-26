namespace Aion2G13Plugin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, "Aion2G13Plugin.Single", out bool first);
        if (!first)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
