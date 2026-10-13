using System.IO;
using AionDPS.History;
using AionDPS.Ui;
using Microsoft.Data.Sqlite;

namespace AionDPS.Combat;

/// <summary>Settings that survive a crash (atomic save, a copy of the last start), "reset to defaults" with its undo, and the history remembering which boss fights went online.</summary>
public static class SelfCheckSettings
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Settings backup, reset with undo, uploaded fights:");
        string dir = Path.Combine(Path.GetTempPath(), "aiondps-settings-selftest-" + Guid.NewGuid().ToString("N"));
        string? before = Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR");
        Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", dir);
        bool ok = true;
        try
        {
            var settings = new MeterSettings { HotkeyHideUi = "Ctrl+Alt+Q", AutoUploadBoss = true, Language = "de", SetupCompleted = true };
            settings.Save();
            bool noTemp = !File.Exists(Path.Combine(dir, "meter-settings.json.tmp"));
            MeterSettings.BackupOnStart();

            // a power cut while it was written: half a file
            File.WriteAllText(Path.Combine(dir, "meter-settings.json"), "{ \"HotkeyHideUi\": \"Ctrl+Al");
            MeterSettings back = MeterSettings.Load();
            bool restored = back.HotkeyHideUi == "Ctrl+Alt+Q" && back.AutoUploadBoss && MeterSettings.RecoveredFromBackup;
            bool kept = Directory.GetFiles(dir, "meter-settings.damaged-*.json").Length == 1;
            Console.WriteLine($"  -> saving leaves no half file ({noTemp}); a damaged file comes back from the last start's copy ({restored}) and is kept ({kept})");
            ok &= noTemp && restored && kept;

            // reset keeps what must never change by itself, and can be taken back
            back.Save();
            MeterSettings.ResetToDefaults();
            MeterSettings reset = MeterSettings.Load();
            bool defaults = reset.HotkeyHideUi == new MeterSettings().HotkeyHideUi && reset.Language == "";
            bool kept2 = reset.AutoUploadBoss && reset.SetupCompleted;
            bool undone = MeterSettings.UndoReset() && MeterSettings.Load().HotkeyHideUi == "Ctrl+Alt+Q" && !MeterSettings.UndoReset();
            Console.WriteLine($"  -> reset gives the defaults ({defaults}), keeps the upload choice and the wizard ({kept2}), and undo brings it all back once ({undone})");
            ok &= defaults && kept2 && undone;
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", before);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        return ok;
    }
}
