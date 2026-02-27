// Copyright (C) 2026 Lewandowskista
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

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
