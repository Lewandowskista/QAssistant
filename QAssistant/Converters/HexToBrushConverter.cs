using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;

namespace QAssistant.Converters
{
    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            try
            {
                if (value is string hexColor && !string.IsNullOrEmpty(hexColor))
                {
                    // Remove # if present
                    var hex = hexColor.StartsWith("#") ? hexColor.Substring(1) : hexColor;
                    
                    // Parse hex to RGB
                    if (hex.Length == 6)
                    {
                        byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
                        byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
                        byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
                        
                        return new SolidColorBrush(Color.FromArgb(255, r, g, b));
                    }
                }
            }
            catch { }
            
            // Default color if conversion fails
            return new SolidColorBrush(Color.FromArgb(255, 167, 139, 250)); // Purple
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
