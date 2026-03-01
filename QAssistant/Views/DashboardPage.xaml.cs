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
using System.Linq;
using QAssistant.Models;
using QAssistant.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace QAssistant.Views
{
    public sealed partial class DashboardPage : Page
    {
        private MainViewModel? _vm;

        public DashboardPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                Render();
            }
        }

        private void Render()
        {
            if (_vm?.SelectedProject is not { } project) return;

            ProjectNameText.Text = project.Name;
            ProjectSubtitleText.Text = $"Created {project.CreatedAt:MMMM d, yyyy}  ·  {project.Tasks.Count} tasks  ·  {project.TestCases.Count} test cases  ·  {project.Runbooks.Count} runbooks";

            // ── Metric cards ────────────────────────────────────────
            var openTasks = project.Tasks.Count(t => t.Status is not (Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate));
            OpenTasksCount.Text = openTasks.ToString();

            var blockers = project.Tasks.Count(t =>
                t.Priority == TaskPriority.Critical &&
                t.Status is not (Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate));
            BlockersCount.Text = blockers.ToString();

            var allCases = project.TestCases;
            int passed = allCases.Count(c => c.Status == TestCaseStatus.Passed);
            int total = allCases.Count;
            PassRateText.Text = total > 0 ? $"{(double)passed / total * 100:F0}%" : "—";

            int failedTests = allCases.Count(c => c.Status == TestCaseStatus.Failed);
            FailedTestsCount.Text = failedTests.ToString();

            int notRun = allCases.Count(c => c.Status == TestCaseStatus.NotRun);
            NotRunCount.Text = notRun.ToString();

            var now = DateTime.Now;
            var overdue = project.Tasks.Count(t =>
                t.DueDate.HasValue && t.DueDate.Value < now &&
                t.Status is not (Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate));
            OverdueCount.Text = overdue.ToString();

            // ── Test Case Breakdown ─────────────────────────────────
            TaskBreakdownContainer.Children.Clear();
            var caseGroups = project.TestCases
                .GroupBy(c => c.Status)
                .OrderBy(g => (int)g.Key)
                .ToList();

            foreach (var group in caseGroups)
            {
                var (label, hex) = group.Key switch
                {
                    TestCaseStatus.Passed => ("Passed", "#10B981"),
                    TestCaseStatus.Failed => ("Failed", "#EF4444"),
                    TestCaseStatus.Blocked => ("Blocked", "#F59E0B"),
                    TestCaseStatus.Skipped => ("Skipped", "#6B7280"),
                    TestCaseStatus.NotRun => ("Not Run", "#9CA3AF"),
                    _ => (group.Key.ToString(), "#6B7280")
                };
                var row = BuildBarRow(label, group.Count(), total, hex);
                TaskBreakdownContainer.Children.Add(row);
            }

            if (caseGroups.Count == 0)
            {
                TaskBreakdownContainer.Children.Add(new TextBlock
                {
                    Text = "No test cases yet",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12
                });
            }

            // ── Upcoming Due Dates ───────────────────────────────────
            UpcomingContainer.Children.Clear();
            var upcoming = project.Tasks
                .Where(t => t.DueDate.HasValue &&
                            t.DueDate.Value >= now &&
                            t.Status is not (Models.TaskStatus.Done or Models.TaskStatus.Canceled or Models.TaskStatus.Duplicate))
                .OrderBy(t => t.DueDate)
                .Take(7)
                .ToList();

            UpcomingEmptyText.Visibility = upcoming.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var t in upcoming)
            {
                var daysLeft = (t.DueDate!.Value.Date - now.Date).Days;
                string dueLabel = daysLeft == 0 ? "Today" : daysLeft == 1 ? "Tomorrow" : $"In {daysLeft} days";
                string dueColor = daysLeft == 0 ? "#F59E0B" : daysLeft <= 2 ? "#FBBF24" : "#10B981";

                var chip = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 167, 139, 250)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                    BorderThickness = new Thickness(1)
                };
                var inner = new Grid();
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var title = new TextBlock
                {
                    Text = t.Title.Length > 36 ? t.Title[..33] + "..." : t.Title,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var due = new TextBlock
                {
                    Text = dueLabel,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(dueColor[1..3], 16),
                        Convert.ToByte(dueColor[3..5], 16),
                        Convert.ToByte(dueColor[5..7], 16))),
                    FontSize = 11,
                    Margin = new Thickness(8, 0, 0, 0)
                };

                Grid.SetColumn(title, 0);
                Grid.SetColumn(due, 1);
                inner.Children.Add(title);
                inner.Children.Add(due);
                chip.Child = inner;
                UpcomingContainer.Children.Add(chip);
            }

            // ── Test Plans ───────────────────────────────────────────
            TestPlansContainer.Children.Clear();
            var plans = project.TestPlans.Where(p => !p.IsArchived).OrderByDescending(p => p.CreatedAt).Take(5).ToList();
            TestPlansEmptyText.Visibility = plans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var plan in plans)
            {
                var cases = project.TestCases.Where(c => c.TestPlanId == plan.Id).ToList();
                int planPassed = cases.Count(c => c.Status == TestCaseStatus.Passed);
                int planFailed = cases.Count(c => c.Status == TestCaseStatus.Failed);
                int planTotal = cases.Count;
                double planRate = planTotal > 0 ? (double)planPassed / planTotal * 100 : 0;
                string rateColor = planRate >= 80 ? "#10B981" : planRate >= 50 ? "#F59E0B" : "#EF4444";

                var planRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                planRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                planRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameText = new StackPanel { Spacing = 2 };
                nameText.Children.Add(new TextBlock
                {
                    Text = plan.Name,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    FontSize = 12
                });
                nameText.Children.Add(new TextBlock
                {
                    Text = $"{planTotal} cases · {planFailed} failed",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 11
                });

                var rateText = new TextBlock
                {
                    Text = planTotal > 0 ? $"{planRate:F0}%" : "—",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(rateColor[1..3], 16),
                        Convert.ToByte(rateColor[3..5], 16),
                        Convert.ToByte(rateColor[5..7], 16))),
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Grid.SetColumn(nameText, 0);
                Grid.SetColumn(rateText, 1);
                planRow.Children.Add(nameText);
                planRow.Children.Add(rateText);
                TestPlansContainer.Children.Add(planRow);
            }

            // ── Recent Notes ─────────────────────────────────────────
            RecentNotesContainer.Children.Clear();
            var notes = project.Notes.OrderByDescending(n => n.UpdatedAt).Take(5).ToList();
            NotesEmptyText.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var note in notes)
            {
                var noteBtn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 7, 10, 7),
                    BorderThickness = new Thickness(0)
                };
                var noteContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                noteContent.Children.Add(new FontIcon
                {
                    Glyph = "\uE70B",
                    FontSize = 13,
                    FontFamily = new FontFamily("Segoe Fluent Icons")
                });
                noteContent.Children.Add(new TextBlock
                {
                    Text = note.Title,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                noteBtn.Content = noteContent;
                noteBtn.Click += (s, e) => Frame.Navigate(typeof(NotesPage), _vm);
                RecentNotesContainer.Children.Add(noteBtn);
            }

            // ── Active Runbooks ─────────────────────────────────────
            RunbooksContainer.Children.Clear();
            var runbooks = project.Runbooks
                .OrderByDescending(r => r.Steps.Any(s => s.Status == RunbookStepStatus.InProgress))
                .ThenByDescending(r => r.UpdatedAt)
                .Take(5)
                .ToList();
            RunbooksEmptyText.Visibility = runbooks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var runbook in runbooks)
            {
                int stepTotal = runbook.Steps.Count;
                int stepDone = runbook.Steps.Count(s => s.Status is RunbookStepStatus.Done or RunbookStepStatus.Skipped);
                bool hasInProgress = runbook.Steps.Any(s => s.Status == RunbookStepStatus.InProgress);
                bool hasBlocked = runbook.Steps.Any(s => s.Status == RunbookStepStatus.Blocked);

                string statusHex = hasBlocked ? "#EF4444" : hasInProgress ? "#F59E0B" : stepTotal > 0 && stepDone == stepTotal ? "#10B981" : "#9CA3AF";
                string statusLabel = hasBlocked ? "Blocked" : hasInProgress ? "In Progress" : stepTotal > 0 && stepDone == stepTotal ? "Complete" : stepTotal == 0 ? "Empty" : "Pending";

                string catLabel = runbook.Category switch
                {
                    RunbookCategory.GoLive => "Go-Live",
                    _ => runbook.Category.ToString()
                };

                var rHex = Convert.ToByte(statusHex[1..3], 16);
                var gHex = Convert.ToByte(statusHex[3..5], 16);
                var bHex = Convert.ToByte(statusHex[5..7], 16);

                var runbookBtn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 7, 10, 7),
                    BorderThickness = new Thickness(0)
                };
                var innerGrid = new Grid();
                innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
                leftStack.Children.Add(new FontIcon
                {
                    Glyph = "\uE9D5",
                    FontSize = 13,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var infoStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
                infoStack.Children.Add(new TextBlock
                {
                    Text = runbook.Title,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"{catLabel}  ·  {stepDone}/{stepTotal} steps done",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 10
                });
                leftStack.Children.Add(infoStack);
                Grid.SetColumn(leftStack, 0);
                innerGrid.Children.Add(leftStack);

                var badge = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, rHex, gHex, bHex)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                badge.Child = new TextBlock
                {
                    Text = statusLabel,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, rHex, gHex, bHex)),
                    FontSize = 10
                };
                Grid.SetColumn(badge, 1);
                innerGrid.Children.Add(badge);

                runbookBtn.Content = innerGrid;
                runbookBtn.Click += (s, e) => Frame.Navigate(typeof(NotesPage), _vm);
                RunbooksContainer.Children.Add(runbookBtn);
            }
        }

        private static UIElement BuildBarRow(string label, int count, int total, string hex)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            double pct = total > 0 ? (double)count / total : 0;
            var r = Convert.ToByte(hex[1..3], 16);
            var g = Convert.ToByte(hex[3..5], 16);
            var b = Convert.ToByte(hex[5..7], 16);

            var barBg = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, r, g, b)),
                CornerRadius = new CornerRadius(4),
                Height = 8,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var barFill = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b)),
                CornerRadius = new CornerRadius(4),
                Height = 8,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0
            };
            barBg.Child = barFill;
            barBg.SizeChanged += (s, e) =>
            {
                barFill.Width = e.NewSize.Width * pct;
            };

            var countBlock = new TextBlock
            {
                Text = count.ToString(),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(barBg, 1);
            Grid.SetColumn(countBlock, 2);
            grid.Children.Add(labelBlock);
            grid.Children.Add(barBg);
            grid.Children.Add(countBlock);

            return grid;
        }
    }
}
