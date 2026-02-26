using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    public class ReminderService
    {
        private Timer? _precisionTimer;
        private Timer? _dailySummaryTimer;
        private Func<List<Project>>? _getProjects;
        private Action<string?, string?>? _showBanner;
        private readonly HashSet<Guid> _notifiedOverdue = new();
        private readonly HashSet<Guid> _notifiedUpcoming = new();

        private string? _lastBannerTitle;
        private string? _lastBannerMessage;

        public void Start(Func<List<Project>> getProjects, Action<string?, string?> showBanner)
        {
            _getProjects = getProjects;
            _showBanner = showBanner;

            // Check immediately on start
            CheckDueTasks();

            // Check every minute for precision reminders
            _precisionTimer = new Timer(_ => CheckDueTasks(), null,
                TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));

            // Schedule daily summary at 9am
            ScheduleDailySummary();
        }

        public void Stop()
        {
            _precisionTimer?.Dispose();
            _dailySummaryTimer?.Dispose();
        }

        public void TriggerCheck() => CheckDueTasks();

        private void CheckDueTasks()
        {
            if (_getProjects == null || _showBanner == null) return;

            var now = DateTime.Now;
            var currentOverdueTasks = new List<ProjectTask>();
            var currentUpcomingTasks = new List<ProjectTask>();

            foreach (var project in _getProjects())
            {
                foreach (var task in project.Tasks)
                {
                    if (task.Status is Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate)
                    {
                        _notifiedOverdue.Remove(task.Id);
                        _notifiedUpcoming.Remove(task.Id);
                        continue;
                    }

                    if (task.DueDate == null)
                    {
                        _notifiedOverdue.Remove(task.Id);
                        _notifiedUpcoming.Remove(task.Id);
                        continue;
                    }

                    var due = task.DueDate.Value;

                    if (due < now)
                    {
                        currentOverdueTasks.Add(task);
                        _notifiedUpcoming.Remove(task.Id);
                    }
                    else if (due < now.AddMinutes(30))
                    {
                        currentUpcomingTasks.Add(task);
                        _notifiedOverdue.Remove(task.Id);
                    }
                    else
                    {
                        _notifiedOverdue.Remove(task.Id);
                        _notifiedUpcoming.Remove(task.Id);
                    }
                }
            }

            string? newTitle = null;
            string? newMessage = null;
            string? tag = null;

            if (currentOverdueTasks.Count > 0)
            {
                newTitle = "Overdue Tasks";
                newMessage = $"{currentOverdueTasks.Count} task{(currentOverdueTasks.Count > 1 ? "s are" : " is")} now overdue: {string.Join(", ", currentOverdueTasks.Take(3).Select(t => t.Title))}{(currentOverdueTasks.Count > 3 ? "..." : "")}";
                tag = "Overdue";
            }
            else if (currentUpcomingTasks.Count > 0)
            {
                newTitle = "Upcoming Deadline";
                newMessage = $"{currentUpcomingTasks.Count} task{(currentUpcomingTasks.Count > 1 ? "s are" : " is")} due soon: {string.Join(", ", currentUpcomingTasks.Take(3).Select(t => t.Title))}{(currentUpcomingTasks.Count > 3 ? "..." : "")}";
                tag = "Upcoming";
            }

            // Update UI Banner if truth changed
            if (newTitle != _lastBannerTitle || newMessage != _lastBannerMessage)
            {
                _showBanner(newTitle, newMessage);
                _lastBannerTitle = newTitle;
                _lastBannerMessage = newMessage;
            }

            // Show Toast if there are NEWLY notified items
            if (newTitle != null && newMessage != null)
            {
                bool hasNew = false;
                if (tag == "Overdue")
                {
                    foreach (var t in currentOverdueTasks)
                    {
                        if (!_notifiedOverdue.Contains(t.Id))
                        {
                            hasNew = true;
                            _notifiedOverdue.Add(t.Id);
                        }
                    }
                }
                else if (tag == "Upcoming")
                {
                    foreach (var t in currentUpcomingTasks)
                    {
                        if (!_notifiedUpcoming.Contains(t.Id))
                        {
                            hasNew = true;
                            _notifiedUpcoming.Add(t.Id);
                        }
                    }
                }

                if (hasNew)
                {
                    NotificationService.Instance.ShowToast(newTitle, newMessage, tag);
                }
            }
            else
            {
                // Clear active toasts if the condition is gone
                NotificationService.Instance.RemoveToast("Overdue");
                NotificationService.Instance.RemoveToast("Upcoming");
            }
        }

        private void ScheduleDailySummary()
        {
            var now = DateTime.Now;
            var next9am = DateTime.Today.AddHours(9);
            if (now > next9am) next9am = next9am.AddDays(1);

            var delay = next9am - now;
            _dailySummaryTimer = new Timer(_ => ShowDailySummary(), null,
                delay, TimeSpan.FromHours(24));
        }

        private void ShowDailySummary()
        {
            if (_getProjects == null || _showBanner == null) return;

            var today = DateTime.Today;
            var now = DateTime.Now;
            int dueToday = 0, overdue = 0, total = 0;

            foreach (var project in _getProjects())
            {
                foreach (var task in project.Tasks)
                {
                    if (task.Status is Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate) continue;
                    total++;
                    
                    if (task.DueDate.HasValue)
                    {
                        if (task.DueDate.Value < now) overdue++;
                        else if (task.DueDate.Value.Date == today) dueToday++;
                    }
                }
            }

            if (total == 0) return;

            var parts = new List<string>();
            if (dueToday > 0) parts.Add($"{dueToday} due today");
            if (overdue > 0) parts.Add($"{overdue} overdue");
            if (total > 0) parts.Add($"{total} total pending");

            _showBanner("Daily Summary", string.Join(" · ", parts));
        }
    }
}