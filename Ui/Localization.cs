using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace AionSniffer.Ui;

/// <summary>
/// The GUI's own display language -- independent of whatever language Chat.log happens to be
/// written in (see ChatLog/ChatLogParser's multi-language remarks): a German player can run an
/// English game client, or vice versa, and the two settings have nothing to do with each other.
/// Backed by assets/i18n/ui_strings.json, a flat "key -> {language code: text}" table covering the
/// menu bar, buttons, group headers and tooltips -- deliberately NOT game-proper-noun class/faction
/// names (Gladiator, Elyos, ...), which stay in their game-canonical English form for now; see
/// assets/README.md for why translating those safely needs a verified per-language mapping this
/// project doesn't have yet (its own class_names_multilang.json is explicitly not index-aligned).
///
/// A single process-wide instance (<see cref="Instance"/>) rather than one per window: every open
/// window's bindings need to repaint together the instant the language changes in Settings, and a
/// shared INotifyPropertyChanged source is what makes that automatic instead of needing every
/// window to be told individually.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    /// <summary>The eight languages this build actually has data for -- exactly the set
    /// ChatLogParser also understands, and (mostly) the set OriginAion's own L10N folders cover;
    /// see assets/README.md for the "ita"/"plk" folder-name swap that this list's codes already
    /// correct for. NativeName is what a speaker of that language would call it themselves, shown
    /// in the Settings picker so nobody has to already read the current language to find their own.
    /// Declared BEFORE Instance below deliberately: static field initializers run in declaration
    /// order, and Instance's own initializer runs the constructor, which needs this list already
    /// built (via DetectSystemLanguage) -- the two were briefly the other way around and threw
    /// ArgumentNullException("source") out of DetectSystemLanguage's own Any() call, the whole GUI
    /// failing to start with no window ever appearing. Caught by a real launch, not the CLI
    /// selftest, which never constructs this class.</summary>
    public static readonly IReadOnlyList<(string Code, string NativeName)> SupportedLanguages = new[]
    {
        ("en", "English"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("es", "Español"),
        ("ru", "Русский"),
        ("pl", "Polski"),
        ("tr", "Türkçe"),
        ("zh", "中文"),
    };

    public static LocalizationManager Instance { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _table = new();
    private string _language;

    private LocalizationManager()
    {
        Load();
        _language = DetectSystemLanguage();
    }

    /// <summary>Changing this repaints every open window bound via <see cref="LocExtension"/> --
    /// no restart, no per-window plumbing. Silently ignored for a code this build has no table
    /// for, rather than switching to a half-translated state nobody asked for.</summary>
    public string Language
    {
        get => _language;
        set
        {
            if (_language == value || SupportedLanguages.All(l => l.Code != value))
            {
                return;
            }

            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        }
    }

    /// <summary>Looks up one string in the CURRENT language, falling back to English and then to
    /// the raw key itself -- so a key missing from the table (a fresh translation not added yet,
    /// or a typo in the XAML) shows up as visibly-wrong placeholder text instead of throwing or
    /// silently rendering blank.</summary>
    public string this[string key] =>
        _table.TryGetValue(key, out var byLanguage)
            ? (byLanguage.TryGetValue(_language, out var s) ? s : byLanguage.GetValueOrDefault("en", key))
            : key;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whatever the OS's own display language is, if this build has a table for it --
    /// otherwise English. Only consulted once, at startup, before MeterSettings.Language (an
    /// explicit prior choice) has a chance to override it -- see MainWindow's startup wiring.</summary>
    private static string DetectSystemLanguage()
    {
        string ui = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return SupportedLanguages.Any(l => l.Code == ui) ? ui : "en";
    }

    private void Load()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "i18n", "ui_strings.json");
            if (!File.Exists(path))
            {
                return;
            }

            var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path));
            if (raw is not null)
            {
                foreach (var (key, value) in raw)
                {
                    _table[key] = value;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A missing or corrupt table must not stop the meter from starting -- every lookup
            // above already falls back to the raw key, same as it does for one missing entry.
        }
    }
}

/// <summary>
/// XAML usage: `Header="{loc:Loc MenuFile}"` (with `xmlns:loc="clr-namespace:AionSniffer.Ui"` on
/// the window root). Resolves to a live one-way binding against
/// <see cref="LocalizationManager.Instance"/>'s indexer, not a one-shot string lookup -- so
/// changing the language in Settings repaints this element immediately, the same way any other
/// data-bound value would, with no extra code in the window behind it.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
