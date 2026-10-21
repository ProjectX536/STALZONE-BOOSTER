using System.Globalization;
using System.Windows.Data;

namespace Booster.App.Converters;

/// <summary>Отрицание bool в обе стороны: вторая из пары радиокнопок, поле, доступное при выключенном флаге.</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
