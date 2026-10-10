using System.Globalization;
using System.Windows.Data;
using AionDPS.Data;

namespace AionDPS.Ui;

/// <summary>
/// Class name (the English wire name) -> the name in the UI language, as the game client spells it
/// (<see cref="ClassCatalog.DisplayName"/>), for the places a class name is bound at runtime.
/// </summary>
public sealed class ClassNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string className && className.Length > 0
            ? ClassCatalog.DisplayName(className, LocalizationManager.Instance.Language)
            : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
