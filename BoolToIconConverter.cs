using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace StudioLog.Converters
{
    public class BoolToIconConverter : IValueConverter
    {
        public static readonly BoolToIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
            {
                // Return a checkmark TextBlock
                return new TextBlock
                {
                    Text = "✓",
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.Bold,
                    FontSize = 14
                };
            }

            return null; // No icon when not selected
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
