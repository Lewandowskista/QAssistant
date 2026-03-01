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
using Windows.System;

namespace QAssistant.Views
{
    public sealed partial class TasksPage : Page
    {
        private MainViewModel? _vm;
        private bool _isLinearMode = false;
        private bool _isJiraMode = false;
        private List<ProjectTask> _linearTasks = new();
        private List<ProjectTask> _jiraTasks = new();
        private ProjectTask? _selectedTask;
        private string _activeTab = "Details";
        private string _lightboxUrl = string.Empty;
        private DispatcherTimer? _rateLimitDismissTimer;
        private ProjectTask? _draggedTask;
        private List<LinearWorkflowState> _linearStates = new();

        private Guid ProjectId => _vm?.SelectedProject?.Id ?? Guid.Empty;

        private string? LoadProjectCred(string key) =>
            ProjectId != Guid.Empty
                ? CredentialService.LoadProjectCredential(ProjectId, key)
                : CredentialService.LoadCredential(key);

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
                {
                    RefreshBoard(_vm.SelectedProject.Tasks);

                    // If a notification was clicked, open the sidebar for that task
                    if (_vm.PendingTaskId is Guid taskId && taskId != Guid.Empty)
                    {
                        _vm.PendingTaskId = null;
                        var task = _vm.SelectedProject.Tasks.FirstOrDefault(t => t.Id == taskId);
                        if (task != null)
                        {
                            _selectedTask = task;
                            ShowDetailPanel(task);
                        }
                    }
                }
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

            var key = LoadProjectCred("LinearApiKey");
            var teamId = LoadProjectCred("LinearTeamId");

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

                // Merge persisted analysis history back into freshly fetched tasks
                if (_vm?.SelectedProject != null)
                {
                    var saved = _vm.SelectedProject.LinearAnalysisHistory;
                    foreach (var task in _linearTasks)
                    {
                        if (!string.IsNullOrEmpty(task.ExternalId) &&
                            saved.TryGetValue(task.ExternalId, out var history))
                        {
                            task.AnalysisHistory = new List<AnalysisEntry>(history);
                        }
                    }
                }

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
            _isJiraMode = false;
            ManualModeBtn.Background = (Brush)Application.Current.Resources["ListAccentLowBrush"];
            ManualModeBtn.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
            LinearModeBtn.Background = new SolidColorBrush(Colors.Transparent);
            LinearModeBtn.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
            JiraModeBtn.Background = new SolidColorBrush(Colors.Transparent);
            JiraModeBtn.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
            AddTaskBtn.Visibility = Visibility.Visible;
            RefreshBtn.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Collapsed;
            CloseDetailPanel();
            RefreshBoard(_vm?.SelectedProject?.Tasks ?? new List<ProjectTask>());
        }

