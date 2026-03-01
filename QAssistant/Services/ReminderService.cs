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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using QAssistant.Models;

namespace QAssistant.Services
{
    public record NotificationItem(string Title, string Message, string Category, Guid ProjectId, Guid TaskId, DateTime? DueDate);

    public class ReminderService : IDisposable
    {
        private Timer? _precisionTimer;
        private Timer? _dailySummaryTimer;
        private Func<List<Project>>? _getProjects;
        private Action<List<NotificationItem>>? _showBanners;
        private readonly object _lock = new();
        private readonly HashSet<Guid> _notifiedOverdue = new();
        private readonly HashSet<Guid> _notifiedUpcoming = new();
        private readonly HashSet<Guid> _notifiedLater = new();
        private bool _disposed;

        private string _lastBannerState = string.Empty;

        public void Start(Func<List<Project>> getProjects, Action<List<NotificationItem>> showBanners)
        {
            ArgumentNullException.ThrowIfNull(getProjects);
            ArgumentNullException.ThrowIfNull(showBanners);

            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                StopCore();

                _getProjects = getProjects;
                _showBanners = showBanners;
            }

            // Check immediately on start
            CheckDueTasks();

            lock (_lock)
            {
                // Guard: Stop() or Dispose() may have been called while CheckDueTasks() ran
                if (_getProjects == null || _disposed) return;

                // Check every minute for precision reminders
                _precisionTimer = new Timer(_ => SafeTimerCallback(CheckDueTasks), null,
                    TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));

