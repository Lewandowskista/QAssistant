using System;
using System.Collections.Generic;
using System.Linq;
using QAssistant.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using QAssistant.Models;
using QAssistant.Services;

namespace QAssistant.Views
{
    public sealed partial class TasksPage : Page
    {
        private MainViewModel? _vm;
        private bool _isLinearMode = false;
        private List<ProjectTask> _linearTasks = new();
        private ProjectTask? _selectedTask;
        private string _activeTab = "Details";

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

                var todo = list.Where(t => t.Status == TaskStatus.Todo).ToList();
                var inProgress = list.Where(t => t.Status == TaskStatus.InProgress).ToList();
                var inReview = list.Where(t => t.Status == TaskStatus.InReview).ToList();
                var done = list.Where(t => t.Status == TaskStatus.Done).ToList();
                var blocked = list.Where(t => t.Status == TaskStatus.Blocked).ToList();

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

        private void SetActiveTab(string tab)
        {
            _activeTab = tab;

            DetailsTab.Visibility = tab == "Details" ? Visibility.Visible : Visibility.Collapsed;
            DescriptionTab.Visibility = tab == "Description" ? Visibility.Visible : Visibility.Collapsed;
            CommentsTab.Visibility = tab == "Comments" ? Visibility.Visible : Visibility.Collapsed;

            var activeColor = Windows.UI.Color.FromArgb(255, 167, 139, 250);
            var inactiveColor = Windows.UI.Color.FromArgb(255, 107, 114, 128);
            var activeBg = Windows.UI.Color.FromArgb(255, 26, 26, 36);

            TabDetails.Foreground = new SolidColorBrush(tab == "Details" ? activeColor : inactiveColor);
            TabDetails.Background = new SolidColorBrush(tab == "Details" ? activeBg : Colors.Transparent);
            TabDetails.BorderBrush = new SolidColorBrush(tab == "Details" ? activeColor : Colors.Transparent);
            TabDetails.BorderThickness = new Thickness(0, 0, 0, tab == "Details" ? 2 : 0);

            TabDescription.Foreground = new SolidColorBrush(tab == "Description" ? activeColor : inactiveColor);
            TabDescription.Background = new SolidColorBrush(tab == "Description" ? activeBg : Colors.Transparent);
            TabDescription.BorderBrush = new SolidColorBrush(tab == "Description" ? activeColor : Colors.Transparent);
            TabDescription.BorderThickness = new Thickness(0, 0, 0, tab == "Description" ? 2 : 0);

            TabComments.Foreground = new SolidColorBrush(tab == "Comments" ? activeColor : inactiveColor);
            TabComments.Background = new SolidColorBrush(tab == "Comments" ? activeBg : Colors.Transparent);
            TabComments.BorderBrush = new SolidColorBrush(tab == "Comments" ? activeColor : Colors.Transparent);
            TabComments.BorderThickness = new Thickness(0, 0, 0, tab == "Comments" ? 2 : 0);
        }

        private void TabDetails_Click(object sender, RoutedEventArgs e) => SetActiveTab("Details");
        private void TabDescription_Click(object sender, RoutedEventArgs e) => SetActiveTab("Description");
        private async void TabComments_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab("Comments");
            if (_isLinearMode && _selectedTask != null && !string.IsNullOrEmpty(_selectedTask.ExternalId))
                await LoadCommentsAsync(_selectedTask.ExternalId!);
        }

