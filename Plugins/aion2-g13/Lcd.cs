using System.Reflection;
using System.Runtime.InteropServices;

namespace Aion2G13Plugin;

/// <summary>The G13's display through Logitech's LCD interface (LogitechLcd.dll, part of the Logitech Gaming Software). Registers the program as an applet; without the software or the display everything here quietly does nothing.</summary>
public static class Lcd
{
    private const int Mono = 1;
    private static bool _on;
    private static readonly string[] Lines = { "", "", "", "" };

    [DllImport("LogitechLcd.dll", CharSet = CharSet.Unicode)] private static extern bool LogiLcdInit(string friendlyName, int lcdType);
    [DllImport("LogitechLcd.dll")] private static extern bool LogiLcdIsConnected(int lcdType);
    [DllImport("LogitechLcd.dll", CharSet = CharSet.Unicode)] private static extern bool LogiLcdMonoSetText(int lineNumber, string text);
    [DllImport("LogitechLcd.dll")] private static extern void LogiLcdUpdate();
    [DllImport("LogitechLcd.dll")] private static extern void LogiLcdShutdown();

    public static bool Active => _on;

    /// <summary>Why the display is not used (empty while it works).</summary>
    public static string Problem { get; private set; } = "";

    public static void Start()
    {
        NativeLibrary.SetDllImportResolver(typeof(Lcd).Assembly, Resolve);
        try
        {
            if (!LogiLcdInit("Aion 2 G13 Plugin", Mono))
            {
                Problem = "Die Logitech-Software hat das Plugin nicht angenommen (läuft sie?).";
                return;
            }

            _on = true;
            Problem = LogiLcdIsConnected(Mono) ? "" : "Kein G13-Display gefunden (Gerät nicht verbunden?).";
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Problem = "LogitechLcd.dll nicht gefunden oder passt nicht (" + ex.GetType().Name + "). Die Anzeige bleibt aus, der Profilwechsel geht trotzdem.";
        }
    }

    public static void Show(string line0, string line1 = "", string line2 = "", string line3 = "")
    {
        if (!_on)
        {
            return;
        }

        try
        {
            string[] text = { line0, line1, line2, line3 };
            for (int i = 0; i < 4; i++)
            {
                if (text[i] != Lines[i])
                {
                    Lines[i] = text[i];
                    LogiLcdMonoSetText(i, text[i].Length > 26 ? text[i][..26] : text[i]);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            _on = false;
        }
    }

    /// <summary>The interface wants an update call now and then (about 10 times a second is plenty).</summary>
    public static void Tick()
    {
        if (!_on)
        {
            return;
        }

        try
        {
            LogiLcdUpdate();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            _on = false;
        }
    }

    public static void Stop()
    {
        if (_on)
        {
            try
            {
                LogiLcdShutdown();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
            {
            }

            _on = false;
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != "LogitechLcd.dll")
        {
            return IntPtr.Zero;
        }

        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, name) };
        foreach (string root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        }.Distinct())
        {
            foreach (string folder in new[] { "Logitech Gaming Software", Path.Combine("Logitech", "GamePanel Software"), "Logitech" })
            {
                string dir = Path.Combine(root, folder);
                try
                {
                    if (Directory.Exists(dir))
                    {
                        // the 64-bit copy first: this program is 64-bit
                        candidates.AddRange(Directory.EnumerateFiles(dir, name, SearchOption.AllDirectories)
                            .OrderByDescending(f => f.Contains("x64", StringComparison.OrdinalIgnoreCase)));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        foreach (string file in candidates)
        {
            if (File.Exists(file) && NativeLibrary.TryLoad(file, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }
}