                // Schedule daily summary at 9am
                ScheduleDailySummaryCore();
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                StopCore();
            }
        }

        private void StopCore()
        {
            _precisionTimer?.Dispose();
            _precisionTimer = null;
            _dailySummaryTimer?.Dispose();
            _dailySummaryTimer = null;
            _getProjects = null;
            _showBanners = null;
            _notifiedOverdue.Clear();
            _notifiedUpcoming.Clear();
            _notifiedLater.Clear();
            _lastBannerState = string.Empty;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                StopCore();
            }
            GC.SuppressFinalize(this);
        }

        public void TriggerCheck() => CheckDueTasks();

        private void CheckDueTasks()
        {
            Func<List<Project>>? getProjects;
            Action<List<NotificationItem>>? showBanners;
            lock (_lock)
            {
                if (_disposed) return;
                getProjects = _getProjects;
                showBanners = _showBanners;
            }
            if (getProjects == null || showBanners == null) return;

            // Snapshot project/task data to avoid cross-thread InvalidOperationException.
            List<(ProjectTask Task, Project Project)> taskPairs;
            try
            {
                taskPairs = getProjects()
                    .SelectMany(p => p.Tasks.ToList().Select(t => (Task: t, Project: p)))
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReminderService.CheckDueTasks snapshot error: {ex.Message}");
                return;
            }

            var now = DateTime.Now;
            var today = DateTime.Today;
            var overdueItems = new List<NotificationItem>();
            var todayItems = new List<NotificationItem>();
            var laterItems = new List<NotificationItem>();

            bool shouldUpdate;
            bool hasNewOverdue = false, hasNewToday = false, hasNewLater = false;

            lock (_lock)
            {
                if (_disposed || _getProjects == null) return;

                foreach (var (task, project) in taskPairs)
                {
                    if (task.Status is Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate)
                    {
                        _notifiedOverdue.Remove(task.Id);
                        _notifiedUpcoming.Remove(task.Id);
                        _notifiedLater.Remove(task.Id);
                        continue;
                    }

                    if (task.DueDate == null)
                    {
                        _notifiedOverdue.Remove(task.Id);
                        _notifiedUpcoming.Remove(task.Id);
                        _notifiedLater.Remove(task.Id);
                        continue;
                    }

                    var due = task.DueDate.Value;

                    if (due <= now)
                    {
                        overdueItems.Add(new("Overdue", task.Title, "Overdue", project.Id, task.Id, due));
                        _notifiedUpcoming.Remove(task.Id);
                        _notifiedLater.Remove(task.Id);
                    }
                    else if (due.Date == today)
                    {
                        todayItems.Add(new("Due Today", $"{task.Title} — {due:HH:mm}", "DueToday", project.Id, task.Id, due));
                        _notifiedOverdue.Remove(task.Id);
                        _notifiedLater.Remove(task.Id);
                    }
                    else
                    {
                        laterItems.Add(new("Upcoming", task.Title, "Later", project.Id, task.Id, due));
                        _notifiedOverdue.Remove(task.Id);
                        _notifiedUpcoming.Remove(task.Id);
                    }
                }

                // Combine all categories, prioritized, limit to 5
                var allItemsPreview = overdueItems
                    .Concat(todayItems)
                    .Concat(laterItems)
                    .Take(5);
                var stateKey = string.Join("|", allItemsPreview.Select(i => $"{i.TaskId}:{i.Category}"));
                shouldUpdate = stateKey != _lastBannerState;
                if (shouldUpdate)
                    _lastBannerState = stateKey;

                // Track NEWLY notified items (one toast per category)
                foreach (var item in overdueItems)
                {
                    if (_notifiedOverdue.Add(item.TaskId)) hasNewOverdue = true;
                }
                foreach (var item in todayItems)
                {
                    if (_notifiedUpcoming.Add(item.TaskId)) hasNewToday = true;
                }
                foreach (var item in laterItems)
                {
                    if (_notifiedLater.Add(item.TaskId)) hasNewLater = true;
                }

                // Prune stale GUIDs for tasks no longer in the data set to prevent unbounded growth
                var activeTaskIds = new HashSet<Guid>(taskPairs.Select(tp => tp.Task.Id));
                _notifiedOverdue.IntersectWith(activeTaskIds);
                _notifiedUpcoming.IntersectWith(activeTaskIds);
                _notifiedLater.IntersectWith(activeTaskIds);
            }

            // Combine outside lock for external calls
            var allItems = overdueItems
                .Concat(todayItems)
                .Concat(laterItems)
                .Take(5)
                .ToList();

            if (shouldUpdate)
            {
                try
                {
                    showBanners(allItems);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ReminderService.CheckDueTasks showBanners error: {ex.Message}");
                }
            }

            if (hasNewOverdue && overdueItems.Count > 0)
            {
                NotificationService.Instance.ShowToast("Overdue Tasks",
                    $"{overdueItems.Count} task{(overdueItems.Count > 1 ? "s are" : " is")} overdue", "Overdue");
            }
            if (hasNewToday && todayItems.Count > 0)
            {
                NotificationService.Instance.ShowToast("Due Today",
                    $"{todayItems.Count} task{(todayItems.Count > 1 ? "s are" : " is")} due today", "DueToday");
            }
            if (hasNewLater && laterItems.Count > 0)
            {
                NotificationService.Instance.ShowToast("Upcoming Tasks",
                    $"{laterItems.Count} upcoming deadline{(laterItems.Count > 1 ? "s" : "")}", "Later");
            }

            if (overdueItems.Count == 0) NotificationService.Instance.RemoveToast("Overdue");
            if (todayItems.Count == 0) NotificationService.Instance.RemoveToast("DueToday");
            if (laterItems.Count == 0) NotificationService.Instance.RemoveToast("Later");
        }

        private void ScheduleDailySummaryCore()
        {
            var now = DateTime.Now;
            var next9am = DateTime.Today.AddHours(9);
            if (now > next9am) next9am = next9am.AddDays(1);

            var delay = next9am - now;
            _dailySummaryTimer = new Timer(_ => SafeTimerCallback(ShowDailySummary), null,
                delay, TimeSpan.FromHours(24));
        }

        private void ShowDailySummary()
        {
            Func<List<Project>>? getProjects;
            Action<List<NotificationItem>>? showBanners;
            lock (_lock)
            {
                if (_disposed) return;
                getProjects = _getProjects;
                showBanners = _showBanners;
            }
            if (getProjects == null || showBanners == null) return;

            var today = DateTime.Today;
            var now = DateTime.Now;
            int dueToday = 0, overdue = 0, total = 0;

            List<ProjectTask> allTasks;
            try
            {
                allTasks = getProjects()
                    .SelectMany(p => p.Tasks.ToList())
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReminderService.ShowDailySummary snapshot error: {ex.Message}");
                return;
            }

            foreach (var task in allTasks)
            {
                if (task.Status is Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate) continue;
                total++;

                if (task.DueDate.HasValue)
                {
                    if (task.DueDate.Value < now) overdue++;
                    else if (task.DueDate.Value.Date == today) dueToday++;
                }
            }

            if (total == 0) return;

            var parts = new List<string>();
            if (dueToday > 0) parts.Add($"{dueToday} due today");
            if (overdue > 0) parts.Add($"{overdue} overdue");
            if (total > 0) parts.Add($"{total} total pending");

            try
            {
                showBanners([new("Daily Summary", string.Join(" · ", parts), "Summary", Guid.Empty, Guid.Empty, null)]);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReminderService.ShowDailySummary showBanners error: {ex.Message}");
            }
        }

        private static void SafeTimerCallback(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReminderService timer callback error: {ex.Message}");
            }
        }
    }
}