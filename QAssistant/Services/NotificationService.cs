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
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace QAssistant.Services
{
    public class NotificationService
    {
        private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());
        public static NotificationService Instance => _instance.Value;

        private NotificationService()
        {
            // Initialize AppNotificationManager for unpackaged app
            try
            {
                AppNotificationManager.Default.Register();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register AppNotificationManager: {ex.Message}");
            }
        }

        public void ShowToast(string title, string message, string? tag = null)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(message)
                    .BuildNotification();

                if (tag != null)
                {
                    notification.Tag = tag;
                    notification.Group = "Reminders";
                }

                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show toast: {ex.Message}");
                // Fallback attempt with manual XML if builder fails for some reason
                try
                {
                    var toastXml = $@"
<toast>
    <visual>
        <binding template='ToastGeneric'>
            <text>{System.Security.SecurityElement.Escape(title)}</text>
            <text>{System.Security.SecurityElement.Escape(message)}</text>
        </binding>
    </visual>
</toast>";
                    var toast = new AppNotification(toastXml);
                    if (tag != null)
                    {
                        toast.Tag = tag;
                        toast.Group = "Reminders";
                    }
                    AppNotificationManager.Default.Show(toast);
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"Fallback failed: {ex2.Message}");
                }
            }
        }

        public void RemoveToast(string tag)
        {
            try
            {
                _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync(tag, "Reminders");
            }
            catch { }
        }

        public void Unregister()
        {
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch { }
        }
    }
}
