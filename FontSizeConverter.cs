using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Q9CS_CrossPlatform
{
    public class FontSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double windowWidth && parameter is double scaleFactor)
            {
                // Calculate font size as a percentage of window width
                double fontSize = windowWidth * scaleFactor;
                // Ensure minimum and maximum font sizes
                return Math.Max(12, Math.Min(fontSize, 36));
            }
            return 14; // Default font size
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}