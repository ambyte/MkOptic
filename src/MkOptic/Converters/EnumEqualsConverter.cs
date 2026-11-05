using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace MkOptic.Converters;

/// <summary>
/// Two-way converter between an enum value and a "this option is selected" flag (for radio buttons).
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static EnumEqualsConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && Equals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter : BindingOperations.DoNothing;
}
