using System;
using System.Globalization;
using System.Windows.Data;

namespace ScadaGUI.Converters
{
    public class ValueWithUnitsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 1 || !(values[0] is double value))
            {
                return string.Empty;
            }

            string units = values.Length > 1 ? values[1] as string : null;

            return string.IsNullOrWhiteSpace(units)
                ? value.ToString("F2", culture)
                : $"{value.ToString("F2", culture)} {units}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
