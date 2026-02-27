using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QAssistant.Helpers;
using QAssistant.Models;
using QAssistant.Services;
using QAssistant.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        private readonly HashSet<Guid> _selectedPlanIds = [];
        private bool _criticalityExpanded;
        private string? _criticalityAssessmentText;

        private Guid ProjectId => _vm?.SelectedProject?.Id ?? Guid.Empty;

        private string? LoadProjectCred(string key) =>
            ProjectId != Guid.Empty
                ? CredentialService.LoadProjectCredential(ProjectId, key)
                : CredentialService.LoadCredential(key);

        public TestsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                MigrateOrphanedTestCases();
                RenderTestPlans();
            }
        }

        // ── Sub-tab navigation ───────────────────────────────────

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
            var tabs = new[] { TestCaseGenBtn, TestRunsBtn, ReportsBtn };
            foreach (var tab in tabs)
            {
                bool active = tab.Tag.ToString() == _activeSubTab;
                tab.Background = active
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
                    : new SolidColorBrush(Colors.Transparent);
                tab.Foreground = active
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
            }
        }

        private void ShowActivePanel()
        {
            TestCaseGenerationPanel.Visibility = _activeSubTab == "TestCaseGeneration" ? Visibility.Visible : Visibility.Collapsed;
            TestRunsPanel.Visibility = _activeSubTab == "TestRuns" ? Visibility.Visible : Visibility.Collapsed;
            ReportsPanel.Visibility = _activeSubTab == "Reports" ? Visibility.Visible : Visibility.Collapsed;

            if (_activeSubTab == "TestRuns")
                RenderExecutionHistory();
            if (_activeSubTab == "Reports")
                RenderReportsDashboard();
        }

        // ── Backward compatibility ───────────────────────────────

        private void MigrateOrphanedTestCases()
        {
            if (_vm?.SelectedProject == null) return;

            var orphans = _vm.SelectedProject.TestCases
                .Where(tc => tc.TestPlanId == null || tc.TestPlanId == Guid.Empty)
                .ToList();

            if (orphans.Count == 0) return;

            var plan = new TestPlan
            {
                TestPlanId = NextTestPlanId(),
                Name = "Imported Test Cases",
                Description = "Test cases imported from a previous session.",
                Source = orphans[0].Source
            };
            _vm.SelectedProject.TestPlans.Add(plan);

            foreach (var tc in orphans)
                tc.TestPlanId = plan.Id;

            _ = _vm.SaveAsync();
        }

        // ── ID helpers ───────────────────────────────────────────

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

        // ── Generate test cases ──────────────────────────────────

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
            var prompt = GeminiService.BuildTestCaseGenerationPrompt(tasks, selectedSource);

            string response = string.Empty;
            var generateTask = System.Threading.Tasks.Task.Run(async () =>
            {
                var service = new GeminiService(geminiKey);
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

                // Create a new test plan for this generation batch
                var plan = new TestPlan
                {
                    TestPlanId = NextTestPlanId(),
                    Name = $"{selectedSource} · {DateTime.Now:MMM d, yyyy h:mm tt}",
                    Description = $"Auto-generated from {tasks.Count} {selectedSource} issue(s).",
                    Source = source
                };
                _vm.SelectedProject.TestPlans.Add(plan);

                // Assign sequential IDs and link to plan
                foreach (var tc in generatedCases)
                {
                    tc.TestCaseId = NextTestCaseId();
                    tc.TestPlanId = plan.Id;
                    _vm.SelectedProject.TestCases.Add(tc);
                }

                await _vm.SaveAsync();

                GenerationStatusText.Text = $"Generated {generatedCases.Count} test cases in {plan.TestPlanId} · {DateTime.Now:h:mm tt}";
                RenderTestPlans();
            }
            catch (Exception ex)
            {
                GenerationStatusText.Text = $"Parse error: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task<List<ProjectTask>> FetchIssuesFromSourceAsync(string source)
        {
            if (source == "Jira")
            {
                var domain = LoadProjectCred("JiraDomain");
                var email = LoadProjectCred("JiraEmail");
                var token = LoadProjectCred("JiraApiToken");
                var projectKey = LoadProjectCred("JiraProjectKey");

                if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(email) ||
                    string.IsNullOrEmpty(token) || string.IsNullOrEmpty(projectKey))
                    throw new Exception("Jira credentials not configured. Go to Settings.");

                var service = new JiraService(domain, email, token);
                return await service.GetIssuesAsync(projectKey);
            }
            else
            {
                var key = LoadProjectCred("LinearApiKey");
                var teamId = LoadProjectCred("LinearTeamId");

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(teamId))
                    throw new Exception("Linear credentials not configured. Go to Settings.");

                var service = new LinearService(key);
                return await service.GetIssuesAsync(teamId);
            }
        }

        // ── Render: Test Plans (collapsible) ─────────────────────

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
            TestCaseCountText.Text = $"{plans.Count} plan(s) · {allCases.Count} case(s)";

            foreach (var plan in plans.OrderByDescending(p => p.CreatedAt))
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

            // ── Plan header (click to collapse/expand) ──
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
            titleStack.Children.Add(titleRow);

            // Status summary
            var statusSummary = BuildStatusSummary(cases);
            titleStack.Children.Add(statusSummary);

            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            // Delete plan button
            var deletePlanBtn = new Button
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
            var capturedPlan = plan;
            deletePlanBtn.Click += async (s, _) => await DeleteTestPlanAsync(capturedPlan);
            Grid.SetColumn(deletePlanBtn, 2);
            headerGrid.Children.Add(deletePlanBtn);

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

            // ── Collapsible body: test case cards ──
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

        // ── Render: single test case card ────────────────────────

        private Border BuildTestCaseCard(TestCase tc, TestPlan plan)
        {
            var cardStack = new StackPanel { Spacing = 10 };

            // ── Header row: ID + Title + Run + Status + Delete ──
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

            // ── Traceability label ──
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

            // ── Separator ──
            cardStack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                Margin = new Thickness(0, 2, 0, 2)
            });

            // ── Field rows ──
            AddFieldSection(cardStack, "PRE-CONDITIONS", tc.PreConditions);
            AddFieldSection(cardStack, "TEST STEPS", tc.TestSteps);
            AddFieldSection(cardStack, "TEST DATA", tc.TestData);
            AddFieldSection(cardStack, "EXPECTED RESULT", tc.ExpectedResult);

            // ── Actual Result ──
            if (!string.IsNullOrWhiteSpace(tc.ActualResult))
                AddFieldSection(cardStack, "ACTUAL RESULT", tc.ActualResult);

            // ── Footer: source + timestamp ──
            var footerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(0, 4, 0, 0)
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
                Text = tc.GeneratedAt.ToString("MMM d, yyyy · h:mm tt"),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            });
            cardStack.Children.Add(footerPanel);

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

        // ── Execute test case (dialog) ───────────────────────────

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

        // ── Render: Test Runs (execution history) ────────────────

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
            ExecutionCountText.Text = $"{executions.Count} execution(s)";

            foreach (var exec in executions.OrderByDescending(e => e.ExecutedAt))
            {
                var card = BuildExecutionCard(exec);
                ExecutionsContainer.Children.Add(card);
            }

            // ── Criticality Assessment expandable section ──
            var criticalityCard = BuildCriticalityAssessmentCard();
            ExecutionsContainer.Children.Add(criticalityCard);
        }

        private Border BuildCriticalityAssessmentCard()
        {
            var outerStack = new StackPanel { Spacing = 0 };

            // ── Expand/Collapse button ──
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
                Text = "Expand for detailed view",
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

            // ── Collapsible body ──
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
                Text = "CRITICALITY ASSESSMENT",
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
                AddFieldSection(bodyStack, "AI ANALYSIS", _criticalityAssessmentText);
            }
            else
            {
                var generateBtn = new Button
                {
                    Content = "Generate AI Criticality Assessment",
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

            var prompt = GeminiService.BuildCriticalityAssessmentPrompt(
                project.Tasks,
                project.TestCases,
                project.TestExecutions,
                project.TestPlans);

            try
            {
                _criticalityAssessmentText = "Generating assessment...";
                RenderExecutionHistory();

                var service = new GeminiService(geminiKey);
                var response = await service.AnalyzeIssueAsync(prompt);

                // Cap stored response length to prevent unbounded memory use
                const int MaxAssessmentLength = 20_000;
                _criticalityAssessmentText = response.Length > MaxAssessmentLength
                    ? response[..MaxAssessmentLength] + "\n\n(truncated)"
                    : response;
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

            // ── Traceability header: TE → TC → TP ──
            var traceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            traceRow.Children.Add(MakeTraceBadge(exec.ExecutionId, GetStatusBrush(exec.Result)));
            traceRow.Children.Add(new TextBlock
            {
                Text = "→",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            traceRow.Children.Add(MakeTraceBadge(
                tc?.TestCaseId ?? "?",
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))));
            traceRow.Children.Add(new TextBlock
            {
                Text = "→",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            traceRow.Children.Add(MakeTraceBadge(
                plan?.TestPlanId ?? "?",
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 189, 248))));
            cardStack.Children.Add(traceRow);

            // Test case title
            if (tc != null)
            {
                cardStack.Children.Add(new TextBlock
                {
                    Text = tc.Title,
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
                Text = exec.ExecutedAt.ToString("MMM d, yyyy · h:mm:ss tt"),
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

        // ── Delete helpers ───────────────────────────────────────

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

            _vm?.SelectedProject?.TestCases.Remove(tc);
            if (_vm != null) await _vm.SaveAsync();
            RenderTestPlans();
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

            _vm.SelectedProject.TestCases.Clear();
            _vm.SelectedProject.TestPlans.Clear();
            await _vm.SaveAsync();
            GenerationStatusText.Text = "All test plans cleared.";
            RenderTestPlans();
        }

        // ── Reports: Dashboard rendering ─────────────────────────

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

        // ── Reports: Plan filter ─────────────────────────────────

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
                .Where(te => planIds.Contains(te.TestPlanId) || caseIds.Contains(te.TestCaseId))
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

        // ── Reports: Dashboard cards ─────────────────────────────

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

            // ── Metric cards row ──
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

            // ── Status Breakdown ──
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

            // ── Plan-level summary table ──
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

        // ── Reports: Export ──────────────────────────────────────

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

        // ── Reports: UI helper widgets ───────────────────────────

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

        // ── Shared UI helpers ────────────────────────────────────

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
    }
}
