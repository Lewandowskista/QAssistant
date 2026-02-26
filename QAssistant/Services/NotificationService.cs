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
