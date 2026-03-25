using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Host.ViewModels
{
    public class ListToStringConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is List<string> list)
            {
                return string.Join(", ", list);
            }
            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                var list = new List<string>();
                foreach (var item in str.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = item.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        list.Add(trimmed);
                    }
                }
                return list;
            }
            return new List<string>();
        }
    }
}
