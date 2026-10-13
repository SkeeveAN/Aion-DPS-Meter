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

            // the history: an old file's fights are "unknown", a new boss fight is "not uploaded" until it goes online - also when the upload came first
            string db = Path.Combine(dir, "fights.db");
            using (var raw = new SqliteConnection($"Data Source={db}"))
            {
                raw.Open();
                using SqliteCommand cmd = raw.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE fights (id INTEGER PRIMARY KEY AUTOINCREMENT, game TEXT NOT NULL, server_name TEXT, started_at TEXT NOT NULL, ended_at TEXT NOT NULL,
                        target_name TEXT NOT NULL, kind TEXT NOT NULL, total_damage INTEGER NOT NULL, participant_count INTEGER NOT NULL, self_name TEXT, events_gz BLOB NOT NULL);
                    CREATE TABLE participants (id INTEGER PRIMARY KEY AUTOINCREMENT, fight_id INTEGER NOT NULL, name TEXT NOT NULL, class_name TEXT NOT NULL, faction TEXT NOT NULL,
                        is_self INTEGER NOT NULL, is_enemy INTEGER NOT NULL, damage INTEGER NOT NULL, dps REAL, healing INTEGER NOT NULL, damage_taken INTEGER NOT NULL);
                    INSERT INTO fights (game, server_name, started_at, ended_at, target_name, kind, total_damage, participant_count, self_name, events_gz)
                    VALUES ('aion2', NULL, '2026-10-01T20:00:00.0000000+02:00', '2026-10-01T20:03:00.0000000+02:00', 'Old Boss', 'pve', 1, 1, NULL, x'00');
                    """;
                cmd.ExecuteNonQuery();
            }

            DateTime start = new(2026, 10, 13, 20, 0, 0, DateTimeKind.Local);
            using (var store = new FightStore(db))
            {
                bool oldUnknown = store.Query(null).Single().Upload == UploadState.Unknown && FightStore.CountNotUploaded(db) == 0;
                FightDetail Make(string target, DateTime at) => new(
                    new FightSummary(0, "aion2", null, at, at.AddSeconds(60), target, "boss", 5000, 1, "Me"),
                    new[] { new FightParticipant("Me", "Gladiator", "", true, false, 5000, 80, 0, 0) },
                    new[] { new DamageEvent(at, 1, 100, 5000, false, "Cleave") }, new Dictionary<int, string> { [1] = "Me", [100] = target });
                store.Insert(Make("New Boss", start));
                bool open = store.Query(null, onlyNotUploaded: true).Count == 1 && FightStore.CountNotUploaded(db) == 1;
                store.MarkUploaded("new boss", start.ToUniversalTime().AddSeconds(2), start.ToUniversalTime().AddSeconds(58));
                bool marked = store.Query(null, onlyNotUploaded: true).Count == 0 && store.Query("New Boss").Single().Upload == UploadState.Uploaded;
                // the upload went first, the recorder files the fight later
                DateTime later = start.AddHours(1);
                store.MarkUploaded("Second Boss", later.ToUniversalTime(), later.ToUniversalTime().AddSeconds(60));
                store.Insert(Make("Second Boss", later));
                bool early = store.Query("Second Boss").Single().Upload == UploadState.Uploaded;
                Console.WriteLine($"  -> fights from before are 'unknown' and not counted ({oldUnknown}); a boss fight is 'not uploaded' ({open}) until marked ({marked}), also when the upload came first ({early})");
                ok &= oldUnknown && open && marked && early;
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", before);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        return ok;
    }
}
