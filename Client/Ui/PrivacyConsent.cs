using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace AionDPS.Ui;

/// <summary>
/// The player's answer to the privacy policy (<see cref="MeterSettings.PrivacyDecision"/>): nothing is uploaded without a current "accepted"; a "declined" also puts the meter in its
/// restricted mode (see <see cref="Restricted"/>). The policy has a version: when its text changes, <see cref="Version"/> is raised here (and on the website's privacy page, in all
/// languages) and everybody who accepted is asked once more. Keep the three in step - see the project memory.
/// </summary>
public static class PrivacyConsent
{
    /// <summary>Raise with every change of the privacy texts (website page and the client's texts): the accepted version in the settings is then too old and the player is asked again.</summary>
    public const int Version = 1;

    public const int NotAsked = 0, Accepted = 1, Declined = 2;

    public const string PolicyUrl = "https://aiondps.com/privacy";

    /// <summary>Raised after the answer changed (the main window shows or hides what the restricted mode removes).</summary>
    public static event Action? Changed;

    /// <summary>The accepted version is the current one.</summary>
    public static bool IsAccepted(MeterSettings s) => s.PrivacyDecision == Accepted && s.PrivacyVersion >= Version;

    /// <summary>Anything may go online: the player accepted the current policy.</summary>
    public static bool CanUpload => IsAccepted(MeterSettings.Load());

    /// <summary>The player declined: the meter shows only the DPS list and the tools that need no server and no other player's data.</summary>
    public static bool Restricted => MeterSettings.Load().PrivacyDecision == Declined;

    /// <summary>Never asked, or accepted an older version of the policy.</summary>
    public static bool NeedsAsking(MeterSettings s) => s.PrivacyDecision == NotAsked || (s.PrivacyDecision == Accepted && s.PrivacyVersion < Version);

    /// <summary>The tools that stay in the restricted mode (the others need uploads, the server or other players' data).</summary>
    public static readonly IReadOnlySet<string> RestrictedTools = new HashSet<string> { "checklist", "attributes", "statcalc", "loot", "craft", "timetable", "theme", "network" };

    /// <summary>The answer was written by somebody else (the first-start wizard): the main window re-reads it.</summary>
    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>Records the answer. A "declined" switches every upload off.</summary>
    public static void Record(bool accepted)
    {
        MeterSettings s = MeterSettings.Load();
        s.PrivacyDecision = accepted ? Accepted : Declined;
        s.PrivacyVersion = accepted ? Version : 0;
        if (!accepted)
        {
            s.AutoUploadBoss = false;
            s.AutoUploadProfile = false;
            s.UploadOtherPlayersProfiles = false;
            s.ShareLoginWithLegion = false;
        }

        s.Save();
        Changed?.Invoke();
    }

    /// <summary>Shows the policy and records the answer; true when accepted. Closing the window counts as declined.</summary>
    public static bool Ask(Window? owner)
    {
        var window = new PrivacyWindow(owner);
        window.ShowDialog();
        Record(window.Accepted);
        return window.Accepted;
    }

    /// <summary>A switch for an upload was turned on: true when that may stay (the policy is accepted, or the player accepts it now).</summary>
    public static bool EnsureForUploadSwitch(Window? owner) => CanUpload || Ask(owner);
}

/// <summary>
/// The policy text as paragraphs and bullet items: a line that starts with "• " is an item whose further lines are indented under its first word (a hanging indent), a blank line
/// separates paragraphs. Built from the plain text of the language file, so the client and the website show the same structure.
/// </summary>
public static class PolicyTextView
{
    public static StackPanel Build(string text)
    {
        var panel = new StackPanel();
        var paragraph = new List<string>();

        void Paragraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            var block = new TextBlock { Text = string.Join(" ", paragraph), TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 0, 0, 10) };
            block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            panel.Children.Add(block);
            paragraph.Clear();
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
            {
                Paragraph();
            }
            else if (line.StartsWith("• ", StringComparison.Ordinal))
            {
                Paragraph();
                var item = new Grid { Margin = new Thickness(6, 0, 0, 8) };
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var bullet = new TextBlock { Text = "•", LineHeight = 19 };
                var body = new TextBlock { Text = line[2..], TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
                bullet.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
                body.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                Grid.SetColumn(body, 1);
                item.Children.Add(bullet);
                item.Children.Add(body);
                panel.Children.Add(item);
            }
            else
            {
                paragraph.Add(line);
            }
        }

        Paragraph();
        return panel;
    }
}

/// <summary>The policy in a window of the meter's own look: the text (the same as the website's privacy page, section "What the client reads ..."), the address of the full page, and the two answers.</summary>
public sealed class PrivacyWindow : Window
{
    public bool Accepted { get; private set; }

    public PrivacyWindow(Window? owner)
    {
        var loc = LocalizationManager.Instance;
        Title = loc["Privacy.Title"];
        Width = 660;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 40);
        MinWidth = 520;
        ShowInTaskbar = owner is null;
        WindowStartupLocation = owner is { IsLoaded: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        if (owner is { IsLoaded: true })
        {
            Owner = owner;
        }

        var stack = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
        var heading = new TextBlock { Text = loc["Privacy.Heading"], FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        stack.Children.Add(heading);

        // no scrolling where the screen is tall enough (the text needs about 380 pixels); a small screen scrolls
        stack.Children.Add(new ScrollViewer { MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height - 260), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = PolicyTextView.Build(loc["Privacy.Text"]), Padding = new Thickness(0, 0, 10, 0) });

        var link = new Hyperlink(new Run(loc["Privacy.Link"])) { NavigateUri = new Uri(PrivacyConsent.PolicyUrl) };
        link.RequestNavigate += (_, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // no browser: the address is in the text
            }
        };
        stack.Children.Add(new TextBlock(link) { Margin = new Thickness(0, 10, 0, 0) });

        var note = new TextBlock { Text = loc["Privacy.DeclineNote"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 12 };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(note);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var decline = new Button { Content = loc["Privacy.Decline"], Padding = new Thickness(16, 5, 16, 5), MinWidth = 110, Margin = new Thickness(0, 0, 10, 0) };
        var accept = new Button { Content = loc["Privacy.Accept"], Padding = new Thickness(16, 5, 16, 5), MinWidth = 110, IsDefault = true };
        decline.Click += (_, _) => Close();
        accept.Click += (_, _) =>
        {
            Accepted = true;
            Close();
        };
        buttons.Children.Add(decline);
        buttons.Children.Add(accept);
        stack.Children.Add(buttons);

        Content = stack;
        ThemedChrome.Apply(this);
    }
}
