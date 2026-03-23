using System.Globalization;
using Avalonia.Data.Converters;
using Singularidi.Themes;

namespace Singularidi.Converters;

public class ColorModeToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is NoteColorMode mode && parameter is string param
            && Enum.TryParse<NoteColorMode>(param, out var target))
            return mode == target;

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string param
            && Enum.TryParse<NoteColorMode>(param, out var target))
            return target;

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
