using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using QAssistant.Helpers;
using QAssistant.Models;
using QAssistant.Services;
using QAssistant.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace QAssistant.Views
{
    public sealed partial class TasksPage : Page
    {
        private MainViewModel? _vm;
        private bool _isLinearMode = false;
        private List<ProjectTask> _linearTasks = new();
        private ProjectTask? _selectedTask;
        private string _activeTab = "Details";
        private string _lightboxUrl = string.Empty;
        private DispatcherTimer? _rateLimitDismissTimer;
        private ProjectTask? _draggedTask;
        private List<LinearWorkflowState> _linearStates = new();

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

                var backlog = list.Where(t => t.Status == Models.TaskStatus.Backlog).ToList();
                var todo = list.Where(t => t.Status == Models.TaskStatus.Todo).ToList();
                var inProgress = list.Where(t => t.Status == Models.TaskStatus.InProgress).ToList();
                var inReview = list.Where(t => t.Status == Models.TaskStatus.InReview).ToList();
                var done = list.Where(t => t.Status == Models.TaskStatus.Done).ToList();
                var canceled = list.Where(t => t.Status == Models.TaskStatus.Canceled).ToList();
                var duplicate = list.Where(t => t.Status == Models.TaskStatus.Duplicate).ToList();

                BacklogList.ItemsSource = backlog;
                TodoList.ItemsSource = todo;
                InProgressList.ItemsSource = inProgress;
                InReviewList.ItemsSource = inReview;
                DoneList.ItemsSource = done;
                CanceledList.ItemsSource = canceled;
                DuplicateList.ItemsSource = duplicate;

                BacklogCount.Text = backlog.Count.ToString();
                TodoCount.Text = todo.Count.ToString();
                InProgressCount.Text = inProgress.Count.ToString();
                InReviewCount.Text = inReview.Count.ToString();
                DoneCount.Text = done.Count.ToString();
                CanceledCount.Text = canceled.Count.ToString();
                DuplicateCount.Text = duplicate.Count.ToString();
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

                try
                {
                    _linearStates = await service.GetWorkflowStatesAsync();
                }
                catch { _linearStates = new(); }

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
            DetailPanelColumn.Width = new GridLength(340);
            SetActiveTab("Description");

            DetailIdentifier.Text = task.IssueIdentifier;
            DetailTitle.Text = task.Title;

            var (statusColor, statusText) = GetStatusStyle(task.Status);
            DetailStatus.Text = statusText;
            DetailStatusBadge.Background = new SolidColorBrush(statusColor);
            DetailStatus.Foreground = new SolidColorBrush(Colors.White);

            DetailPriority.Text = task.Priority.ToString();
            DetailPriority.Foreground = GetPriorityBrush(task.Priority);

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
                DetailDueDate.Text = task.DueDate.Value.ToString("MMM d, yyyy h:mm tt");
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

            RenderDescription(task.Description);
            RenderMedia(task);

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
                LinearDetailPriority.Foreground = GetPriorityBrush(task.Priority);
            }
            else
            {
                LinearCommentFields.Visibility = Visibility.Collapsed;
                LinearActions.Visibility = Visibility.Collapsed;
                LinearTaskInfo.Visibility = Visibility.Collapsed;
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
                    DetailDueTimePicker.Time = task.DueDate.Value.TimeOfDay;
                    DetailDateTimePicker.Visibility = Visibility.Visible;
                }
                else
                {
                    DetailSetDueDate.IsChecked = false;
                    DetailDueDatePicker.Date = DateTimeOffset.Now;
                    DetailDueTimePicker.Time = DateTime.Now.TimeOfDay;
                    DetailDateTimePicker.Visibility = Visibility.Collapsed;
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

        private void RenderMedia(ProjectTask task)
        {
            MediaContainer.Children.Clear();

            var urls = LinearService.ExtractMediaUrls(task.RawDescription);

            // Also include attachment URLs from the Linear API
            if (task.AttachmentUrls?.Count > 0)
            {
                foreach (var url in task.AttachmentUrls)
                {
                    if (!string.IsNullOrEmpty(url) &&
                        !urls.Any(u => u.Equals(url, StringComparison.OrdinalIgnoreCase)))
                    {
                        urls.Add(url);
                    }
                }
            }

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
                    var capturedUrl = url;
                    var img = new Image
                    {
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                        MaxHeight = 300,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    _ = LoadImageWithAuthAsync(img, url);

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

        private void ShowMediaLightbox(string url)
        {
            _lightboxUrl = url;
            _ = LoadImageWithAuthAsync(LightboxImage, url);
            LightboxOverlay.Visibility = Visibility.Visible;
        }

        private async System.Threading.Tasks.Task LoadImageWithAuthAsync(Image img, string url)
        {
            try
            {
                using var httpClient = new HttpClient();

                // Linear-hosted upload URLs require the API key to download
                if (url.Contains("linear.app", StringComparison.OrdinalIgnoreCase))
                {
                    var key = CredentialService.LoadCredential("LinearApiKey");
                    if (!string.IsNullOrEmpty(key))
                        httpClient.DefaultRequestHeaders.Add("Authorization", key);
                }

                var bytes = await httpClient.GetByteArrayAsync(new Uri(url));

                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);

                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                img.Source = bitmap;
            }
            catch
            {
                // Fallback: try loading directly for public URLs
                try
                {
                    img.Source = new BitmapImage(new Uri(url));
                }
                catch { }
            }
        }

        private async System.Threading.Tasks.Task<List<(string MimeType, string Base64Data)>> DownloadImageAttachmentsAsync(
            List<string> urls, int maxImages = 4, long maxBytesPerImage = 4 * 1024 * 1024)
        {
            var results = new List<(string MimeType, string Base64Data)>();

            foreach (var url in urls)
            {
                if (results.Count >= maxImages) break;

                var lower = url.ToLowerInvariant();
                // Skip video and embed URLs
                if (lower.EndsWith(".mp4") || lower.EndsWith(".webm") ||
                    lower.Contains("youtube") || lower.Contains("loom"))
                    continue;

                try
                {
                    using var httpClient = new HttpClient();
                    if (url.Contains("linear.app", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = CredentialService.LoadCredential("LinearApiKey");
                        if (!string.IsNullOrEmpty(key))
                            httpClient.DefaultRequestHeaders.Add("Authorization", key);
                    }

                    var bytes = await httpClient.GetByteArrayAsync(new Uri(url));
                    if (bytes.Length > maxBytesPerImage) continue;

                    var mimeType = GetMimeTypeFromUrl(url);
                    var base64 = Convert.ToBase64String(bytes);
                    results.Add((mimeType, base64));
                }
                catch
                {
                    // Skip attachments that fail to download
                }
            }

            return results;
        }

        private static string GetMimeTypeFromUrl(string url)
        {
            var lower = url.Split('?')[0].ToLowerInvariant();
            if (lower.EndsWith(".png")) return "image/png";
            if (lower.EndsWith(".gif")) return "image/gif";
            if (lower.EndsWith(".webp")) return "image/webp";
            if (lower.EndsWith(".bmp")) return "image/bmp";
            return "image/jpeg";
        }

        private void CloseLightbox_Click(object sender, RoutedEventArgs e)
        {
            LightboxOverlay.Visibility = Visibility.Collapsed;
            LightboxImage.Source = null;
            _lightboxUrl = string.Empty;
        }

        private async void LightboxOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lightboxUrl))
                await Windows.System.Launcher.LaunchUriAsync(new Uri(_lightboxUrl));
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
            {
                await LoadCommentsAsync(_selectedTask.ExternalId);
            }
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

        private (Windows.UI.Color color, string text) GetStatusStyle(Models.TaskStatus status) => status switch
        {
            Models.TaskStatus.Backlog => (Windows.UI.Color.FromArgb(255, 75, 85, 99), "Backlog"),
            Models.TaskStatus.Todo => (Windows.UI.Color.FromArgb(255, 55, 65, 81), "Todo"),
            Models.TaskStatus.InProgress => (Windows.UI.Color.FromArgb(255, 30, 58, 138), "In Progress"),
            Models.TaskStatus.InReview => (Windows.UI.Color.FromArgb(255, 76, 29, 149), "In Review"),
            Models.TaskStatus.Done => (Windows.UI.Color.FromArgb(255, 6, 78, 59), "Done"),
            Models.TaskStatus.Canceled => (Windows.UI.Color.FromArgb(255, 127, 29, 29), "Canceled"),
            Models.TaskStatus.Duplicate => (Windows.UI.Color.FromArgb(255, 107, 114, 128), "Duplicate"),
            _ => (Windows.UI.Color.FromArgb(255, 55, 65, 81), status.ToString())
        };

        private static SolidColorBrush GetPriorityBrush(TaskPriority priority) => priority switch
        {
            TaskPriority.Low => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)),      // Green
            TaskPriority.Medium => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)),    // Yellow
            TaskPriority.High => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 249, 115, 22)),      // Orange
            TaskPriority.Critical => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)),   // Red
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
        };

        private void DetailSetDueDate_Checked(object sender, RoutedEventArgs e)
        {
            DetailDateTimePicker.Visibility = Visibility.Visible;
        }

        private void DetailSetDueDate_Unchecked(object sender, RoutedEventArgs e)
        {
            DetailDateTimePicker.Visibility = Visibility.Collapsed;
        }

        private async void SaveTaskChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null || _vm?.SelectedProject == null) return;

            _selectedTask.Status = (Models.TaskStatus)DetailStatusPicker.SelectedItem!;
            _selectedTask.Priority = (TaskPriority)DetailPriorityPicker.SelectedItem!;

            if (DetailSetDueDate.IsChecked == true)
            {
                var d = DetailDueDatePicker.Date.DateTime;
                var t = DetailDueTimePicker.Time;
                _selectedTask.DueDate = new DateTime(d.Year, d.Month, d.Day, t.Hours, t.Minutes, 0);
            }
            else
            {
                _selectedTask.DueDate = null;
            }

            await _vm.SaveAsync();
            RefreshBoard(_vm.SelectedProject.Tasks);
            CloseDetailPanel();

            // Trigger immediate reminder update
            App.MainWindowInstance?.ReminderService.TriggerCheck();
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
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _vm.SelectedProject.Tasks.Remove(_selectedTask);
                await _vm.SaveAsync();
                RefreshBoard(_vm.SelectedProject.Tasks);
                CloseDetailPanel();

                // Trigger immediate reminder update
                App.MainWindowInstance?.ReminderService.TriggerCheck();
            }
        }

        private async void PostComment_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null || string.IsNullOrWhiteSpace(DetailCommentBox.Text)) return;

            // Ensure the selected task has an ExternalId before calling LinearService.AddCommentAsync
            if (string.IsNullOrEmpty(_selectedTask.ExternalId))
            {
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Cannot post comment: task is not linked to Linear.";
                return;
            }

            var key = CredentialService.LoadCredential("LinearApiKey");
            if (string.IsNullOrEmpty(key)) return;

            try
            {
                var service = new LinearService(key);
                await service.AddCommentAsync(_selectedTask.ExternalId, DetailCommentBox.Text.Trim());
                DetailCommentBox.Text = string.Empty;
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Comment posted successfully.";
                await LoadCommentsAsync(_selectedTask.ExternalId);
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
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var dueTimePicker = new TimePicker
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Time = DateTime.Now.TimeOfDay
            };
            var dateStack = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
            dateStack.Children.Add(dueDatePicker);
            dateStack.Children.Add(dueTimePicker);

            var setDueDate = new CheckBox
            {
                Content = "Set due date",
                IsChecked = false,
                Foreground = new SolidColorBrush(Colors.White)
            };
            setDueDate.Checked += (s, e) => dateStack.Visibility = Visibility.Visible;
            setDueDate.Unchecked += (s, e) => dateStack.Visibility = Visibility.Collapsed;

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = "Task Title", Foreground = new SolidColorBrush(Colors.White) });
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
            panel.Children.Add(dateStack);

            var dialog = new ContentDialog
            {
                Title = "New Task",
                Content = panel,
                PrimaryButtonText = "Add Task",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(titleBox.Text))
            {
                DateTime? dueDate = null;
                if (setDueDate.IsChecked == true)
                {
                    var d = dueDatePicker.Date.DateTime;
                    var t = dueTimePicker.Time;
                    dueDate = new DateTime(d.Year, d.Month, d.Day, t.Hours, t.Minutes, 0);
                }

                var task = new ProjectTask
                {
                    Title = titleBox.Text.Trim(),
                    Description = descBox.Text.Trim(),
                    Status = (Models.TaskStatus)statusPicker.SelectedItem!,
                    Priority = (TaskPriority)priorityPicker.SelectedItem!,
                    TicketUrl = ticketBox.Text.Trim(),
                    DueDate = dueDate
                };
                _vm.SelectedProject.Tasks.Add(task);
                await _vm.SaveAsync();
                RefreshBoard(_vm.SelectedProject.Tasks);

                // Trigger immediate reminder update
                App.MainWindowInstance?.ReminderService.TriggerCheck();
            }
        }

        private async void AnalyzeIssue_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null) return;

            var geminiKey = CredentialService.LoadCredential("GeminiApiKey");
            if (string.IsNullOrEmpty(geminiKey))
            {
                var noKeyDialog = new ContentDialog
                {
                    Title = "API Key Missing",
                    Content = "Please add your Google AI Studio API key in Settings.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                DialogHelper.ApplyDarkTheme(noKeyDialog);
                await noKeyDialog.ShowAsync();
                return;
            }

            // Fetch Linear comments if applicable (needed before building the prompt)
            System.Collections.Generic.List<Models.LinearComment>? linearComments = null;
            if (_isLinearMode && !string.IsNullOrEmpty(_selectedTask.ExternalId))
            {
                try
                {
                    var linearKey = CredentialService.LoadCredential("LinearApiKey");
                    if (!string.IsNullOrEmpty(linearKey))
                    {
                        var linearService = new LinearService(linearKey);
                        linearComments = await linearService.GetCommentsAsync(_selectedTask.ExternalId);
                        if (linearComments.Count == 0)
                            linearComments = null;
                    }
                }
                catch { }
            }

            // Download image attachments for multimodal analysis
            var imageData = new List<(string MimeType, string Base64Data)>();
            if (_selectedTask.AttachmentUrls is { Count: > 0 })
            {
                imageData = await DownloadImageAttachmentsAsync(_selectedTask.AttachmentUrls);
            }

            // Build a compact TOON-formatted prompt to reduce token consumption
            var toonPrompt = GeminiService.BuildToonPrompt(_selectedTask, linearComments, imageData.Count);

            var loadingContent = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new ProgressRing { IsActive = true, Width = 40, Height = 40 },
                    new TextBlock
                    {
                        Text = "Gemini is analyzing the issue...",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                        FontSize = 13
                    }
                }
            };

            var loadingDialog = new ContentDialog
            {
                Title = "Analyzing Issue...",
                Content = loadingContent,
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };


            string analysisResult = string.Empty;
            var images = imageData.Count > 0 ? imageData : null;
            var analyzeTask = System.Threading.Tasks.Task.Run(async () =>
            {
                var service = new GeminiService(geminiKey);
                analysisResult = await service.AnalyzeIssueAsync(toonPrompt, images);
            });

            // Use the in-page BusyOverlay instead of a ContentDialog to avoid
            // modal overlay issues. Show the overlay, run the analysis, then hide it.
            BusyOverlay.Visibility = Visibility.Visible;

            try
            {
                await analyzeTask;
            }
            catch (AggregateException ae) when (ae.InnerException is GeminiAllModelsRateLimitedException)
            {
                ShowRateLimitBanner();
                return;
            }
            catch (GeminiAllModelsRateLimitedException)
            {
                ShowRateLimitBanner();
                return;
            }
            catch (Exception ex)
            {
                // Surface error in the result dialog instead of crashing silently
                analysisResult = $"Analysis failed: {ex.Message}";
            }
            finally
            {
                BusyOverlay.Visibility = Visibility.Collapsed;
                // Give the UI a short moment to apply the visibility change before showing the result dialog
                await System.Threading.Tasks.Task.Delay(50);
            }

            var contentStack = new StackPanel { Spacing = 16 };

            var sections = new[] { "Root Cause Analysis", "Impact Assessment", "Suggested Fix", "Prevention Recommendations" };

            var resultLines = analysisResult.Split('\n');
            int sectionIndex = 0;

            foreach (var section in sections)
            {
                var sectionStartIndex = Array.FindIndex(resultLines, r => r.Contains(section, StringComparison.OrdinalIgnoreCase));
                if (sectionStartIndex < 0) continue;

                int sectionEndIndex = resultLines.Length;
                for (int i = sectionStartIndex + 1; i < resultLines.Length; i++)
                {
                    if (sections.Any(s => resultLines[i].Contains(s, StringComparison.OrdinalIgnoreCase)))
                    {
                        sectionEndIndex = i;
                        break;
                    }
                }

                // Check if the header line itself contains inline content after the section name
                var headerLine = resultLines[sectionStartIndex];
                var nameIdx = headerLine.IndexOf(section, StringComparison.OrdinalIgnoreCase);
                var inlineContent = headerLine.Substring(nameIdx + section.Length).TrimStart(':', ' ', '*', '#').Trim();

                var bodyLines = resultLines.Skip(sectionStartIndex + 1).Take(sectionEndIndex - sectionStartIndex - 1);
                var contentParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(inlineContent))
                    contentParts.Add(inlineContent);
                contentParts.AddRange(bodyLines);

                var rawContent = string.Join("\n", contentParts).Trim();

                // Clean markdown artifacts for better display and normalize bullets
                var cleanedLines = rawContent.Split('\n').Select(line =>
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("### ")) return trimmed[4..];
                    if (trimmed.StartsWith("## ")) return trimmed[3..];
                    if (trimmed.StartsWith("# ")) return trimmed[2..];
                    if (trimmed.StartsWith("- ")) return "\u2022 " + trimmed[2..];
                    if (trimmed.StartsWith("* ") && !trimmed.StartsWith("**")) return "\u2022 " + trimmed[2..];
                    return line;
                });
                var sectionContent = string.Join("\n", cleanedLines).Trim();

                if (string.IsNullOrWhiteSpace(sectionContent)) continue;

                var sectionHeader = new TextBlock
                {
                    Text = section,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    FontFamily = new FontFamily("Segoe UI, Segoe UI Symbol, Segoe UI Emoji"),
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 4),
                    IsTextSelectionEnabled = true
                };

                var sectionBodyBlock = new TextBlock
                {
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    FontFamily = new FontFamily("Segoe UI, Segoe UI Symbol, Segoe UI Emoji"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20,
                    IsTextSelectionEnabled = true
                };

                // Parse inline formatting: **bold** and `code`
                var inlinePattern = new System.Text.RegularExpressions.Regex(@"(\*\*.*?\*\*|`[^`]+`)");
                var segments = inlinePattern.Split(sectionContent);
                foreach (var segment in segments)
                {
                    if (string.IsNullOrEmpty(segment)) continue;

                    if (segment.StartsWith("**") && segment.EndsWith("**") && segment.Length > 4)
                    {
                        sectionBodyBlock.Inlines.Add(new Run
                        {
                            Text = segment[2..^2],
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold
                        });
                    }
                    else if (segment.StartsWith('`') && segment.EndsWith('`') && segment.Length > 2)
                    {
                        sectionBodyBlock.Inlines.Add(new Run
                        {
                            Text = segment[1..^1],
                            FontFamily = new FontFamily("Consolas"),
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
                        });
                    }
                    else
                    {
                        sectionBodyBlock.Inlines.Add(new Run { Text = segment });
                    }
                }

                contentStack.Children.Add(sectionHeader);
                contentStack.Children.Add(sectionBodyBlock);

                if (sectionIndex < sections.Length - 1)
                {
                    var divider = new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                        Margin = new Thickness(0, 8, 0, 0)
                    };
                    contentStack.Children.Add(divider);
                }

                sectionIndex++;
            }

            var scrollContent = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 500,
                Content = contentStack
            };

            var resultContent = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Child = scrollContent
            };

            var resultDialog = new ContentDialog
            {
                Title = $"AI Analysis — {_selectedTask.Title}",
                Content = resultContent,
                CloseButtonText = "Close",
                PrimaryButtonText = "Copy to Clipboard",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            resultDialog.Resources["ContentDialogMaxWidth"] = 700.0;
            DialogHelper.ApplyDarkTheme(resultDialog);

            var resultAction = await resultDialog.ShowAsync();
            if (resultAction == ContentDialogResult.Primary)
            {
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(analysisResult);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            }
        }

        private void KanbanList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            if (e.Items.Count > 0 && e.Items[0] is ProjectTask task)
            {
                _draggedTask = task;
                e.Data.RequestedOperation = DataPackageOperation.Move;
            }
        }

        private void KanbanColumn_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }

        private async void KanbanColumn_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            if (_draggedTask == null) return;

            var border = sender as Border;
            var tag = border?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return;

            if (!Enum.TryParse<Models.TaskStatus>(tag, out var newStatus)) return;
            if (_draggedTask.Status == newStatus) { _draggedTask = null; return; }

            var task = _draggedTask;
            _draggedTask = null;
            task.Status = newStatus;

            if (_isLinearMode && task.Source == TaskSource.Linear && !string.IsNullOrEmpty(task.ExternalId))
            {
                var key = CredentialService.LoadCredential("LinearApiKey");

                // Lazily fetch workflow states if they weren't loaded yet
                if (_linearStates.Count == 0 && !string.IsNullOrEmpty(key))
                {
                    try
                    {
                        var svc = new LinearService(key);
                        _linearStates = await svc.GetWorkflowStatesAsync();
                    }
                    catch { _linearStates = new(); }
                }

                var matchedStateId = LinearService.FindMatchingStateId(newStatus, _linearStates);

                if (matchedStateId != null)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(key))
                        {
                            var service = new LinearService(key);
                            await service.UpdateIssueStatusAsync(task.ExternalId, matchedStateId);
                            StatusText.Visibility = Visibility.Visible;
                            StatusText.Text = $"Updated {task.IssueIdentifier} → {newStatus}";
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusText.Visibility = Visibility.Visible;
                        StatusText.Text = $"Failed to update Linear: {ex.Message}";
                    }
                }
                else
                {
                    StatusText.Visibility = Visibility.Visible;
                    StatusText.Text = $"No matching Linear state for '{newStatus}'. Check team workflow states.";
                }

                RefreshBoard(_linearTasks);
            }
            else
            {
                if (_vm?.SelectedProject != null)
                {
                    await _vm.SaveAsync();
                    RefreshBoard(_vm.SelectedProject.Tasks);
                }
            }

            // Trigger immediate reminder update
            App.MainWindowInstance?.ReminderService.TriggerCheck();
        }

        private void ShowRateLimitBanner()
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
            RateLimitBanner.Visibility = Visibility.Visible;

            _rateLimitDismissTimer?.Stop();
            _rateLimitDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _rateLimitDismissTimer.Tick += (_, _) =>
            {
                RateLimitBanner.Visibility = Visibility.Collapsed;
                _rateLimitDismissTimer.Stop();
            };
            _rateLimitDismissTimer.Start();
        }
    }
}