using System;
using System.Collections.Generic;
using System.Linq;
using DesktopApp.Models;
using DesktopApp.Services;
using DesktopApp.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace DesktopApp.Views
{
    public sealed partial class TasksPage : Page
    {
        private MainViewModel? _vm;
        private bool _isLinearMode = false;
        private List<ProjectTask> _linearTasks = new();

        public TasksPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                if (_vm.SelectedProject != null)
                    RefreshBoard(_vm.SelectedProject.Tasks);
            }
        }

        private void RefreshBoard(IEnumerable<ProjectTask> tasks)
        {
            try
            {
                var list = tasks?.ToList() ?? new List<ProjectTask>();

                var todo = list.Where(t => t.Status == Models.TaskStatus.Todo).ToList();
                var inProgress = list.Where(t => t.Status == Models.TaskStatus.InProgress).ToList();
                var inReview = list.Where(t => t.Status == Models.TaskStatus.InReview).ToList();
                var done = list.Where(t => t.Status == Models.TaskStatus.Done).ToList();
                var blocked = list.Where(t => t.Status == Models.TaskStatus.Blocked).ToList();

                TodoList.ItemsSource = todo;
                InProgressList.ItemsSource = inProgress;
                InReviewList.ItemsSource = inReview;
                DoneList.ItemsSource = done;
                BlockedList.ItemsSource = blocked;

                TodoCount.Text = todo.Count.ToString();
                InProgressCount.Text = inProgress.Count.ToString();
                InReviewCount.Text = inReview.Count.ToString();
                DoneCount.Text = done.Count.ToString();
                BlockedCount.Text = blocked.Count.ToString();
            }
            catch (Exception ex)
            {
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = $"Board error: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task FetchLinearIssuesAsync()
        {
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = "Fetching Linear issues...";

            var key = CredentialService.LoadCredential("LinearApiKey");
            var teamId = CredentialService.LoadCredential("LinearTeamId");

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(teamId))
            {
                StatusText.Text = "No credentials found. Go to Settings.";
                return;
            }

            try
            {
                var service = new LinearService(key);
                _linearTasks = await service.GetIssuesAsync(teamId);

                if (_linearTasks.Count == 0)
                    StatusText.Text = "No issues found in this team.";
                else
                {
                    RefreshBoard(_linearTasks);
                    StatusText.Text = $"Synced {_linearTasks.Count} issues · {DateTime.Now:h:mm tt}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void ManualMode_Click(object sender, RoutedEventArgs e)
        {
            _isLinearMode = false;
            ManualModeBtn.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250));
            ManualModeBtn.Foreground = new SolidColorBrush(Colors.White);
            LinearModeBtn.Background = new SolidColorBrush(Colors.Transparent);
            LinearModeBtn.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
            AddTaskBtn.Visibility = Visibility.Visible;
            RefreshBtn.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Collapsed;
            RefreshBoard(_vm?.SelectedProject?.Tasks ?? new List<ProjectTask>());
        }

        private async void LinearMode_Click(object sender, RoutedEventArgs e)
        {
            _isLinearMode = true;
            LinearModeBtn.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250));
            LinearModeBtn.Foreground = new SolidColorBrush(Colors.White);
            ManualModeBtn.Background = new SolidColorBrush(Colors.Transparent);
            ManualModeBtn.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
            AddTaskBtn.Visibility = Visibility.Collapsed;
            RefreshBtn.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Visible;

            var key = CredentialService.LoadCredential("LinearApiKey");
            if (string.IsNullOrEmpty(key))
            {
                StatusText.Text = "No Linear API key found. Go to Settings to connect.";
                return;
            }

            await FetchLinearIssuesAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await FetchLinearIssuesAsync();
        }

        private async void Task_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not ProjectTask task) return;

            if (_isLinearMode)
                await ShowLinearTaskDetailAsync(task);
            else
                await ShowManualTaskDetailAsync(task);
        }

        private async System.Threading.Tasks.Task ShowLinearTaskDetailAsync(ProjectTask task)
        {
            var commentBox = new TextBox
            {
                PlaceholderText = "Add a comment...",
                AcceptsReturn = true,
                Height = 80,
                TextWrapping = TextWrapping.Wrap
            };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = task.Title,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = task.Description,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Priority: {task.Priority}",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)),
                FontSize = 12
            });

            if (!string.IsNullOrEmpty(task.Assignee))
                panel.Children.Add(new TextBlock
                {
                    Text = $"Assignee: {task.Assignee}",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12
                });

            if (!string.IsNullOrEmpty(task.Labels))
                panel.Children.Add(new TextBlock
                {
                    Text = $"Labels: {task.Labels}",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    FontSize = 12
                });

            if (task.DueDate.HasValue)
                panel.Children.Add(new TextBlock
                {
                    Text = $"Due: {task.DueDate:MMM d, yyyy}",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12
                });

            panel.Children.Add(new TextBlock { Text = "Add Comment", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(commentBox);

            var dialog = new ContentDialog
            {
                Title = task.IssueType ?? "Linear Issue",
                Content = panel,
                PrimaryButtonText = "Post Comment",
                SecondaryButtonText = "Open in Linear",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary
                && !string.IsNullOrWhiteSpace(commentBox.Text)
                && !string.IsNullOrEmpty(task.ExternalId))
            {
                var key = CredentialService.LoadCredential("LinearApiKey");
                if (!string.IsNullOrEmpty(key))
                {
                    var service = new LinearService(key);
                    await service.AddCommentAsync(task.ExternalId, commentBox.Text.Trim());
                    StatusText.Visibility = Visibility.Visible;
                    StatusText.Text = "Comment posted successfully.";
                }
            }
            else if (result == ContentDialogResult.Secondary && !string.IsNullOrEmpty(task.TicketUrl))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(task.TicketUrl));
            }
        }

        private async System.Threading.Tasks.Task ShowManualTaskDetailAsync(ProjectTask task)
        {
            if (_vm?.SelectedProject == null) return;

            var statusPicker = new ComboBox
            {
                ItemsSource = Enum.GetValues(typeof(Models.TaskStatus)),
                SelectedItem = task.Status,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var priorityPicker = new ComboBox
            {
                ItemsSource = Enum.GetValues(typeof(TaskPriority)),
                SelectedItem = task.Priority,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = task.Title,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = task.Description,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock { Text = "Status", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(statusPicker);
            panel.Children.Add(new TextBlock { Text = "Priority", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(priorityPicker);

            var dialog = new ContentDialog
            {
                Title = "Edit Task",
                Content = panel,
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                task.Status = (Models.TaskStatus)statusPicker.SelectedItem!;
                task.Priority = (TaskPriority)priorityPicker.SelectedItem!;
                await _vm.SaveAsync();
                RefreshBoard(_vm.SelectedProject.Tasks);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                _vm.SelectedProject.Tasks.Remove(task);
                await _vm.SaveAsync();
                RefreshBoard(_vm.SelectedProject.Tasks);
            }
        }

        private async void AddTask_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            var titleBox = new TextBox { PlaceholderText = "Task title..." };
            var descBox = new TextBox { PlaceholderText = "Description (optional)", AcceptsReturn = true, Height = 80 };
            var statusPicker = new ComboBox
            {
                ItemsSource = Enum.GetValues(typeof(Models.TaskStatus)),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var priorityPicker = new ComboBox
            {
                ItemsSource = Enum.GetValues(typeof(TaskPriority)),
                SelectedIndex = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var ticketBox = new TextBox { PlaceholderText = "Ticket URL (optional)" };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = "Title", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(titleBox);
            panel.Children.Add(new TextBlock { Text = "Description", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(descBox);
            panel.Children.Add(new TextBlock { Text = "Status", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(statusPicker);
            panel.Children.Add(new TextBlock { Text = "Priority", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(priorityPicker);
            panel.Children.Add(new TextBlock { Text = "Ticket URL", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(ticketBox);

            var dialog = new ContentDialog
            {
                Title = "New Task",
                Content = panel,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(titleBox.Text))
            {
                var task = new ProjectTask();
                task.Title = titleBox.Text.Trim();
                task.Description = descBox.Text.Trim();
                task.Status = (Models.TaskStatus)statusPicker.SelectedItem!;
                task.Priority = (TaskPriority)priorityPicker.SelectedItem!;
                task.TicketUrl = ticketBox.Text.Trim();
                _vm.SelectedProject.Tasks.Add(task);
                await _vm.SaveAsync();
                RefreshBoard(_vm.SelectedProject.Tasks);
            }
        }
    }
}