        private async System.Threading.Tasks.Task LoadCommentsAsync(string issueId)
        {
            CommentsContainer.Children.Clear();

            var loadingText = new TextBlock
            {
                Text = "Loading comments...",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 12
            };
            CommentsContainer.Children.Add(loadingText);

            try
            {
                var key = CredentialService.LoadCredential("LinearApiKey");
                if (string.IsNullOrEmpty(key)) return;

                var service = new LinearService(key);
                var comments = await service.GetCommentsAsync(issueId);

                CommentsContainer.Children.Clear();

                if (comments.Count == 0)
                {
                    CommentsContainer.Children.Add(new TextBlock
                    {
                        Text = "No comments yet.",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                        FontSize = 12
                    });
                    return;
                }

                foreach (var comment in comments)
                {
                    var commentBorder = new Border
                    {
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12),
                        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                        BorderThickness = new Thickness(1)
                    };

                    var commentPanel = new StackPanel { Spacing = 6 };

                    var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    headerPanel.Children.Add(new TextBlock
                    {
                        Text = comment.AuthorName,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    });
                    headerPanel.Children.Add(new TextBlock
                    {
                        Text = comment.CreatedAt.ToString("MMM d, yyyy · h:mm tt"),
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    commentPanel.Children.Add(headerPanel);
                    commentPanel.Children.Add(new TextBlock
                    {
                        Text = comment.Body,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 20
                    });

                    commentBorder.Child = commentPanel;
                    CommentsContainer.Children.Add(commentBorder);
                }
            }
            catch (Exception ex)
            {
                CommentsContainer.Children.Clear();
                CommentsContainer.Children.Add(new TextBlock
                {
                    Text = $"Error loading comments: {ex.Message}",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private void ShowDetailPanel(ProjectTask task)
        {
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

            // Description
            RenderDescription(task.Description);

            // Media
            RenderMedia(task.RawDescription);
            if (_isLinearMode)
            {
                ManualTaskFields.Visibility = Visibility.Collapsed;
                ManualActions.Visibility = Visibility.Collapsed;
                LinearCommentFields.Visibility = Visibility.Visible;
                LinearActions.Visibility = Visibility.Visible;
                DetailCommentBox.Text = string.Empty;
                CommentsContainer.Children.Clear();

                LinearTaskInfo.Visibility = Visibility.Visible;
                LinearDetailIdentifier.Text = task.IssueIdentifier;
                LinearDetailStatus.Text = task.Status.ToString();
                LinearDetailPriority.Text = task.Priority.ToString();
            }

            else
            {
                LinearCommentFields.Visibility = Visibility.Collapsed;
                LinearActions.Visibility = Visibility.Collapsed;
                ManualTaskFields.Visibility = Visibility.Visible;
                ManualActions.Visibility = Visibility.Visible;

                DetailStatusPicker.ItemsSource = Enum.GetValues(typeof(TaskStatus));
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

        private void RenderMedia(string? rawDescription)
        {
            MediaContainer.Children.Clear();

            var urls = LinearService.ExtractMediaUrls(rawDescription);
            if (urls.Count == 0)
            {
                MediaSection.Visibility = Visibility.Collapsed;
                return;
            }

            MediaSection.Visibility = Visibility.Visible;

            foreach (var url in urls)
            {
                var lower = url.ToLower();
                if (lower.EndsWith(".mp4") || lower.EndsWith(".webm") ||
                    lower.Contains("youtube") || lower.Contains("loom"))
                {
                    var webView = new Microsoft.UI.Xaml.Controls.WebView2
                    {
                        Height = 200
                    };
                    _ = webView.EnsureCoreWebView2Async();
                    webView.Source = new Uri(url);
                    MediaContainer.Children.Add(webView);
                }
                else
                {
                    var img = new Image
                    {
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                        MaxHeight = 300,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    var bitmap = new BitmapImage(new Uri(url));
                    img.Source = bitmap;

                    var capturedUrl = url;
                    img.Tapped += (s, e) => ShowMediaLightbox(capturedUrl);

                    var border = new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        Child = img,
                        Margin = new Thickness(0, 4, 0, 4),
                        Opacity = 0.95
                    };
                    border.PointerEntered += (s, e) => border.Opacity = 1.0;
                    border.PointerExited += (s, e) => border.Opacity = 0.95;
                    MediaContainer.Children.Add(border);
                }
            }
        }

        private void CloseDetailPanel()
        {
            DetailPanelColumn.Width = new GridLength(0);
            SetActiveTab("Details");
            _selectedTask = null;
        }

        private void ShowMediaLightbox(string url)
        {
            var newWindow = new Microsoft.UI.Xaml.Window();

            var img = new Image
            {
                Source = new BitmapImage(new Uri(url)),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var openBtn = new Button
            {
                Content = "Open in Browser",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12)
            };
            openBtn.Click += async (s, e) =>
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));

            var scrollViewer = new ScrollViewer
            {
                Content = img,
                ZoomMode = ZoomMode.Enabled,
                MinZoomFactor = 0.25f,
                MaxZoomFactor = 4.0f,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var grid = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(scrollViewer, 0);
            Grid.SetRow(openBtn, 1);
            grid.Children.Add(scrollViewer);
            grid.Children.Add(openBtn);

            newWindow.Content = grid;
            newWindow.Title = "Media Viewer";

            // Resize window to fit image once loaded
            img.ImageOpened += (s, e) =>
            {
                var bmp = (BitmapImage)img.Source;
                double imgW = bmp.PixelWidth;
                double imgH = bmp.PixelHeight;

                double maxW = 1200;
                double maxH = 800;

                double scale = Math.Min(maxW / imgW, maxH / imgH);
                scale = Math.Min(scale, 1.0);

                int winW = (int)(imgW * scale) + 40;
                int winH = (int)(imgH * scale) + 100;

                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
                    Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                        WinRT.Interop.WindowNative.GetWindowHandle(newWindow)));

                appWindow.Resize(new Windows.Graphics.SizeInt32(winW, winH));
                appWindow.Show();
            };

            newWindow.Activate();
        }

        private void CloseDetail_Click(object sender, RoutedEventArgs e)
        {
            CloseDetailPanel();
        }

        private (Windows.UI.Color color, string text) GetStatusStyle(TaskStatus status) => status switch
        {
            TaskStatus.Todo => (Windows.UI.Color.FromArgb(255, 55, 65, 81), "Todo"),
            TaskStatus.InProgress => (Windows.UI.Color.FromArgb(255, 30, 58, 138), "In Progress"),
            TaskStatus.InReview => (Windows.UI.Color.FromArgb(255, 76, 29, 149), "In Review"),
            TaskStatus.Done => (Windows.UI.Color.FromArgb(255, 6, 78, 59), "Done"),
            TaskStatus.Blocked => (Windows.UI.Color.FromArgb(255, 127, 29, 29), "Blocked"),
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

            _selectedTask.Status = (TaskStatus)DetailStatusPicker.SelectedItem!;
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
            if (_selectedTask == null || string.IsNullOrWhiteSpace(DetailCommentBox.Text) || string.IsNullOrEmpty(_selectedTask.ExternalId)) return;

            var key = CredentialService.LoadCredential("LinearApiKey");
            if (string.IsNullOrEmpty(key)) return;

            try
            {
                var service = new LinearService(key);
                await service.AddCommentAsync(_selectedTask.ExternalId!, DetailCommentBox.Text.Trim());
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
                ItemsSource = Enum.GetValues(typeof(TaskStatus)),
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
                    Status = (TaskStatus)statusPicker.SelectedItem!,
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