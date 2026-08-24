using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AionSniffer.Data;

namespace AionSniffer.Ui;

/// <summary>
/// ItemGrade? -> the exact color Aion itself uses for that rarity tier, so an item's name in the
/// Loot list reads the same way it would in the game's own tooltip -- per the user ("mit Farbe").
/// Colors taken directly from aioncodex.com's own CSS (item_grade_N classes, see
/// assets/README.md), not guessed from general game knowledge. A null grade (an id ItemDatabase
/// couldn't resolve at all -- confirmed for a few real ids, e.g. one that has no catalog entry on
/// aioncodex.com under either the "/4x/" or the current live database) renders muted instead of a
/// rarity color, since there's no rarity to report, only an unresolved "Item #ID" placeholder name.
/// </summary>
public sealed class ItemGradeColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Common = new(Color.FromRgb(0xff, 0xff, 0xff));
    private static readonly SolidColorBrush Rare = new(Color.FromRgb(0x69, 0xe1, 0x5e));
    private static readonly SolidColorBrush Hero = new(Color.FromRgb(0x4c, 0xcf, 0xff));
    private static readonly SolidColorBrush Unique = new(Color.FromRgb(0xf0, 0xb7, 0x1c));
    private static readonly SolidColorBrush Legendary = new(Color.FromRgb(0xf0, 0x80, 0x33));
    private static readonly SolidColorBrush Ultimate = new(Color.FromRgb(0x8f, 0x39, 0xce));
    private static readonly SolidColorBrush Unresolved = new(Color.FromRgb(0x9a, 0x9a, 0xa2));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ItemGrade.Common => Common,
        ItemGrade.Rare => Rare,
        ItemGrade.Hero => Hero,
        ItemGrade.Unique => Unique,
        ItemGrade.Legendary => Legendary,
        ItemGrade.Ultimate => Ultimate,
        _ => Unresolved,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
