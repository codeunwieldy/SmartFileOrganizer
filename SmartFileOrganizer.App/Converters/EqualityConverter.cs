using System.Globalization;

namespace SmartFileOrganizer.App.Converters
{
    public sealed class EqualityConverter : IValueConverter
    {
        public static readonly EqualityConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (parameter is string paramStr && paramStr.Contains('|'))
            {
                var parts = paramStr.Split('|');
                if (parts.Length == 2)
                {
                    // Format: "trueValue|falseValue"
                    return value is bool boolValue && boolValue ? parts[0] : parts[1];
                }
            }
            
            return Equals(value, parameter);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}