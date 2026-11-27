using System.Diagnostics;

namespace Aion2G13Plugin;

public sealed class MainForm : Form
{
    private static readonly Color Back = Color.FromArgb(24, 26, 31), Panel = Color.FromArgb(34, 37, 44), Fore = Color.FromArgb(230, 232, 236), Gold = Color.FromArgb(232, 190, 90), Dim = Color.FromArgb(150, 155, 165);

    private readonly Settings _settings = Settings.Load();
    private readonly Watcher _watcher = new();
    private readonly TextBox _link = new();
    private readonly Label _status = new(), _current = new();
    private readonly Dictionary<string, (ComboBox Kind, TextBox Value)> _rows = new();
    private readonly NotifyIcon _tray = new();
    private string _applied = "";
    private readonly System.Windows.Forms.Timer _lcdTimer = new() { Interval = 100 };
    private bool _startHidden;

    private static readonly string[] Kinds = { "Nichts tun", "Tastenkürzel senden", "Programm starten" };
    private static readonly string[] KindIds = { "none", "hotkey", "program" };

    public MainForm()
    {
        Text = "Aion 2 G13 Plugin " + (typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "");
        BackColor = Back;
        ForeColor = Fore;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(700, 540);
        StartPosition = FormStartPosition.CenterScreen;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(16), AutoScroll = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        Controls.Add(grid);

        Row(grid, Heading("Aion 2 G13 Plugin"), span: true);
        Row(grid, Info("Wechselt das Profil deines Logitech G13 automatisch passend zur Klasse deines Charakters in Aion 2."), span: true);
        Row(grid, Heading("Verbindung zum DPS-Tool"), span: true);
        Row(grid, Info("Im DPS-Tool unter Tools den Stream-Link einschalten und hier einfügen (zum Beispiel http://127.0.0.1:8920/xxxx/). Der Link bleibt auf diesem PC."), span: true);
        _link.Text = _settings.Link;
        Style(_link);
        _link.Dock = DockStyle.Fill;
        _link.Leave += (_, _) => Connect();
        grid.Controls.Add(_link, 0, grid.RowCount++);
        grid.SetColumnSpan(_link, 4);

        _status.ForeColor = Dim;
        _status.AutoSize = true;
        Row(grid, _status, span: true);
        _current.ForeColor = Gold;
        _current.Font = new Font(Font, FontStyle.Bold);
        _current.AutoSize = true;
        _current.Text = "Erkannte Klasse: noch keine";
        Row(grid, _current, span: true);

        Row(grid, Heading("Was soll bei welcher Klasse passieren?"), span: true);
        Row(grid, Info("Tastenkürzel: in der Logitech-Software legst du auf ein Profil-Umschalten ein Kürzel (zum Beispiel Strg+Alt+F13, F13 bis F24 nutzt kaum ein Spiel). Dasselbe Kürzel trägst du hier bei der Klasse ein. Das Programm sendet es, sobald diese Klasse erkannt wird."), span: true);

        foreach (string cls in Settings.Classes)
        {
            var rule = _settings.RuleFor(cls);
            var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Panel, ForeColor = Fore };
            kind.Items.AddRange(Kinds);
            kind.SelectedIndex = Math.Max(0, Array.IndexOf(KindIds, rule.Kind));
            var value = new TextBox { Text = rule.Value };
            Style(value);
            value.Dock = DockStyle.Fill;
            var test = new Button { Text = "Testen", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Panel, ForeColor = Fore };
            string name = cls;
            kind.SelectedIndexChanged += (_, _) => SaveRules();
            value.TextChanged += (_, _) => SaveRules();
            test.Click += (_, _) => Apply(name, manual: true);
            _rows[cls] = (kind, value);

            int r = grid.RowCount++;
            grid.Controls.Add(new Label { Text = cls, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Fore }, 0, r);
            grid.Controls.Add(kind, 1, r);
            grid.Controls.Add(value, 2, r);
            grid.Controls.Add(test, 3, r);
        }

        Row(grid, Info("Hinweis: Der eigene Charakter wird erkannt, sobald das DPS-Tool ihn in der Tabelle zeigt (nach dem ersten Kampf nach dem Anmelden). Das Tool wechselt nur, wenn sich die Klasse ändert."), span: true);

