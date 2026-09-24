using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using AionDPS.Data;

namespace AionDPS.Ui;

/// <summary>
/// Skill name (as it appears in DamageEvent.Skill/SkillRow.Skill -- a Chat.log-localized, possibly
/// rank-suffixed string) -> icon, for PlayerDetailsWindow's skill table. Unlike
/// ClassIconConverter/RaceIconConverter, the bound string is not itself the file name: it goes
/// through SkillDatabase.FindByLocalizedName first (handles EN/DE/FR and rank suffixes like "Rupture
/// IV") to get the SkillInfo.Icon file name aioncodex.com actually shipped under
/// assets/skills/icons/. Returns null (no icon shown) for anything the database doesn't recognise --
/// a handful of 4.6 skills without a textual match, or an unmapped ability -- rather than guessing a
/// file name that likely does not exist.
/// </summary>
public sealed class SkillIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string skillName || skillName.Length == 0)
        {
            return null;
        }

        string? icon = SkillDatabase.FindByLocalizedName(skillName)?.Icon;
        if (string.IsNullOrEmpty(icon))
        {
            return null;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "skills", "icons", icon);
        return File.Exists(path) ? new BitmapImage(new Uri(path, UriKind.Absolute)) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
