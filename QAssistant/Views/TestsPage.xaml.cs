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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using QAssistant.Helpers;
using QAssistant.Models;
using QAssistant.Services;
using QAssistant.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QAssistant.Views
{
    public sealed partial class TestsPage : Page
    {
        private MainViewModel? _vm;
        private string _activeSubTab = "TestCaseGeneration";
        private readonly HashSet<Guid> _collapsedPlans = [];
        private readonly HashSet<Guid> _collapsedExecutionPlans = [];
        private readonly HashSet<Guid> _collapsedExecutionTestCases = [];
        private readonly HashSet<Guid> _selectedPlanIds = [];
        private bool _criticalityExpanded;
        private string? _criticalityAssessmentText;
        private int _suggestionsSnapshotExecutionCount = -1;
        private int _suggestionsSnapshotFailedCount = -1;
        private bool _showArchived;
        private bool _showArchivedRuns;
        private string? _designDocumentContent;
        private string? _designDocumentName;
        private string _coverageViewMode = "Issue";
        private string _testCaseViewMode = "AllPlans";
        private List<string>? _smokeSubsetCaseIds;

        private Guid ProjectId => _vm?.SelectedProject?.Id ?? Guid.Empty;

        private string? LoadProjectCred(string key) =>
            ProjectId != Guid.Empty
                ? CredentialService.LoadProjectCredential(ProjectId, key)
                : CredentialService.LoadCredential(key);

        public TestsPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                await MigrateOrphanedTestCasesAsync();
                RenderTestPlans();
            }
        }

        // â”€â”€ Sub-tab navigation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void SubTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _activeSubTab = btn.Tag.ToString()!;
                UpdateSubTabStyles();
                ShowActivePanel();
            }
        }

        private void UpdateSubTabStyles()
        {
            var tabs = new[] { TestCaseGenBtn, TestRunsBtn, ReportsBtn, CoverageMatrixBtn, RegressionBuilderBtn };
            foreach (var tab in tabs)
            {
                bool active = tab.Tag.ToString() == _activeSubTab;
                tab.Background = active
                    ? (Brush)Application.Current.Resources["ListAccentLowBrush"]
                    : new SolidColorBrush(Colors.Transparent);
                tab.Foreground = active
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["TextSecondaryBrush"];
            }
        }

        private void ShowActivePanel()
        {
            TestCaseGenerationPanel.Visibility = _activeSubTab == "TestCaseGeneration" ? Visibility.Visible : Visibility.Collapsed;
            TestRunsPanel.Visibility = _activeSubTab == "TestRuns" ? Visibility.Visible : Visibility.Collapsed;
            ReportsPanel.Visibility = _activeSubTab == "Reports" ? Visibility.Visible : Visibility.Collapsed;
            CoverageMatrixPanel.Visibility = _activeSubTab == "CoverageMatrix" ? Visibility.Visible : Visibility.Collapsed;
            RegressionBuilderPanel.Visibility = _activeSubTab == "RegressionBuilder" ? Visibility.Visible : Visibility.Collapsed;

            if (_activeSubTab == "TestCaseGeneration")
                RenderTestPlans();
            if (_activeSubTab == "TestRuns")
                RenderExecutionHistory();
            if (_activeSubTab == "Reports")
                RenderReportsDashboard();
            if (_activeSubTab == "CoverageMatrix")
                RenderCoverageMatrix();
            if (_activeSubTab == "RegressionBuilder")
                RenderRegressionBuilder();
        }

        private void ShowArchivedToggle_Click(object sender, RoutedEventArgs e)
        {
            _showArchived = ShowArchivedToggle.IsChecked == true;
            RenderTestPlans();
        }

        private void TestCaseViewPicker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (TestCasesContainer == null) return;
            var tag = (TestCaseViewPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "AllPlans";
            _testCaseViewMode = tag;
            RenderTestPlans();
        }

        private void ShowArchivedRunsToggle_Click(object sender, RoutedEventArgs e)
        {
            _showArchivedRuns = ShowArchivedRunsToggle.IsChecked == true;
            RenderExecutionHistory();
        }

        private async void UploadDesignDoc_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".doc");
            picker.FileTypeFilter.Add(".rtf");
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add(".html");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                _designDocumentContent = await Windows.Storage.FileIO.ReadTextAsync(file);
                _designDocumentName = file.Name;

                const int MaxDesignDocLength = 30_000;
                if (_designDocumentContent.Length > MaxDesignDocLength)
                    _designDocumentContent = _designDocumentContent[..MaxDesignDocLength];

                DesignDocStatus.Text = file.Name;
                DesignDocStatus.Visibility = Visibility.Visible;
                ClearDesignDocBtn.Visibility = Visibility.Visible;
                GenerationStatusText.Text = $"Design doc loaded: {file.Name}";
            }
            catch (Exception ex)
            {
                GenerationStatusText.Text = $"Failed to read document: {ex.Message}";
            }
        }

        private void ClearDesignDoc_Click(object sender, RoutedEventArgs e)
        {
            _designDocumentContent = null;
            _designDocumentName = null;
            DesignDocStatus.Text = string.Empty;
            DesignDocStatus.Visibility = Visibility.Collapsed;
            ClearDesignDocBtn.Visibility = Visibility.Collapsed;
            GenerationStatusText.Text = "Design document cleared.";
        }

        // â”€â”€ Backward compatibility â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async System.Threading.Tasks.Task MigrateOrphanedTestCasesAsync()
        {
            if (_vm?.SelectedProject == null) return;

            var orphans = _vm.SelectedProject.TestCases
                .Where(tc => tc.TestPlanId == null || tc.TestPlanId == Guid.Empty)
                .ToList();

            if (orphans.Count == 0) return;

            var distinctSources = orphans.Select(tc => tc.Source).Distinct().ToList();
            var plan = new TestPlan
            {
                TestPlanId = NextTestPlanId(),
                Name = "Imported Test Cases",
                Description = "Test cases imported from a previous session.",
                Source = distinctSources.Count == 1 ? distinctSources[0] : TaskSource.Manual
            };
            _vm.SelectedProject.TestPlans.Add(plan);

            foreach (var tc in orphans)
                tc.TestPlanId = plan.Id;

            await _vm.SaveAsync();
        }

        // â”€â”€ ID helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private string NextTestPlanId()
        {
            int max = _vm?.SelectedProject?.TestPlans
                .Select(p => int.TryParse(p.TestPlanId.Replace("TP-", ""), out var n) ? n : 0)
                .DefaultIfEmpty(0).Max() ?? 0;
            return $"TP-{max + 1:D3}";
        }

        private string NextTestCaseId()
        {
            int max = _vm?.SelectedProject?.TestCases
                .Select(tc => int.TryParse(tc.TestCaseId.Replace("TC-", ""), out var n) ? n : 0)
                .DefaultIfEmpty(0).Max() ?? 0;
            return $"TC-{max + 1:D3}";
        }

        private string NextExecutionId()
        {
            int max = _vm?.SelectedProject?.TestExecutions
                .Select(te => int.TryParse(te.ExecutionId.Replace("TE-", ""), out var n) ? n : 0)
                .DefaultIfEmpty(0).Max() ?? 0;
            return $"TE-{max + 1:D3}";
        }

        // â”€â”€ Generate test cases â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async void GenerateTestCases_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            var geminiKey = LoadProjectCred("GeminiApiKey");
            if (string.IsNullOrEmpty(geminiKey))
            {
                var dialog = new ContentDialog
                {
                    Title = "API Key Missing",
                    Content = "Please add your Google AI Studio API key in Settings.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                DialogHelper.ApplyDarkTheme(dialog);
                await dialog.ShowAsync();
                return;
            }

            var selectedSource = (SourcePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Linear";

            List<ProjectTask> tasks;
            try
            {
                GenerationStatusText.Text = $"Fetching issues from {selectedSource}...";
                tasks = await FetchIssuesFromSourceAsync(selectedSource);
            }
            catch (Exception ex)
            {
                GenerationStatusText.Text = $"Error: {ex.Message}";
                return;
            }

            if (tasks.Count == 0)
            {
                GenerationStatusText.Text = "No issues found. Check your credentials in Settings.";
                return;
            }

            GenerationStatusText.Text = $"Fetched {tasks.Count} issues. Generating test cases...";

            var source = selectedSource == "Jira" ? TaskSource.Jira : TaskSource.Linear;
            var prompt = GeminiService.BuildTestCaseGenerationPrompt(tasks, selectedSource, _vm?.SelectedProject, _designDocumentContent);

            string response = string.Empty;
            var generateTask = System.Threading.Tasks.Task.Run(async () =>
            {
                using var service = new GeminiService(geminiKey);
                response = await service.AnalyzeIssueAsync(prompt);
            });

            TestCaseBusyOverlay.Visibility = Visibility.Visible;
            GenerateBtn.IsEnabled = false;

            try
            {
                await generateTask;
            }
            catch (GeminiAllModelsRateLimitedException)
            {
                GenerationStatusText.Text = "Rate limit exceeded. Please try again later.";
                return;
            }
            catch (AggregateException ae) when (ae.InnerException is GeminiAllModelsRateLimitedException)
            {
                GenerationStatusText.Text = "Rate limit exceeded. Please try again later.";
                return;
            }
            catch (Exception ex)
            {
                GenerationStatusText.Text = $"Generation failed: {ex.Message}";
                return;
            }
            finally
            {
                TestCaseBusyOverlay.Visibility = Visibility.Collapsed;
                GenerateBtn.IsEnabled = true;
            }

            try
            {
                var generatedCases = GeminiService.ParseTestCasesFromResponse(response, source);

                if (generatedCases.Count == 0)
                {
                    GenerationStatusText.Text = "No test cases could be parsed from the response.";
                    return;
                }

                var project = _vm!.SelectedProject!;

                // Create a new test plan for this generation batch
                var plan = new TestPlan
                {
                    TestPlanId = NextTestPlanId(),
                    Name = NormalizeForDisplay($"{selectedSource} \u00B7 {DateTime.Now:MMM d, yyyy h:mm tt}"),
                    Description = $"Auto-generated from {tasks.Count} {selectedSource} issue(s).",
                    Source = source
                };
                project.TestPlans.Add(plan);

                // Assign sequential IDs and link to plan
                foreach (var tc in generatedCases)
                {
                    tc.TestCaseId = NextTestCaseId();
                    tc.TestPlanId = plan.Id;
                    project.TestCases.Add(tc);
                }

                await _vm.SaveAsync();

                GenerationStatusText.Text = NormalizeForDisplay($"Generated {generatedCases.Count} test cases in {plan.TestPlanId} \u00B7 {DateTime.Now:h:mm tt}");
                RenderTestPlans();
            }
            catch (Exception ex)
            {
                GenerationStatusText.Text = $"Parse error: {ex.Message}";
            }

        }

        // Normalize any mojibake or non-breaking spaces that may appear due to
        // encoding mismatches (e.g. 'Â' or NBSP). Keep visible punctuation like
        // middle dot as a proper Unicode character.
        private static readonly Regex MultiSpaceRegex = new(" {2,}", RegexOptions.Compiled);

        private static string NormalizeForDisplay(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            // Replace non-breaking space with regular space
            var s = input.Replace('\u00A0', ' ');
            // Remove stray 'Â' characters that appear from mojibake (0xC2)
            s = s.Replace("Â", string.Empty);
            // Collapse multiple spaces
            s = MultiSpaceRegex.Replace(s, " ");
            return s.Trim();
        }

        private async System.Threading.Tasks.Task<List<ProjectTask>> FetchIssuesFromSourceAsync(string source)
        {
            var project = _vm?.SelectedProject;
            if (project == null) throw new Exception("No project selected.");

            if (source == "Jira")
            {
                if (project.JiraConnections.Count == 0)
                    throw new Exception("No Jira connections configured. Go to Settings.");

                bool anyToken = false;
                var allTasks = new List<ProjectTask>();
                foreach (var conn in project.JiraConnections)
                {
                    var token = CredentialService.LoadProjectCredential(project.Id, $"JiraApiToken_{conn.Id}");
                    if (string.IsNullOrEmpty(token)) continue;
                    anyToken = true;
                    var service = new JiraService(conn.Domain, conn.Email, token);
                    var tasks = await service.GetIssuesAsync(conn.ProjectKey);
                    allTasks.AddRange(tasks);
                }

                if (!anyToken)
                    throw new Exception("Jira API token not configured. Go to Settings.");

                return allTasks;
            }
            else
            {
                if (project.LinearConnections.Count == 0)
                    throw new Exception("No Linear connections configured. Go to Settings.");

                bool anyKey = false;
                var allTasks = new List<ProjectTask>();
                foreach (var conn in project.LinearConnections)
                {
                    var key = CredentialService.LoadProjectCredential(project.Id, $"LinearApiKey_{conn.Id}");
                    if (string.IsNullOrEmpty(key)) continue;
                    anyKey = true;
                    var service = new LinearService(key);
                    var tasks = await service.GetIssuesAsync(conn.TeamId);
                    allTasks.AddRange(tasks);
                }

                if (!anyKey)
                    throw new Exception("Linear API key not configured. Go to Settings.");

                return allTasks;
            }
        }

        // â”€â”€ Render: Test Plans (collapsible) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        // ── Import CSV / Excel ────────────────────────────────────────────────

        private async void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".xlsx");
            InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            ParsedCsvData parsed;
            try
            {
                parsed = CsvImportService.Parse(file.Path);
            }
            catch (Exception ex)
            {
                GenerationStatusText.Text = $"Failed to read file: {ex.Message}";
                return;
            }

            if (parsed.Headers.Count == 0 || parsed.Rows.Count == 0)
            {
                GenerationStatusText.Text = "No data found in the selected file.";
                return;
            }

            var dialog = new CsvImportDialog(parsed, file.Name)
            {
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var columnMap = dialog.ColumnMapping;
            var planName = string.IsNullOrWhiteSpace(dialog.PlanName)
                ? NormalizeForDisplay($"CSV Import \u00B7 {DateTime.Now:MMM d, yyyy h:mm tt}")
                : dialog.PlanName;

            var project = _vm.SelectedProject;
            var plan = new TestPlan
            {
                TestPlanId = NextTestPlanId(),
                Name = planName,
                Description = NormalizeForDisplay($"Imported from {file.Name} \u00B7 {parsed.Rows.Count} row(s)."),
                Source = TaskSource.Manual
            };
            project.TestPlans.Add(plan);

            int imported = 0;
            foreach (var row in parsed.Rows)
            {
                var tc = CsvImportService.MapToTestCase(row, columnMap);
                if (string.IsNullOrWhiteSpace(tc.Title)) continue;

                if (string.IsNullOrWhiteSpace(tc.TestCaseId))
                    tc.TestCaseId = NextTestCaseId();

                tc.TestPlanId = plan.Id;
                tc.Source = TaskSource.Manual;
                project.TestCases.Add(tc);
                imported++;
            }

            await _vm.SaveAsync();

            GenerationStatusText.Text = NormalizeForDisplay($"Imported {imported} test case(s) into {plan.TestPlanId} \u00B7 {DateTime.Now:h:mm tt}");
            RenderTestPlans();
        }

        private void RenderTestPlans()
        {
            TestCasesContainer.Children.Clear();

            var project = _vm?.SelectedProject;
            if (project == null) return;

            var plans = project.TestPlans;
            var allCases = project.TestCases;

            if (plans.Count == 0 && allCases.Count == 0)
            {
                TestCaseEmptyState.Visibility = Visibility.Visible;
                ClearAllBtn.Visibility = Visibility.Collapsed;
                TestCaseCountText.Text = string.Empty;
                TestCasesContainer.Children.Add(TestCaseEmptyState);
                return;
            }

            TestCaseEmptyState.Visibility = Visibility.Collapsed;
            ClearAllBtn.Visibility = Visibility.Visible;

            bool regressionOnly = _testCaseViewMode == "RegressionSuites";
            var scopedPlans = regressionOnly
                ? plans.Where(p => p.IsRegressionSuite).ToList()
                : plans.ToList();

            int archivedCount = scopedPlans.Count(p => p.IsArchived);
            int activeCount = scopedPlans.Count - archivedCount;
            var visiblePlans = _showArchived
                ? scopedPlans.OrderByDescending(p => p.CreatedAt).ToList()
                : scopedPlans.Where(p => !p.IsArchived).OrderByDescending(p => p.CreatedAt).ToList();

            int visibleCaseCount = regressionOnly
                ? allCases.Count(tc => visiblePlans.Any(p => p.Id == tc.TestPlanId))
                : allCases.Count;

            var archiveInfo = archivedCount > 0 ? $" \u00B7 {archivedCount} archived" : "";
            TestCaseCountText.Text = NormalizeForDisplay($"{activeCount} plan(s) \u00B7 {visibleCaseCount} case(s){archiveInfo}");

            if (visiblePlans.Count == 0)
            {
                TestCaseEmptyState.Visibility = Visibility.Visible;
                TestCasesContainer.Children.Add(TestCaseEmptyState);
                return;
            }

            foreach (var plan in visiblePlans)
            {
                var casesInPlan = allCases.Where(tc => tc.TestPlanId == plan.Id).ToList();
                var planCard = BuildTestPlanCard(plan, casesInPlan);
                TestCasesContainer.Children.Add(planCard);
            }
        }

        private Border BuildTestPlanCard(TestPlan plan, List<TestCase> cases)
        {
            bool collapsed = _collapsedPlans.Contains(plan.Id);

            var outerStack = new StackPanel { Spacing = 0 };

            // â”€â”€ Plan header (click to collapse/expand) â”€â”€
            var headerGrid = new Grid { Padding = new Thickness(0, 0, 0, 8) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Chevron
            var chevron = new FontIcon
            {
                Glyph = collapsed ? "\uE76C" : "\uE70D",
                FontSize = 12,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(chevron, 0);
            headerGrid.Children.Add(chevron);

            // Title area
            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(new TextBlock
            {
                Text = plan.TestPlanId,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = plan.Name,
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{cases.Count} case(s)",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175))
                }
            });

            // Archived badge
            if (plan.IsArchived)
            {
                titleRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 32, 16)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "ARCHIVED",
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                        CharacterSpacing = 100
                    }
                });
            }

            titleStack.Children.Add(titleRow);

            // Status summary
            var statusSummary = BuildStatusSummary(cases);
            titleStack.Children.Add(statusSummary);

            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            // Action buttons panel
            var capturedPlan = plan;
            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

            // Run All button
            var runAllBtn = BuildPlanActionButton("\uE768", "Run all test cases");
            runAllBtn.Click += async (s, _) => await RunAllTestCasesAsync(capturedPlan);
            actionPanel.Children.Add(runAllBtn);

            // Reset button
            var resetBtn = BuildPlanActionButton("\uE72C", "Reset all statuses to Not Run");
            resetBtn.Click += async (s, _) => await ResetTestPlanStatusesAsync(capturedPlan);
            actionPanel.Children.Add(resetBtn);

            // Duplicate button
            var duplicateBtn = BuildPlanActionButton("\uE8C8", "Duplicate plan for re-execution");
            duplicateBtn.Click += async (s, _) => await DuplicateTestPlanAsync(capturedPlan);
            actionPanel.Children.Add(duplicateBtn);

            // Archive/Unarchive button
            var archiveBtn = BuildPlanActionButton(plan.IsArchived ? "\uE7A7" : "\uE7B8",
                plan.IsArchived ? "Unarchive plan" : "Archive plan");
            archiveBtn.Click += async (s, _) => await ToggleArchiveTestPlanAsync(capturedPlan);
            actionPanel.Children.Add(archiveBtn);

            // Delete plan button
            var deletePlanBtn = BuildPlanActionButton("\uE74D", "Delete plan");
            deletePlanBtn.PointerEntered += (s, _) =>
            {
                if (deletePlanBtn.Content is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
            };
            deletePlanBtn.PointerExited += (s, _) =>
            {
                if (deletePlanBtn.Content is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
            };
            deletePlanBtn.Click += async (s, _) => await DeleteTestPlanAsync(capturedPlan);
            actionPanel.Children.Add(deletePlanBtn);

            Grid.SetColumn(actionPanel, 2);
            headerGrid.Children.Add(actionPanel);

            // Make header clickable for collapse/expand
            var headerButton = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = headerGrid
            };

            outerStack.Children.Add(headerButton);

            // â”€â”€ Collapsible body: test case cards â”€â”€
            var bodyStack = new StackPanel
            {
                Spacing = 10,
                Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible,
                Margin = new Thickness(22, 0, 0, 0)
            };

            foreach (var tc in cases)
            {
                var card = BuildTestCaseCard(tc, plan);
                bodyStack.Children.Add(card);
            }

            outerStack.Children.Add(bodyStack);

            // Wire collapse/expand
            headerButton.Click += (s, _) =>
            {
                if (_collapsedPlans.Contains(plan.Id))
                {
                    _collapsedPlans.Remove(plan.Id);
                    bodyStack.Visibility = Visibility.Visible;
                    chevron.Glyph = "\uE70D";
                }
                else
                {
                    _collapsedPlans.Add(plan.Id);
                    bodyStack.Visibility = Visibility.Collapsed;
                    chevron.Glyph = "\uE76C";
                }
            };

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Opacity = plan.IsArchived ? 0.6 : 1.0,
                Child = outerStack
            };
        }

        private static StackPanel BuildStatusSummary(List<TestCase> cases)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var groups = cases.GroupBy(c => c.Status)
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var group in groups)
            {
                var dot = new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = GetStatusBrush(group.Key),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var label = new TextBlock
                {
                    Text = $"{group.Count()} {group.Key}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                item.Children.Add(dot);
                item.Children.Add(label);
                panel.Children.Add(item);
            }

            return panel;
        }

        // â”€â”€ Render: single test case card â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private Border BuildTestCaseCard(TestCase tc, TestPlan plan)
        {
            var cardStack = new StackPanel { Spacing = 10 };

            // â”€â”€ Header row: ID + Title + Run + Status + Delete â”€â”€
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titlePanel.Children.Add(new TextBlock
            {
                Text = tc.TestCaseId,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = tc.Title,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(titlePanel, 0);
            headerGrid.Children.Add(titlePanel);

            // Execute button
            var capturedTc = tc;
            var capturedPlan = plan;
            var runBtn = new Button
            {
                Content = "Execute",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            runBtn.Click += async (s, _) => await ExecuteTestCaseAsync(capturedTc, capturedPlan);
            Grid.SetColumn(runBtn, 1);
            headerGrid.Children.Add(runBtn);

            // Priority badge
            var priorityBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                Background = GetPriorityBadgeBackground(tc.Priority),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = tc.Priority.ToString(),
                    FontSize = 11,
                    Foreground = GetPriorityForeground(tc.Priority),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            Grid.SetColumn(priorityBadge, 2);
            headerGrid.Children.Add(priorityBadge);

            // Status badge (read-only)
            var statusBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                Background = GetStatusBadgeBackground(tc.Status),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = tc.Status.ToString(),
                    FontSize = 11,
                    Foreground = GetStatusForeground(tc.Status),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            Grid.SetColumn(statusBadge, 3);
            headerGrid.Children.Add(statusBadge);

            // Delete button
            var deleteBtn = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                Content = new FontIcon
                {
                    Glyph = "\uE74D",
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
                }
            };
            deleteBtn.PointerEntered += (s, _) =>
            {
                if (deleteBtn.Content is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
            };
            deleteBtn.PointerExited += (s, _) =>
            {
                if (deleteBtn.Content is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
            };
            deleteBtn.Click += async (s, _) => await DeleteTestCaseAsync(capturedTc);
            Grid.SetColumn(deleteBtn, 4);
            headerGrid.Children.Add(deleteBtn);

            cardStack.Children.Add(headerGrid);

            // â”€â”€ Traceability label â”€â”€
            var traceText = $"{tc.TestCaseId} → {plan.TestPlanId}";
            var execCount = _vm?.SelectedProject?.TestExecutions.Count(te => te.TestCaseId == tc.Id) ?? 0;
            if (execCount > 0)
                traceText += $" · {execCount} execution(s)";

            cardStack.Children.Add(new TextBlock
            {
                Text = traceText,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
            });

            // â”€â”€ Separator â”€â”€
            cardStack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                Margin = new Thickness(0, 2, 0, 2)
            });

            // â”€â”€ Field rows â”€â”€
            AddFieldSection(cardStack, "PRE-CONDITIONS", tc.PreConditions);
            AddFieldSection(cardStack, "TEST STEPS", tc.TestSteps);
            AddFieldSection(cardStack, "TEST DATA", tc.TestData);
            AddFieldSection(cardStack, "EXPECTED RESULT", tc.ExpectedResult);

            // â”€â”€ Actual Result â”€â”€
            if (!string.IsNullOrWhiteSpace(tc.ActualResult))
                AddFieldSection(cardStack, "ACTUAL RESULT", tc.ActualResult);

            // â”€â”€ Footer: source + timestamp â”€â”€
            // ── Footer: source + timestamp | Bug Report button ──
            var footerGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var footerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            footerPanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                Child = new TextBlock
                {
                    Text = tc.Source.ToString(),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    FontSize = 10
                }
            });
            footerPanel.Children.Add(new TextBlock
            {
                Text = tc.GeneratedAt.ToString("MMM d, yyyy \u00B7 h:mm tt"),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(footerPanel, 0);
            footerGrid.Children.Add(footerPanel);

            var bugReportBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children =
                    {
                        new FontIcon { Glyph = "\uEBE8", FontSize = 11, FontFamily = new FontFamily("Segoe Fluent Icons") },
                        new TextBlock { Text = "Bug Report", FontSize = 11 }
                    }
                },
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 40, 28)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 60, 40)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(bugReportBtn, "Generate a structured bug report from this test case");
            bugReportBtn.Click += async (s, _) => await ShowTestCaseBugReportDialogAsync(capturedTc, capturedPlan);
            Grid.SetColumn(bugReportBtn, 1);
            footerGrid.Children.Add(bugReportBtn);
            cardStack.Children.Add(footerGrid);

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = cardStack
            };
        }

        // â”€â”€ Execute test case (dialog) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async System.Threading.Tasks.Task ExecuteTestCaseAsync(TestCase tc, TestPlan plan)
        {
            var statusPicker = new ComboBox
            {
                ItemsSource = Enum.GetValues(typeof(TestCaseStatus)),
                SelectedItem = TestCaseStatus.Passed,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var actualBox = new TextBox
            {
                PlaceholderText = "Enter the actual result...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 60,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var notesBox = new TextBox
            {
                PlaceholderText = "Notes (optional)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 40
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = $"{tc.TestCaseId} → {plan.TestPlanId}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
            });
            panel.Children.Add(new TextBlock
            {
                Text = tc.Title,
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock { Text = "Result", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(statusPicker);
            panel.Children.Add(new TextBlock { Text = "Actual Result", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(actualBox);
            panel.Children.Add(new TextBlock { Text = "Notes", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(notesBox);

            var dialog = new ContentDialog
            {
                Title = "Execute Test Case",
                Content = panel,
                PrimaryButtonText = "Record Execution",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            if (_vm?.SelectedProject == null) return;

            var selectedStatus = (TestCaseStatus)statusPicker.SelectedItem!;

            const int MaxFieldLength = 10_000;

            var actualText = actualBox.Text.Trim();
            if (actualText.Length > MaxFieldLength)
                actualText = actualText[..MaxFieldLength];

            var notesText = notesBox.Text.Trim();
            if (notesText.Length > MaxFieldLength)
                notesText = notesText[..MaxFieldLength];

            // Create execution record
            var execution = new TestExecution
            {
                ExecutionId = NextExecutionId(),
                TestCaseId = tc.Id,
                TestPlanId = plan.Id,
                Result = selectedStatus,
                ActualResult = actualText,
                Notes = notesText,
                ExecutedAt = DateTime.Now
            };
            _vm.SelectedProject.TestExecutions.Add(execution);

            // Update the test case to reflect latest execution
            tc.Status = selectedStatus;
            tc.ActualResult = actualText;

            await _vm.SaveAsync();
            RenderTestPlans();
        }

        // â”€â”€ Render: Test Runs (execution history) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RenderExecutionHistory()
        {
            ExecutionsContainer.Children.Clear();

            var project = _vm?.SelectedProject;
            if (project == null) return;

            var executions = project.TestExecutions;
            if (executions.Count == 0)
            {
                ExecutionEmptyState.Visibility = Visibility.Visible;
                ExecutionCountText.Text = string.Empty;
                ExecutionsContainer.Children.Add(ExecutionEmptyState);
                return;
            }

            ExecutionEmptyState.Visibility = Visibility.Collapsed;

            // Group executions by test plan as expandable dropdowns
            var grouped = executions
                .GroupBy(e => e.TestPlanId)
                .OrderByDescending(g => g.Max(e => e.ExecutedAt))
                .ToList();

            int archivedGroupCount = grouped.Count(g => g.All(e => e.IsArchived));
            int activeGroupCount = grouped.Count - archivedGroupCount;
            var archiveInfo = archivedGroupCount > 0 ? $" \u00B7 {archivedGroupCount} archived" : "";
            ExecutionCountText.Text = NormalizeForDisplay($"{activeGroupCount} run group(s) \u00B7 {executions.Count} execution(s){archiveInfo}");

            var visibleGroups = _showArchivedRuns
                ? grouped
                : grouped.Where(g => !g.All(e => e.IsArchived)).ToList();

            foreach (var group in visibleGroups)
            {
                var plan = project.TestPlans.FirstOrDefault(p => p.Id == group.Key);
                var planExecs = group.OrderByDescending(e => e.ExecutedAt).ToList();
                var planCard = BuildExecutionPlanGroup(plan, planExecs);
                ExecutionsContainer.Children.Add(planCard);
            }

            // â”€â”€ Criticality Assessment expandable section â”€â”€
            var criticalityCard = BuildCriticalityAssessmentCard();
            ExecutionsContainer.Children.Add(criticalityCard);
        }

        private Border BuildExecutionPlanGroup(TestPlan? plan, List<TestExecution> planExecs)
            {
                var planId = plan?.Id ?? planExecs.FirstOrDefault()?.TestPlanId ?? Guid.Empty;
                bool collapsed = _collapsedExecutionPlans.Contains(planId);
                bool isAllArchived = planExecs.All(e => e.IsArchived);
                var snapshotPlanDisplayId = planExecs.FirstOrDefault()?.SnapshotPlanDisplayId;
                var snapshotPlanName = planExecs.FirstOrDefault()?.SnapshotPlanName;

            var outerStack = new StackPanel { Spacing = 0 };

            // Header (click to collapse/expand)
            var headerGrid = new Grid { Padding = new Thickness(0, 0, 0, 8) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevron = new FontIcon
            {
                Glyph = collapsed ? "\uE76C" : "\uE70D",
                FontSize = 12,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 189, 248)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(chevron, 0);
            headerGrid.Children.Add(chevron);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(new TextBlock
            {
                Text = plan?.TestPlanId ?? snapshotPlanDisplayId ?? "Unknown Plan",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 189, 248)),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = plan?.Name ?? snapshotPlanName ?? "Orphaned Executions",
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{planExecs.Count} execution(s)",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175))
                }
            });
            if (isAllArchived)
            {
                titleRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 32, 16)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "ARCHIVED",
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                        CharacterSpacing = 100
                    }
                });
            }
            titleStack.Children.Add(titleRow);

            // Status summary for this plan's executions
            var execStatusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var execGroups = planExecs.GroupBy(e => e.Result).OrderBy(g => g.Key).ToList();
            foreach (var g in execGroups)
            {
                var dot = new Border
                {
                    Width = 8, Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = GetStatusBrush(g.Key),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var label = new TextBlock
                {
                    Text = $"{g.Count()} {g.Key}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                item.Children.Add(dot);
                item.Children.Add(label);
                execStatusPanel.Children.Add(item);
            }
            titleStack.Children.Add(execStatusPanel);

            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            var latestExec = planExecs.First();
            var runActionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            runActionPanel.Children.Add(new TextBlock
            {
                Text = latestExec.ExecutedAt.ToString("MMM d, yyyy"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var archiveRunBtn = BuildPlanActionButton(isAllArchived ? "\uE7A7" : "\uE7B8",
                isAllArchived ? "Unarchive run" : "Archive run");
            archiveRunBtn.Click += async (s, _) => await ToggleArchiveExecutionGroupAsync(planId);
            runActionPanel.Children.Add(archiveRunBtn);

            var deleteRunBtn = BuildPlanActionButton("\uE74D", "Delete all executions in this run");
            deleteRunBtn.PointerEntered += (s, _) =>
            {
                if (deleteRunBtn.Content is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
            };
            deleteRunBtn.PointerExited += (s, _) =>
            {
                if (deleteRunBtn.Content is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
            };
            deleteRunBtn.Click += async (s, _) => await DeleteExecutionGroupAsync(planId, plan?.TestPlanId ?? snapshotPlanDisplayId);
            runActionPanel.Children.Add(deleteRunBtn);

            Grid.SetColumn(runActionPanel, 2);
            headerGrid.Children.Add(runActionPanel);

            var headerButton = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = headerGrid
            };
            outerStack.Children.Add(headerButton);

            // Collapsible body: group executions by test case as expandable dropdowns
            var bodyStack = new StackPanel
            {
                Spacing = 8,
                Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible,
                Margin = new Thickness(22, 0, 0, 0)
            };

            var groupedByTestCase = planExecs
                .GroupBy(e => e.TestCaseId)
                .OrderByDescending(g => g.Max(e => e.ExecutedAt))
                .ToList();

            foreach (var tcGroup in groupedByTestCase)
            {
                var tcDropdown = BuildExecutionTestCaseDropdown(tcGroup.Key, tcGroup.OrderByDescending(e => e.ExecutedAt).ToList());
                bodyStack.Children.Add(tcDropdown);
            }
            outerStack.Children.Add(bodyStack);

            // Wire collapse/expand
            headerButton.Click += (s, _) =>
            {
                if (_collapsedExecutionPlans.Contains(planId))
                {
                    _collapsedExecutionPlans.Remove(planId);
                    bodyStack.Visibility = Visibility.Visible;
                    chevron.Glyph = "\uE70D";
                }
                else
                {
                    _collapsedExecutionPlans.Add(planId);
                    bodyStack.Visibility = Visibility.Collapsed;
                    chevron.Glyph = "\uE76C";
                }
            };

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
                Opacity = isAllArchived ? 0.6 : 1.0,
                Child = outerStack
            };
        }

        private Border BuildExecutionTestCaseDropdown(Guid testCaseId, List<TestExecution> executions)
        {
            var project = _vm!.SelectedProject!;
            var tc = project.TestCases.FirstOrDefault(c => c.Id == testCaseId);
            var snapshotTcDisplayId = executions.FirstOrDefault()?.SnapshotTestCaseDisplayId;
            var snapshotTcTitle = executions.FirstOrDefault()?.SnapshotTestCaseTitle;
            bool collapsed = _collapsedExecutionTestCases.Contains(testCaseId);

            var outerStack = new StackPanel { Spacing = 0 };

            // Header row
            var headerGrid = new Grid { Padding = new Thickness(0, 0, 0, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevron = new FontIcon
            {
                Glyph = collapsed ? "\uE76C" : "\uE70D",
                FontSize = 11,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(chevron, 0);
            headerGrid.Children.Add(chevron);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(new TextBlock
            {
                Text = tc?.TestCaseId ?? snapshotTcDisplayId ?? "?",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = tc?.Title ?? snapshotTcTitle ?? "Unknown Test Case",
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(titleRow);

            // Status summary for this test case's executions
            var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 0) };
            var latestResult = executions.First().Result;
            statusRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = GetStatusBadgeBackground(latestResult),
                Child = new TextBlock
                {
                    Text = latestResult.ToString(),
                    FontSize = 10,
                    Foreground = GetStatusForeground(latestResult),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            });
            statusRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                Child = new TextBlock
                {
                    Text = $"{executions.Count} run(s)",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175))
                }
            });
            titleStack.Children.Add(statusRow);

            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            var timestampText = new TextBlock
            {
                Text = executions.First().ExecutedAt.ToString("MMM d, yyyy"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(timestampText, 2);
            headerGrid.Children.Add(timestampText);

            var headerButton = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = headerGrid
            };
            outerStack.Children.Add(headerButton);

            // Collapsible body: individual execution cards
            var bodyStack = new StackPanel
            {
                Spacing = 6,
                Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible,
                Margin = new Thickness(20, 4, 0, 0)
            };

            foreach (var exec in executions)
            {
                var card = BuildExecutionCard(exec);
                bodyStack.Children.Add(card);
            }
            outerStack.Children.Add(bodyStack);

            // Wire collapse/expand
            headerButton.Click += (s, _) =>
            {
                if (_collapsedExecutionTestCases.Contains(testCaseId))
                {
                    _collapsedExecutionTestCases.Remove(testCaseId);
                    bodyStack.Visibility = Visibility.Visible;
                    chevron.Glyph = "\uE70D";
                }
                else
                {
                    _collapsedExecutionTestCases.Add(testCaseId);
                    bodyStack.Visibility = Visibility.Collapsed;
                    chevron.Glyph = "\uE76C";
                }
            };

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 22, 32)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 38, 54)),
                BorderThickness = new Thickness(1),
                Child = outerStack
            };
        }

        private Border BuildCriticalityAssessmentCard()
        {
            var outerStack = new StackPanel { Spacing = 0 };

            // â”€â”€ Expand/Collapse button â”€â”€
            var expandBtnContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            expandBtnContent.Children.Add(new FontIcon
            {
                Glyph = _criticalityExpanded ? "\uE70D" : "\uE76C",
                FontSize = 12,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                VerticalAlignment = VerticalAlignment.Center
            });
            expandBtnContent.Children.Add(new TextBlock
            {
                Text = "AI Suggestions",
                FontSize = 13,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var expandBtn = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = expandBtnContent
            };

            outerStack.Children.Add(expandBtn);

            // â”€â”€ Collapsible body â”€â”€
            var bodyStack = new StackPanel
            {
                Spacing = 12,
                Visibility = _criticalityExpanded ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 12, 0, 0)
            };

            // Priority failure summary (always shown when expanded)
            var project = _vm!.SelectedProject!;
            var failedCases = project.TestCases.Where(tc => tc.Status == TestCaseStatus.Failed).ToList();

            var summaryTitle = new TextBlock
            {
                Text = "AI SUGGESTIONS",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 150
            };
            bodyStack.Children.Add(summaryTitle);

            // Priority breakdown bars
            var priorityBreakdown = new StackPanel { Spacing = 8 };
            var blockerFailed = failedCases.Count(tc => tc.Priority == TestCasePriority.Blocker);
            var majorFailed = failedCases.Count(tc => tc.Priority == TestCasePriority.Major);
            var mediumFailed = failedCases.Count(tc => tc.Priority == TestCasePriority.Medium);
            var lowFailed = failedCases.Count(tc => tc.Priority == TestCasePriority.Low);
            int totalFailed = failedCases.Count;

            AddPriorityBar(priorityBreakdown, "Blocker", blockerFailed, totalFailed, Windows.UI.Color.FromArgb(255, 220, 38, 38));
            AddPriorityBar(priorityBreakdown, "Major", majorFailed, totalFailed, Windows.UI.Color.FromArgb(255, 249, 115, 22));
            AddPriorityBar(priorityBreakdown, "Medium", mediumFailed, totalFailed, Windows.UI.Color.FromArgb(255, 251, 191, 36));
            AddPriorityBar(priorityBreakdown, "Low", lowFailed, totalFailed, Windows.UI.Color.FromArgb(255, 107, 114, 128));
            bodyStack.Children.Add(priorityBreakdown);

            // Separator
            bodyStack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                Margin = new Thickness(0, 4, 0, 4)
            });

            // AI-generated assessment area
            if (!string.IsNullOrWhiteSpace(_criticalityAssessmentText))
            {
                bool isGenerating = _criticalityAssessmentText == "Generating suggestions...";
                bool hasNewData = !isGenerating
                    && _suggestionsSnapshotExecutionCount >= 0
                    && (project.TestExecutions.Count != _suggestionsSnapshotExecutionCount
                        || project.TestCases.Count(tc => tc.Status == TestCaseStatus.Failed) != _suggestionsSnapshotFailedCount);

                if (hasNewData)
                {
                    var banner = new Border
                    {
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 251, 191, 36)),
                        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 251, 191, 36)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 8, 10, 8)
                    };
                    var bannerInner = new Grid();
                    bannerInner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    bannerInner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var bannerLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                    bannerLeft.Children.Add(new FontIcon
                    {
                        Glyph = "\uE7BA",
                        FontSize = 12,
                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    bannerLeft.Children.Add(new TextBlock
                    {
                        Text = "New executions added — suggestions may be outdated",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    Grid.SetColumn(bannerLeft, 0);
                    bannerInner.Children.Add(bannerLeft);

                    var regenBtn = new Button
                    {
                        Content = "Regenerate",
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12, 5, 12, 5),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    regenBtn.Click += async (s, _) => await GenerateCriticalityAssessmentAsync();
                    Grid.SetColumn(regenBtn, 1);
                    bannerInner.Children.Add(regenBtn);

                    banner.Child = bannerInner;
                    bodyStack.Children.Add(banner);
                }

                bodyStack.Children.Add(BuildFormattedSuggestionsPanel(_criticalityAssessmentText));
            }
            else
            {
                var generateBtn = new Button
                {
                    Content = "Get AI Suggestions",
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 8, 16, 8),
                    FontSize = 12
                };
                generateBtn.Click += async (s, _) => await GenerateCriticalityAssessmentAsync();
                bodyStack.Children.Add(generateBtn);
            }

            outerStack.Children.Add(bodyStack);

            // Wire expand/collapse
            expandBtn.Click += (s, _) =>
            {
                _criticalityExpanded = !_criticalityExpanded;
                bodyStack.Visibility = _criticalityExpanded ? Visibility.Visible : Visibility.Collapsed;
                if (expandBtnContent.Children[0] is FontIcon chevron)
                    chevron.Glyph = _criticalityExpanded ? "\uE70D" : "\uE76C";
            };

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
                Child = outerStack
            };
        }

        private async System.Threading.Tasks.Task GenerateCriticalityAssessmentAsync()
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;

            var geminiKey = LoadProjectCred("GeminiApiKey");
            if (string.IsNullOrEmpty(geminiKey))
            {
                var dialog = new ContentDialog
                {
                    Title = "API Key Missing",
                    Content = "Please add your Google AI Studio API key in Settings.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                DialogHelper.ApplyDarkTheme(dialog);
                await dialog.ShowAsync();
                return;
            }

            var prompt = GeminiService.BuildTestRunSuggestionsPrompt(
                project.TestCases,
                project.TestExecutions,
                project.TestPlans,
                project);

            try
            {
                _criticalityAssessmentText = "Generating suggestions...";
                RenderExecutionHistory();

                using var service = new GeminiService(geminiKey);
                var response = await service.AnalyzeIssueAsync(prompt);

                // Cap stored response length to prevent unbounded memory use
                const int MaxAssessmentLength = 20_000;
                _criticalityAssessmentText = response.Length > MaxAssessmentLength
                    ? response[..MaxAssessmentLength] + "\n\n(truncated)"
                    : response;
                _suggestionsSnapshotExecutionCount = project.TestExecutions.Count;
                _suggestionsSnapshotFailedCount = project.TestCases.Count(tc => tc.Status == TestCaseStatus.Failed);
                RenderExecutionHistory();
            }
            catch (GeminiAllModelsRateLimitedException)
            {
                _criticalityAssessmentText = "Rate limit exceeded. Please try again later.";
                RenderExecutionHistory();
            }
            catch (Exception)
            {
                _criticalityAssessmentText = "Failed to generate assessment. Please check your API key and try again.";
                RenderExecutionHistory();
            }
        }

        private static void AddPriorityBar(StackPanel parent, string label, int count, int total, Windows.UI.Color color)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(color),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelText, 0);
            row.Children.Add(labelText);

            double pct = total > 0 ? (double)count / total * 100 : 0;

            var barBg = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var barGrid = new Grid();
            barGrid.Children.Add(barBg);

            if (pct > 0)
            {
                var barFill = new Border
                {
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0
                };
                barGrid.Children.Add(barFill);
                barGrid.SizeChanged += (s, e) =>
                {
                    barFill.Width = e.NewSize.Width * pct / 100;
                };
            }

            Grid.SetColumn(barGrid, 1);
            barGrid.Margin = new Thickness(8, 0, 8, 0);
            row.Children.Add(barGrid);

            var valueText = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                HorizontalTextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueText, 2);
            row.Children.Add(valueText);

            parent.Children.Add(row);
        }

        private Border BuildExecutionCard(TestExecution exec)
        {
            var project = _vm!.SelectedProject!;
            var tc = project.TestCases.FirstOrDefault(c => c.Id == exec.TestCaseId);
            var plan = project.TestPlans.FirstOrDefault(p => p.Id == exec.TestPlanId);

            var cardStack = new StackPanel { Spacing = 6 };

            // â”€â”€ Traceability header: TE → TC → TP â”€â”€
            var traceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            traceRow.Children.Add(MakeTraceBadge(exec.ExecutionId, GetStatusBrush(exec.Result)));
            traceRow.Children.Add(new TextBlock
            {
                Text = "\u2192",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            traceRow.Children.Add(MakeTraceBadge(
                tc?.TestCaseId ?? exec.SnapshotTestCaseDisplayId ?? "?",
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))));
            traceRow.Children.Add(new TextBlock
            {
                Text = "\u2192",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            traceRow.Children.Add(MakeTraceBadge(
                plan?.TestPlanId ?? exec.SnapshotPlanDisplayId ?? "?",
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 189, 248))));

            var capturedExec = exec;
            var traceHeaderGrid = new Grid();
            traceHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            traceHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(traceRow, 0);
            traceHeaderGrid.Children.Add(traceRow);

            var deleteExecBtn = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new FontIcon
                {
                    Glyph = "\uE74D",
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
                }
            };
            ToolTipService.SetToolTip(deleteExecBtn, "Delete this execution record");
            deleteExecBtn.PointerEntered += (s, _) =>
            {
                if (deleteExecBtn.Content is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
            };
            deleteExecBtn.PointerExited += (s, _) =>
            {
                if (deleteExecBtn.Content is FontIcon icon)
                    icon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
            };
            deleteExecBtn.Click += async (s, _) => await DeleteSingleExecutionAsync(capturedExec);
            Grid.SetColumn(deleteExecBtn, 1);
            traceHeaderGrid.Children.Add(deleteExecBtn);

            cardStack.Children.Add(traceHeaderGrid);

            // Test case title
            var tcDisplayTitle = tc?.Title ?? exec.SnapshotTestCaseTitle;
            if (!string.IsNullOrEmpty(tcDisplayTitle))
            {
                cardStack.Children.Add(new TextBlock
                {
                    Text = tcDisplayTitle,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            // Result badge
            var resultRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            resultRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Background = GetStatusBadgeBackground(exec.Result),
                Child = new TextBlock
                {
                    Text = exec.Result.ToString(),
                    FontSize = 11,
                    Foreground = GetStatusForeground(exec.Result),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            });
            if (tc != null)
            {
                resultRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3, 8, 3),
                    Background = GetPriorityBadgeBackground(tc.Priority),
                    Child = new TextBlock
                    {
                        Text = tc.Priority.ToString(),
                        FontSize = 11,
                        Foreground = GetPriorityForeground(tc.Priority),
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    }
                });
            }
            resultRow.Children.Add(new TextBlock
            {
                Text = exec.ExecutedAt.ToString("MMM d, yyyy \u00B7 h:mm:ss tt"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                VerticalAlignment = VerticalAlignment.Center
            });
            cardStack.Children.Add(resultRow);

            // Actual result
            if (!string.IsNullOrWhiteSpace(exec.ActualResult))
            {
                AddFieldSection(cardStack, "ACTUAL RESULT", exec.ActualResult);
            }

            // Notes
            if (!string.IsNullOrWhiteSpace(exec.Notes))
            {
                AddFieldSection(cardStack, "NOTES", exec.Notes);
            }

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = cardStack
            };
        }

        private static Border MakeTraceBadge(string text, SolidColorBrush foreground)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                Child = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = foreground,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
        }

        // â”€â”€ Plan action helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static Button BuildPlanActionButton(string glyph, string tooltip)
        {
            var btn = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
                }
            };
            ToolTipService.SetToolTip(btn, tooltip);
            return btn;
        }

        private async System.Threading.Tasks.Task ToggleArchiveTestPlanAsync(TestPlan plan)
        {
            if (_vm?.SelectedProject == null) return;

            plan.IsArchived = !plan.IsArchived;
            await _vm.SaveAsync();
            GenerationStatusText.Text = plan.IsArchived
                ? $"Archived {plan.TestPlanId}."
                : $"Unarchived {plan.TestPlanId}.";
            RenderTestPlans();
        }

        private async System.Threading.Tasks.Task DuplicateTestPlanAsync(TestPlan sourcePlan)
        {
            if (_vm?.SelectedProject == null) return;

            var project = _vm.SelectedProject;
            var sourceCases = project.TestCases.Where(tc => tc.TestPlanId == sourcePlan.Id).ToList();

            var newPlan = new TestPlan
            {
                TestPlanId = NextTestPlanId(),
                Name = $"{sourcePlan.Name} (copy)",
                Description = $"Duplicated from {sourcePlan.TestPlanId} for re-execution.",
                Source = sourcePlan.Source
            };
            project.TestPlans.Add(newPlan);

            foreach (var tc in sourceCases)
            {
                var copy = new TestCase
                {
                    TestCaseId = NextTestCaseId(),
                    Title = tc.Title,
                    PreConditions = tc.PreConditions,
                    TestSteps = tc.TestSteps,
                    TestData = tc.TestData,
                    ExpectedResult = tc.ExpectedResult,
                    ActualResult = string.Empty,
                    Status = TestCaseStatus.NotRun,
                    Priority = tc.Priority,
                    GeneratedAt = DateTime.Now,
                    SourceIssueId = tc.SourceIssueId,
                    Source = tc.Source,
                    TestPlanId = newPlan.Id
                };
                project.TestCases.Add(copy);
            }

            await _vm.SaveAsync();
            GenerationStatusText.Text = $"Duplicated {sourcePlan.TestPlanId} → {newPlan.TestPlanId} ({sourceCases.Count} case(s))";
            RenderTestPlans();
        }

        private async System.Threading.Tasks.Task ResetTestPlanStatusesAsync(TestPlan plan)
        {
            if (_vm?.SelectedProject == null) return;

            var dialog = new ContentDialog
            {
                Title = "Reset Statuses",
                Content = $"Reset all test case statuses in {plan.TestPlanId} to Not Run?\nThis will clear actual results. Execution history is preserved.",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var cases = _vm.SelectedProject.TestCases.Where(tc => tc.TestPlanId == plan.Id).ToList();
            foreach (var tc in cases)
            {
                tc.Status = TestCaseStatus.NotRun;
                tc.ActualResult = string.Empty;
            }

            await _vm.SaveAsync();
            GenerationStatusText.Text = $"Reset {cases.Count} case(s) in {plan.TestPlanId}.";
            RenderTestPlans();
        }

        private async System.Threading.Tasks.Task RunAllTestCasesAsync(TestPlan plan)
        {
            if (_vm?.SelectedProject == null) return;

            var cases = _vm.SelectedProject.TestCases.Where(tc => tc.TestPlanId == plan.Id).ToList();
            if (cases.Count == 0) return;

            var statusPicker = new ComboBox
            {
                ItemsSource = Enum.GetValues(typeof(TestCaseStatus)),
                SelectedItem = TestCaseStatus.Passed,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var notesBox = new TextBox
            {
                PlaceholderText = "Batch notes (optional)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 40
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = $"{plan.TestPlanId} · {cases.Count} test case(s)",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
            });
            panel.Children.Add(new TextBlock
            {
                Text = "This will record an execution for every test case in this plan with the selected result.",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock { Text = "Result for all", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(statusPicker);
            panel.Children.Add(new TextBlock { Text = "Notes", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(notesBox);

            var dialog = new ContentDialog
            {
                Title = "Run All Test Cases",
                Content = panel,
                PrimaryButtonText = "Record All",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var selectedStatus = (TestCaseStatus)statusPicker.SelectedItem!;
            const int MaxFieldLength = 10_000;
            var notesText = notesBox.Text.Trim();
            if (notesText.Length > MaxFieldLength)
                notesText = notesText[..MaxFieldLength];

            foreach (var tc in cases)
            {
                var execution = new TestExecution
                {
                    ExecutionId = NextExecutionId(),
                    TestCaseId = tc.Id,
                    TestPlanId = plan.Id,
                    Result = selectedStatus,
                    ActualResult = string.Empty,
                    Notes = notesText,
                    ExecutedAt = DateTime.Now
                };
                _vm.SelectedProject.TestExecutions.Add(execution);

                tc.Status = selectedStatus;
            }

            await _vm.SaveAsync();
            GenerationStatusText.Text = $"Recorded {cases.Count} execution(s) in {plan.TestPlanId}.";
            RenderTestPlans();
        }

        // â”€â”€ Delete helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async System.Threading.Tasks.Task DeleteTestPlanAsync(TestPlan plan)
        {
            if (_vm?.SelectedProject == null) return;

            var caseCount = _vm.SelectedProject.TestCases.Count(tc => tc.TestPlanId == plan.Id);
            var dialog = new ContentDialog
            {
                Title = "Delete Test Plan",
                Content = $"Delete plan {plan.TestPlanId} ({plan.Name}) and its {caseCount} test case(s)?\nAssociated execution history will be preserved.\nThis cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            foreach (var exec in _vm.SelectedProject.TestExecutions.Where(e => e.TestPlanId == plan.Id))
            {
                exec.SnapshotPlanDisplayId ??= plan.TestPlanId;
                exec.SnapshotPlanName ??= plan.Name;
            }
            foreach (var deletedCase in _vm.SelectedProject.TestCases.Where(c => c.TestPlanId == plan.Id))
            {
                foreach (var exec in _vm.SelectedProject.TestExecutions.Where(e => e.TestCaseId == deletedCase.Id))
                {
                    exec.SnapshotTestCaseDisplayId ??= deletedCase.TestCaseId;
                    exec.SnapshotTestCaseTitle ??= deletedCase.Title;
                }
            }

            _vm.SelectedProject.TestCases.RemoveAll(tc => tc.TestPlanId == plan.Id);
            _vm.SelectedProject.TestPlans.Remove(plan);
            await _vm.SaveAsync();
            RenderTestPlans();
        }

        private async System.Threading.Tasks.Task DeleteTestCaseAsync(TestCase tc)
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Test Case",
                Content = $"Delete test case {tc.TestCaseId}: {tc.Title}?\nExecution history will be preserved.\nThis cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            if (_vm?.SelectedProject != null)
            {
                foreach (var exec in _vm.SelectedProject.TestExecutions.Where(e => e.TestCaseId == tc.Id))
                {
                    exec.SnapshotTestCaseDisplayId ??= tc.TestCaseId;
                    exec.SnapshotTestCaseTitle ??= tc.Title;
                }
            }

            _vm?.SelectedProject?.TestCases.Remove(tc);
            if (_vm != null) await _vm.SaveAsync();
            RenderTestPlans();
        }

        private async System.Threading.Tasks.Task DeleteSingleExecutionAsync(TestExecution exec)
        {
            if (_vm?.SelectedProject == null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Execution",
                Content = $"Delete execution {exec.ExecutionId}?\nThis cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _vm.SelectedProject.TestExecutions.Remove(exec);
            await _vm.SaveAsync();
            RenderExecutionHistory();
        }

        private async System.Threading.Tasks.Task DeleteExecutionGroupAsync(Guid planId, string? planDisplayId)
        {
            if (_vm?.SelectedProject == null) return;

            var execs = _vm.SelectedProject.TestExecutions.Where(e => e.TestPlanId == planId).ToList();
            var dialog = new ContentDialog
            {
                Title = "Delete Test Run",
                Content = $"Delete all {execs.Count} execution(s) for {planDisplayId ?? "this plan"}?\nThis cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _vm.SelectedProject.TestExecutions.RemoveAll(e => e.TestPlanId == planId);
            await _vm.SaveAsync();
            RenderExecutionHistory();
        }

        private async System.Threading.Tasks.Task ToggleArchiveExecutionGroupAsync(Guid planId)
        {
            if (_vm?.SelectedProject == null) return;

            var execs = _vm.SelectedProject.TestExecutions.Where(e => e.TestPlanId == planId).ToList();
            if (execs.Count == 0) return;

            bool allArchived = execs.All(e => e.IsArchived);
            foreach (var e in execs)
                e.IsArchived = !allArchived;

            await _vm.SaveAsync();
            RenderExecutionHistory();
        }

        private async void ClearAllTestCases_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            var planCount = _vm.SelectedProject.TestPlans.Count;
            var caseCount = _vm.SelectedProject.TestCases.Count;

            var dialog = new ContentDialog
            {
                Title = "Clear All Test Plans",
                Content = $"Delete all {planCount} plan(s) and {caseCount} test case(s)?\nExecution history will be preserved.\nThis cannot be undone.",
                PrimaryButtonText = "Clear All",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            foreach (var plan in _vm.SelectedProject.TestPlans)
            {
                foreach (var exec in _vm.SelectedProject.TestExecutions.Where(e => e.TestPlanId == plan.Id))
                {
                    exec.SnapshotPlanDisplayId ??= plan.TestPlanId;
                    exec.SnapshotPlanName ??= plan.Name;
                }
            }
            foreach (var deletedCase in _vm.SelectedProject.TestCases)
            {
                foreach (var exec in _vm.SelectedProject.TestExecutions.Where(e => e.TestCaseId == deletedCase.Id))
                {
                    exec.SnapshotTestCaseDisplayId ??= deletedCase.TestCaseId;
                    exec.SnapshotTestCaseTitle ??= deletedCase.Title;
                }
            }

            _vm.SelectedProject.TestCases.Clear();
            _vm.SelectedProject.TestPlans.Clear();
            await _vm.SaveAsync();
            GenerationStatusText.Text = "All test plans cleared.";
            RenderTestPlans();
        }

        // â”€â”€ Reports: Dashboard rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void ReportTypePicker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_activeSubTab == "Reports")
                RenderReportsDashboard();
        }

        private void RenderReportsDashboard()
        {
            ReportsContainer.Children.Clear();

            var project = _vm?.SelectedProject;
            if (project == null) return;

            var allPlans = project.TestPlans;

            if (allPlans.Count == 0 && project.TestCases.Count == 0 && project.TestExecutions.Count == 0)
            {
                ReportsEmptyState.Visibility = Visibility.Visible;
                ReportsContainer.Children.Add(ReportsEmptyState);
                return;
            }

            ReportsEmptyState.Visibility = Visibility.Collapsed;

            // Initialize selection to all plans on first visit
            if (_selectedPlanIds.Count == 0 && allPlans.Count > 0)
            {
                foreach (var p in allPlans)
                    _selectedPlanIds.Add(p.Id);
            }

            RebuildPlanFilterFlyout();

            // Resolve filtered data
            var (filteredPlans, filteredCases, filteredExecs) = GetFilteredData();

            var reportTag = (ReportTypePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Summary";

            if (reportTag == "Summary")
                RenderSummaryDashboard(project, filteredPlans, filteredCases, filteredExecs);
            else if (reportTag == "TestCasesCsv")
                RenderTestCasesCsvPreview(project, filteredPlans, filteredCases);
            else if (reportTag == "ExecutionsCsv")
                RenderExecutionsCsvPreview(project, filteredExecs, filteredCases, filteredPlans);
        }

        // â”€â”€ Reports: Plan filter â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private (List<TestPlan> plans, List<TestCase> cases, List<TestExecution> execs) GetFilteredData()
        {
            var project = _vm!.SelectedProject!;

            var plans = project.TestPlans
                .Where(p => _selectedPlanIds.Contains(p.Id))
                .ToList();

            var planIds = new HashSet<Guid>(plans.Select(p => p.Id));

            var cases = project.TestCases
                .Where(tc => tc.TestPlanId.HasValue && planIds.Contains(tc.TestPlanId.Value))
                .ToList();

            var caseIds = new HashSet<Guid>(cases.Select(c => c.Id));

            var execs = project.TestExecutions
                .Where(te => (te.TestPlanId.HasValue && planIds.Contains(te.TestPlanId.Value)) || caseIds.Contains(te.TestCaseId))
                .ToList();

            return (plans, cases, execs);
        }

        private void RebuildPlanFilterFlyout()
        {
            PlanFilterContainer.Children.Clear();
            var project = _vm?.SelectedProject;
            if (project == null) return;

            foreach (var plan in project.TestPlans.OrderByDescending(p => p.CreatedAt))
            {
                bool isChecked = _selectedPlanIds.Contains(plan.Id);
                var caseCount = project.TestCases.Count(tc => tc.TestPlanId == plan.Id);

                var cb = new CheckBox
                {
                    IsChecked = isChecked,
                    Tag = plan.Id,
                    Margin = new Thickness(0, 2, 0, 2),
                    MinWidth = 240
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new TextBlock
                {
                    Text = plan.TestPlanId,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = plan.Name.Length > 28 ? plan.Name[..25] + "..." : plan.Name,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"({caseCount})",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    VerticalAlignment = VerticalAlignment.Center
                });

                cb.Content = row;

                var capturedId = plan.Id;
                cb.Checked += (s, _) =>
                {
                    _selectedPlanIds.Add(capturedId);
                    UpdatePlanFilterLabel();
                    RenderReportsDashboard();
                };
                cb.Unchecked += (s, _) =>
                {
                    _selectedPlanIds.Remove(capturedId);
                    UpdatePlanFilterLabel();
                    RenderReportsDashboard();
                };

                PlanFilterContainer.Children.Add(cb);
            }

            UpdatePlanFilterLabel();
        }

        private void UpdatePlanFilterLabel()
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;

            int total = project.TestPlans.Count;
            int selected = _selectedPlanIds.Count;

            if (selected == 0)
                PlanFilterBtn.Content = "No Plans Selected";
            else if (selected == total)
                PlanFilterBtn.Content = "All Plans";
            else if (selected == 1)
            {
                var plan = project.TestPlans.FirstOrDefault(p => _selectedPlanIds.Contains(p.Id));
                PlanFilterBtn.Content = plan != null ? $"{plan.TestPlanId} - {(plan.Name.Length > 18 ? plan.Name[..15] + "..." : plan.Name)}" : "1 Plan";
            }
            else
                PlanFilterBtn.Content = $"{selected} of {total} Plans";
        }

        private void SelectAllPlans_Click(object sender, RoutedEventArgs e)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;

            _selectedPlanIds.Clear();
            foreach (var p in project.TestPlans)
                _selectedPlanIds.Add(p.Id);

            RenderReportsDashboard();
        }

        private void ClearPlanSelection_Click(object sender, RoutedEventArgs e)
        {
            _selectedPlanIds.Clear();
            RenderReportsDashboard();
        }

        // â”€â”€ Reports: Dashboard cards â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RenderSummaryDashboard(Project project, List<TestPlan> plans, List<TestCase> cases, List<TestExecution> execs)
        {
            int totalPlans = plans.Count;
            int totalCases = cases.Count;
            int totalExecs = execs.Count;
            int passed = cases.Count(c => c.Status == TestCaseStatus.Passed);
            int failed = cases.Count(c => c.Status == TestCaseStatus.Failed);
            int blocked = cases.Count(c => c.Status == TestCaseStatus.Blocked);
            int skipped = cases.Count(c => c.Status == TestCaseStatus.Skipped);
            int notRun = cases.Count(c => c.Status == TestCaseStatus.NotRun);
            double passRate = totalCases > 0 ? (double)passed / totalCases * 100 : 0;

            // â”€â”€ Metric cards row â”€â”€
            var metricsGrid = new Grid();
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnSpacing = 12;

            AddMetricCard(metricsGrid, 0, "Test Plans", totalPlans.ToString(), "\uE9D5", "#38BDF8");
            AddMetricCard(metricsGrid, 1, "Test Cases", totalCases.ToString(), "\uE8A4", "#A78BFA");
            AddMetricCard(metricsGrid, 2, "Executions", totalExecs.ToString(), "\uE768", "#FBBF24");
            AddMetricCard(metricsGrid, 3, "Pass Rate", $"{passRate:F1}%", "\uE73E", passRate >= 70 ? "#34D399" : passRate >= 40 ? "#FBBF24" : "#F87171");

            ReportsContainer.Children.Add(metricsGrid);

            // â”€â”€ Status Breakdown â”€â”€
            var breakdownCard = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1)
            };

            var breakdownStack = new StackPanel { Spacing = 12 };
            breakdownStack.Children.Add(new TextBlock
            {
                Text = "STATUS BREAKDOWN",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 150
            });

            AddStatusBar(breakdownStack, "Passed", passed, totalCases, Windows.UI.Color.FromArgb(255, 52, 211, 153));
            AddStatusBar(breakdownStack, "Failed", failed, totalCases, Windows.UI.Color.FromArgb(255, 248, 113, 113));
            AddStatusBar(breakdownStack, "Blocked", blocked, totalCases, Windows.UI.Color.FromArgb(255, 245, 158, 11));
            AddStatusBar(breakdownStack, "Skipped", skipped, totalCases, Windows.UI.Color.FromArgb(255, 107, 114, 128));
            AddStatusBar(breakdownStack, "Not Run", notRun, totalCases, Windows.UI.Color.FromArgb(255, 156, 163, 175));

            breakdownCard.Child = breakdownStack;
            ReportsContainer.Children.Add(breakdownCard);

            // â”€â”€ Plan-level summary table â”€â”€
            if (plans.Count > 0)
            {
                var planCard = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(20),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                    BorderThickness = new Thickness(1)
                };

                var planStack = new StackPanel { Spacing = 8 };
                planStack.Children.Add(new TextBlock
                {
                    Text = "TEST PLANS OVERVIEW",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    CharacterSpacing = 150,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                // Table header
                var headerRow = BuildPlanTableRow("PLAN ID", "NAME", "CASES", "PASS RATE", isHeader: true);
                planStack.Children.Add(headerRow);

                foreach (var plan in plans.OrderByDescending(p => p.CreatedAt))
                {
                    var planCases = cases.Where(tc => tc.TestPlanId == plan.Id).ToList();
                    int planPassed = planCases.Count(c => c.Status == TestCaseStatus.Passed);
                    double planRate = planCases.Count > 0 ? (double)planPassed / planCases.Count * 100 : 0;

                    var row = BuildPlanTableRow(
                        plan.TestPlanId,
                        plan.Name.Length > 35 ? plan.Name[..32] + "..." : plan.Name,
                        planCases.Count.ToString(),
                        $"{planRate:F1}%",
                        isHeader: false);
                    planStack.Children.Add(row);
                }

                planCard.Child = planStack;
                ReportsContainer.Children.Add(planCard);
            }

            ReportStatusText.Text = $"Summary · {totalPlans} plan(s) · {totalCases} case(s)";
        }

        private void RenderTestCasesCsvPreview(Project project, List<TestPlan> filteredPlans, List<TestCase> filteredCases)
        {
            var previewCard = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = "TEST CASES CSV EXPORT",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 150
            });

            string scopeLabel = filteredPlans.Count == project.TestPlans.Count
                ? "all"
                : $"{filteredPlans.Count} selected";

            stack.Children.Add(new TextBlock
            {
                Text = $"This export will include {filteredCases.Count} test case(s) across {scopeLabel} test plan(s).",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Columns: Test Plan ID, Test Plan Name, Test Case ID, Title, Status, Pre-Conditions, Test Steps, Test Data, Expected Result, Actual Result, Source, Generated At",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });

            // Show selected plan badges
            if (filteredPlans.Count < project.TestPlans.Count)
            {
                var badgeWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
                foreach (var plan in filteredPlans.OrderBy(p => p.TestPlanId))
                {
                    badgeWrap.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 3, 8, 3),
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                        Child = new TextBlock
                        {
                            Text = plan.TestPlanId,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
                        }
                    });
                }
                stack.Children.Add(badgeWrap);
            }

            previewCard.Child = stack;
            ReportsContainer.Children.Add(previewCard);

            ReportStatusText.Text = $"CSV · {filteredCases.Count} case(s) ready to export";
        }

        private void RenderExecutionsCsvPreview(Project project, List<TestExecution> filteredExecs, List<TestCase> filteredCases, List<TestPlan> filteredPlans)
        {
            var previewCard = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = "EXECUTION HISTORY CSV EXPORT",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 150
            });

            string scopeLabel = filteredPlans.Count == project.TestPlans.Count
                ? "all plans"
                : $"{filteredPlans.Count} selected plan(s)";

            stack.Children.Add(new TextBlock
            {
                Text = $"This export will include {filteredExecs.Count} execution record(s) from {scopeLabel}.",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Columns: Execution ID, Test Case ID, Test Case Title, Test Plan ID, Result, Actual Result, Notes, Executed At",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });

            // Show selected plan badges
            if (filteredPlans.Count < project.TestPlans.Count)
            {
                var badgeWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
                foreach (var plan in filteredPlans.OrderBy(p => p.TestPlanId))
                {
                    badgeWrap.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 3, 8, 3),
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                        Child = new TextBlock
                        {
                            Text = plan.TestPlanId,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
                        }
                    });
                }
                stack.Children.Add(badgeWrap);
            }

            previewCard.Child = stack;
            ReportsContainer.Children.Add(previewCard);

            ReportStatusText.Text = $"CSV · {filteredExecs.Count} execution(s) ready to export";
        }

        // â”€â”€ Reports: Export â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async void ExportReport_Click(object sender, RoutedEventArgs e)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;

            var reportTag = (ReportTypePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Summary";
            var (filteredPlans, filteredCases, filteredExecs) = GetFilteredData();

            if (filteredPlans.Count == 0)
            {
                ReportStatusText.Text = "No plans selected. Use the Scope filter to pick at least one.";
                return;
            }

            try
            {
                if (reportTag == "Summary")
                    await ExportSummaryPdfAsync(project, filteredPlans, filteredExecs);
                else if (reportTag == "TestCasesCsv")
                    await ExportTestCasesCsvAsync(project, filteredPlans);
                else if (reportTag == "ExecutionsCsv")
                    await ExportExecutionsCsvAsync(project, filteredExecs);
            }
            catch (Exception ex)
            {
                ReportStatusText.Text = $"Export failed: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task ExportSummaryPdfAsync(Project project, List<TestPlan> filteredPlans, List<TestExecution> filteredExecs)
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("PDF Document", [".pdf"]);
            picker.SuggestedFileName = $"{SanitizeFileName(project.Name)}_TestReport_{DateTime.Now:yyyyMMdd}";
            InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            ReportStatusText.Text = "Generating PDF...";
            ExportReportBtn.IsEnabled = false;

            try
            {
                var plans = filteredPlans;
                var execs = filteredExecs;
                var assessment = _criticalityAssessmentText;
                var pdfBytes = await System.Threading.Tasks.Task.Run(() =>
                    ReportService.GenerateTestSummaryPdf(project, plans, execs, assessment));

                await File.WriteAllBytesAsync(file.Path, pdfBytes);
                ReportStatusText.Text = $"Exported to {file.Name}";
            }
            finally
            {
                ExportReportBtn.IsEnabled = true;
            }
        }

        private async System.Threading.Tasks.Task ExportTestCasesCsvAsync(Project project, List<TestPlan> filteredPlans)
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV File", [".csv"]);
            picker.SuggestedFileName = $"{SanitizeFileName(project.Name)}_TestCases_{DateTime.Now:yyyyMMdd}";
            InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var csv = ReportService.GenerateTestCasesCsv(project, filteredPlans);
            await File.WriteAllTextAsync(file.Path, csv, System.Text.Encoding.UTF8);
            ReportStatusText.Text = $"Exported to {file.Name}";
        }

        private async System.Threading.Tasks.Task ExportExecutionsCsvAsync(Project project, List<TestExecution> filteredExecs)
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV File", [".csv"]);
            picker.SuggestedFileName = $"{SanitizeFileName(project.Name)}_Executions_{DateTime.Now:yyyyMMdd}";
            InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var csv = ReportService.GenerateExecutionsCsv(project, filteredExecs);
            await File.WriteAllTextAsync(file.Path, csv, System.Text.Encoding.UTF8);
            ReportStatusText.Text = $"Exported to {file.Name}";
        }

        // â”€â”€ Reports: UI helper widgets â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void AddMetricCard(Grid parent, int column, string label, string value, string glyph, string colorHex)
        {
            var hex = colorHex.StartsWith('#') ? colorHex[1..] : colorHex;
            byte r = Convert.ToByte(hex[0..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            var accentColor = Windows.UI.Color.FromArgb(255, r, g, b);

            var card = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Spacing = 6 };

            stack.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 20,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(accentColor)
            });

            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 26,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240))
            });

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
            });

            card.Child = stack;
            Grid.SetColumn(card, column);
            parent.Children.Add(card);
        }

        private static void AddStatusBar(StackPanel parent, string label, int count, int total, Windows.UI.Color color)
        {
            double pct = total > 0 ? (double)count / total * 100 : 0;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelText, 0);
            row.Children.Add(labelText);

            // Progress bar
            var barBg = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var barGrid = new Grid();
            barGrid.Children.Add(barBg);

            if (pct > 0)
            {
                var barFill = new Border
                {
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0 // Will be set after measure
                };
                barGrid.Children.Add(barFill);

                // Use SizeChanged to set width proportionally
                barGrid.SizeChanged += (s, e) =>
                {
                    barFill.Width = e.NewSize.Width * pct / 100;
                };
            }

            Grid.SetColumn(barGrid, 1);
            barGrid.Margin = new Thickness(8, 0, 8, 0);
            row.Children.Add(barGrid);

            var valueText = new TextBlock
            {
                Text = $"{count}  ({pct:F1}%)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                HorizontalTextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueText, 2);
            row.Children.Add(valueText);

            parent.Children.Add(row);
        }

        private static Border BuildPlanTableRow(string planId, string name, string cases, string passRate, bool isHeader)
        {
            var row = new Grid { Padding = new Thickness(8, 6, 8, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var fontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            var fontSize = isHeader ? 10 : 12;
            var fg = isHeader
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240));
            var idFg = isHeader
                ? fg
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250));

            var t1 = new TextBlock { Text = planId, FontSize = fontSize, FontWeight = fontWeight, Foreground = idFg, VerticalAlignment = VerticalAlignment.Center };
            if (isHeader) t1.CharacterSpacing = 150;
            Grid.SetColumn(t1, 0);
            row.Children.Add(t1);

            var t2 = new TextBlock { Text = name, FontSize = fontSize, FontWeight = fontWeight, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(t2, 1);
            row.Children.Add(t2);

            var t3 = new TextBlock { Text = cases, FontSize = fontSize, FontWeight = fontWeight, Foreground = fg, HorizontalTextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t3, 2);
            row.Children.Add(t3);

            var t4 = new TextBlock { Text = passRate, FontSize = fontSize, FontWeight = fontWeight, Foreground = fg, HorizontalTextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t4, 3);
            row.Children.Add(t4);

            var border = new Border
            {
                Child = row,
                CornerRadius = new CornerRadius(6)
            };

            if (isHeader)
            {
                border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36));
                border.Margin = new Thickness(0, 0, 0, 4);
            }

            return border;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sanitized = string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
            return string.IsNullOrWhiteSpace(sanitized) ? "Export" : sanitized;
        }

        // â”€â”€ Shared UI helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void AddFieldSection(StackPanel parent, string label, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            parent.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 100
            });
            parent.Children.Add(new TextBlock
            {
                Text = content,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        private static StackPanel BuildFormattedSuggestionsPanel(string text)
        {
            var contentStack = new StackPanel { Spacing = 6 };
            var defaultFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240));
            var headerFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36));
            var knownSections = new[] { "Overall Status", "Deployment Readiness", "Key Risks", "Suggestions" };
            var lines = text.Split('\n');
            int sectionIndex = 0;

            foreach (var section in knownSections)
            {
                var startIdx = Array.FindIndex(lines, l => l.Contains(section, StringComparison.OrdinalIgnoreCase));
                if (startIdx < 0) continue;

                int endIdx = lines.Length;
                for (int i = startIdx + 1; i < lines.Length; i++)
                {
                    if (knownSections.Any(s => lines[i].Contains(s, StringComparison.OrdinalIgnoreCase)))
                    {
                        endIdx = i;
                        break;
                    }
                }

                var headerLine = lines[startIdx];
                var namePos = headerLine.IndexOf(section, StringComparison.OrdinalIgnoreCase);
                var inlineAfterHeader = headerLine[(namePos + section.Length)..].TrimStart(':', ' ', '*', '#').Trim();

                var bodyLines = lines.Skip(startIdx + 1).Take(endIdx - startIdx - 1);
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(inlineAfterHeader))
                    parts.Add(inlineAfterHeader);
                parts.AddRange(bodyLines);

                var cleanedLines = parts.Select(line =>
                {
                    var t = line.TrimStart();
                    if (t.StartsWith("### ")) return t[4..];
                    if (t.StartsWith("## ")) return t[3..];
                    if (t.StartsWith("# ")) return t[2..];
                    if (t.StartsWith("- ")) return "\u2022 " + t[2..];
                    if (t.StartsWith("* ") && !t.StartsWith("**")) return "\u2022 " + t[2..];
                    return line;
                });

                var sectionContent = string.Join("\n", cleanedLines).Trim();
                if (string.IsNullOrWhiteSpace(sectionContent)) continue;

                if (sectionIndex > 0)
                {
                    contentStack.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                        Margin = new Thickness(0, 4, 0, 4)
                    });
                }

                contentStack.Children.Add(new TextBlock
                {
                    Text = section.ToUpperInvariant(),
                    Foreground = headerFg,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    CharacterSpacing = 100,
                    Margin = new Thickness(0, 0, 0, 2),
                    IsTextSelectionEnabled = true
                });

                foreach (var bodyLine in sectionContent.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(bodyLine)) continue;
                    bool isBullet = bodyLine.TrimStart().StartsWith("\u2022 ");
                    var lineBlock = new TextBlock
                    {
                        Foreground = defaultFg,
                        FontFamily = new FontFamily("Segoe UI, Segoe UI Symbol, Segoe UI Emoji"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 20,
                        IsTextSelectionEnabled = true,
                        Margin = isBullet ? new Thickness(8, 0, 0, 0) : new Thickness(0)
                    };
                    AppendInlineMarkdown(lineBlock, bodyLine.TrimStart());
                    contentStack.Children.Add(lineBlock);
                }

                sectionIndex++;
            }

            // Fallback: no known sections found – render raw with inline formatting
            if (contentStack.Children.Count == 0)
            {
                foreach (var rawLine in text.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    var lineBlock = new TextBlock
                    {
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                        FontFamily = new FontFamily("Segoe UI, Segoe UI Symbol, Segoe UI Emoji"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 20,
                        IsTextSelectionEnabled = true
                    };
                    AppendInlineMarkdown(lineBlock, rawLine.TrimStart());
                    contentStack.Children.Add(lineBlock);
                }
            }

            return contentStack;
        }

        private static void AppendInlineMarkdown(TextBlock block, string text)
        {
            var pattern = new Regex(@"(\*\*.*?\*\*|`[^`]+`)");
            foreach (var segment in pattern.Split(text))
            {
                if (string.IsNullOrEmpty(segment)) continue;

                if (segment.StartsWith("**") && segment.EndsWith("**") && segment.Length > 4)
                {
                    block.Inlines.Add(new Run
                    {
                        Text = segment[2..^2],
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold
                    });
                }
                else if (segment.StartsWith('`') && segment.EndsWith('`') && segment.Length > 2)
                {
                    block.Inlines.Add(new Run
                    {
                        Text = segment[1..^1],
                        FontFamily = new FontFamily("Consolas"),
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
                    });
                }
                else
                {
                    block.Inlines.Add(new Run { Text = segment });
                }
            }
        }

        private static SolidColorBrush GetStatusForeground(TestCaseStatus status) => status switch
        {
            TestCaseStatus.Passed => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)),
            TestCaseStatus.Failed => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
            TestCaseStatus.Blocked => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)),
            TestCaseStatus.Skipped => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175))
        };

        private static SolidColorBrush GetStatusBrush(TestCaseStatus status) => status switch
        {
            TestCaseStatus.Passed => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)),
            TestCaseStatus.Failed => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
            TestCaseStatus.Blocked => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)),
            TestCaseStatus.Skipped => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175))
        };

        private static SolidColorBrush GetStatusBadgeBackground(TestCaseStatus status) => status switch
        {
            TestCaseStatus.Passed => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 6, 78, 59)),
            TestCaseStatus.Failed => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 127, 29, 29)),
            TestCaseStatus.Blocked => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 32, 16)),
            TestCaseStatus.Skipped => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 40, 55)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53))
        };

        private static SolidColorBrush GetPriorityForeground(TestCasePriority priority) => priority switch
        {
            TestCasePriority.Blocker => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
            TestCasePriority.Major => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 146, 60)),
            TestCasePriority.Medium => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
            TestCasePriority.Low => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175))
        };

        private static SolidColorBrush GetPriorityBadgeBackground(TestCasePriority priority) => priority switch
        {
            TestCasePriority.Blocker => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 127, 29, 29)),
            TestCasePriority.Major => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 67, 30, 10)),
            TestCasePriority.Medium => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 32, 16)),
            TestCasePriority.Low => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53))
        };

        // ── Coverage Matrix ───────────────────────────────────────────────

        private void CoverageViewMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _coverageViewMode = btn.Tag.ToString()!;
                UpdateCoverageViewModeStyles();
                RenderCoverageMatrix();
            }
        }

        private void UpdateCoverageViewModeStyles()
        {
            var btns = new[] { CoverageByIssueBtn, CoverageByModuleBtn };
            foreach (var btn in btns)
            {
                bool active = btn.Tag.ToString() == _coverageViewMode;
                btn.Background = active
                    ? (Brush)Application.Current.Resources["ListAccentLowBrush"]
                    : new SolidColorBrush(Colors.Transparent);
                btn.Foreground = active
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["TextSecondaryBrush"];
            }
        }

        private void RenderCoverageMatrix()
        {
            CoverageMatrixContainer.Children.Clear();
            UpdateCoverageViewModeStyles();

            var project = _vm?.SelectedProject;
            if (project == null) return;

            if (project.TestCases.Count == 0 && project.Tasks.Count == 0)
            {
                CoverageEmptyState.Visibility = Visibility.Visible;
                CoverageMatrixContainer.Children.Add(CoverageEmptyState);
                CoverageStatusText.Text = string.Empty;
                return;
            }

            CoverageEmptyState.Visibility = Visibility.Collapsed;

            if (_coverageViewMode == "SapModule")
                RenderSapModuleCoverage(project);
            else
                RenderIssuesCoverage(project);
        }

        private void RenderIssuesCoverage(Project project)
        {
            var tasks = project.Tasks;
            var testCases = project.TestCases;

            if (tasks.Count == 0)
            {
                CoverageMatrixContainer.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(24),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 8,
                        Margin = new Thickness(0, 12, 0, 12),
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "No issues loaded",
                                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                                FontSize = 14,
                                HorizontalAlignment = HorizontalAlignment.Center
                            },
                            new TextBlock
                            {
                                Text = "Fetch issues from Jira or Linear on the Tasks page to map requirements to test coverage.",
                                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 75, 85, 99)),
                                FontSize = 12,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                MaxWidth = 420,
                                TextAlignment = TextAlignment.Center
                            }
                        }
                    }
                });

                int linked = testCases.Count(tc => !string.IsNullOrEmpty(tc.SourceIssueId));
                CoverageStatusText.Text = NormalizeForDisplay($"{testCases.Count} test case(s) \u00B7 {linked} linked to issues");
                return;
            }

            // Build coverage map: task key → list of test cases
            var coverageMap = new Dictionary<string, List<TestCase>>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in tasks)
            {
                var key = GetTaskKey(task);
                coverageMap[key] = testCases.Where(tc => TestCaseBelongsToTask(tc, task)).ToList();
            }

            int coveredIssues = coverageMap.Values.Count(v => v.Count > 0);
            int totalIssues = tasks.Count;
            double pct = totalIssues > 0 ? (double)coveredIssues / totalIssues * 100 : 0;

            CoverageStatusText.Text = NormalizeForDisplay($"{coveredIssues}/{totalIssues} issues covered \u00B7 {pct:F0}%");

            CoverageMatrixContainer.Children.Add(BuildCoverageSummaryCard(coveredIssues, totalIssues, testCases.Count));
            CoverageMatrixContainer.Children.Add(BuildCoverageLegend());

            var untested = tasks.Where(t => (coverageMap.GetValueOrDefault(GetTaskKey(t))?.Count ?? 0) == 0).ToList();
            var underTested = tasks.Where(t => (coverageMap.GetValueOrDefault(GetTaskKey(t))?.Count ?? 0) == 1).ToList();
            var wellTested = tasks.Where(t => (coverageMap.GetValueOrDefault(GetTaskKey(t))?.Count ?? 0) >= 2).ToList();

            if (untested.Count > 0)
                CoverageMatrixContainer.Children.Add(BuildIssueCoverageSection("UNTESTED", untested, coverageMap,
                    Windows.UI.Color.FromArgb(255, 248, 113, 113)));
            if (underTested.Count > 0)
                CoverageMatrixContainer.Children.Add(BuildIssueCoverageSection("UNDER-TESTED", underTested, coverageMap,
                    Windows.UI.Color.FromArgb(255, 251, 191, 36)));
            if (wellTested.Count > 0)
                CoverageMatrixContainer.Children.Add(BuildIssueCoverageSection("WELL-TESTED", wellTested, coverageMap,
                    Windows.UI.Color.FromArgb(255, 52, 211, 153)));
        }

        private static string GetTaskKey(ProjectTask task) =>
            !string.IsNullOrEmpty(task.IssueIdentifier) ? task.IssueIdentifier
            : task.ExternalId ?? task.Id.ToString();

        private static bool TestCaseBelongsToTask(TestCase tc, ProjectTask task)
        {
            if (!string.IsNullOrEmpty(tc.SourceIssueId))
            {
                return string.Equals(tc.SourceIssueId, task.IssueIdentifier, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tc.SourceIssueId, task.ExternalId, StringComparison.OrdinalIgnoreCase);
            }

            // Keyword-overlap fallback for test cases generated before sourceIssueId was tracked
            if (string.IsNullOrEmpty(tc.Title) || string.IsNullOrEmpty(task.Title))
                return false;

            var tcWords = new HashSet<string>(
                tc.Title.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3), StringComparer.Ordinal);

            int matches = task.Title.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .Count(w => tcWords.Contains(w));

            return matches >= 2;
        }

        private static Border BuildCoverageSummaryCard(int covered, int total, int totalTestCases)
        {
            int untested = total - covered;
            double pct = total > 0 ? (double)covered / total * 100 : 0;

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = "REQUIREMENTS COVERAGE OVERVIEW",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 150
            });

            var metricsGrid = new Grid();
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnSpacing = 12;

            AddCoverageMetricCell(metricsGrid, 0, "Total Issues", total.ToString(), "#94A3B8");
            AddCoverageMetricCell(metricsGrid, 1, "Covered", covered.ToString(), "#34D399");
            AddCoverageMetricCell(metricsGrid, 2, "Untested", untested.ToString(), "#F87171");
            AddCoverageMetricCell(metricsGrid, 3, "Coverage", $"{pct:F1}%",
                pct >= 70 ? "#34D399" : pct >= 40 ? "#FBBF24" : "#F87171");
            stack.Children.Add(metricsGrid);

            stack.Children.Add(new TextBlock
            {
                Text = $"Overall: {covered} of {total} issue(s) have at least 1 test case  ·  {totalTestCases} total test case(s)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
            });

            var barGrid = new Grid();
            barGrid.Children.Add(new Border
            {
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53))
            });

            if (pct > 0)
            {
                var fillColor = pct >= 70
                    ? Windows.UI.Color.FromArgb(255, 52, 211, 153)
                    : pct >= 40
                        ? Windows.UI.Color.FromArgb(255, 251, 191, 36)
                        : Windows.UI.Color.FromArgb(255, 248, 113, 113);

                var barFill = new Border
                {
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(fillColor),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0
                };
                barGrid.Children.Add(barFill);
                barGrid.SizeChanged += (s, e) => barFill.Width = e.NewSize.Width * pct / 100;
            }

            stack.Children.Add(barGrid);

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = stack
            };
        }

        private static void AddCoverageMetricCell(Grid parent, int column, string label, string value, string colorHex)
        {
            var hex = colorHex.StartsWith('#') ? colorHex[1..] : colorHex;
            byte r = Convert.ToByte(hex[0..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            var color = Windows.UI.Color.FromArgb(255, r, g, b);

            var cell = new StackPanel { Spacing = 4 };
            cell.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            });
            cell.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
            });

            Grid.SetColumn(cell, column);
            parent.Children.Add(cell);
        }

        private static Border BuildCoverageLegend()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };

            (string Text, Windows.UI.Color Bg, Windows.UI.Color Fg)[] items =
            [
                ("Untested (0 TCs)", Windows.UI.Color.FromArgb(255, 127, 29, 29), Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                ("Under-tested (1 TC)", Windows.UI.Color.FromArgb(255, 45, 32, 16), Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                ("Well-tested (2+ TCs)", Windows.UI.Color.FromArgb(255, 6, 78, 59), Windows.UI.Color.FromArgb(255, 52, 211, 153))
            ];

            foreach (var (text, bg, fg) in items)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                item.Children.Add(new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(bg),
                    BorderBrush = new SolidColorBrush(fg),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center
                });
                item.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(item);
            }

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = row
            };
        }

        private static Border BuildIssueCoverageSection(string label, List<ProjectTask> tasks,
            Dictionary<string, List<TestCase>> coverageMap, Windows.UI.Color sectionColor)
        {
            var stack = new StackPanel { Spacing = 6 };

            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 4) };
            headerRow.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(sectionColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerRow.Children.Add(new TextBlock
            {
                Text = $"{label}  ({tasks.Count})",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(sectionColor),
                CharacterSpacing = 150,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(headerRow);

            foreach (var task in tasks.OrderBy(t => GetTaskKey(t)))
            {
                var tcs = coverageMap.GetValueOrDefault(GetTaskKey(task)) ?? [];
                stack.Children.Add(BuildIssueRow(task, tcs, sectionColor));
            }

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = stack
            };
        }

        private static Border BuildIssueRow(ProjectTask task, List<TestCase> tcs, Windows.UI.Color coverageColor)
        {
            var grid = new Grid { Padding = new Thickness(8, 6, 8, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var idBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = GetTaskKey(task),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            Grid.SetColumn(idBadge, 0);
            grid.Children.Add(idBadge);

            var titleText = new TextBlock
            {
                Text = task.Title,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(titleText, 1);
            grid.Children.Add(titleText);

            var countBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, coverageColor.R, coverageColor.G, coverageColor.B)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = $"{tcs.Count} TC",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(coverageColor),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            Grid.SetColumn(countBadge, 2);
            grid.Children.Add(countBadge);

            var sourceBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = task.Source.ToString(),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
                }
            };
            Grid.SetColumn(sourceBadge, 3);
            grid.Children.Add(sourceBadge);

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = grid
            };
        }

        // ── SAP Module Coverage ───────────────────────────────────────────

        private static readonly Dictionary<SapCommerceModule, string[]> SapModuleKeywords = new()
        {
            [SapCommerceModule.Cart] =
                ["cart", "basket", "add to cart", "minicart", "mini cart", "cart item", "cart total", "cart page", "cart quantity", "cart entry"],
            [SapCommerceModule.Checkout] =
                ["checkout", "payment", "order", "shipping", "delivery", "address", "billing", "confirmation", "place order", "order summary", "purchase"],
            [SapCommerceModule.Pricing] =
                ["price", "pricing", "tax", "currency", "net price", "gross price", "base price", "unit price", "price calculation", "surcharge"],
            [SapCommerceModule.Promotions] =
                ["promotion", "promo", "coupon", "voucher", "campaign", "rule engine", "rebate", "discount code", "offer code", "free gift"],
            [SapCommerceModule.CatalogSync] =
                ["catalog", "product", "category", "sync", "import", "feed", "classification", "variant", "stock", "media", "impex", "solr"],
            [SapCommerceModule.B2B] =
                ["b2b", "business to business", "company", "organisation", "organization", "b2b unit", "b2b user", "budget", "cost center",
                 "approval", "purchase order", "account manager", "quick order", "account payment", "b2b customer", "b2b checkout"],
            [SapCommerceModule.OMS] =
                ["order management", "oms", "fulfillment", "fulfilment", "consignment", "warehouse", "pickup", "click and collect",
                 "return", "refund", "cancellation", "cancel order", "backorder", "split order", "dispatch", "allocation", "shipment"],
            [SapCommerceModule.Personalization] =
                ["personalization", "personalisation", "targeting", "segment", "experience", "smartedit", "cms", "content slot",
                 "component", "audience", "variation", "customization", "action definition", "cxcms", "cms page"],
            [SapCommerceModule.CPQ] =
                ["cpq", "configure", "configuration", "configurator", "quote", "guided selling", "bundle", "product configuration",
                 "rule-based pricing", "complex pricing", "subscription", "attribute group", "product variant config"]
        };

        private static SapCommerceModule? DetectSapModuleForTestCase(TestCase tc)
        {
            if (tc.SapModule.HasValue) return tc.SapModule;

            var searchText = $"{tc.Title} {tc.TestSteps} {tc.ExpectedResult}".ToLowerInvariant();

            int bestScore = 0;
            SapCommerceModule? bestModule = null;

            foreach (var (module, keywords) in SapModuleKeywords)
            {
                int score = keywords.Count(kw => searchText.Contains(kw, StringComparison.OrdinalIgnoreCase));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestModule = module;
                }
            }

            return bestScore > 0 ? bestModule : null;
        }

        private void RenderSapModuleCoverage(Project project)
        {
            var testCases = project.TestCases;

            var moduleMap = new Dictionary<SapCommerceModule, List<TestCase>>
            {
                [SapCommerceModule.Cart] = [],
                [SapCommerceModule.Checkout] = [],
                [SapCommerceModule.Pricing] = [],
                [SapCommerceModule.Promotions] = [],
                [SapCommerceModule.CatalogSync] = [],
                [SapCommerceModule.B2B] = [],
                [SapCommerceModule.OMS] = [],
                [SapCommerceModule.Personalization] = [],
                [SapCommerceModule.CPQ] = []
            };

            int unclassified = 0;
            foreach (var tc in testCases)
            {
                var module = DetectSapModuleForTestCase(tc);
                if (module.HasValue)
                    moduleMap[module.Value].Add(tc);
                else
                    unclassified++;
            }

            int totalClassified = testCases.Count - unclassified;
            CoverageStatusText.Text = NormalizeForDisplay($"SAP Commerce \u00B7 {totalClassified}/{testCases.Count} test case(s) classified");

            var headerCard = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "SAP COMMERCE MODULE COVERAGE",
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                            FontSize = 10,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            CharacterSpacing = 150
                        },
                        new TextBlock
                        {
                            Text = "Test cases are automatically classified by SAP Commerce module based on keywords in their title and test steps. Set SapModule on a test case to override.",
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 75, 85, 99)),
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
            CoverageMatrixContainer.Children.Add(headerCard);

            (SapCommerceModule Module, string Glyph, Windows.UI.Color Color, string DisplayName)[] modules =
            [
                (SapCommerceModule.Cart,            "\uE7BF", Windows.UI.Color.FromArgb(255,  56, 189, 248), "Cart"),
                (SapCommerceModule.Checkout,        "\uE8D0", Windows.UI.Color.FromArgb(255,  52, 211, 153), "Checkout"),
                (SapCommerceModule.Pricing,         "\uE7B8", Windows.UI.Color.FromArgb(255, 251, 191,  36), "Pricing"),
                (SapCommerceModule.Promotions,      "\uE8D6", Windows.UI.Color.FromArgb(255, 251, 146,  60), "Promotions"),
                (SapCommerceModule.CatalogSync,     "\uE8CB", Windows.UI.Color.FromArgb(255, 167, 139, 250), "Catalog Sync"),
                (SapCommerceModule.B2B,             "\uE902", Windows.UI.Color.FromArgb(255, 139,  92, 246), "B2B Commerce"),
                (SapCommerceModule.OMS,             "\uE8A9", Windows.UI.Color.FromArgb(255,  45, 212, 191), "Order Management"),
                (SapCommerceModule.Personalization, "\uE77C", Windows.UI.Color.FromArgb(255, 244, 114, 182), "Personalization"),
                (SapCommerceModule.CPQ,             "\uE70F", Windows.UI.Color.FromArgb(255, 251, 113, 133), "CPQ")
            ];

            foreach (var (module, glyph, color, displayName) in modules)
            {
                var cases = moduleMap[module];
                CoverageMatrixContainer.Children.Add(BuildSapModuleCard(displayName, glyph, color, cases));
            }

            if (unclassified > 0)
            {
                CoverageMatrixContainer.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(16),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new FontIcon
                            {
                                Glyph = "\uE783",
                                FontSize = 16,
                                FontFamily = new FontFamily("Segoe Fluent Icons"),
                                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                                VerticalAlignment = VerticalAlignment.Center
                            },
                            new TextBlock
                            {
                                Text = $"{unclassified} test case(s) could not be automatically classified into a SAP Commerce module.",
                                FontSize = 12,
                                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                    }
                });
            }
        }

        private static Border BuildSapModuleCard(string name, string glyph, Windows.UI.Color color, List<TestCase> cases)
        {
            int total = cases.Count;
            int passed = cases.Count(tc => tc.Status == TestCaseStatus.Passed);
            int failed = cases.Count(tc => tc.Status == TestCaseStatus.Failed);
            int blocked = cases.Count(tc => tc.Status == TestCaseStatus.Blocked);
            int notRun = cases.Count(tc => tc.Status == TestCaseStatus.NotRun || tc.Status == TestCaseStatus.Skipped);
            double passRate = total > 0 ? (double)passed / total * 100 : 0;

            string coverageLabel;
            Windows.UI.Color coverageColor;
            Windows.UI.Color coverageBg;
            if (total == 0)
            {
                coverageLabel = "Untested";
                coverageColor = Windows.UI.Color.FromArgb(255, 248, 113, 113);
                coverageBg = Windows.UI.Color.FromArgb(255, 127, 29, 29);
            }
            else if (total == 1)
            {
                coverageLabel = "Minimal";
                coverageColor = Windows.UI.Color.FromArgb(255, 251, 191, 36);
                coverageBg = Windows.UI.Color.FromArgb(255, 45, 32, 16);
            }
            else if (total <= 3)
            {
                coverageLabel = "Partial";
                coverageColor = Windows.UI.Color.FromArgb(255, 251, 146, 60);
                coverageBg = Windows.UI.Color.FromArgb(255, 67, 30, 10);
            }
            else
            {
                coverageLabel = "Good";
                coverageColor = Windows.UI.Color.FromArgb(255, 52, 211, 153);
                coverageBg = Windows.UI.Color.FromArgb(255, 6, 78, 59);
            }

            var outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            // Left: icon + name + coverage badge
            var leftStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 4,
                Margin = new Thickness(0, 0, 16, 0)
            };
            leftStack.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 20,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(color)
            });
            leftStack.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240))
            });
            leftStack.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(coverageBg),
                Child = new TextBlock
                {
                    Text = coverageLabel,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(coverageColor)
                }
            });
            Grid.SetColumn(leftStack, 0);
            outerGrid.Children.Add(leftStack);

            // Middle: stats pills + pass-rate bar
            var middleStack = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

            var statsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (total > 0)
            {
                statsRow.Children.Add(MakeCoveragePill($"{passed} Passed", Windows.UI.Color.FromArgb(255, 52, 211, 153)));
                if (failed > 0)
                    statsRow.Children.Add(MakeCoveragePill($"{failed} Failed", Windows.UI.Color.FromArgb(255, 248, 113, 113)));
                if (blocked > 0)
                    statsRow.Children.Add(MakeCoveragePill($"{blocked} Blocked", Windows.UI.Color.FromArgb(255, 245, 158, 11)));
                if (notRun > 0)
                    statsRow.Children.Add(MakeCoveragePill($"{notRun} Not Run", Windows.UI.Color.FromArgb(255, 107, 114, 128)));
            }
            else
            {
                statsRow.Children.Add(new TextBlock
                {
                    Text = "No test cases detected for this module",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 75, 85, 99)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            middleStack.Children.Add(statsRow);

            if (total > 0)
            {
                var barGrid = new Grid();
                barGrid.Children.Add(new Border
                {
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53))
                });

                if (passRate > 0)
                {
                    var fillColor = passRate >= 70
                        ? Windows.UI.Color.FromArgb(255, 52, 211, 153)
                        : passRate >= 40
                            ? Windows.UI.Color.FromArgb(255, 251, 191, 36)
                            : Windows.UI.Color.FromArgb(255, 248, 113, 113);

                    var barFill = new Border
                    {
                        Height = 6,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(fillColor),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Width = 0
                    };
                    barGrid.Children.Add(barFill);
                    barGrid.SizeChanged += (s, e) => barFill.Width = e.NewSize.Width * passRate / 100;
                }

                middleStack.Children.Add(barGrid);

                middleStack.Children.Add(new TextBlock
                {
                    Text = $"Pass rate: {passRate:F1}%",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
                });
            }

            Grid.SetColumn(middleStack, 1);
            outerGrid.Children.Add(middleStack);

            // Right: total count
            var rightStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 2,
                Margin = new Thickness(12, 0, 0, 0)
            };
            rightStack.Children.Add(new TextBlock
            {
                Text = total.ToString(),
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                HorizontalTextAlignment = TextAlignment.Center
            });
            rightStack.Children.Add(new TextBlock
            {
                Text = total == 1 ? "test case" : "test cases",
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                HorizontalTextAlignment = TextAlignment.Center
            });
            Grid.SetColumn(rightStack, 2);
            outerGrid.Children.Add(rightStack);

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = outerGrid
            };
        }

        private static Border MakeCoveragePill(string text, Windows.UI.Color color)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, color.R, color.G, color.B)),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(color),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
        }

        // ── Bug Report ────────────────────────────────────────────────

        private async System.Threading.Tasks.Task ShowTestCaseBugReportDialogAsync(TestCase tc, TestPlan plan)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;

            var executions = project.TestExecutions
                .Where(e => e.TestCaseId == tc.Id)
                .ToList();

            var envNames = project.Environments.Select(e => e.Name).ToList();
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

            var includeHistoryCheck = new CheckBox
            {
                Content = $"Include execution history ({executions.Count} run(s))",
                IsChecked = executions.Count > 0,
                IsEnabled = executions.Count > 0,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240))
            };

            string GetReport() =>
                BugReportService.GenerateFromTestCase(
                    tc,
                    plan.Name,
                    (envPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "",
                    reporterBox.Text.Trim(),
                    includeHistoryCheck.IsChecked == true ? executions : null);

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

            void Regenerate() { reportBox.Text = GetReport(); }
            envPicker.SelectionChanged += (s, _) => Regenerate();
            reporterBox.TextChanged += (s, _) => Regenerate();
            includeHistoryCheck.Checked += (s, _) => Regenerate();
            includeHistoryCheck.Unchecked += (s, _) => Regenerate();

            var hasLinear = !string.IsNullOrEmpty(LoadProjectCred("LinearApiKey"));
            var hasJira = !string.IsNullOrEmpty(LoadProjectCred("JiraDomain"));

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
            panel.Children.Add(includeHistoryCheck);
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

            string secondaryLabel = hasLinear ? "Post to Linear" : (hasJira ? "Post to Jira" : "");

            var dialog = new ContentDialog
            {
                Title = $"Bug Report \u2014 {tc.TestCaseId}",
                Content = new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = 560,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                PrimaryButtonText = "Copy Markdown",
                SecondaryButtonText = secondaryLabel,
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            dialog.Resources["ContentDialogMaxWidth"] = 680.0;
            DialogHelper.ApplyDarkTheme(dialog);

            var action = await dialog.ShowAsync();

            if (action == ContentDialogResult.Primary)
            {
                var dp = new DataPackage();
                dp.SetText(reportBox.Text);
                Clipboard.SetContent(dp);
            }
            else if (action == ContentDialogResult.Secondary)
            {
                await PostTestCaseBugReportAsync(tc, reportBox.Text, hasLinear);
            }
        }

        private async System.Threading.Tasks.Task PostTestCaseBugReportAsync(TestCase tc, string markdown, bool preferLinear)
        {
            var title = $"[Bug] {tc.TestCaseId}: {tc.Title}";
            try
            {
                if (preferLinear)
                {
                    var key = LoadProjectCred("LinearApiKey");
                    var teamId = LoadProjectCred("LinearTeamId");
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(teamId))
                        throw new Exception("No Linear credentials. Go to Settings.");

                    var service = new LinearService(key);
                    var url = await service.CreateIssueAsync(teamId, title, markdown, priority: 2);
                    if (url != null && Helpers.UriSecurity.IsSafeHttpUrl(url))
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                }
                else
                {
                    var domain = LoadProjectCred("JiraDomain");
                    var email = LoadProjectCred("JiraEmail");
                    var token = LoadProjectCred("JiraApiToken");
                    var projectKey = LoadProjectCred("JiraProjectKey");
                    if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(email) ||
                        string.IsNullOrEmpty(token) || string.IsNullOrEmpty(projectKey))
                        throw new Exception("No Jira credentials. Go to Settings.");

                    var service = new JiraService(domain, email, token);
                    var url = await service.CreateIssueAsync(projectKey, title, markdown, "High");
                    if (url != null && Helpers.UriSecurity.IsSafeHttpUrl(url))
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                }
            }
            catch (Exception ex)
            {
                var errDialog = new ContentDialog
                {
                    Title = "Failed to Post Bug Report",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                DialogHelper.ApplyDarkTheme(errDialog);
                await errDialog.ShowAsync();
            }
        }

        // ── Regression Suite Builder ──────────────────────────────────────────

        private void RenderRegressionBuilder()
        {
            RegressionBuilderContainer.Children.Clear();

            var project = _vm?.SelectedProject;
            if (project == null) return;

            DateTimeOffset? from = RegressionFromDate.SelectedDate;
            DateTimeOffset? to = RegressionToDate.SelectedDate;

            var doneLinked = GetDoneLinkedTestCases(from, to);
            var previouslyFailed = GetPreviouslyFailedTestCases(out var lastRunPlanId, out var lastRunDate);
            var smokeSubset = GetSmokeSubsetTestCases();

            var seenIds = new HashSet<Guid>(doneLinked.Select(tc => tc.Id));
            seenIds.UnionWith(previouslyFailed.Select(tc => tc.Id));
            seenIds.UnionWith(smokeSubset.Select(tc => tc.Id));
            int totalUnique = seenIds.Count;

            RegressionBuilderContainer.Children.Add(
                BuildRegressionSummaryCard(doneLinked.Count, previouslyFailed.Count, smokeSubset.Count, totalUnique, from, to));

            string doneDesc = from.HasValue || to.HasValue
                ? "Test cases linked to Done tasks" +
                  (from.HasValue ? $" from {from.Value:MMM d, yyyy}" : "") +
                  (to.HasValue ? $" until {to.Value:MMM d, yyyy}" : "") +
                  " · matched by SourceIssueId"
                : "Test cases linked to all Done tasks (no date filter) · matched by SourceIssueId";
            RegressionBuilderContainer.Children.Add(BuildRegressionSection(
                "DONE-LINKED TEST CASES", "\uE73E",
                Windows.UI.Color.FromArgb(255, 52, 211, 153),
                doneLinked, doneDesc, project));

            string failedDesc = lastRunPlanId != null
                ? $"Test cases that failed in the most recent run: {lastRunPlanId}" +
                  (lastRunDate.HasValue ? NormalizeForDisplay($" \u00B7 {lastRunDate.Value:MMM d, yyyy}") : "")
                : "No execution history found in this project";
            RegressionBuilderContainer.Children.Add(BuildRegressionSection(
                "PREVIOUSLY FAILED", "\uE8A0",
                Windows.UI.Color.FromArgb(255, 248, 113, 113),
                previouslyFailed, failedDesc, project));

            RegressionBuilderContainer.Children.Add(
                BuildSmokeSubsetSection(project, doneLinked, previouslyFailed));

            RegressionStatusText.Text = NormalizeForDisplay(
                $"{totalUnique} unique test case(s) selected");
        }

        private List<TestCase> GetDoneLinkedTestCases(DateTimeOffset? from, DateTimeOffset? to)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return [];

            var doneTasks = project.Tasks
                .Where(t => t.Status == TaskStatus.Done)
                .ToList();

            // Filter by date range using DueDate as a proxy for completion date.
            // Tasks without a DueDate are included when a filter is active.
            if (from.HasValue)
                doneTasks = doneTasks
                    .Where(t => !t.DueDate.HasValue || t.DueDate.Value >= from.Value.DateTime)
                    .ToList();
            if (to.HasValue)
                doneTasks = doneTasks
                    .Where(t => !t.DueDate.HasValue || t.DueDate.Value <= to.Value.DateTime)
                    .ToList();

            if (doneTasks.Count == 0) return [];

            var doneKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in doneTasks)
            {
                if (!string.IsNullOrEmpty(t.IssueIdentifier)) doneKeys.Add(t.IssueIdentifier);
                if (!string.IsNullOrEmpty(t.ExternalId)) doneKeys.Add(t.ExternalId);
            }

            return project.TestCases
                .Where(tc => !string.IsNullOrEmpty(tc.SourceIssueId) && doneKeys.Contains(tc.SourceIssueId))
                .ToList();
        }

        private List<TestCase> GetPreviouslyFailedTestCases(out string? lastRunPlanId, out DateTime? lastRunDate)
        {
            lastRunPlanId = null;
            lastRunDate = null;

            var project = _vm?.SelectedProject;
            if (project == null || project.TestExecutions.Count == 0) return [];

            var latestExec = project.TestExecutions.OrderByDescending(e => e.ExecutedAt).First();
            var lastPlanGuid = latestExec.TestPlanId;
            lastRunDate = latestExec.ExecutedAt;

            var plan = project.TestPlans.FirstOrDefault(p => p.Id == lastPlanGuid);
            lastRunPlanId = plan?.TestPlanId;

            var failedCaseIds = project.TestExecutions
                .Where(e => e.TestPlanId == lastPlanGuid && e.Result == TestCaseStatus.Failed)
                .Select(e => e.TestCaseId)
                .Distinct()
                .ToHashSet();

            return project.TestCases.Where(tc => failedCaseIds.Contains(tc.Id)).ToList();
        }

        private List<TestCase> GetSmokeSubsetTestCases()
        {
            var project = _vm?.SelectedProject;
            if (project == null || _smokeSubsetCaseIds == null || _smokeSubsetCaseIds.Count == 0) return [];
            var idSet = new HashSet<string>(_smokeSubsetCaseIds, StringComparer.OrdinalIgnoreCase);
            return project.TestCases.Where(tc => idSet.Contains(tc.TestCaseId)).ToList();
        }

        private Border BuildRegressionSummaryCard(
            int doneCount, int failedCount, int smokeCount, int totalUnique,
            DateTimeOffset? from, DateTimeOffset? to)
        {
            var stack = new StackPanel { Spacing = 12 };

            stack.Children.Add(new TextBlock
            {
                Text = "REGRESSION SUITE PREVIEW",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 150
            });

            string dateInfo;
            if (from.HasValue && to.HasValue)
                dateInfo = NormalizeForDisplay($"Done tasks \u00B7 {from.Value:MMM d, yyyy} \u2013 {to.Value:MMM d, yyyy}");
            else if (from.HasValue)
                dateInfo = NormalizeForDisplay($"Done tasks from {from.Value:MMM d, yyyy} onwards");
            else if (to.HasValue)
                dateInfo = NormalizeForDisplay($"Done tasks until {to.Value:MMM d, yyyy}");
            else
                dateInfo = "All Done tasks \u00B7 no date filter applied";

            stack.Children.Add(new TextBlock
            {
                Text = NormalizeForDisplay(dateInfo),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                FontSize = 12
            });

            var metricsGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnSpacing = 12;

            AddMetricCard(metricsGrid, 0, "Done-Linked", doneCount.ToString(), "\uE73E", "#34D399");
            AddMetricCard(metricsGrid, 1, "Previously Failed", failedCount.ToString(), "\uE8A0", "#F87171");
            AddMetricCard(metricsGrid, 2, "AI Smoke Subset", smokeCount.ToString(), "\uE946", "#A78BFA");
            AddMetricCard(metricsGrid, 3, "Total (unique)", totalUnique.ToString(), "\uE9D5", "#FBBF24");
            stack.Children.Add(metricsGrid);

            if (totalUnique == 0)
            {
                stack.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 4, 0, 0),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                    Child = new TextBlock
                    {
                        Text = "No test cases matched the current sources. Try adjusting the date range, or ensure test cases have SourceIssueId set and tasks are marked Done.",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap
                    }
                });
            }

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = stack
            };
        }

        private Border BuildRegressionSection(
            string title, string glyph, Windows.UI.Color accentColor,
            List<TestCase> cases, string description, Project project)
        {
            var outerStack = new StackPanel { Spacing = 0 };

            var headerGrid = new Grid { Padding = new Thickness(0, 0, 0, 6) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var chevron = new FontIcon
            {
                Glyph = "\uE70D",
                FontSize = 12,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(accentColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(chevron, 0);
            headerGrid.Children.Add(chevron);

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(accentColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = cases.Count.ToString(),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(accentColor),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            });
            Grid.SetColumn(titleRow, 1);
            headerGrid.Children.Add(titleRow);

            var headerBtn = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = headerGrid
            };
            outerStack.Children.Add(headerBtn);

            var descText = new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(22, 0, 0, 8)
            };
            outerStack.Children.Add(descText);

            var bodyStack = new StackPanel
            {
                Spacing = 6,
                Visibility = Visibility.Visible,
                Margin = new Thickness(22, 0, 0, 0)
            };

            if (cases.Count == 0)
            {
                bodyStack.Children.Add(new TextBlock
                {
                    Text = "No test cases found for this source.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 75, 85, 99)),
                    Margin = new Thickness(0, 4, 0, 4)
                });
            }
            else
            {
                foreach (var tc in cases)
                    bodyStack.Children.Add(BuildRegressionTestCaseRow(tc, accentColor));
            }
            outerStack.Children.Add(bodyStack);

            headerBtn.Click += (s, _) =>
            {
                bool isVisible = bodyStack.Visibility == Visibility.Visible;
                bodyStack.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                descText.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                chevron.Glyph = isVisible ? "\uE76C" : "\uE70D";
            };

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(50, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                Child = outerStack
            };
        }

        private Border BuildSmokeSubsetSection(
            Project project, List<TestCase> doneLinked, List<TestCase> previouslyFailed)
        {
            var accentColor = Windows.UI.Color.FromArgb(255, 167, 139, 250);
            var outerStack = new StackPanel { Spacing = 0 };

            var headerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 0, 0, 6)
            };
            headerRow.Children.Add(new FontIcon
            {
                Glyph = "\uE946",
                FontSize = 14,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(accentColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerRow.Children.Add(new TextBlock
            {
                Text = "AI SMOKE SUBSET",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (_smokeSubsetCaseIds != null)
            {
                headerRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = GetSmokeSubsetTestCases().Count.ToString(),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(accentColor),
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    }
                });
            }
            outerStack.Children.Add(headerRow);

            outerStack.Children.Add(new TextBlock
            {
                Text = "AI selects a minimal set of critical test cases for a quick smoke run, prioritising Blocker/Major cases and distinct functional areas.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var capturedDone = doneLinked;
            var capturedFailed = previouslyFailed;
            var capturedProject = project;

            if (_smokeSubsetCaseIds == null)
            {
                var genBtn = new Button
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                    Foreground = new SolidColorBrush(accentColor),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 8, 14, 8)
                };
                genBtn.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE946", FontSize = 12, FontFamily = new FontFamily("Segoe Fluent Icons") },
                        new TextBlock { Text = "Generate AI Smoke Subset", FontSize = 12 }
                    }
                };
                genBtn.Click += async (s, _) =>
                    await GenerateSmokeSubsetAsync(capturedDone, capturedFailed, capturedProject);
                outerStack.Children.Add(genBtn);
            }
            else
            {
                var smokeTestCases = GetSmokeSubsetTestCases();
                if (smokeTestCases.Count == 0)
                {
                    outerStack.Children.Add(new TextBlock
                    {
                        Text = _smokeSubsetCaseIds.Count == 0
                            ? "The AI found no additional cases to recommend. All critical cases are already covered by the other sources."
                            : $"The AI suggested {_smokeSubsetCaseIds.Count} case ID(s) but none matched existing test case IDs. Ensure test case IDs are up to date.",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    var bodyStack = new StackPanel { Spacing = 6 };
                    foreach (var tc in smokeTestCases)
                        bodyStack.Children.Add(BuildRegressionTestCaseRow(tc, accentColor));
                    outerStack.Children.Add(bodyStack);
                }

                var regenBtn = new Button
                {
                    Background = new SolidColorBrush(Colors.Transparent),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0, 10, 0, 0),
                    FontSize = 11
                };
                regenBtn.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE72C", FontSize = 10, FontFamily = new FontFamily("Segoe Fluent Icons") },
                        new TextBlock { Text = "Regenerate" }
                    }
                };
                regenBtn.Click += async (s, _) =>
                {
                    _smokeSubsetCaseIds = null;
                    await GenerateSmokeSubsetAsync(capturedDone, capturedFailed, capturedProject);
                };
                outerStack.Children.Add(regenBtn);
            }

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 19, 19, 26)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(50, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                Child = outerStack
            };
        }

        private static Border BuildRegressionTestCaseRow(TestCase tc, Windows.UI.Color accentColor)
        {
            var grid = new Grid { Padding = new Thickness(8, 6, 8, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var idBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 50)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = tc.TestCaseId,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            Grid.SetColumn(idBadge, 0);
            grid.Children.Add(idBadge);

            var titleText = new TextBlock
            {
                Text = tc.Title,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(titleText, 1);
            grid.Children.Add(titleText);

            var priorityBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = GetPriorityBadgeBackground(tc.Priority),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                Child = new TextBlock
                {
                    Text = tc.Priority.ToString(),
                    FontSize = 10,
                    Foreground = GetPriorityForeground(tc.Priority),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            Grid.SetColumn(priorityBadge, 2);
            grid.Children.Add(priorityBadge);

            var statusBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = GetStatusBadgeBackground(tc.Status),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = tc.Status.ToString(),
                    FontSize = 10,
                    Foreground = GetStatusForeground(tc.Status),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            Grid.SetColumn(statusBadge, 3);
            grid.Children.Add(statusBadge);

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Child = grid
            };
        }

        private async System.Threading.Tasks.Task GenerateSmokeSubsetAsync(
            List<TestCase> doneLinked, List<TestCase> previouslyFailed, Project project)
        {
            var geminiKey = LoadProjectCred("GeminiApiKey");
            if (string.IsNullOrEmpty(geminiKey))
            {
                var dlg = new ContentDialog
                {
                    Title = "API Key Missing",
                    Content = "Please add your Google AI Studio API key in Settings.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                DialogHelper.ApplyDarkTheme(dlg);
                await dlg.ShowAsync();
                return;
            }

            RegressionBusyOverlay.Visibility = Visibility.Visible;
            BuildRegressionSuiteBtn.IsEnabled = false;

            try
            {
                var doneTasks = project.Tasks.Where(t => t.Status == TaskStatus.Done).ToList();
                var prompt = GeminiService.BuildSmokeSubsetPrompt(project.TestCases, doneTasks, project);
                using var service = new GeminiService(geminiKey);
                var response = await service.AnalyzeIssueAsync(prompt);
                _smokeSubsetCaseIds = GeminiService.ParseSmokeSubsetIds(response);
            }
            catch (GeminiAllModelsRateLimitedException)
            {
                RegressionStatusText.Text = "Rate limit exceeded. Please try again later.";
                _smokeSubsetCaseIds = [];
            }
            catch (AggregateException ae) when (ae.InnerException is GeminiAllModelsRateLimitedException)
            {
                RegressionStatusText.Text = "Rate limit exceeded. Please try again later.";
                _smokeSubsetCaseIds = [];
            }
            catch (Exception ex)
            {
                RegressionStatusText.Text = $"AI error: {ex.Message}";
                _smokeSubsetCaseIds = [];
            }
            finally
            {
                RegressionBusyOverlay.Visibility = Visibility.Collapsed;
                BuildRegressionSuiteBtn.IsEnabled = true;
            }

            RenderRegressionBuilder();
        }

        private async void BuildRegressionSuite_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            DateTimeOffset? from = RegressionFromDate.SelectedDate;
            DateTimeOffset? to = RegressionToDate.SelectedDate;

            var doneLinked = GetDoneLinkedTestCases(from, to);
            var previouslyFailed = GetPreviouslyFailedTestCases(out _, out _);
            var smokeSubset = GetSmokeSubsetTestCases();

            var included = new List<TestCase>();
            var seenIds = new HashSet<Guid>();
            foreach (var tc in doneLinked.Concat(previouslyFailed).Concat(smokeSubset))
            {
                if (seenIds.Add(tc.Id))
                    included.Add(tc);
            }

            if (included.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "Nothing to Include",
                    Content = "No test cases were found for the selected sources and date range. Adjust your filters and try again.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                DialogHelper.ApplyDarkTheme(dlg);
                await dlg.ShowAsync();
                return;
            }

            var project = _vm.SelectedProject;

            string dateLabel;
            if (from.HasValue && to.HasValue)
                dateLabel = NormalizeForDisplay($"{from.Value:MMM d} \u2013 {to.Value:MMM d, yyyy}");
            else if (from.HasValue)
                dateLabel = NormalizeForDisplay($"from {from.Value:MMM d, yyyy}");
            else if (to.HasValue)
                dateLabel = NormalizeForDisplay($"until {to.Value:MMM d, yyyy}");
            else
                dateLabel = DateTime.Now.ToString("MMM d, yyyy");

            var plan = new TestPlan
            {
                TestPlanId = NextTestPlanId(),
                Name = NormalizeForDisplay($"Regression Suite \u00B7 {dateLabel}"),
                Description = BuildRegressionDescription(
                    doneLinked.Count, previouslyFailed.Count, smokeSubset.Count, included.Count),
                Source = TaskSource.Manual,
                IsRegressionSuite = true
            };
            project.TestPlans.Add(plan);

            foreach (var tc in included)
            {
                var copy = new TestCase
                {
                    TestCaseId = NextTestCaseId(),
                    Title = tc.Title,
                    PreConditions = tc.PreConditions,
                    TestSteps = tc.TestSteps,
                    TestData = tc.TestData,
                    ExpectedResult = tc.ExpectedResult,
                    ActualResult = string.Empty,
                    Status = TestCaseStatus.NotRun,
                    Priority = tc.Priority,
                    GeneratedAt = DateTime.Now,
                    SourceIssueId = tc.SourceIssueId,
                    Source = tc.Source,
                    TestPlanId = plan.Id,
                    SapModule = tc.SapModule
                };
                project.TestCases.Add(copy);
            }

            await _vm.SaveAsync();

            RegressionStatusText.Text = NormalizeForDisplay(
                $"Built {plan.TestPlanId} \u00B7 {included.Count} case(s)");

            // Navigate to Test Case Generation tab showing the Regression Suites view
            _testCaseViewMode = "RegressionSuites";
            TestCaseViewPicker.SelectedIndex = 1;
            _activeSubTab = "TestCaseGeneration";
            UpdateSubTabStyles();
            ShowActivePanel();
        }

        private static string BuildRegressionDescription(
            int doneCount, int failedCount, int smokeCount, int total)
        {
            var parts = new List<string>();
            if (doneCount > 0) parts.Add($"{doneCount} done-linked");
            if (failedCount > 0) parts.Add($"{failedCount} previously failed");
            if (smokeCount > 0) parts.Add($"{smokeCount} AI smoke");
            return $"Regression suite: {string.Join(", ", parts)} \u2192 {total} unique test case(s).";
        }

        private void RegressionDate_Changed(object sender, DatePickerSelectedValueChangedEventArgs e)
        {
            if (_activeSubTab == "RegressionBuilder")
                RenderRegressionBuilder();
        }

        private void RegressionClearDates_Click(object sender, RoutedEventArgs e)
        {
            RegressionFromDate.SelectedDate = null;
            RegressionToDate.SelectedDate = null;
            if (_activeSubTab == "RegressionBuilder")
                RenderRegressionBuilder();
        }
    }
}
