using System;
using System.Linq;
using QAssistant.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace QAssistant.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isLoading = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSavedKeys();
        }

        private void LoadSavedKeys()
        {
            var linearKey = CredentialService.LoadCredential("LinearApiKey");
            if (!string.IsNullOrEmpty(linearKey))
                LinearApiKeyBox.Password = linearKey;

            var linearTeam = CredentialService.LoadCredential("LinearTeamId");
            if (!string.IsNullOrEmpty(linearTeam))
                LinearTeamIdBox.Text = linearTeam;

            var jiraDomain = CredentialService.LoadCredential("JiraDomain");
            if (!string.IsNullOrEmpty(jiraDomain))
                JiraDomainBox.Text = jiraDomain;

            var jiraEmail = CredentialService.LoadCredential("JiraEmail");
            if (!string.IsNullOrEmpty(jiraEmail))
                JiraEmailBox.Text = jiraEmail;

            var jiraToken = CredentialService.LoadCredential("JiraApiToken");
            if (!string.IsNullOrEmpty(jiraToken))
                JiraApiTokenBox.Password = jiraToken;

            var jiraProject = CredentialService.LoadCredential("JiraProjectKey");
            if (!string.IsNullOrEmpty(jiraProject))
                JiraProjectKeyBox.Text = jiraProject;

            var geminiKey = CredentialService.LoadCredential("GeminiApiKey");
            if (!string.IsNullOrEmpty(geminiKey))
                GeminiApiKeyBox.Password = geminiKey;

            // Load tray setting — default to true on first run
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

        // ── General Settings ─────────────────────────────────────────
        private void MinimizeToTrayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var enabled = MinimizeToTrayToggle.IsOn;
            CredentialService.SaveCredential("MinimizeToTray", enabled ? "true" : "false");
            App.MinimizeToTray = enabled;
        }

        // ── Linear ───────────────────────────────────────────────────
        private void SaveLinear_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LinearApiKeyBox.Password) ||
                string.IsNullOrWhiteSpace(LinearTeamIdBox.Text))
            {
                ShowStatus(LinearStatusBorder, LinearStatusText,
                    "Please fill in both the API Key and Team ID.", false);
                return;
            }

            CredentialService.SaveCredential("LinearApiKey", LinearApiKeyBox.Password.Trim());
            CredentialService.SaveCredential("LinearTeamId", LinearTeamIdBox.Text.Trim());
            ShowStatus(LinearStatusBorder, LinearStatusText, "Linear keys saved successfully.", true);
        }

        private async void TestLinear_Click(object sender, RoutedEventArgs e)
        {
            var key = CredentialService.LoadCredential("LinearApiKey");
            var teamId = CredentialService.LoadCredential("LinearTeamId");

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
            CredentialService.DeleteCredential("LinearApiKey");
            CredentialService.DeleteCredential("LinearTeamId");
            LinearApiKeyBox.Password = string.Empty;
            LinearTeamIdBox.Text = string.Empty;
            ShowStatus(LinearStatusBorder, LinearStatusText, "Linear disconnected.", true);
        }

        // ── Jira ─────────────────────────────────────────────────────
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

            CredentialService.SaveCredential("JiraDomain", JiraDomainBox.Text.Trim());
            CredentialService.SaveCredential("JiraEmail", JiraEmailBox.Text.Trim());
            CredentialService.SaveCredential("JiraApiToken", JiraApiTokenBox.Password.Trim());
            CredentialService.SaveCredential("JiraProjectKey", JiraProjectKeyBox.Text.Trim());
            ShowStatus(JiraStatusBorder, JiraStatusText, "Jira keys saved successfully.", true);
        }

        private async void TestJira_Click(object sender, RoutedEventArgs e)
        {
            var domain = CredentialService.LoadCredential("JiraDomain");
            var email = CredentialService.LoadCredential("JiraEmail");
            var token = CredentialService.LoadCredential("JiraApiToken");
            var projectKey = CredentialService.LoadCredential("JiraProjectKey");

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
            CredentialService.DeleteCredential("JiraDomain");
            CredentialService.DeleteCredential("JiraEmail");
            CredentialService.DeleteCredential("JiraApiToken");
            CredentialService.DeleteCredential("JiraProjectKey");
            JiraDomainBox.Text = string.Empty;
            JiraEmailBox.Text = string.Empty;
            JiraApiTokenBox.Password = string.Empty;
            JiraProjectKeyBox.Text = string.Empty;
            ShowStatus(JiraStatusBorder, JiraStatusText, "Jira disconnected.", true);
        }

        private void SaveGeminiKey_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GeminiApiKeyBox.Password))
            {
                ShowStatus(GeminiStatusBorder, GeminiStatusText,
                    "Please enter your Google AI Studio API key.", false);
                return;
            }

            CredentialService.SaveCredential("GeminiApiKey", GeminiApiKeyBox.Password.Trim());
            ShowStatus(GeminiStatusBorder, GeminiStatusText, "Google AI Studio API key saved successfully.", true);
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
            var storage = new StorageService();
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

            await dialog.ShowAsync();
        }
    }
}