        _tray.Icon = SystemIcons.Application;
        _tray.Text = "Aion 2 G13 Plugin " + (typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "");
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; };
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add("Öffnen", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; });
        _tray.ContextMenuStrip.Items.Add("Beenden", null, (_, _) => { _tray.Visible = false; Application.Exit(); });

        _startHidden = _settings.Link.Length > 0;
        Lcd.Start();
        _lcdTimer.Tick += (_, _) => Lcd.Tick();
        _lcdTimer.Start();
        ShowLcd("wartet auf Kampf");
        if (Lcd.Problem.Length > 0)
        {
            _status.Text = Lcd.Problem;
        }

        _watcher.StatusChanged += s => BeginInvoke(() => _status.Text = s);
        _watcher.ClassSeen += cls => BeginInvoke(() => ClassSeen(cls));
        Load += (_, _) => Connect();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) { Hide(); } };
        FormClosing += (_, e) => { _watcher.Stop(); _lcdTimer.Stop(); Lcd.Stop(); _tray.Visible = false; };
    }

    /// <summary>With a link saved the program starts in the tray (the Logitech software starts it with the PC); without one the window opens for the set-up.</summary>
    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden)
        {
            _startHidden = false;
            if (!IsHandleCreated)
            {
                CreateHandle();
            }

            Connect();
            base.SetVisibleCore(false);
            return;
        }

        base.SetVisibleCore(value);
    }

    private void ShowLcd(string line2, string line3 = "") =>
        Lcd.Show("Aion 2", _applied.Length > 0 ? _applied : "Klasse: ?", line2, line3);

    private void Connect()
    {
        _settings.Link = _link.Text.Trim();
        _settings.Save();
        _watcher.Start(_settings.Link);
    }

    private void SaveRules()
    {
        foreach (var (cls, (kind, value)) in _rows)
        {
            var rule = _settings.RuleFor(cls);
            rule.Kind = KindIds[Math.Max(0, kind.SelectedIndex)];
            rule.Value = value.Text.Trim();
        }

        _settings.Save();
    }

    private void ClassSeen(string seen)
    {
        string? cls = Settings.Classes.FirstOrDefault(c => string.Equals(c, seen, StringComparison.OrdinalIgnoreCase));
        if (cls is null)
        {
            _current.Text = $"Erkannte Klasse: {seen} (unbekannt)";
            return;
        }

        _current.Text = $"Erkannte Klasse: {cls}";
        if (cls != _applied)
        {
            _applied = cls;
            Apply(cls, manual: false);
        }
    }

    private void Apply(string cls, bool manual)
    {
        var rule = _settings.RuleFor(cls);
        string done;
        try
        {
            switch (rule.Kind)
            {
                case "hotkey" when rule.Value.Length > 0:
                    done = Keys.Send(rule.Value) ? $"Kürzel {rule.Value} gesendet" : $"Kürzel '{rule.Value}' ungültig oder nicht gesendet";
                    break;
                case "program" when rule.Value.Length > 0:
                    Process.Start(new ProcessStartInfo(rule.Value) { UseShellExecute = true });
                    done = $"Programm gestartet: {rule.Value}";
                    break;
                default:
                    done = "nichts eingestellt";
                    break;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            done = "Fehler: " + ex.Message;
        }

        _status.Text = (manual ? "Test " : "Wechsel ") + cls + ": " + done;
        if (!manual)
        {
            ShowLcd(rule.Kind == "none" ? "kein Wechsel" : "Profil gewechselt", rule.Kind == "hotkey" ? rule.Value : "");
        }
    }

    private Label Heading(string text) => new() { Text = text, AutoSize = true, ForeColor = Gold, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };

    private Label Info(string text) => new() { Text = text, ForeColor = Dim, MaximumSize = new Size(700, 0), AutoSize = true, Margin = new Padding(0, 0, 0, 6) };

    private static void Style(TextBox box)
    {
        box.BackColor = Panel;
        box.ForeColor = Fore;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void Row(TableLayoutPanel grid, Control control, bool span)
    {
        grid.Controls.Add(control, 0, grid.RowCount++);
        if (span)
        {
            grid.SetColumnSpan(control, 4);
        }
    }
}
