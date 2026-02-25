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
        private Timer? _hourlyTimer;
        private Timer? _dailySummaryTimer;
        private Func<List<Project>>? _getProjects;
        private Action<string, string>? _showBanner;

        public void Start(Func<List<Project>> getProjects, Action<string, string> showBanner)
        {
            _getProjects = getProjects;
            _showBanner = showBanner;

            // Check immediately on start
            CheckDueTasks();

            // Check every hour
            _hourlyTimer = new Timer(_ => CheckDueTasks(), null,
                TimeSpan.FromHours(1), TimeSpan.FromHours(1));

            // Schedule daily summary at 9am
            ScheduleDailySummary();
        }

        public void Stop()
        {
            _hourlyTimer?.Dispose();
            _dailySummaryTimer?.Dispose();
        }

        private void CheckDueTasks()
        {
            if (_getProjects == null || _showBanner == null) return;

            var today = DateTime.Today;
            var overdue = new List<string>();
            var dueToday = new List<string>();

            foreach (var project in _getProjects())
            {
                foreach (var task in project.Tasks)
                {
                    if (task.Status is Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate) continue;
                    if (task.DueDate == null) continue;

                    var due = task.DueDate.Value.Date;
                    if (due < today)
                        overdue.Add(task.Title);
                    else if (due == today)
                        dueToday.Add(task.Title);
                }
            }

            if (overdue.Count > 0)
                _showBanner("Overdue Tasks",
                    $"{overdue.Count} task{(overdue.Count > 1 ? "s are" : " is")} overdue: {string.Join(", ", overdue.Take(3))}{(overdue.Count > 3 ? "..." : "")}");

            if (dueToday.Count > 0)
                _showBanner("Due Today",
                    $"{dueToday.Count} task{(dueToday.Count > 1 ? "s are" : " is")} due today: {string.Join(", ", dueToday.Take(3))}{(dueToday.Count > 3 ? "..." : "")}");
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
            int dueToday = 0, overdue = 0, total = 0;

            foreach (var project in _getProjects())
            {
                foreach (var task in project.Tasks)
                {
                    if (task.Status is Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate) continue;
                    total++;
                    if (task.DueDate?.Date == today) dueToday++;
                    else if (task.DueDate?.Date < today) overdue++;
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