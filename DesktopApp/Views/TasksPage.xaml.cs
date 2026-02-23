using System;
using System.Collections.Generic;
using System.Linq;
using DesktopApp.Models;
using DesktopApp.Services;
using DesktopApp.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace DesktopApp.Views
{
    public sealed partial class TasksPage : Page
    {
        private MainViewModel? _vm;
        private bool _isLinearMode = false;
        private List<ProjectTask> _linearTasks = new();
        private ProjectTask? _selectedTask;

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
            CloseDetailPanel();
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
            CloseDetailPanel();

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

        private void Task_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not ProjectTask task) return;
            _selectedTask = task;
            ShowDetailPanel(task);
        }

        private void ShowDetailPanel(ProjectTask task)
        {
            // Set width of detail panel
            DetailPanelColumn.Width = new GridLength(340);

            // Header
            DetailIdentifier.Text = task.IssueIdentifier;
            DetailTitle.Text = task.Title;

            // Status badge
            var (statusColor, statusText) = GetStatusStyle(task.Status);
            DetailStatus.Text = statusText;
            DetailStatusBadge.Background = new SolidColorBrush(statusColor);
            DetailStatus.Foreground = new SolidColorBrush(Colors.White);

            // Priority badge
            DetailPriority.Text = task.Priority.ToString();

            // Meta
            if (!string.IsNullOrEmpty(task.Assignee))
            {
                DetailAssignee.Text = task.Assignee;
                DetailAssigneePanel.Visibility = Visibility.Visible;
            }
            else
            {
                DetailAssigneePanel.Visibility = Visibility.Collapsed;
            }

            if (task.DueDate.HasValue)
            {
                DetailDueDate.Text = task.DueDate.Value.ToString("MMM d, yyyy");
                DetailDueDatePanel.Visibility = Visibility.Visible;
            }
            else
            {
                DetailDueDatePanel.Visibility = Visibility.Collapsed;
            }

            if (!string.IsNullOrEmpty(task.Labels))
            {
                DetailLabels.Text = task.Labels;
                DetailLabelsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                DetailLabelsPanel.Visibility = Visibility.Collapsed;
            }

            // Description with basic markdown rendering
            RenderDescription(task.Description);

            if (_isLinearMode)
            {
                ManualTaskFields.Visibility = Visibility.Collapsed;
                ManualActions.Visibility = Visibility.Collapsed;
                LinearCommentFields.Visibility = Visibility.Visible;
                LinearActions.Visibility = Visibility.Visible;
                DetailCommentBox.Text = string.Empty;
            }
            else
            {
                LinearCommentFields.Visibility = Visibility.Collapsed;
                LinearActions.Visibility = Visibility.Collapsed;
                ManualTaskFields.Visibility = Visibility.Visible;
                ManualActions.Visibility = Visibility.Visible;

                DetailStatusPicker.ItemsSource = Enum.GetValues(typeof(Models.TaskStatus));
                DetailStatusPicker.SelectedItem = task.Status;
                DetailPriorityPicker.ItemsSource = Enum.GetValues(typeof(TaskPriority));
                DetailPriorityPicker.SelectedItem = task.Priority;

                if (task.DueDate.HasValue)
                {
                    DetailSetDueDate.IsChecked = true;
                    DetailDueDatePicker.Date = new DateTimeOffset(task.DueDate.Value);
                    DetailDueDatePicker.Visibility = Visibility.Visible;
                }
                else
                {
                    DetailSetDueDate.IsChecked = false;
                    DetailDueDatePicker.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void RenderDescription(string? description)
        {
            DetailDescription.Blocks.Clear();

            if (string.IsNullOrEmpty(description))
            {
                var para = new Paragraph();
                para.Inlines.Add(new Run
                {
                    Text = "No description provided.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
                });
                DetailDescription.Blocks.Add(para);
                return;
            }

            var lines = description.Split('\n');
            foreach (var line in lines)
            {
                var para = new Paragraph { LineHeight = 22 };

                if (line.StartsWith("# "))
                {
                    para.Inlines.Add(new Run
                    {
                        Text = line[2..],
                        FontSize = 16,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240))
                    });
                }
                else if (line.StartsWith("## "))
                {
                    para.Inlines.Add(new Run
                    {
                        Text = line[3..],
                        FontSize = 14,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240))
                    });
                }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    para.Inlines.Add(new Run
                    {
                        Text = "• " + line[2..],
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240))
                    });
                }
                else if (line.StartsWith("[image]"))
                {
                    para.Inlines.Add(new Run
                    {
                        Text = "🖼 [image]",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
                    });
                }
                else if (line.StartsWith("[code block]"))
                {
                    para.Inlines.Add(new Run
                    {
                        Text = "{ code block }",
                        FontFamily = new FontFamily("Consolas"),
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
                    });
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    DetailDescription.Blocks.Add(para);
                    continue;
                }
                else
                {
                    para.Inlines.Add(new Run
                    {
                        Text = line,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240))
                    });
                }

                DetailDescription.Blocks.Add(para);
            }
        }

        private void CloseDetailPanel()
        {
            DetailPanelColumn.Width = new GridLength(0);
            _selectedTask = null;
        }

        private void CloseDetail_Click(object sender, RoutedEventArgs e)
        {
            CloseDetailPanel();
        }

        private (Windows.UI.Color color, string text) GetStatusStyle(Models.TaskStatus status) => status switch
        {
            Models.TaskStatus.Todo => (Windows.UI.Color.FromArgb(255, 55, 65, 81), "Todo"),
            Models.TaskStatus.InProgress => (Windows.UI.Color.FromArgb(255, 30, 58, 138), "In Progress"),
            Models.TaskStatus.InReview => (Windows.UI.Color.FromArgb(255, 76, 29, 149), "In Review"),
            Models.TaskStatus.Done => (Windows.UI.Color.FromArgb(255, 6, 78, 59), "Done"),
            Models.TaskStatus.Blocked => (Windows.UI.Color.FromArgb(255, 127, 29, 29), "Blocked"),
            _ => (Windows.UI.Color.FromArgb(255, 55, 65, 81), status.ToString())
        };

        private void DetailSetDueDate_Checked(object sender, RoutedEventArgs e)
        {
            DetailDueDatePicker.Visibility = Visibility.Visible;
        }

        private void DetailSetDueDate_Unchecked(object sender, RoutedEventArgs e)
        {
            DetailDueDatePicker.Visibility = Visibility.Collapsed;
        }

        private async void SaveTaskChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null || _vm?.SelectedProject == null) return;

            _selectedTask.Status = (Models.TaskStatus)DetailStatusPicker.SelectedItem!;
            _selectedTask.Priority = (TaskPriority)DetailPriorityPicker.SelectedItem!;
            _selectedTask.DueDate = DetailSetDueDate.IsChecked == true
                ? DetailDueDatePicker.Date.DateTime : null;

            await _vm.SaveAsync();
            RefreshBoard(_vm.SelectedProject.Tasks);
            CloseDetailPanel();
        }

        private async void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null || _vm?.SelectedProject == null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Task",
                Content = $"Are you sure you want to delete '{_selectedTask.Title}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _vm.SelectedProject.Tasks.Remove(_selectedTask);
                await _vm.SaveAsync();
                RefreshBoard(_vm.SelectedProject.Tasks);
                CloseDetailPanel();
            }
        }

        private async void PostComment_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null || string.IsNullOrWhiteSpace(DetailCommentBox.Text)) return;

            var key = CredentialService.LoadCredential("LinearApiKey");
            if (string.IsNullOrEmpty(key)) return;

            try
            {
                var service = new LinearService(key);
                await service.AddCommentAsync(_selectedTask.ExternalId, DetailCommentBox.Text.Trim());
                DetailCommentBox.Text = string.Empty;
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Comment posted successfully.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error posting comment: {ex.Message}";
            }
        }

        private async void OpenInLinear_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null || string.IsNullOrEmpty(_selectedTask.TicketUrl)) return;
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_selectedTask.TicketUrl));
        }

        private async void AddTask_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            var titleBox = new TextBox { PlaceholderText = "Task title..." };
            var descBox = new TextBox
            {
                PlaceholderText = "Description (optional)",
                AcceptsReturn = true,
                Height = 80,
                TextWrapping = TextWrapping.Wrap
            };
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
            var dueDatePicker = new DatePicker
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };
            var setDueDate = new CheckBox
            {
                Content = "Set due date",
                IsChecked = false,
                Foreground = new SolidColorBrush(Colors.White)
            };
            setDueDate.Checked += (s, e) => dueDatePicker.Visibility = Visibility.Visible;
            setDueDate.Unchecked += (s, e) => dueDatePicker.Visibility = Visibility.Collapsed;

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
            panel.Children.Add(new TextBlock { Text = "Due Date", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(setDueDate);
            panel.Children.Add(dueDatePicker);

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
                var task = new ProjectTask
                {
                    Title = titleBox.Text.Trim(),
                    Description = descBox.Text.Trim(),
                    Status = (Models.TaskStatus)statusPicker.SelectedItem!,
                    Priority = (TaskPriority)priorityPicker.SelectedItem!,
                    TicketUrl = ticketBox.Text.Trim(),
                    DueDate = setDueDate.IsChecked == true ? dueDatePicker.Date.DateTime : null
                };
                _vm.SelectedProject.Tasks.Add(task);
                await _vm.SaveAsync();
                RefreshBoard(_vm.SelectedProject.Tasks);
            }
        }
    }
}