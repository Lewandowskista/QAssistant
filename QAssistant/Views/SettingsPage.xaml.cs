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
using System.IO;
using System.Linq;
using QAssistant.Helpers;
using QAssistant.Models;
using QAssistant.Services;
using QAssistant.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QAssistant.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isLoading = true;
        private bool _apiKeyVisible = false;
        private MainViewModel? _vm;
        private Guid _projectId;
        private Guid? _editingLinearConnectionId;
        private Guid? _editingJiraConnectionId;

        public SettingsPage()
        {
            this.InitializeComponent();
            // Update footer watermark to current year and product
            try
            {
                var year = DateTime.Now.Year;
                CopyrightText.Text = $"© {year} Lewandowskista · QAssistant";
            }
            catch
            {
                // ignore in case UI not yet ready
            }
        }

        /// <summary>
        /// Initialize the page with a ViewModel when hosted outside of Frame navigation.
        /// </summary>
        public void Initialize(MainViewModel vm)
        {
            _vm = vm;
            _projectId = vm.SelectedProject?.Id ?? Guid.Empty;
            LoadSavedKeys();
            LoadStorageDiagnostics();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                _projectId = vm.SelectedProject?.Id ?? Guid.Empty;
            }
            LoadSavedKeys();
            LoadStorageDiagnostics();
        }

        private void LoadStorageDiagnostics()
        {
            try
            {
                var storage = StorageService.Instance;
                var dataPath = storage.GetDataPath();
                var logPath = storage.GetLogPath();

                DataPathText.Text = dataPath;
                LogPathText.Text = logPath;
            }
            catch (Exception ex)
            {
                DataPathText.Text = $"Error: {ex.Message}";
            }
        }

        private void LoadSavedKeys()
        {
            // Show which project these keys apply to
            if (_vm?.SelectedProject != null)
                ProjectContextText.Text = $"Configuring keys for: {_vm.SelectedProject.Name}";
            else
                ProjectContextText.Text = "No project selected — keys will be saved globally.";

            // ── Automation API (global, not per-project) ──
            var apiEnabled = StorageService.Instance.GetSetting("AutomationApiEnabled");
            AutomationApiToggle.IsOn = apiEnabled == "true";

            var apiPort = StorageService.Instance.GetSetting("AutomationApiPort");
            AutomationApiPortBox.Text = string.IsNullOrEmpty(apiPort) ? "5248" : apiPort;

            var apiKey = CredentialService.LoadCredential("AutomationApiKey");
            if (!string.IsNullOrEmpty(apiKey))
            {
                AutomationApiKeyBox.Password = apiKey;
                AutomationApiKeyRevealBox.Text = apiKey;
            }

            MigrateLegacyCredentials();
            RenderLinearConnectionsList();
            RenderJiraConnectionsList();

            var geminiKey = LoadProjectCred("GeminiApiKey");
            if (!string.IsNullOrEmpty(geminiKey))
                GeminiApiKeyBox.Password = geminiKey;

            var ccv2SubCode = CredentialService.LoadCredential("Ccv2SubscriptionCode");
            if (!string.IsNullOrEmpty(ccv2SubCode))
                Ccv2SubscriptionCodeBox.Text = ccv2SubCode;

            var ccv2Token = CredentialService.LoadCredential("Ccv2ApiToken");
            if (!string.IsNullOrEmpty(ccv2Token))
                Ccv2ApiTokenBox.Password = ccv2Token;

            // SAP Commerce Context (global, not per-project — default off)
            var sapContextEnabled = StorageService.Instance.GetSetting("SapCommerceContextEnabled");
            SapCommerceContextToggle.IsOn = sapContextEnabled == "true";

            // Load tray setting — default to true on first run (global, not per-project)
            var trayEnabled = StorageService.Instance.GetSetting("MinimizeToTray");
            if (string.IsNullOrEmpty(trayEnabled))
            {
                StorageService.Instance.SaveSetting("MinimizeToTray", "true");
                MinimizeToTrayToggle.IsOn = true;
            }
            else
            {
                MinimizeToTrayToggle.IsOn = trayEnabled != "false";
            }

            _isLoading = false;
        }

        // ── Project-scoped credential helpers ────────────────────
        private string? LoadProjectCred(string key)
        {
            if (_projectId != Guid.Empty)
                return CredentialService.LoadProjectCredential(_projectId, key);
            return CredentialService.LoadCredential(key);
        }

        private void SaveProjectCred(string key, string value)
        {
            if (_projectId != Guid.Empty)
                CredentialService.SaveProjectCredential(_projectId, key, value);
            else
                CredentialService.SaveCredential(key, value);
        }

        private void DeleteProjectCred(string key)
        {
            if (_projectId != Guid.Empty)
                CredentialService.DeleteProjectCredential(_projectId, key);
            else
                CredentialService.DeleteCredential(key);
        }

        // ── General Settings ─────────────────────────────────────────
        private void MinimizeToTrayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var enabled = MinimizeToTrayToggle.IsOn;
            StorageService.Instance.SaveSetting("MinimizeToTray", enabled ? "true" : "false");
            App.MinimizeToTray = enabled;
        }

        private void SapCommerceContextToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var enabled = SapCommerceContextToggle.IsOn;
            StorageService.Instance.SaveSetting("SapCommerceContextEnabled", enabled ? "true" : "false");
        }

        private void RefreshProjects_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the main window and call refresh
                if (App.MainWindowInstance != null)
                {
                    System.Diagnostics.Debug.WriteLine("Refresh button clicked - calling ForceRefreshProjectList");
                    App.MainWindowInstance.ForceRefreshProjectList();

                    var dialog = new ContentDialog
                    {
                        Title = "Projects Sidebar Refreshed",
                        Content = "The projects sidebar has been refreshed.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    DialogHelper.ApplyDarkTheme(dialog);
                    _ = dialog.ShowAsync();
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Error",
                        Content = "Could not access main window.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    DialogHelper.ApplyDarkTheme(dialog);
                    _ = dialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Error refreshing projects: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                DialogHelper.ApplyDarkTheme(dialog);
                _ = dialog.ShowAsync();
            }
        }

        // ── Automation API ────────────────────────────────────────────
        private void AutomationApiToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var enabled = AutomationApiToggle.IsOn;
            StorageService.Instance.SaveSetting("AutomationApiEnabled", enabled ? "true" : "false");

            if (enabled)
            {
                var apiKey = AutomationApiService.GetOrCreateApiKey();
                AutomationApiKeyBox.Password = apiKey;

                var portStr = StorageService.Instance.GetSetting("AutomationApiPort");
                int port = int.TryParse(portStr, out var p) && p is >= 1024 and <= 65535 ? p : 5248;
                App.AutomationApi.Start(port);
                ShowStatus(AutomationApiStatusBorder, AutomationApiStatusText,
                    $"Automation API started on http://localhost:{port}/", true);
            }
            else
            {
                App.AutomationApi.Stop();
                ShowStatus(AutomationApiStatusBorder, AutomationApiStatusText,
                    "Automation API stopped.", true);
            }
        }

        private void SaveAutomationPort_Click(object sender, RoutedEventArgs e)
        {
            var portText = AutomationApiPortBox.Text.Trim();
            if (!int.TryParse(portText, out var port) || port is < 1024 or > 65535)
            {
                ShowStatus(AutomationApiStatusBorder, AutomationApiStatusText,
                    "Invalid port number. Use a value between 1024 and 65535.", false);
                return;
            }

            StorageService.Instance.SaveSetting("AutomationApiPort", port.ToString());
            ShowStatus(AutomationApiStatusBorder, AutomationApiStatusText,
                $"Port saved ({port}). Toggle the API off and on to apply.", true);
        }

        private void RegenerateApiKey_Click(object sender, RoutedEventArgs e)
        {
            var newKey = AutomationApiService.RegenerateApiKey();
            AutomationApiKeyBox.Password = newKey;
            AutomationApiKeyRevealBox.Text = newKey;
            ShowStatus(AutomationApiStatusBorder, AutomationApiStatusText,
                "API key regenerated. Update your test runner configuration.", true);
        }

        private void ToggleApiKeyVisibility_Click(object sender, RoutedEventArgs e)
        {
            _apiKeyVisible = !_apiKeyVisible;
            AutomationApiKeyBox.Visibility = _apiKeyVisible ? Visibility.Collapsed : Visibility.Visible;
            AutomationApiKeyRevealBox.Visibility = _apiKeyVisible ? Visibility.Visible : Visibility.Collapsed;
            ToggleApiKeyIcon.Glyph = _apiKeyVisible ? "\uED1A" : "\uE7B3";
            ToggleApiKeyText.Text = _apiKeyVisible ? "Hide Key" : "Show Key";
        }

        private void CopyApiKey_Click(object sender, RoutedEventArgs e)
        {
            var key = AutomationApiKeyBox.Password;
            if (string.IsNullOrEmpty(key)) return;
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(key);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            ShowStatus(AutomationApiStatusBorder, AutomationApiStatusText,
                "API key copied to clipboard.", true);
        }

        // ── Linear ───────────────────────────────────────────────────
        private async void OpenLinearKeys_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://linear.app/settings/api"));
        }

        private void RenderLinearConnectionsList()
        {
            LinearConnectionsList.Children.Clear();
            var project = _vm?.SelectedProject;
            if (project == null) return;

            if (project.LinearConnections.Count == 0)
            {
                LinearConnectionsList.Children.Add(new TextBlock
                {
                    Text = "No connections configured.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12
                });
                return;
            }

            foreach (var conn in project.LinearConnections)
            {
                var capturedConn = conn;
                var card = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)),
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10)
                };
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { Spacing = 2 };
                info.Children.Add(new TextBlock
                {
                    Text = conn.Label,
                    Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"Team: {conn.TeamId}",
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                    FontSize = 11
                });
                Grid.SetColumn(info, 0);
                row.Children.Add(info);

                var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                var editBtn = new Button
                {
                    Content = "Edit",
                    Background = (Brush)Application.Current.Resources["HoverBrush"],
                    Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 12
                };
                editBtn.Click += (s, e) => EditLinearConnection(capturedConn.Id);

                var deleteBtn = new Button
                {
                    Content = "✕",
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 63, 26, 26)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 5, 8, 5),
                    FontSize = 12
                };
                deleteBtn.Click += (s, e) => DeleteLinearConnection(capturedConn.Id);

                btns.Children.Add(editBtn);
                btns.Children.Add(deleteBtn);
                Grid.SetColumn(btns, 1);
                row.Children.Add(btns);

                card.Child = row;
                LinearConnectionsList.Children.Add(card);
            }
        }

        private void AddLinear_Click(object sender, RoutedEventArgs e)
        {
            _editingLinearConnectionId = null;
            LinearFormTitle.Text = "New Connection";
            LinearConnLabelBox.Text = string.Empty;
            LinearConnApiKeyBox.Password = string.Empty;
            LinearConnTeamIdBox.Text = string.Empty;
            LinearConnectionFormPanel.Visibility = Visibility.Visible;
            LinearStatusBorder.Visibility = Visibility.Collapsed;
        }

        private void EditLinearConnection(Guid id)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;
            var conn = project.LinearConnections.FirstOrDefault(c => c.Id == id);
            if (conn == null) return;

            _editingLinearConnectionId = id;
            LinearFormTitle.Text = $"Edit: {conn.Label}";
            LinearConnLabelBox.Text = conn.Label;
            LinearConnApiKeyBox.Password = string.Empty;
            LinearConnTeamIdBox.Text = conn.TeamId;
            LinearConnectionFormPanel.Visibility = Visibility.Visible;
            LinearStatusBorder.Visibility = Visibility.Collapsed;
        }

        private void DeleteLinearConnection(Guid id)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;
            project.LinearConnections.RemoveAll(c => c.Id == id);
            CredentialService.DeleteProjectCredential(project.Id, $"LinearApiKey_{id}");
            RenderLinearConnectionsList();
            _ = _vm!.SaveAsync();
            ShowStatus(LinearStatusBorder, LinearStatusText, "Connection removed.", true);
        }

        private void SaveLinear_Click(object sender, RoutedEventArgs e)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;

            var label = LinearConnLabelBox.Text.Trim();
            var teamId = LinearConnTeamIdBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(teamId))
            {
                ShowStatus(LinearStatusBorder, LinearStatusText, "Please fill in Label and Team ID.", false);
                return;
            }

            if (_editingLinearConnectionId == null)
            {
                if (string.IsNullOrWhiteSpace(LinearConnApiKeyBox.Password))
                {
                    ShowStatus(LinearStatusBorder, LinearStatusText, "API Key is required for a new connection.", false);
                    return;
                }
                var conn = new LinearConnection { Label = label, TeamId = teamId };
                CredentialService.SaveProjectCredential(project.Id, $"LinearApiKey_{conn.Id}", LinearConnApiKeyBox.Password.Trim());
                project.LinearConnections.Add(conn);
            }
            else
            {
                var conn = project.LinearConnections.FirstOrDefault(c => c.Id == _editingLinearConnectionId);
                if (conn == null) return;
                conn.Label = label;
                conn.TeamId = teamId;
                if (!string.IsNullOrWhiteSpace(LinearConnApiKeyBox.Password))
                    CredentialService.SaveProjectCredential(project.Id, $"LinearApiKey_{conn.Id}", LinearConnApiKeyBox.Password.Trim());
            }

            _ = _vm!.SaveAsync();
            RenderLinearConnectionsList();
            LinearConnectionFormPanel.Visibility = Visibility.Collapsed;
            _editingLinearConnectionId = null;
            ShowStatus(LinearStatusBorder, LinearStatusText, "Connection saved.", true);
        }

        private async void TestLinear_Click(object sender, RoutedEventArgs e)
        {
            var key = LinearConnApiKeyBox.Password.Trim();
            if (string.IsNullOrEmpty(key) && _editingLinearConnectionId.HasValue && _vm?.SelectedProject != null)
                key = CredentialService.LoadProjectCredential(_vm.SelectedProject.Id, $"LinearApiKey_{_editingLinearConnectionId}") ?? string.Empty;

            if (string.IsNullOrEmpty(key))
            {
                ShowStatus(LinearStatusBorder, LinearStatusText, "Enter an API Key first.", false);
                return;
            }

            ShowStatus(LinearStatusBorder, LinearStatusText, "Testing connection...", true);
            try
            {
                var service = new LinearService(key);
                var teams = await service.GetTeamsAsync();
                ShowStatus(LinearStatusBorder, LinearStatusText, $"Connected! Found {teams.Count} team(s).", true);
            }
            catch (Exception ex)
            {
                ShowStatus(LinearStatusBorder, LinearStatusText, $"Connection failed: {ex.Message}", false);
            }
        }

        private void CancelLinearForm_Click(object sender, RoutedEventArgs e)
        {
            LinearConnectionFormPanel.Visibility = Visibility.Collapsed;
            _editingLinearConnectionId = null;
            LinearStatusBorder.Visibility = Visibility.Collapsed;
        }

        // ── Jira ─────────────────────────────────────────────────────
        private async void OpenJiraKeys_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://id.atlassian.com/manage-profile/security/api-tokens"));
        }

        private void RenderJiraConnectionsList()
        {
            JiraConnectionsList.Children.Clear();
            var project = _vm?.SelectedProject;
            if (project == null) return;

            if (project.JiraConnections.Count == 0)
            {
                JiraConnectionsList.Children.Add(new TextBlock
                {
                    Text = "No connections configured.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12
                });
                return;
            }

            foreach (var conn in project.JiraConnections)
            {
                var capturedConn = conn;
                var card = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)),
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10)
                };
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { Spacing = 2 };
                info.Children.Add(new TextBlock
                {
                    Text = conn.Label,
                    Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"{conn.Domain}.atlassian.net · {conn.ProjectKey}",
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                    FontSize = 11
                });
                Grid.SetColumn(info, 0);
                row.Children.Add(info);

                var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                var editBtn = new Button
                {
                    Content = "Edit",
                    Background = (Brush)Application.Current.Resources["HoverBrush"],
                    Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 12
                };
                editBtn.Click += (s, e) => EditJiraConnection(capturedConn.Id);

                var deleteBtn = new Button
                {
                    Content = "✕",
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 63, 26, 26)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 5, 8, 5),
                    FontSize = 12
                };
                deleteBtn.Click += (s, e) => DeleteJiraConnection(capturedConn.Id);

                btns.Children.Add(editBtn);
                btns.Children.Add(deleteBtn);
                Grid.SetColumn(btns, 1);
                row.Children.Add(btns);

                card.Child = row;
                JiraConnectionsList.Children.Add(card);
            }
        }

        private void AddJira_Click(object sender, RoutedEventArgs e)
        {
            _editingJiraConnectionId = null;
            JiraFormTitle.Text = "New Connection";
            JiraConnLabelBox.Text = string.Empty;
            JiraConnDomainBox.Text = string.Empty;
            JiraConnEmailBox.Text = string.Empty;
            JiraConnApiTokenBox.Password = string.Empty;
            JiraConnProjectKeyBox.Text = string.Empty;
            JiraConnectionFormPanel.Visibility = Visibility.Visible;
            JiraStatusBorder.Visibility = Visibility.Collapsed;
        }

        private void EditJiraConnection(Guid id)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;
            var conn = project.JiraConnections.FirstOrDefault(c => c.Id == id);
            if (conn == null) return;

            _editingJiraConnectionId = id;
            JiraFormTitle.Text = $"Edit: {conn.Label}";
            JiraConnLabelBox.Text = conn.Label;
            JiraConnDomainBox.Text = conn.Domain;
            JiraConnEmailBox.Text = conn.Email;
            JiraConnApiTokenBox.Password = string.Empty;
            JiraConnProjectKeyBox.Text = conn.ProjectKey;
            JiraConnectionFormPanel.Visibility = Visibility.Visible;
            JiraStatusBorder.Visibility = Visibility.Collapsed;
        }

        private void DeleteJiraConnection(Guid id)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;
            project.JiraConnections.RemoveAll(c => c.Id == id);
            CredentialService.DeleteProjectCredential(project.Id, $"JiraApiToken_{id}");
            RenderJiraConnectionsList();
            _ = _vm!.SaveAsync();
            ShowStatus(JiraStatusBorder, JiraStatusText, "Connection removed.", true);
        }

        private void SaveJira_Click(object sender, RoutedEventArgs e)
        {
            var project = _vm?.SelectedProject;
            if (project == null) return;

            var label = JiraConnLabelBox.Text.Trim();
            var domain = JiraConnDomainBox.Text.Trim();
            var email = JiraConnEmailBox.Text.Trim();
            var projectKey = JiraConnProjectKeyBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(domain) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(projectKey))
            {
                ShowStatus(JiraStatusBorder, JiraStatusText, "Please fill in Label, Domain, Email, and Project Key.", false);
                return;
            }

            if (_editingJiraConnectionId == null)
            {
                if (string.IsNullOrWhiteSpace(JiraConnApiTokenBox.Password))
                {
                    ShowStatus(JiraStatusBorder, JiraStatusText, "API Token is required for a new connection.", false);
                    return;
                }
                var conn = new JiraConnection { Label = label, Domain = domain, Email = email, ProjectKey = projectKey };
                CredentialService.SaveProjectCredential(project.Id, $"JiraApiToken_{conn.Id}", JiraConnApiTokenBox.Password.Trim());
                project.JiraConnections.Add(conn);
            }
            else
            {
                var conn = project.JiraConnections.FirstOrDefault(c => c.Id == _editingJiraConnectionId);
                if (conn == null) return;
                conn.Label = label;
                conn.Domain = domain;
                conn.Email = email;
                conn.ProjectKey = projectKey;
                if (!string.IsNullOrWhiteSpace(JiraConnApiTokenBox.Password))
                    CredentialService.SaveProjectCredential(project.Id, $"JiraApiToken_{conn.Id}", JiraConnApiTokenBox.Password.Trim());
            }

            _ = _vm!.SaveAsync();
            RenderJiraConnectionsList();
            JiraConnectionFormPanel.Visibility = Visibility.Collapsed;
            _editingJiraConnectionId = null;
            ShowStatus(JiraStatusBorder, JiraStatusText, "Connection saved.", true);
        }

        private async void TestJira_Click(object sender, RoutedEventArgs e)
        {
            var domain = JiraConnDomainBox.Text.Trim();
            var email = JiraConnEmailBox.Text.Trim();
            var token = JiraConnApiTokenBox.Password.Trim();
            if (string.IsNullOrEmpty(token) && _editingJiraConnectionId.HasValue && _vm?.SelectedProject != null)
                token = CredentialService.LoadProjectCredential(_vm.SelectedProject.Id, $"JiraApiToken_{_editingJiraConnectionId}") ?? string.Empty;

            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                ShowStatus(JiraStatusBorder, JiraStatusText, "Fill in Domain, Email, and API Token first.", false);
                return;
            }

            ShowStatus(JiraStatusBorder, JiraStatusText, "Testing connection...", true);
            try
            {
                using var service = new JiraService(domain, email, token);
                var projects = await service.GetProjectsAsync();
                ShowStatus(JiraStatusBorder, JiraStatusText, $"Connected! Found {projects.Count} project(s).", true);
            }
            catch (Exception ex)
            {
                ShowStatus(JiraStatusBorder, JiraStatusText, $"Connection failed: {ex.Message}", false);
            }
        }

        private void CancelJiraForm_Click(object sender, RoutedEventArgs e)
        {
            JiraConnectionFormPanel.Visibility = Visibility.Collapsed;
            _editingJiraConnectionId = null;
            JiraStatusBorder.Visibility = Visibility.Collapsed;
        }

        // ── Legacy credential migration ───────────────────────────────
        private void MigrateLegacyCredentials()
        {
            if (_vm?.SelectedProject == null) return;
            var project = _vm.SelectedProject;
            bool dirty = false;

            if (project.LinearConnections.Count == 0)
            {
                var apiKey = LoadProjectCred("LinearApiKey");
                var teamId = LoadProjectCred("LinearTeamId");
                if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(teamId))
                {
                    try
                    {
                        var conn = new LinearConnection { Label = "Default", TeamId = teamId };
                        CredentialService.SaveProjectCredential(project.Id, $"LinearApiKey_{conn.Id}", apiKey);
                        project.LinearConnections.Add(conn);
                        dirty = true;
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Migration] Linear credential save failed: {ex.Message}");
                    }
                }
            }

            if (project.JiraConnections.Count == 0)
            {
                var domain = LoadProjectCred("JiraDomain");
                var email = LoadProjectCred("JiraEmail");
                var token = LoadProjectCred("JiraApiToken");
                var key = LoadProjectCred("JiraProjectKey");
                if (!string.IsNullOrEmpty(domain) && !string.IsNullOrEmpty(email) &&
                    !string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(key))
                {
                    try
                    {
                        var conn = new JiraConnection { Label = "Default", Domain = domain, Email = email, ProjectKey = key };
                        CredentialService.SaveProjectCredential(project.Id, $"JiraApiToken_{conn.Id}", token);
                        project.JiraConnections.Add(conn);
                        dirty = true;
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Migration] Jira credential save failed: {ex.Message}");
                    }
                }
            }

            if (dirty)
                _ = _vm.SaveAsync();
        }

        private async void OpenGeminiKeys_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://aistudio.google.com/apikey"));
        }

        private void SaveGeminiKey_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GeminiApiKeyBox.Password))
            {
                ShowStatus(GeminiStatusBorder, GeminiStatusText,
                    "Please enter your Google AI Studio API key.", false);
                return;
            }

            SaveProjectCred("GeminiApiKey", GeminiApiKeyBox.Password.Trim());
            ShowStatus(GeminiStatusBorder, GeminiStatusText, "Google AI Studio API key saved for this project.", true);
        }

        // ── SAP CCv2 ─────────────────────────────────────────────────
        private async void OpenCcv2Docs_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://help.sap.com/docs/SAP_COMMERCE_CLOUD_PUBLIC_CLOUD/9116f1cfd16049c9a5e3ea3ab7e6f204/b5f8e16db98c4c2b9bf81a99f25d8b5b.html"));
        }

        private void SaveCcv2_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Ccv2SubscriptionCodeBox.Text) ||
                string.IsNullOrWhiteSpace(Ccv2ApiTokenBox.Password))
            {
                ShowStatus(Ccv2StatusBorder, Ccv2StatusText,
                    "Please fill in both the Subscription Code and API Token.", false);
                return;
            }

            CredentialService.SaveCredential("Ccv2SubscriptionCode", Ccv2SubscriptionCodeBox.Text.Trim());
            CredentialService.SaveCredential("Ccv2ApiToken", Ccv2ApiTokenBox.Password.Trim());
            ShowStatus(Ccv2StatusBorder, Ccv2StatusText, "CCv2 credentials saved.", true);
        }

        private async void TestCcv2_Click(object sender, RoutedEventArgs e)
        {
            var subCode = CredentialService.LoadCredential("Ccv2SubscriptionCode");
            var token = CredentialService.LoadCredential("Ccv2ApiToken");

            if (string.IsNullOrEmpty(subCode) || string.IsNullOrEmpty(token))
            {
                ShowStatus(Ccv2StatusBorder, Ccv2StatusText, "Save CCv2 credentials first.", false);
                return;
            }

            ShowStatus(Ccv2StatusBorder, Ccv2StatusText, "Testing connection...", true);
            try
            {
                using var svc = new Ccv2ManagementService(subCode, token);
                var envs = await svc.GetEnvironmentsAsync();
                ShowStatus(Ccv2StatusBorder, Ccv2StatusText,
                    $"✓ Connected — {envs.Count} environment(s) found.", true);
            }
            catch (Exception ex)
            {
                ShowStatus(Ccv2StatusBorder, Ccv2StatusText,
                    $"Connection failed: {ex.Message}", false);
            }
        }

        private void DisconnectCcv2_Click(object sender, RoutedEventArgs e)
        {
            CredentialService.DeleteCredential("Ccv2SubscriptionCode");
            CredentialService.DeleteCredential("Ccv2ApiToken");
            Ccv2SubscriptionCodeBox.Text = string.Empty;
            Ccv2ApiTokenBox.Password = string.Empty;
            ShowStatus(Ccv2StatusBorder, Ccv2StatusText, "CCv2 credentials removed.", true);
        }

        // ── Helpers ──────────────────────────────────────────────────
        private void ShowStatus(Border border, TextBlock text, string message, bool success)
        {
            border.Visibility = Visibility.Visible;
            text.Text = message;
            border.Background = success
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 50, 30))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 20, 20));
            text.Foreground = success
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
        }

        // ── Diagnostics ──────────────────────────────────────────────
        private async void ViewStorageDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var storage = StorageService.Instance;
            var logPath = storage.GetLogPath();
            var dataPath = storage.GetDataPath();

            var diagnosticInfo = $"Data Path: {dataPath}\n";
            diagnosticInfo += $"Log Path: {logPath}\n\n";

            // Check file existence
            diagnosticInfo += $"Data file exists: {System.IO.File.Exists(dataPath)}\n";
            diagnosticInfo += $"Log file exists: {System.IO.File.Exists(logPath)}\n\n";

            // Try to read log file
            try
            {
                if (System.IO.File.Exists(logPath))
                {
                    var logContent = System.IO.File.ReadAllText(logPath);
                    diagnosticInfo += "Recent Logs:\n";
                    var lines = logContent.Split('\n');
                    var recentLines = lines.Length > 10 ? lines.Skip(lines.Length - 10).ToArray() : lines;
                    diagnosticInfo += string.Join("\n", recentLines);
                }
                else
                {
                    diagnosticInfo += "No log file found yet.\n";
                }
            }
            catch (Exception ex)
            {
                diagnosticInfo += $"Error reading log: {ex.Message}\n";
            }

            var dialog = new ContentDialog
            {
                Title = "Storage Diagnostics",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = diagnosticInfo,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 11,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                    }
                },
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            await dialog.ShowAsync();
        }

        private async void OpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var storage = StorageService.Instance;
                var logPath = storage.GetLogPath();

                if (!System.IO.File.Exists(logPath))
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Log File Not Found",
                        Content = $"Log file does not exist yet at:\n{logPath}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    DialogHelper.ApplyDarkTheme(dialog);
                    await dialog.ShowAsync();
                    return;
                }

                var folder = System.IO.Path.GetDirectoryName(logPath);
                await Windows.System.Launcher.LaunchFolderPathAsync(folder);
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Could not open log folder: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                DialogHelper.ApplyDarkTheme(dialog);
                await dialog.ShowAsync();
            }
        }

        // ── Project Sharing ──────────────────────────────────────────
        private async void ExportProject_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null)
            {
                ShowStatus(ShareStatusBorder, ShareStatusText, "No project selected to export.", false);
                return;
            }

            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("JSON File", [".json"]);
            picker.SuggestedFileName = $"{_vm.SelectedProject.Name.Replace(" ", "_")}_export";
            InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            try
            {
                await StorageService.Instance.ExportProjectAsync(_vm.SelectedProject, file.Path);
                ShowStatus(ShareStatusBorder, ShareStatusText,
                    $"Project exported to {file.Name}. Note: credentials are not included and must be re-entered on the target machine.",
                    true);
            }
            catch (Exception ex)
            {
                ShowStatus(ShareStatusBorder, ShareStatusText, $"Export failed: {ex.Message}", false);
            }
        }

        private async void ImportProject_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null)
            {
                ShowStatus(ShareStatusBorder, ShareStatusText, "Application not ready.", false);
                return;
            }

            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                var imported = await StorageService.Instance.ImportProjectFromJsonAsync(file.Path);
                if (imported == null)
                {
                    ShowStatus(ShareStatusBorder, ShareStatusText, "Import failed: file could not be parsed.", false);
                    return;
                }

                // Assign a fresh ID so the imported copy never collides with an existing project
                imported.Id = Guid.NewGuid();
                imported.Name = $"{imported.Name} (imported)";

                _vm.Projects.Add(imported);
                await _vm.SaveAsync();
                App.MainWindowInstance?.ForceRefreshProjectList();

                ShowStatus(ShareStatusBorder, ShareStatusText,
                    $"Project '{imported.Name}' imported successfully. Remember to re-enter any credentials under Settings.",
                    true);
            }
            catch (Exception ex)
            {
                ShowStatus(ShareStatusBorder, ShareStatusText, $"Import failed: {ex.Message}", false);
            }
        }
    }
}