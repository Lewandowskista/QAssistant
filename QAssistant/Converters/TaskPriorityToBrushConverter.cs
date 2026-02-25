using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using QAssistant.Models;

namespace QAssistant.Converters
{
    public class TaskPriorityToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush LowBrush = new(Windows.UI.Color.FromArgb(255, 16, 185, 129));       // #10B981 Green
        private static readonly SolidColorBrush MediumBrush = new(Windows.UI.Color.FromArgb(255, 245, 158, 11));     // #F59E0B Yellow
        private static readonly SolidColorBrush HighBrush = new(Windows.UI.Color.FromArgb(255, 249, 115, 22));       // #F97316 Orange
        private static readonly SolidColorBrush CriticalBrush = new(Windows.UI.Color.FromArgb(255, 239, 68, 68));    // #EF4444 Red
        private static readonly SolidColorBrush DefaultBrush = new(Windows.UI.Color.FromArgb(255, 107, 114, 128));   // #6B7280

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is TaskPriority priority)
            {
                return priority switch
                {
                    TaskPriority.Low => LowBrush,
                    TaskPriority.Medium => MediumBrush,
                    TaskPriority.High => HighBrush,
                    TaskPriority.Critical => CriticalBrush,
                    _ => DefaultBrush
                };
            }
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
