using System;
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

namespace QAssistant.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isLoading = true;
        private MainViewModel? _vm;
        private Guid _projectId;

        public SettingsPage()
        {
            this.InitializeComponent();
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
            var apiEnabled = CredentialService.LoadCredential("AutomationApiEnabled");
            AutomationApiToggle.IsOn = apiEnabled == "true";

            var apiPort = CredentialService.LoadCredential("AutomationApiPort");
            AutomationApiPortBox.Text = string.IsNullOrEmpty(apiPort) ? "5248" : apiPort;

            var apiKey = CredentialService.LoadCredential("AutomationApiKey");
            if (!string.IsNullOrEmpty(apiKey))
                AutomationApiKeyBox.Password = apiKey;

            var linearKey = LoadProjectCred("LinearApiKey");
            if (!string.IsNullOrEmpty(linearKey))
                LinearApiKeyBox.Password = linearKey;

            var linearTeam = LoadProjectCred("LinearTeamId");
            if (!string.IsNullOrEmpty(linearTeam))
                LinearTeamIdBox.Text = linearTeam;

            var jiraDomain = LoadProjectCred("JiraDomain");
            if (!string.IsNullOrEmpty(jiraDomain))
                JiraDomainBox.Text = jiraDomain;

            var jiraEmail = LoadProjectCred("JiraEmail");
            if (!string.IsNullOrEmpty(jiraEmail))
                JiraEmailBox.Text = jiraEmail;

            var jiraToken = LoadProjectCred("JiraApiToken");
            if (!string.IsNullOrEmpty(jiraToken))
                JiraApiTokenBox.Password = jiraToken;

            var jiraProject = LoadProjectCred("JiraProjectKey");
            if (!string.IsNullOrEmpty(jiraProject))
                JiraProjectKeyBox.Text = jiraProject;

            var geminiKey = LoadProjectCred("GeminiApiKey");
            if (!string.IsNullOrEmpty(geminiKey))
                GeminiApiKeyBox.Password = geminiKey;

            // Load tray setting — default to true on first run (global, not per-project)
            var trayEnabled = CredentialService.LoadCredential("MinimizeToTray");
            if (string.IsNullOrEmpty(trayEnabled))
            {
                CredentialService.SaveCredential("MinimizeToTray", "true");
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
            CredentialService.SaveCredential("MinimizeToTray", enabled ? "true" : "false");
            App.MinimizeToTray = enabled;
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
            CredentialService.SaveCredential("AutomationApiEnabled", enabled ? "true" : "false");

            if (enabled)
            {
                var apiKey = AutomationApiService.GetOrCreateApiKey();
                AutomationApiKeyBox.Password = apiKey;

                var portStr = CredentialService.LoadCredential("AutomationApiPort");
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

            CredentialService.SaveCredential("AutomationApiPort", port.ToString());
            ShowStatus(AutomationApiStatusBorder, AutomationApiStatusText,
                $"Port saved ({port}). Toggle the API off and on to apply.", true);
        }

        private void RegenerateApiKey_Click(object sender, RoutedEventArgs e)
        {
            var newKey = AutomationApiService.RegenerateApiKey();
            AutomationApiKeyBox.Password = newKey;
            ShowStatus(AutomationApiStatusBorder, AutomationApiStatusText,
                "API key regenerated. Update your test runner configuration.", true);
        }

        // ── Linear ───────────────────────────────────────────────────
        private async void OpenLinearKeys_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://linear.app/settings/api"));
        }

        private void SaveLinear_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LinearApiKeyBox.Password) ||
                string.IsNullOrWhiteSpace(LinearTeamIdBox.Text))
            {
                ShowStatus(LinearStatusBorder, LinearStatusText,
                    "Please fill in both the API Key and Team ID.", false);
                return;
            }

            SaveProjectCred("LinearApiKey", LinearApiKeyBox.Password.Trim());
            SaveProjectCred("LinearTeamId", LinearTeamIdBox.Text.Trim());
            ShowStatus(LinearStatusBorder, LinearStatusText, "Linear keys saved for this project.", true);
        }

        private async void TestLinear_Click(object sender, RoutedEventArgs e)
        {
            var key = LoadProjectCred("LinearApiKey");
            var teamId = LoadProjectCred("LinearTeamId");

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(teamId))
            {
                ShowStatus(LinearStatusBorder, LinearStatusText,
                    "Save your Linear keys first.", false);
                return;
            }

            ShowStatus(LinearStatusBorder, LinearStatusText, "Testing connection...", true);

            try
            {
                var service = new LinearService(key);
                var teams = await service.GetTeamsAsync();
                ShowStatus(LinearStatusBorder, LinearStatusText,
                    $"Connected! Found {teams.Count} team(s).", true);
            }
            catch (Exception ex)
            {
                ShowStatus(LinearStatusBorder, LinearStatusText,
                    $"Connection failed: {ex.Message}", false);
            }
        }

        private void DisconnectLinear_Click(object sender, RoutedEventArgs e)
        {
            DeleteProjectCred("LinearApiKey");
            DeleteProjectCred("LinearTeamId");
            LinearApiKeyBox.Password = string.Empty;
            LinearTeamIdBox.Text = string.Empty;
            ShowStatus(LinearStatusBorder, LinearStatusText, "Linear disconnected for this project.", true);
        }

        // ── Jira ─────────────────────────────────────────────────────
        private async void OpenJiraKeys_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://id.atlassian.com/manage-profile/security/api-tokens"));
        }

        private void SaveJira_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(JiraDomainBox.Text) ||
                string.IsNullOrWhiteSpace(JiraEmailBox.Text) ||
                string.IsNullOrWhiteSpace(JiraApiTokenBox.Password) ||
                string.IsNullOrWhiteSpace(JiraProjectKeyBox.Text))
            {
                ShowStatus(JiraStatusBorder, JiraStatusText,
                    "Please fill in all Jira fields.", false);
                return;
            }

            SaveProjectCred("JiraDomain", JiraDomainBox.Text.Trim());
            SaveProjectCred("JiraEmail", JiraEmailBox.Text.Trim());
            SaveProjectCred("JiraApiToken", JiraApiTokenBox.Password.Trim());
            SaveProjectCred("JiraProjectKey", JiraProjectKeyBox.Text.Trim());
            ShowStatus(JiraStatusBorder, JiraStatusText, "Jira keys saved for this project.", true);
        }

        private async void TestJira_Click(object sender, RoutedEventArgs e)
        {
            var domain = LoadProjectCred("JiraDomain");
            var email = LoadProjectCred("JiraEmail");
            var token = LoadProjectCred("JiraApiToken");
            var projectKey = LoadProjectCred("JiraProjectKey");

            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(email) ||
                string.IsNullOrEmpty(token) || string.IsNullOrEmpty(projectKey))
            {
                ShowStatus(JiraStatusBorder, JiraStatusText,
                    "Save your Jira keys first.", false);
                return;
            }

            ShowStatus(JiraStatusBorder, JiraStatusText, "Testing connection...", true);

            try
            {
                var service = new JiraService(domain, email, token);
                var projects = await service.GetProjectsAsync();
                ShowStatus(JiraStatusBorder, JiraStatusText,
                    $"Connected! Found {projects.Count} project(s).", true);
            }
            catch (Exception ex)
            {
                ShowStatus(JiraStatusBorder, JiraStatusText,
                    $"Connection failed: {ex.Message}", false);
            }
        }

        private void DisconnectJira_Click(object sender, RoutedEventArgs e)
        {
            DeleteProjectCred("JiraDomain");
            DeleteProjectCred("JiraEmail");
            DeleteProjectCred("JiraApiToken");
            DeleteProjectCred("JiraProjectKey");
            JiraDomainBox.Text = string.Empty;
            JiraEmailBox.Text = string.Empty;
            JiraApiTokenBox.Password = string.Empty;
            JiraProjectKeyBox.Text = string.Empty;
            ShowStatus(JiraStatusBorder, JiraStatusText, "Jira disconnected for this project.", true);
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
    }
}