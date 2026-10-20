using System.IO;
using System.Text;

namespace AionDPS;

/// <summary>
/// What an unhandled error leaves behind: <c>crash.log</c> in the data folder, newest entry last, with the time, the version and the full
/// exception. Without it a crash left nothing to look at (the window just vanished), and the cause had to be guessed. Kept small: past
/// 256 KB the older half is dropped. Writing never throws - the log must not make a crash worse.
/// </summary>
internal static class CrashLog
{
    private const int MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(Ui.MeterSettings.DataDirectory, "crash.log");

    public static void Hook(System.Windows.Application? app)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("unhandled exception" + (e.IsTerminating ? " (the meter ends)" : ""), e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => Write("task error nobody waited for", e.Exception);
        if (app is not null)
        {
            // Only noted here: whether the error is handled is decided by whoever else listens (see Program.Main).
            app.DispatcherUnhandledException += (_, e) => Write("error in the window thread", e.Exception);
        }
    }

    public static void Write(string what, Exception? ex)
    {
        try
        {
            // The accessibility error that is skipped on purpose (see Program.Main) is no crash.
            if (ex is NullReferenceException && ex.StackTrace?.Contains("AutomationPeer", StringComparison.Ordinal) == true)
            {
                return;
            }

            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var entry = new StringBuilder()
                    .Append("=== ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("  Aion DPS ").Append(Update.AppVersion.Text)
                    .Append("  ").AppendLine(what)
                    .AppendLine(ex?.ToString() ?? "(no exception object)")
                    .AppendLine();
                string existing = File.Exists(Path) ? File.ReadAllText(Path) : "";
                if (existing.Length > MaxBytes)
                {
                    int cut = existing.IndexOf("=== ", existing.Length / 2, StringComparison.Ordinal);
                    existing = cut > 0 ? existing[cut..] : "";
                }

                File.WriteAllText(Path, existing + entry);
            }
        }
        catch
        {
            // Nothing more can be done.
        }
    }
}
