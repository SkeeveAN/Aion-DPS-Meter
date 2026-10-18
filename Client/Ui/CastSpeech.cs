using System.Globalization;
using System.IO;
using System.Media;
using Windows.Media.SpeechSynthesis;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>One voice the system offers, as the settings list it.</summary>
public sealed record SpeechVoice(string Id, string Language, string Name, bool Female);

/// <summary>
/// Voice output for the boss casts: says each cast once, with the Windows voice the player chose. It does not depend on the combat overlay
/// (it works with that off) and speaks only while Aion is in the foreground.
/// </summary>
public sealed class CastSpeech
{
    private readonly HashSet<string> _said = new();
    private readonly object _gate = new();
    private int _latest;
    private SoundPlayer? _player;

    /// <summary>The voices the system has, by language and name; empty when the speech engine is not there.</summary>
    public static IReadOnlyList<SpeechVoice> Voices()
    {
        try
        {
            return SpeechSynthesizer.AllVoices
                .Select(v => new SpeechVoice(v.Id, v.Language, Clean(v.DisplayName), v.Gender == VoiceGender.Female))
                .OrderBy(v => v.Language, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Array.Empty<SpeechVoice>();
        }
    }

    private static string Clean(string display) => display.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) ? display[10..] : display;

    /// <summary>The program language (a key of the string table) a voice's language tag belongs to: "de-DE" is "de", "zh-TW" its own; anything unknown is English.</summary>
    public static string TableLanguage(string tag)
    {
        if (tag.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-TW";
        }

        string two = tag.Split('-')[0].ToLowerInvariant();
        return LocalizationManager.SupportedLanguages.Any(l => l.Code == two) ? two : "en";
    }

    /// <summary>The language's name as a speaker of it writes it ("Deutsch (Deutschland)").</summary>
    public static string LanguageName(string tag)
    {
        try
        {
            string name = CultureInfo.GetCultureInfo(tag).NativeName;
            return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..] : tag;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    /// <summary>What is said for one cast (null = nothing): the skill's name (when switched on) with whom it is aimed at, and "lethal" when it is.</summary>
    public static string? Sentence(MeterSettings s, string skill, bool atYou, bool all, bool lethal, string tableLanguage)
    {
        var loc = LocalizationManager.Instance;
        bool announce = (atYou && s.SpeechOnYou) || (all && s.SpeechOnAll) || (lethal && atYou && s.SpeechLethalYou) || (lethal && all && s.SpeechLethalAll);
        if (!announce)
        {
            return null;
        }

        string text = skill;
        if (!s.SpeechSkillName || string.IsNullOrWhiteSpace(skill))
        {
            text = loc.In("Speech.Warn", tableLanguage);
        }
        else if (atYou)
        {
            text += " " + loc.In("Speech.OnYou", tableLanguage);
        }
        else if (all)
        {
            text += " " + loc.In("Speech.OnAll", tableLanguage);
        }

        return lethal ? text + ", " + loc.In("Speech.Lethal", tableLanguage) : text;
    }

    /// <summary>The language the words around the skill's name are spoken in: the chosen voice's.</summary>
    public static string WordsLanguage(MeterSettings s) => TableLanguage(s.SpeechLanguage.Length > 0 ? s.SpeechLanguage : LocalizationManager.Instance.Language);

    /// <summary>Looks at the casts of the moment and speaks the new ones that are meant; called with the tick of the combat overlay.</summary>
    public void Tick(Aion2EntityDirectory? dir, MeterSettings settings)
    {
        if (!settings.SpeechOn || dir is null)
        {
            lock (_gate)
            {
                _said.Clear();
            }

            return;
        }

        if (GameWindow.ForegroundClientArea() is null)
        {
            return;
        }

        DateTime now = DateTime.Now;
        string words = WordsLanguage(settings);
        var fresh = new List<string>();
        var keys = new HashSet<string>();
        foreach (CastRowData row in CombatOverlayData.Casts(dir, settings, now, LocalizationManager.Instance.Language, all: true))
        {
            keys.Add(row.Key);
            bool known;
            lock (_gate)
            {
                known = !_said.Add(row.Key);
            }

            // a cast seen only after it ended (the setting was just switched on, or the game window just came forward) is stale
            if (known || row.ElapsedMs > row.CastMs || Sentence(settings, row.Name, row.AtYou, row.All, row.Lethal, words) is not { } sentence)
            {
                continue;
            }

            fresh.Add(sentence);
        }

        lock (_gate)
        {
            _said.IntersectWith(keys); // what no longer runs is forgotten, so the set does not grow over a long session
        }

        if (fresh.Count > 0)
        {
            Speak(string.Join(". ", fresh), settings.SpeechVoiceId);
        }
    }

    /// <summary>Speaks a text now (a newer one cuts off the one still running); also what the play buttons of the settings use.</summary>
    public void Speak(string text, string voiceId)
    {
        int mine = Interlocked.Increment(ref _latest);
        _ = Task.Run(async () =>
        {
            try
            {
                using var synth = new SpeechSynthesizer();
                if (SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId) is { } voice)
                {
                    synth.Voice = voice;
                }

                using var stream = await synth.SynthesizeTextToStreamAsync(text);
                var bytes = new byte[stream.Size];
                using (var reader = new Windows.Storage.Streams.DataReader(stream))
                {
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(bytes);
                }

                if (mine != Volatile.Read(ref _latest))
                {
                    return; // a newer text came while this one was made
                }

                SoundPlayer player = new(new MemoryStream(bytes));
                lock (_gate)
                {
                    _player?.Stop();
                    _player = player;
                }

                player.Play();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // no voice or no audio device: the meter goes on without the announcement
            }
        });
    }
}
