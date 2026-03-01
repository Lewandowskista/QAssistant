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
                var jobs = await _hacService.GetCronJobsAsync();
                RenderCronJobs(jobs);
                CronJobStatusText.Text = $"{jobs.Count} jobs";
            }
            catch (Exception ex) { CronJobStatusText.Text = $"Error: {ex.Message}"; }
        }

        private void RenderCronJobs(List<CronJobEntry> jobs)
        {
            CronJobsContainer.Children.Clear();
            CronJobEmptyText.Visibility = jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Header
            var header = BuildCronJobRow("Code", "Status", "Last Result", "Next Run", "Trigger", isHeader: true);
            CronJobsContainer.Children.Add(header);

            foreach (var job in jobs)
            {
                var row = BuildCronJobRow(job.Code, job.Status, job.LastResult, job.NextActivationTime, job.TriggerActive);
                CronJobsContainer.Children.Add(row);
            }
        }

        private static UIElement BuildCronJobRow(string code, string status, string lastResult, string next, string trigger, bool isHeader = false)
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