        private async void LinearMode_Click(object sender, RoutedEventArgs e)
        {
            _isLinearMode = true;
            _isJiraMode = false;
            LinearModeBtn.Background = (Brush)Application.Current.Resources["ListAccentLowBrush"];
            LinearModeBtn.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
            ManualModeBtn.Background = new SolidColorBrush(Colors.Transparent);
            ManualModeBtn.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
            JiraModeBtn.Background = new SolidColorBrush(Colors.Transparent);
            JiraModeBtn.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
            AddTaskBtn.Visibility = Visibility.Collapsed;
            RefreshBtn.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Visible;
            CloseDetailPanel();

            var key = LoadProjectCred("LinearApiKey");
            if (string.IsNullOrEmpty(key))
            {
                StatusText.Text = "No Linear API key found. Go to Settings to connect.";
                return;
            }

            await FetchLinearIssuesAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_isJiraMode)
                await FetchJiraIssuesAsync();
            else
                await FetchLinearIssuesAsync();
        }

        private async void JiraMode_Click(object sender, RoutedEventArgs e)
        {
            _isJiraMode = true;
            _isLinearMode = false;
            JiraModeBtn.Background = (Brush)Application.Current.Resources["ListAccentLowBrush"];
            JiraModeBtn.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
            ManualModeBtn.Background = new SolidColorBrush(Colors.Transparent);
            ManualModeBtn.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
            LinearModeBtn.Background = new SolidColorBrush(Colors.Transparent);
            LinearModeBtn.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
            AddTaskBtn.Visibility = Visibility.Collapsed;
            RefreshBtn.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Visible;
            CloseDetailPanel();

            var domain = LoadProjectCred("JiraDomain");
            if (string.IsNullOrEmpty(domain))
            {
                StatusText.Text = "No Jira credentials found. Go to Settings to connect.";
                return;
            }

            await FetchJiraIssuesAsync();
        }

        private async System.Threading.Tasks.Task FetchJiraIssuesAsync()
        {
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = "Fetching Jira issues...";

            var service = CreateJiraService();
            var projectKey = LoadProjectCred("JiraProjectKey");

            if (service == null || string.IsNullOrEmpty(projectKey))
            {
                StatusText.Text = "No credentials found. Go to Settings.";
                return;
            }

            try
            {
                _jiraTasks = await service.GetIssuesAsync(projectKey);

                // Merge persisted analysis history back into freshly fetched tasks
                if (_vm?.SelectedProject != null)
                {
                    var saved = _vm.SelectedProject.JiraAnalysisHistory;
                    foreach (var task in _jiraTasks)
                    {
                        if (!string.IsNullOrEmpty(task.ExternalId) &&
                            saved.TryGetValue(task.ExternalId, out var history))
                        {
                            task.AnalysisHistory = new List<AnalysisEntry>(history);
                        }
                    }
                }

                if (_jiraTasks.Count == 0)
                    StatusText.Text = "No issues found in this project.";
                else
                {
                    RefreshBoard(_jiraTasks);
                    StatusText.Text = $"Synced {_jiraTasks.Count} issues · {DateTime.Now:h:mm tt}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private JiraService? CreateJiraService()
        {
            var domain = LoadProjectCred("JiraDomain");
            var email = LoadProjectCred("JiraEmail");
            var token = LoadProjectCred("JiraApiToken");
            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
                return null;
            return new JiraService(domain, email, token);
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
                JiraActions.Visibility = Visibility.Collapsed;
                JiraTaskInfo.Visibility = Visibility.Collapsed;
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
            else if (_isJiraMode)
            {
                ManualTaskFields.Visibility = Visibility.Collapsed;
                ManualActions.Visibility = Visibility.Collapsed;
                LinearActions.Visibility = Visibility.Collapsed;
                LinearTaskInfo.Visibility = Visibility.Collapsed;
                LinearCommentFields.Visibility = Visibility.Visible;
                JiraActions.Visibility = Visibility.Visible;
                DetailCommentBox.Text = string.Empty;
                CommentsContainer.Children.Clear();

                JiraTaskInfo.Visibility = Visibility.Visible;
                JiraDetailIdentifier.Text = task.IssueIdentifier;
                JiraDetailStatus.Text = task.Status.ToString();
                JiraDetailPriority.Text = task.Priority.ToString();
                JiraDetailPriority.Foreground = GetPriorityBrush(task.Priority);

                if (!string.IsNullOrEmpty(task.IssueType))
                {
                    JiraDetailIssueType.Text = task.IssueType;
                    JiraIssueTypePanel.Visibility = Visibility.Visible;
                }
                else
                {
                    JiraIssueTypePanel.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                LinearCommentFields.Visibility = Visibility.Collapsed;
                LinearActions.Visibility = Visibility.Collapsed;
                LinearTaskInfo.Visibility = Visibility.Collapsed;
                JiraActions.Visibility = Visibility.Collapsed;
                JiraTaskInfo.Visibility = Visibility.Collapsed;
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
                if (!Helpers.UriSecurity.IsSafeHttpUrl(url))
                    continue;

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
            if (!Helpers.UriSecurity.IsSafeHttpUrl(url))
                return;

            try
            {
                using var httpClient = new HttpClient();

                // Linear-hosted upload URLs require the API key to download
                if (url.Contains("linear.app", StringComparison.OrdinalIgnoreCase))
                {
                    var key = LoadProjectCred("LinearApiKey");
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
                    if (!Helpers.UriSecurity.IsSafeHttpUrl(url))
                        continue;

                    using var httpClient = new HttpClient();
                    if (url.Contains("linear.app", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = LoadProjectCred("LinearApiKey");
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
            if (Helpers.UriSecurity.IsHttpUrl(_lightboxUrl))
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
            HistoryTab.Visibility = tab == "History" ? Visibility.Visible : Visibility.Collapsed;
            WorklogTab.Visibility = tab == "Worklog" ? Visibility.Visible : Visibility.Collapsed;

            var activeBrush = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            var inactiveBrush = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"];
            var activeBgBrush = (SolidColorBrush)Application.Current.Resources["HoverBrush"];
            var transparent = new SolidColorBrush(Colors.Transparent);

            TabDetails.Foreground = tab == "Details" ? activeBrush : inactiveBrush;
            TabDetails.Background = tab == "Details" ? activeBgBrush : transparent;
            TabDetails.BorderBrush = tab == "Details" ? activeBrush : transparent;
            TabDetails.BorderThickness = new Thickness(0, 0, 0, tab == "Details" ? 2 : 0);

            TabDescription.Foreground = tab == "Description" ? activeBrush : inactiveBrush;
            TabDescription.Background = tab == "Description" ? activeBgBrush : transparent;
            TabDescription.BorderBrush = tab == "Description" ? activeBrush : transparent;
            TabDescription.BorderThickness = new Thickness(0, 0, 0, tab == "Description" ? 2 : 0);

            TabComments.Foreground = tab == "Comments" ? activeBrush : inactiveBrush;
            TabComments.Background = tab == "Comments" ? activeBgBrush : transparent;
            TabComments.BorderBrush = tab == "Comments" ? activeBrush : transparent;
            TabComments.BorderThickness = new Thickness(0, 0, 0, tab == "Comments" ? 2 : 0);

            TabHistory.Foreground = tab == "History" ? activeBrush : inactiveBrush;
            TabHistory.Background = tab == "History" ? activeBgBrush : transparent;
            TabHistory.BorderBrush = tab == "History" ? activeBrush : transparent;
            TabHistory.BorderThickness = new Thickness(0, 0, 0, tab == "History" ? 2 : 0);

            TabWorklog.Foreground = tab == "Worklog" ? activeBrush : inactiveBrush;
            TabWorklog.Background = tab == "Worklog" ? activeBgBrush : transparent;
            TabWorklog.BorderBrush = tab == "Worklog" ? activeBrush : transparent;
            TabWorklog.BorderThickness = new Thickness(0, 0, 0, tab == "Worklog" ? 2 : 0);
        }

        private void TabDetails_Click(object sender, RoutedEventArgs e) => SetActiveTab("Details");
        private void TabDescription_Click(object sender, RoutedEventArgs e) => SetActiveTab("Description");

        private async void TabComments_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab("Comments");
            if (_selectedTask != null && !string.IsNullOrEmpty(_selectedTask.ExternalId))
            {
                if (_isLinearMode)
                    await LoadLinearCommentsAsync(_selectedTask.ExternalId);
                else if (_isJiraMode)
                    await LoadJiraCommentsAsync(_selectedTask.ExternalId);
            }
        }

        private void TabHistory_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab("History");
            if (_selectedTask != null)
            {
                RenderAnalysisHistory(_selectedTask);
            }
        }

        private async void TabWorklog_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab("Worklog");
            if (_selectedTask != null && !string.IsNullOrEmpty(_selectedTask.ExternalId))
            {
                await LoadWorklogAsync(_selectedTask);
            }
            else
            {
                WorklogContainer.Children.Clear();
                WorklogContainer.Children.Add(new TextBlock
                {
                    Text = "Worklog is available for Linear and Jira issues.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private void RenderAnalysisHistory(ProjectTask task)
        {
            HistoryContainer.Children.Clear();

            if (task.AnalysisHistory.Count == 0)
            {
                HistoryContainer.Children.Add(new TextBlock
                {
                    Text = "No analyses yet. Use the Analyze button to generate one.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var entry in task.AnalysisHistory.OrderByDescending(e => e.Version))
            {
                var entryPanel = new StackPanel { Spacing = 0 };

                // Timeline dot + connector row
                var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

                // Git-style dot
                var dot = new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0)
                };
                headerRow.Children.Add(dot);

                // Version + hash label (like "v3 · a1b2c3d")
                var versionText = new TextBlock
                {
                    Text = $"v{entry.Version} · {entry.Hash}",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerRow.Children.Add(versionText);

                entryPanel.Children.Add(headerRow);

                // Card content area with left border (timeline connector)
                var cardContent = new StackPanel { Spacing = 6 };

                // Top row: timestamp + delete button
                var topRow = new Grid();
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var timestampText = new TextBlock
                {
                    Text = entry.Timestamp.ToString("MMM d, yyyy · h:mm:ss tt"),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(timestampText, 0);
                topRow.Children.Add(timestampText);

                var capturedEntry = entry;
                var capturedTask = task;
                var deleteBtn = new Button
                {
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 2, 4, 2),
                    CornerRadius = new CornerRadius(4),
                    VerticalAlignment = VerticalAlignment.Center,
                    Content = new FontIcon
                    {
                        Glyph = "\uE74D",
                        FontSize = 11,
                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
                    }
                };
                deleteBtn.PointerEntered += (s, e) =>
                {
                    if (deleteBtn.Content is FontIcon icon)
                        icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                };
                deleteBtn.PointerExited += (s, e) =>
                {
                    if (deleteBtn.Content is FontIcon icon)
                        icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
                };
                deleteBtn.Click += async (s, e) => await DeleteAnalysisEntryAsync(capturedTask, capturedEntry);
                Grid.SetColumn(deleteBtn, 1);
                topRow.Children.Add(deleteBtn);

                cardContent.Children.Add(topRow);

                // Status + Priority tags
                var tagsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                tagsRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 58, 138)),
                    Child = new TextBlock
                    {
                        Text = entry.TaskStatus,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 10
                    }
                });
                tagsRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 32, 16)),
                    Child = new TextBlock
                    {
                        Text = entry.TaskPriority,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)),
                        FontSize = 10
                    }
                });
                cardContent.Children.Add(tagsRow);

                // Summary / first meaningful line
                if (!string.IsNullOrWhiteSpace(entry.Summary))
                {
                    cardContent.Children.Add(new TextBlock
                    {
                        Text = entry.Summary,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        MaxLines = 3
                    });
                }

                // "View full analysis" expander button
                var expandBtn = new Button
                {
                    Content = "View full analysis",
                    Background = new SolidColorBrush(Colors.Transparent),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0, 4, 0, 4),
                    FontSize = 11
                };
                var fullPanel = BuildFormattedAnalysisPanel(entry.FullResult);
                fullPanel.Visibility = Visibility.Collapsed;
                fullPanel.Margin = new Thickness(0, 4, 0, 0);
                expandBtn.Click += (s, e) =>
                {
                    if (fullPanel.Visibility == Visibility.Collapsed)
                    {
                        fullPanel.Visibility = Visibility.Visible;
                        expandBtn.Content = "Collapse";
                    }
                    else
                    {
                        fullPanel.Visibility = Visibility.Collapsed;
                        expandBtn.Content = "View full analysis";
                    }
                };

                cardContent.Children.Add(expandBtn);
                cardContent.Children.Add(fullPanel);

                var cardBorder = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(16, 4, 0, 0),
                    Child = cardContent
                };

                entryPanel.Children.Add(cardBorder);
                HistoryContainer.Children.Add(entryPanel);
            }
        }

        private async System.Threading.Tasks.Task DeleteAnalysisEntryAsync(ProjectTask task, AnalysisEntry entry)
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Analysis",
                Content = $"Delete analysis v{entry.Version} ({entry.Hash})?\nThis cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            task.AnalysisHistory.Remove(entry);

            // Sync to persisted Linear history dictionary
            if (_isLinearMode && !string.IsNullOrEmpty(task.ExternalId) && _vm?.SelectedProject != null)
            {
                if (task.AnalysisHistory.Count > 0)
                    _vm.SelectedProject.LinearAnalysisHistory[task.ExternalId] =
                        new List<AnalysisEntry>(task.AnalysisHistory);
                else
                    _vm.SelectedProject.LinearAnalysisHistory.Remove(task.ExternalId);
            }

            if (_vm != null)
                await _vm.SaveAsync();

            RenderAnalysisHistory(task);
        }

        private static StackPanel BuildFormattedAnalysisPanel(string analysisResult)
        {
            var contentStack = new StackPanel { Spacing = 8 };

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

                var headerLine = resultLines[sectionStartIndex];
                var nameIdx = headerLine.IndexOf(section, StringComparison.OrdinalIgnoreCase);
                var inlineContent = headerLine.Substring(nameIdx + section.Length).TrimStart(':', ' ', '*', '#').Trim();

                var bodyLines = resultLines.Skip(sectionStartIndex + 1).Take(sectionEndIndex - sectionStartIndex - 1);
                var contentParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(inlineContent))
                    contentParts.Add(inlineContent);
                contentParts.AddRange(bodyLines);

                var rawContent = string.Join("\n", contentParts).Trim();

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
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 4, 0, 2),
                    IsTextSelectionEnabled = true
                };

                var sectionBodyBlock = new TextBlock
                {
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    FontFamily = new FontFamily("Segoe UI, Segoe UI Symbol, Segoe UI Emoji"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                    IsTextSelectionEnabled = true
                };

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
                    contentStack.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                }

                sectionIndex++;
            }

            // Fallback: if no known sections were found, render the raw text with inline formatting
            if (contentStack.Children.Count == 0)
            {
                var fallbackBlock = new TextBlock
                {
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    FontFamily = new FontFamily("Segoe UI, Segoe UI Symbol, Segoe UI Emoji"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                    IsTextSelectionEnabled = true
                };

                var inlinePattern = new System.Text.RegularExpressions.Regex(@"(\*\*.*?\*\*|`[^`]+`)");
                var segments = inlinePattern.Split(analysisResult);
                foreach (var segment in segments)
                {
                    if (string.IsNullOrEmpty(segment)) continue;

                    if (segment.StartsWith("**") && segment.EndsWith("**") && segment.Length > 4)
                    {
                        fallbackBlock.Inlines.Add(new Run
                        {
                            Text = segment[2..^2],
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold
                        });
                    }
                    else if (segment.StartsWith('`') && segment.EndsWith('`') && segment.Length > 2)
                    {
                        fallbackBlock.Inlines.Add(new Run
                        {
                            Text = segment[1..^1],
                            FontFamily = new FontFamily("Consolas"),
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
                        });
                    }
                    else
                    {
                        fallbackBlock.Inlines.Add(new Run { Text = segment });
                    }
                }

                contentStack.Children.Add(fallbackBlock);
            }

            return contentStack;
        }

        private async System.Threading.Tasks.Task LoadLinearCommentsAsync(string issueId)
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
                var key = LoadProjectCred("LinearApiKey");
                if (string.IsNullOrEmpty(key)) return;

                var service = new LinearService(key);
                var comments = await service.GetCommentsAsync(issueId);
                RenderComments(comments);
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

        private async System.Threading.Tasks.Task LoadJiraCommentsAsync(string issueId)
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
                var service = CreateJiraService();
                if (service == null) return;

                var comments = await service.GetCommentsAsync(issueId);
                RenderComments(comments);
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

        private void RenderComments(List<LinearComment> comments)
        {
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

        private async System.Threading.Tasks.Task LoadWorklogAsync(ProjectTask task)
        {
            WorklogContainer.Children.Clear();

            if (string.IsNullOrEmpty(task.ExternalId))
            {
                WorklogContainer.Children.Add(new TextBlock
                {
                    Text = "Worklog is available for Linear and Jira issues.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            WorklogContainer.Children.Add(new TextBlock
            {
                Text = "Loading worklog...",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 12
            });

            try
            {
                List<WorklogEntry> entries;

                if (_isJiraMode)
                {
                    var service = CreateJiraService();
                    if (service == null) return;
                    entries = await service.GetChangelogAsync(task.ExternalId);
                }
                else if (_isLinearMode)
                {
                    var key = LoadProjectCred("LinearApiKey");
                    if (string.IsNullOrEmpty(key)) return;
                    var service = new LinearService(key);
                    entries = await service.GetIssueHistoryAsync(task.ExternalId);
                }
                else
                {
                    return;
                }

                RenderWorklog(entries);
            }
            catch (Exception ex)
            {
                WorklogContainer.Children.Clear();
                WorklogContainer.Children.Add(new TextBlock
                {
                    Text = $"Error loading worklog: {ex.Message}",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private void RenderWorklog(List<WorklogEntry> entries)
        {
            WorklogContainer.Children.Clear();

            if (entries.Count == 0)
            {
                WorklogContainer.Children.Add(new TextBlock
                {
                    Text = "No changes recorded.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12
                });
                return;
            }

            foreach (var entry in entries.OrderByDescending(e => e.Timestamp))
            {
                var entryPanel = new StackPanel { Spacing = 0 };

                // Timeline dot + header
                var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                headerRow.Children.Add(new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0)
                });

                headerRow.Children.Add(new TextBlock
                {
                    Text = entry.Field,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });

                entryPanel.Children.Add(headerRow);

                // Card content
                var cardContent = new StackPanel { Spacing = 4 };

                // Author + timestamp
                var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (!string.IsNullOrEmpty(entry.Author))
                {
                    metaRow.Children.Add(new TextBlock
                    {
                        Text = entry.Author,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    });
                }
                metaRow.Children.Add(new TextBlock
                {
                    Text = entry.Timestamp.ToString("MMM d, yyyy · h:mm tt"),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                cardContent.Children.Add(metaRow);

                // From → To
                var changeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

                if (!string.IsNullOrEmpty(entry.FromValue))
                {
                    changeRow.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 20, 20)),
                        Child = new TextBlock
                        {
                            Text = entry.FromValue,
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                            FontSize = 10
                        }
                    });
                }

                changeRow.Children.Add(new TextBlock
                {
                    Text = "→",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (!string.IsNullOrEmpty(entry.ToValue))
                {
                    changeRow.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 50, 30)),
                        Child = new TextBlock
                        {
                            Text = entry.ToValue,
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)),
                            FontSize = 10
                        }
                    });
                }

                cardContent.Children.Add(changeRow);

                var cardBorder = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(16, 4, 0, 0),
                    Child = cardContent
                };

                entryPanel.Children.Add(cardBorder);
                WorklogContainer.Children.Add(entryPanel);
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

            if (string.IsNullOrEmpty(_selectedTask.ExternalId))
            {
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Cannot post comment: task is not linked to Linear.";
                return;
            }

            var key = LoadProjectCred("LinearApiKey");
            if (string.IsNullOrEmpty(key)) return;

            try
            {
                var service = new LinearService(key);
                await service.AddCommentAsync(_selectedTask.ExternalId, DetailCommentBox.Text.Trim());
                DetailCommentBox.Text = string.Empty;
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Comment posted successfully.";
                await LoadLinearCommentsAsync(_selectedTask.ExternalId);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error posting comment: {ex.Message}";
            }
        }

        private async void PostJiraComment_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null || string.IsNullOrWhiteSpace(DetailCommentBox.Text)) return;

            if (string.IsNullOrEmpty(_selectedTask.ExternalId))
            {
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Cannot post comment: task is not linked to Jira.";
                return;
            }

            var service = CreateJiraService();
            if (service == null) return;

            try
            {
                await service.AddCommentAsync(_selectedTask.ExternalId, DetailCommentBox.Text.Trim());
                DetailCommentBox.Text = string.Empty;
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Comment posted successfully.";
                await LoadJiraCommentsAsync(_selectedTask.ExternalId);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error posting comment: {ex.Message}";
            }
        }

        private async void OpenInLinear_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null || !Helpers.UriSecurity.IsHttpUrl(_selectedTask.TicketUrl)) return;
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_selectedTask.TicketUrl!));
        }

        private async void OpenInJira_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null || !Helpers.UriSecurity.IsHttpUrl(_selectedTask.TicketUrl)) return;
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_selectedTask.TicketUrl!));
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

            var geminiKey = LoadProjectCred("GeminiApiKey");
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

            // Fetch comments if applicable (needed before building the prompt)
            System.Collections.Generic.List<Models.LinearComment>? linearComments = null;
            if ((_isLinearMode || _isJiraMode) && !string.IsNullOrEmpty(_selectedTask.ExternalId))
            {
                try
                {
                    if (_isLinearMode)
                    {
                        var linearKey = LoadProjectCred("LinearApiKey");
                        if (!string.IsNullOrEmpty(linearKey))
                        {
                            var linearService = new LinearService(linearKey);
                            linearComments = await linearService.GetCommentsAsync(_selectedTask.ExternalId);
                            if (linearComments.Count == 0)
                                linearComments = null;
                        }
                    }
                    else if (_isJiraMode)
                    {
                        var jiraService = CreateJiraService();
                        if (jiraService != null)
                        {
                            linearComments = await jiraService.GetCommentsAsync(_selectedTask.ExternalId);
                            if (linearComments.Count == 0)
                                linearComments = null;
                        }
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
            var toonPrompt = GeminiService.BuildToonPrompt(_selectedTask, linearComments, imageData.Count, _vm?.SelectedProject);

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

            // Save analysis to history
            if (!string.IsNullOrEmpty(analysisResult) && !analysisResult.StartsWith("Analysis failed:"))
            {
                var nextVersion = _selectedTask.AnalysisHistory.Count > 0
                    ? _selectedTask.AnalysisHistory.Max(a => a.Version) + 1
                    : 1;

                var summaryLine = analysisResult.Split('\n')
                    .Select(l => l.Trim().TrimStart('#', '*', ' '))
                    .FirstOrDefault(l => l.Length > 10) ?? "Analysis result";
                if (summaryLine.Length > 120)
                    summaryLine = summaryLine[..117] + "...";

                var historyEntry = new AnalysisEntry
                {
                    Version = nextVersion,
                    Hash = AnalysisEntry.ComputeHash(analysisResult),
                    Timestamp = DateTime.Now,
                    TaskStatus = _selectedTask.Status.ToString(),
                    TaskPriority = _selectedTask.Priority.ToString(),
                    Summary = summaryLine,
                    FullResult = analysisResult
                };
                _selectedTask.AnalysisHistory.Add(historyEntry);

                // For Linear tasks, also persist to the project-level dictionary
                if (_isLinearMode && !string.IsNullOrEmpty(_selectedTask.ExternalId) && _vm?.SelectedProject != null)
                {
                    _vm.SelectedProject.LinearAnalysisHistory[_selectedTask.ExternalId] =
                        new List<AnalysisEntry>(_selectedTask.AnalysisHistory);
                }

                // For Jira tasks, also persist to the project-level dictionary
                if (_isJiraMode && !string.IsNullOrEmpty(_selectedTask.ExternalId) && _vm?.SelectedProject != null)
                {
                    _vm.SelectedProject.JiraAnalysisHistory[_selectedTask.ExternalId] =
                        new List<AnalysisEntry>(_selectedTask.AnalysisHistory);
                }

                if (_vm != null)
                    await _vm.SaveAsync();
            }

            var contentStack = BuildFormattedAnalysisPanel(analysisResult);
            contentStack.Spacing = 16;

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
                var key = LoadProjectCred("LinearApiKey");

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

        // ── Keyboard Shortcuts ───────────────────────────────────────

        private void Page_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // ── Escape: dismiss overlays in priority order ──
            if (e.Key == VirtualKey.Escape)
            {
                if (ShortcutOverlay.Visibility == Visibility.Visible)
                {
                    ShortcutOverlay.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                    return;
                }

                if (LightboxOverlay.Visibility == Visibility.Visible)
                {
                    CloseLightbox_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }

                if (_selectedTask != null)
                {
                    CloseDetailPanel();
                    e.Handled = true;
                    return;
                }
            }

            if (!ctrl) return;

            switch (e.Key)
            {
                // Ctrl+/ — Toggle shortcut overlay
                // The /? key is virtual key code 191 (0xBF)
                case (VirtualKey)191:
                    ToggleShortcutOverlay();
                    e.Handled = true;
                    break;

                // Ctrl+1 — Manual mode
                case VirtualKey.Number1:
                    ManualMode_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                // Ctrl+2 — Linear mode
                case VirtualKey.Number2:
                    LinearMode_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                // Ctrl+R — Refresh (Linear mode only)
                case VirtualKey.R:
                    if (_isLinearMode)
                    {
                        Refresh_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;

                // Ctrl+N — New task (Manual mode only)
                case VirtualKey.N:
                    if (!_isLinearMode)
                    {
                        AddTask_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;

                // Ctrl+S — Save task changes (detail panel open, manual mode)
                case VirtualKey.S:
                    if (_selectedTask != null && !_isLinearMode)
                    {
                        SaveTaskChanges_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;

                // Ctrl+D — Delete task (detail panel open, manual mode)
                case VirtualKey.D:
                    if (_selectedTask != null && !_isLinearMode)
                    {
                        DeleteTask_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;

                // Ctrl+O — Open in Linear (detail panel open, linear mode)
                case VirtualKey.O:
                    if (_selectedTask != null && _isLinearMode)
                    {
                        OpenInLinear_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void ToggleShortcutOverlay()
        {
            ShortcutOverlay.Visibility = ShortcutOverlay.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ShortcutHint_Click(object sender, RoutedEventArgs e)
        {
            ToggleShortcutOverlay();
        }

        private void CloseShortcutOverlay_Click(object sender, RoutedEventArgs e)
        {
            ShortcutOverlay.Visibility = Visibility.Collapsed;
        }

        // ── Bug Report ──────────────────────────────────────────────

        private async void BugReport_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null) return;
            await ShowBugReportDialogAsync(_selectedTask);
        }

        private async System.Threading.Tasks.Task ShowBugReportDialogAsync(ProjectTask task)
        {
            var latestAnalysis = task.AnalysisHistory
                .OrderByDescending(a => a.Version)
                .FirstOrDefault()?.FullResult;

            var envNames = _vm?.SelectedProject?.Environments
                .Select(env => env.Name).ToList() ?? new List<string>();
            if (envNames.Count == 0) envNames.Add("Not specified");

            var envPicker = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58))
            };
            foreach (var n in envNames) envPicker.Items.Add(new ComboBoxItem { Content = n });
            envPicker.SelectedIndex = 0;

            var reporterBox = new TextBox
            {
                PlaceholderText = "Your name (optional)",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                CornerRadius = new CornerRadius(6)
            };

            var includeAnalysisCheck = new CheckBox
            {
                Content = "Include AI analysis",
                IsChecked = latestAnalysis != null,
                IsEnabled = latestAnalysis != null,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240))
            };

            string GetReport() =>
                BugReportService.GenerateFromTask(
                    task,
                    (envPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "",
                    reporterBox.Text.Trim(),
                    includeAnalysisCheck.IsChecked == true ? latestAnalysis : null);

            var reportBox = new TextBox
            {
                Text = GetReport(),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 320,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 10, 16)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 134, 239, 172)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };

            void Regenerate()
            {
                reportBox.Text = GetReport();
            }

            envPicker.SelectionChanged += (s, _) => Regenerate();
            reporterBox.TextChanged += (s, _) => Regenerate();
            includeAnalysisCheck.Checked += (s, _) => Regenerate();
            includeAnalysisCheck.Unchecked += (s, _) => Regenerate();

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = "Environment",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184)),
                FontSize = 12
            });
            panel.Children.Add(envPicker);
            panel.Children.Add(new TextBlock
            {
                Text = "Reporter",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184)),
                FontSize = 12
            });
            panel.Children.Add(reporterBox);
            panel.Children.Add(includeAnalysisCheck);
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58))
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Report Preview (editable)",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184)),
                FontSize = 12
            });
            panel.Children.Add(reportBox);

            var dialog = new ContentDialog
            {
                Title = "Generate Bug Report",
                Content = new ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = "Copy Markdown",
                SecondaryButtonText = _isLinearMode ? "Post to Linear" : (_isJiraMode ? "Post to Jira" : ""),
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            dialog.Resources["ContentDialogMaxWidth"] = 680.0;
            DialogHelper.ApplyDarkTheme(dialog);

            var action = await dialog.ShowAsync();

            if (action == ContentDialogResult.Primary)
            {
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(reportBox.Text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Bug report copied to clipboard.";
            }
            else if (action == ContentDialogResult.Secondary)
            {
                await PostBugReportToTrackerAsync(task, reportBox.Text);
            }
        }

        private async System.Threading.Tasks.Task PostBugReportToTrackerAsync(ProjectTask task, string markdown)
        {
            var title = $"[Bug] {task.Title}";
            try
            {
                if (_isLinearMode)
                {
                    var key = LoadProjectCred("LinearApiKey");
                    var teamId = LoadProjectCred("LinearTeamId");
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(teamId))
                    {
                        StatusText.Visibility = Visibility.Visible;
                        StatusText.Text = "No Linear credentials found. Go to Settings.";
                        return;
                    }
                    var service = new LinearService(key);
                    var url = await service.CreateIssueAsync(teamId, title, markdown, priority: 2);
                    StatusText.Visibility = Visibility.Visible;
                    StatusText.Text = url != null ? $"Bug created: {url}" : "Bug report posted to Linear.";
                    if (url != null && Helpers.UriSecurity.IsSafeHttpUrl(url))
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                }
                else if (_isJiraMode)
                {
                    var service = CreateJiraService();
                    var projectKey = LoadProjectCred("JiraProjectKey");
                    if (service == null || string.IsNullOrEmpty(projectKey))
                    {
                        StatusText.Visibility = Visibility.Visible;
                        StatusText.Text = "No Jira credentials found. Go to Settings.";
                        return;
                    }
                    var url = await service.CreateIssueAsync(projectKey, title, markdown, "High");
                    StatusText.Visibility = Visibility.Visible;
                    StatusText.Text = url != null ? $"Bug created: {url}" : "Bug report posted to Jira.";
                    if (url != null && Helpers.UriSecurity.IsSafeHttpUrl(url))
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                }
            }
            catch (Exception ex)
            {
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = $"Failed to post bug report: {ex.Message}";
            }
        }
    }
}
