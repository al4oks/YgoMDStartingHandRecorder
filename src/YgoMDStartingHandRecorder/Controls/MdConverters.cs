using System.Globalization;
using Avalonia.Data.Converters;

namespace YgoMDStartingHandRecorder.Controls;

public static class MdConverters
{
    public static readonly IValueConverter NotNull = new FuncConverter(v => v != null);
    public static readonly IValueConverter IsNull = new FuncConverter(v => v == null);

    private sealed class FuncConverter(Func<object?, object?> f) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => f(value);
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}