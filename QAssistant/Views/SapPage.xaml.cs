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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using QAssistant.Models;
using QAssistant.Services;
using QAssistant.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace QAssistant.Views
{
    public sealed partial class SapPage : Page
    {
        private MainViewModel? _vm;
        private string _activeTab = "Cronjobs";
        private SapHacService? _hacService;
        private QaEnvironment? _selectedEnv;
        private List<CronJobEntry> _allCronJobs = [];
        private string _currentCronFilter = "All";
        private List<(string EnvName, CatalogSyncDiff Diff)> _syncHistory = [];

        public SapPage() { this.InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                PopulateEnvPicker();
            }
        }

        private void PopulateEnvPicker()
        {
            if (_vm?.SelectedProject is not { } project) return;
            SapEnvPicker.ItemsSource = null;

            var envs = project.Environments
                .Where(env => !string.IsNullOrWhiteSpace(env.HacUrl))
                .ToList();

            SapEnvPicker.ItemsSource = envs.Select(e => e.Name).ToList();

            var def = envs.FirstOrDefault(e => e.IsDefault) ?? envs.FirstOrDefault();
            if (def != null)
            {
                SapEnvPicker.SelectedIndex = envs.IndexOf(def);
                _selectedEnv = def;
            }
        }

        private void SapEnvPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm?.SelectedProject is not { } project) return;
            var envs = project.Environments.Where(env => !string.IsNullOrWhiteSpace(env.HacUrl)).ToList();
            int idx = SapEnvPicker.SelectedIndex;
            if (idx >= 0 && idx < envs.Count)
                _selectedEnv = envs[idx];
        }

        private void SapTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) SetTab(btn.Tag.ToString()!);
        }

        private void SetTab(string tab)
        {
            _activeTab = tab;
            var tabBtns = new[] { SapTabCronjobs, SapTabCatalog, SapTabFlexSearch, SapTabImpex };
            var tags = new[] { "Cronjobs", "Catalog", "FlexSearch", "Impex" };
            for (int i = 0; i < tabBtns.Length; i++)
            {
                bool active = tags[i] == tab;
                tabBtns[i].Background = active
                    ? (Brush)Application.Current.Resources["ListAccentLowBrush"]
                    : new SolidColorBrush(Colors.Transparent);
                tabBtns[i].Foreground = active
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["TextSecondaryBrush"];
            }

            CronjobsPanel.Visibility = tab == "Cronjobs" ? Visibility.Visible : Visibility.Collapsed;
            CatalogPanel.Visibility = tab == "Catalog" ? Visibility.Visible : Visibility.Collapsed;
            FlexSearchPanel.Visibility = tab == "FlexSearch" ? Visibility.Visible : Visibility.Collapsed;
            ImpexPanel.Visibility = tab == "Impex" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SapConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEnv == null)
            {
                SapConnStatus.Text = "Select an environment first";
                return;
            }

            _hacService?.Dispose();
            _hacService = new SapHacService(_selectedEnv.HacUrl);

            SapConnStatus.Text = "Connecting...";
            SapConnectBtn.IsEnabled = false;

            var id = _selectedEnv.Id.ToString("N");
            var user = CredentialService.LoadCredential($"Env_{id}_Username") ?? "admin";
            var pass = CredentialService.LoadCredential($"Env_{id}_Password") ?? "";

            bool ok = await _hacService.LoginAsync(user, pass);
            SapConnStatus.Text = ok ? "✓ Connected" : "✗ Login failed";
            SapConnectBtn.IsEnabled = true;
        }

        // ── CronJobs ─────────────────────────────────────────────────

        private async void RefreshCronJobs_Click(object sender, RoutedEventArgs e)
        {
            if (_hacService == null) { CronJobStatusText.Text = "Not connected"; return; }
            CronJobStatusText.Text = "Loading...";
            try
            {
                _allCronJobs = await _hacService.GetCronJobsAsync();
                ApplyFilterAndRender();
                CheckAndShowCriticalAlerts(_allCronJobs);
                CronJobStatusText.Text = $"{_allCronJobs.Count} jobs";
            }
            catch (Exception ex) { CronJobStatusText.Text = $"Error: {ex.Message}"; }
        }

        private void RenderCronJobs(List<CronJobEntry> jobs)
        {
            CronJobsContainer.Children.Clear();
            CronJobEmptyText.Visibility = jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var header = BuildCronJobRow("Code", "Status", "Last Result", "Next Run", "Trigger", null, isHeader: true);
            CronJobsContainer.Children.Add(header);

            foreach (var job in jobs)
            {
                var localJob = job;
                var row = BuildCronJobRow(job.Code, job.Status, job.LastResult, job.NextActivationTime, job.TriggerActive,
                    () => _ = LoadCronJobHistoryAsync(localJob.Code));
                CronJobsContainer.Children.Add(row);
            }
        }

        private static UIElement BuildCronJobRow(string code, string status, string lastResult, string next, string trigger, Action? onHistoryClick = null, bool isHeader = false)
        {
            var grid = new Grid
            {
                Padding = new Thickness(12, 8, 12, 8),
                Background = isHeader
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36))
                    : new SolidColorBrush(Colors.Transparent)
            };
            for (int i = 0; i < 5; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Windows.UI.Color statusColor = status switch
            {
                var s when s.Contains("FINISHED", StringComparison.OrdinalIgnoreCase) => Windows.UI.Color.FromArgb(255, 16, 185, 129),
                var s when s.Contains("ERROR", StringComparison.OrdinalIgnoreCase) => Windows.UI.Color.FromArgb(255, 239, 68, 68),
                var s when s.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) => Windows.UI.Color.FromArgb(255, 59, 130, 246),
                _ => Windows.UI.Color.FromArgb(255, 107, 114, 128)
            };

            void Add(string text, int col, bool colored = false)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = isHeader ? 11 : 12,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = colored
                        ? new SolidColorBrush(statusColor)
                        : new SolidColorBrush(isHeader
                            ? Windows.UI.Color.FromArgb(255, 107, 114, 128)
                            : Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }

            Add(code, 0);
            Add(status, 1, !isHeader);
            Add(lastResult, 2);
            Add(next, 3);
            Add(trigger, 4);

            if (isHeader)
            {
                Add("", 5);
            }
            else if (onHistoryClick != null)
            {
                var histBtn = new Button
                {
                    Content = "History",
                    FontSize = 11,
                    Padding = new Thickness(8, 3, 8, 3),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 45)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                histBtn.Click += (_, _) => onHistoryClick();
                Grid.SetColumn(histBtn, 5);
                grid.Children.Add(histBtn);
            }

            return new Border
            {
                Child = grid,
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
        }

        // ── CronJob filter / alert / history ─────────────────────────

        private void CronFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            _currentCronFilter = btn.Tag.ToString()!;
            UpdateCronFilterButtons();
            ApplyFilterAndRender();
        }

        private void UpdateCronFilterButtons()
        {
            var filterBtns = new[] { CronFilterAll, CronFilterRunning, CronFilterFailed, CronFilterCritical };
            var tags = new[] { "All", "Running", "Failed", "Critical" };
            for (int i = 0; i < filterBtns.Length; i++)
            {
                bool active = tags[i] == _currentCronFilter;
                filterBtns[i].Background = active
                    ? (Brush)Application.Current.Resources["ListAccentLowBrush"]
                    : new SolidColorBrush(Colors.Transparent);
                filterBtns[i].Foreground = active
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["TextSecondaryBrush"];
            }
        }

        private void ApplyFilterAndRender()
        {
            var filtered = _currentCronFilter switch
            {
                "Running"  => _allCronJobs.Where(j => j.Status.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)).ToList(),
                "Failed"   => _allCronJobs.Where(j =>
                                  j.LastResult.Contains("ERROR",   StringComparison.OrdinalIgnoreCase) ||
                                  j.LastResult.Contains("FAILURE", StringComparison.OrdinalIgnoreCase) ||
                                  j.Status.Contains("ERROR",   StringComparison.OrdinalIgnoreCase) ||
                                  j.Status.Contains("ABORTED", StringComparison.OrdinalIgnoreCase)).ToList(),
                "Critical" => _allCronJobs.Where(j =>
                                  SapHacService.CriticalJobCodes.Any(c =>
                                      j.Code.Contains(c, StringComparison.OrdinalIgnoreCase))).ToList(),
                _          => _allCronJobs
            };
            RenderCronJobs(filtered);
        }

        private async void CheckCriticalAlerts_Click(object sender, RoutedEventArgs e)
        {
            if (_hacService == null) { CronJobStatusText.Text = "Not connected"; return; }
            CronJobStatusText.Text = "Checking critical jobs...";
            try
            {
                var critical = await _hacService.GetCriticalJobStatusAsync();
                CheckAndShowCriticalAlerts(critical);
                CronJobStatusText.Text = _allCronJobs.Count > 0 ? $"{_allCronJobs.Count} jobs" : "Alert check complete";
            }
            catch (Exception ex) { CronJobStatusText.Text = $"Error: {ex.Message}"; }
        }

        private void CheckAndShowCriticalAlerts(List<CronJobEntry> jobs)
        {
            var failed = jobs
                .Where(j => SapHacService.CriticalJobCodes.Any(c =>
                                j.Code.Contains(c, StringComparison.OrdinalIgnoreCase))
                         && (j.LastResult.Contains("ERROR",   StringComparison.OrdinalIgnoreCase) ||
                             j.LastResult.Contains("FAILURE", StringComparison.OrdinalIgnoreCase) ||
                             j.Status.Contains("ERROR",   StringComparison.OrdinalIgnoreCase) ||
                             j.Status.Contains("ABORTED", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            CronAlertsBanner.Visibility = failed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CronAlertsContainer.Children.Clear();
            if (failed.Count == 0) return;

            CronAlertsContainer.Children.Add(new TextBlock
            {
                Text = $"⚠  {failed.Count} critical job(s) require attention",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            foreach (var job in failed)
            {
                CronAlertsContainer.Children.Add(new TextBlock
                {
                    Text = $"  • {job.Code}  —  Status: {job.Status}  |  Last Result: {job.LastResult}",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 252, 165, 165)),
                    FontSize = 12
                });
            }
        }

        private void CloseCronAlerts_Click(object sender, RoutedEventArgs e)
            => CronAlertsBanner.Visibility = Visibility.Collapsed;

        private void CloseCronHistory_Click(object sender, RoutedEventArgs e)
        {
            CronHistoryHeader.Visibility = Visibility.Collapsed;
            CronHistoryScrollView.Visibility = Visibility.Collapsed;
        }

        private async Task LoadCronJobHistoryAsync(string jobCode)
        {
            if (_hacService == null) return;
            CronHistoryHeaderText.Text = $"History — {jobCode}";
            CronHistoryStatusText.Text = "Loading...";
            CronHistoryHeader.Visibility = Visibility.Visible;
            CronHistoryScrollView.Visibility = Visibility.Visible;
            CronHistoryContainer.Children.Clear();
            try
            {
                var history = await _hacService.GetCronJobHistoryAsync(jobCode);
                RenderCronJobHistory(history);
                CronHistoryStatusText.Text = $"({history.Count} entries)";
            }
            catch (Exception ex) { CronHistoryStatusText.Text = $"Error: {ex.Message}"; }
        }

        private void RenderCronJobHistory(List<CronJobHistoryEntry> history)
        {
            CronHistoryContainer.Children.Clear();
            if (history.Count == 0)
            {
                CronHistoryContainer.Children.Add(new TextBlock
                {
                    Text = "No history entries found",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12,
                    Margin = new Thickness(0, 8, 0, 0)
                });
                return;
            }

            CronHistoryContainer.Children.Add(BuildHistoryRow("Status", "Result", "Start Time", "End Time", "Duration", isHeader: true));
            foreach (var entry in history)
                CronHistoryContainer.Children.Add(BuildHistoryRow(entry.Status, entry.Result, entry.StartTime, entry.EndTime, entry.Duration));
        }

        private static UIElement BuildHistoryRow(string status, string result, string start, string end, string duration, bool isHeader = false)
        {
            var grid = new Grid
            {
                Padding = new Thickness(12, 6, 12, 6),
                Background = isHeader
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36))
                    : new SolidColorBrush(Colors.Transparent)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            Windows.UI.Color resultColor = result switch
            {
                var r when r.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) => Windows.UI.Color.FromArgb(255, 16, 185, 129),
                var r when r.Contains("ERROR",   StringComparison.OrdinalIgnoreCase) => Windows.UI.Color.FromArgb(255, 239, 68, 68),
                var r when r.Contains("FAILURE", StringComparison.OrdinalIgnoreCase) => Windows.UI.Color.FromArgb(255, 239, 68, 68),
                _ => Windows.UI.Color.FromArgb(255, 107, 114, 128)
            };

            void Add(string text, int col, bool colored = false)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = isHeader ? 11 : 12,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = colored
                        ? new SolidColorBrush(resultColor)
                        : new SolidColorBrush(isHeader
                            ? Windows.UI.Color.FromArgb(255, 107, 114, 128)
                            : Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }

            Add(status, 0);
            Add(result, 1, !isHeader);
            Add(start, 2);
            Add(end, 3);
            Add(duration, 4);

            return new Border
            {
                Child = grid,
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
        }

        // ── Catalog Sync ──────────────────────────────────────────────

        private async void RefreshCatalog_Click(object sender, RoutedEventArgs e)
        {
            if (_hacService == null) { CatalogStatusText.Text = "Not connected"; return; }
            CatalogStatusText.Text = "Loading...";
            try
            {
                var versions = await _hacService.GetCatalogVersionsAsync();
                RenderCatalog(versions);
                CatalogStatusText.Text = $"{versions.Count} catalog versions";
                await LoadCatalogPickerAsync();
            }
            catch (Exception ex) { CatalogStatusText.Text = $"Error: {ex.Message}"; }
        }

        private void RenderCatalog(List<CatalogVersionInfo> versions)
        {
            CatalogContainer.Children.Clear();
            CatalogEmptyText.Visibility = versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Group by catalog ID
            var grouped = versions.GroupBy(v => v.CatalogId).ToList();
            foreach (var group in grouped)
            {
                var groupHeader = new TextBlock
                {
                    Text = group.Key,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 4)
                };
                CatalogContainer.Children.Add(groupHeader);

                var staged = group.FirstOrDefault(v => v.Version == "Staged");
                var online = group.FirstOrDefault(v => v.Version == "Online");

                foreach (var cv in group.OrderBy(v => v.Version))
                {
                    bool inSync = staged != null && online != null && staged.ItemCount == online.ItemCount;
                    string syncHint = cv.Version == "Online" && staged != null && online != null
                        ? (inSync ? " ✓ in sync" : $" ⚠ {online.ItemCount - staged.ItemCount:+#;-#;0} vs Staged")
                        : string.Empty;

                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var ver = new TextBlock
                    {
                        Text = cv.Version,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var count = new TextBlock
                    {
                        Text = $"{cv.ItemCount:N0} products",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var hint = new TextBlock
                    {
                        Text = syncHint,
                        Foreground = new SolidColorBrush(inSync
                            ? Windows.UI.Color.FromArgb(255, 16, 185, 129)
                            : Windows.UI.Color.FromArgb(255, 245, 158, 11)),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    Grid.SetColumn(ver, 0);
                    Grid.SetColumn(count, 1);
                    Grid.SetColumn(hint, 2);
                    row.Children.Add(ver); row.Children.Add(count); row.Children.Add(hint);
                    CatalogContainer.Children.Add(row);
                }
            }
        }

        // ── Catalog Sync Validator ────────────────────────────────────

        private async Task LoadCatalogPickerAsync()
        {
            if (_hacService == null) return;
            try
            {
                var ids = await _hacService.GetCatalogIdsAsync();
                SyncCatalogPicker.ItemsSource = ids;
                if (ids.Count > 0 && SyncCatalogPicker.SelectedIndex < 0)
                    SyncCatalogPicker.SelectedIndex = 0;
            }
            catch { /* leave picker empty */ }
        }

        private async void ValidateSync_Click(object sender, RoutedEventArgs e)
        {
            if (_hacService == null) { SyncStatusText.Text = "Not connected"; return; }
            if (SyncCatalogPicker.SelectedItem is not string catalogId)
            {
                SyncStatusText.Text = "Select a catalog first";
                return;
            }

            SyncStatusText.Text = "Validating...";
            try
            {
                var diff = await _hacService.GetCatalogSyncDiffAsync(catalogId);
                _syncHistory.Insert(0, (_selectedEnv?.Name ?? "Unknown", diff));
                RenderSyncDiff(diff);
                RenderSyncHistory();
                ShowSyncAlert(diff);
                SyncStatusText.Text = $"Done — {diff.SyncStatus}";
            }
            catch (Exception ex) { SyncStatusText.Text = $"Error: {ex.Message}"; }
        }

        private void RenderSyncDiff(CatalogSyncDiff diff)
        {
            SyncDiffPanel.Visibility = Visibility.Visible;
            SyncDiffCatalogLabel.Text = diff.CatalogId;
            SyncDiffTimestamp.Text = diff.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss");
            SyncDiffStagedCount.Text = diff.StagedCount.ToString("N0");
            SyncDiffOnlineCount.Text = diff.OnlineCount.ToString("N0");

            bool inSync = diff.IsInSync;
            SyncDiffDelta.Text = diff.Delta.ToString("N0");
            SyncDiffDelta.Foreground = new SolidColorBrush(inSync
                ? Windows.UI.Color.FromArgb(255, 16, 185, 129)
                : Windows.UI.Color.FromArgb(255, 239, 68, 68));

            SyncStatusBadge.Background = new SolidColorBrush(inSync
                ? Windows.UI.Color.FromArgb(255, 14, 43, 26)
                : Windows.UI.Color.FromArgb(255, 45, 14, 14));
            SyncStatusBadgeText.Text = diff.SyncStatus;
            SyncStatusBadgeText.Foreground = new SolidColorBrush(inSync
                ? Windows.UI.Color.FromArgb(255, 16, 185, 129)
                : Windows.UI.Color.FromArgb(255, 239, 68, 68));

            MissingProductsSection.Visibility = diff.MissingInOnline.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            MissingCountBadge.Text = diff.MissingInOnline.Count.ToString();
            MissingProductsContainer.Children.Clear();
            foreach (var code in diff.MissingInOnline)
            {
                MissingProductsContainer.Children.Add(new TextBlock
                {
                    Text = $"  {code}",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 252, 165, 165)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12
                });
            }
        }

        private void RenderSyncHistory()
        {
            if (_syncHistory.Count == 0) return;
            SyncHistoryPanel.Visibility = Visibility.Visible;
            SyncHistoryContainer.Children.Clear();
            SyncHistoryContainer.Children.Add(BuildSyncHistoryRow("Environment", "Catalog", "Staged", "Online", "Delta", "Status", "Checked At", isHeader: true));
            foreach (var (envName, diff) in _syncHistory.Take(20))
                SyncHistoryContainer.Children.Add(BuildSyncHistoryRow(envName, diff.CatalogId, diff.StagedCount.ToString("N0"), diff.OnlineCount.ToString("N0"), diff.Delta.ToString("N0"), diff.SyncStatus, diff.CheckedAt.ToString("HH:mm:ss")));
        }

        private static UIElement BuildSyncHistoryRow(string env, string catalog, string staged, string online, string delta, string status, string checkedAt, bool isHeader = false)
        {
            var grid = new Grid { Padding = new Thickness(10, 6, 10, 6) };
            var widths = new[] { 1.2, 1.2, 0.8, 0.8, 0.7, 0.9, 1.0 };
            foreach (var w in widths)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });

            bool isInSync = status == "In Sync";
            Windows.UI.Color statusColor = isInSync
                ? Windows.UI.Color.FromArgb(255, 16, 185, 129)
                : Windows.UI.Color.FromArgb(255, 239, 68, 68);
            Windows.UI.Color defaultColor = isHeader
                ? Windows.UI.Color.FromArgb(255, 107, 114, 128)
                : Windows.UI.Color.FromArgb(255, 226, 232, 240);

            var values = new[] { env, catalog, staged, online, delta, status, checkedAt };
            for (int i = 0; i < values.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = values[i],
                    FontSize = isHeader ? 11 : 12,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = new SolidColorBrush(i == 5 && !isHeader ? statusColor : defaultColor),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(tb, i);
                grid.Children.Add(tb);
            }

            return new Border
            {
                Child = grid,
                Background = isHeader
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36))
                    : new SolidColorBrush(Colors.Transparent),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
        }

        private void ShowSyncAlert(CatalogSyncDiff diff)
        {
            SyncAlertBanner.Visibility = Visibility.Collapsed;
            if (diff.IsInSync) return;
            SyncAlertContainer.Children.Clear();
            SyncAlertContainer.Children.Add(new TextBlock
            {
                Text = $"⚠  Catalog '{diff.CatalogId}': {diff.Delta} product(s) in Staged not found in Online",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            SyncAlertBanner.Visibility = Visibility.Visible;
        }

        private void CloseSyncAlert_Click(object sender, RoutedEventArgs e)
            => SyncAlertBanner.Visibility = Visibility.Collapsed;

        // ── FlexSearch ────────────────────────────────────────────────

        private void FlexTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FlexTemplatesPicker.SelectedItem is ComboBoxItem item && item.Tag is string query)
            {
                FlexQueryBox.Text = query;
                FlexTemplatesPicker.SelectedIndex = -1;
            }
        }

        private async void RunFlexSearch_Click(object sender, RoutedEventArgs e)
        {
            if (_hacService == null) { FlexStatusText.Text = "Not connected"; return; }
            var query = FlexQueryBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return;

            FlexStatusText.Text = "Executing...";
            FlexResultsContainer.Children.Clear();

            try
            {
                var result = await _hacService.RunFlexibleSearchAsync(query);
                if (!string.IsNullOrEmpty(result.Error))
                {
                    FlexStatusText.Text = $"Error: {result.Error}";
                    return;
                }
                RenderFlexResults(result.Headers, result.Rows);
                FlexStatusText.Text = $"{result.Rows.Count} rows";
            }
            catch (Exception ex) { FlexStatusText.Text = $"Error: {ex.Message}"; }
        }

        private void RenderFlexResults(List<string> headers, List<List<string>> rows)
        {
            FlexResultsContainer.Children.Clear();
            if (headers.Count == 0) return;

            var grid = new Grid();
            foreach (var _ in headers)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Header row
            var headerBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36));
            int gridRow = 0;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < headers.Count; c++)
            {
                var cell = new TextBlock
                {
                    Text = headers[c],
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Padding = new Thickness(8, 6, 8, 6)
                };
                var cellBorder = new Border { Background = headerBg, Child = cell };
                Grid.SetRow(cellBorder, 0); Grid.SetColumn(cellBorder, c);
                grid.Children.Add(cellBorder);
            }

            // Data rows
            foreach (var row in rows.Take(200))
            {
                gridRow++;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var rowBg = gridRow % 2 == 0
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 28))
                    : new SolidColorBrush(Colors.Transparent);

                for (int c = 0; c < Math.Min(row.Count, headers.Count); c++)
                {
                    var cell = new TextBlock
                    {
                        Text = row[c],
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                        FontSize = 12,
                        FontFamily = new FontFamily("Consolas"),
                        Padding = new Thickness(8, 4, 8, 4),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    var cellBorder = new Border { Background = rowBg, Child = cell };
                    Grid.SetRow(cellBorder, gridRow); Grid.SetColumn(cellBorder, c);
                    grid.Children.Add(cellBorder);
                }
            }

            FlexResultsContainer.Children.Add(grid);
        }

        // ── ImpEx ─────────────────────────────────────────────────────

        private void ImpexTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImpexTemplatePicker.SelectedItem is ComboBoxItem item && item.Tag is string script)
            {
                ImpexScriptBox.Text = script;
                ImpexTemplatePicker.SelectedIndex = -1;
            }
        }

        private void ValidateImpex_Click(object sender, RoutedEventArgs e)
        {
            var script = ImpexScriptBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(script)) { ImpexStatusText.Text = "Script is empty"; return; }

            var issues = new List<string>();
            var lines = script.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

                if (Regex.IsMatch(line, @"^(INSERT|UPDATE|INSERT_UPDATE|REMOVE)\s", RegexOptions.IgnoreCase))
                {
                    if (!line.Contains(';'))
                        issues.Add($"Line {i + 1}: Missing semicolon separator in header");
                }
            }

            ImpexStatusText.Text = issues.Count == 0
                ? "✓ Basic syntax looks valid"
                : $"⚠ {issues.Count} issue(s): {string.Join(" | ", issues)}";
            ImpexStatusText.Foreground = issues.Count == 0
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11));
        }

        private async void ImportImpex_Click(object sender, RoutedEventArgs e)
        {
            if (_hacService == null) { ImpexStatusText.Text = "Not connected to HAC"; return; }
            var script = ImpexScriptBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(script)) return;

            ImpexStatusText.Text = "Importing...";
            try
            {
                var result = await _hacService.ImportImpExAsync(script);
                ImpexStatusText.Text = result.Success ? "✓ Import successful" : "✗ Import failed";
                ImpexStatusText.Foreground = result.Success
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));
                ImpexLogText.Text = result.Log;
            }
            catch (Exception ex) { ImpexStatusText.Text = $"Error: {ex.Message}"; }
        }
    }
}
