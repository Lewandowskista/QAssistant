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
using System.Net.Http;
using System.Threading.Tasks;
using QAssistant.Helpers;
using QAssistant.Models;
using QAssistant.Services;
using QAssistant.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace QAssistant.Views
{
    /// <summary>View model wrapper for EnvList binding.</summary>
    internal sealed class EnvListItem
    {
        public QaEnvironment Env { get; }
        public HealthStatus Health { get; set; } = HealthStatus.Unknown;
        public string Name => Env.Name;
        public string TypeLabel => Env.Type.ToString();
        public Visibility DefaultBadgeVisibility => Env.IsDefault ? Visibility.Visible : Visibility.Collapsed;
        public SolidColorBrush HealthBrush => Health switch
        {
            HealthStatus.Healthy => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)),
            HealthStatus.Unhealthy => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128))
        };
        public SolidColorBrush ColorBrushDisplay
        {
            get
            {
                try
                {
                    var hex = Env.Color.StartsWith('#') ? Env.Color[1..] : Env.Color;
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(hex[0..2], 16),
                        Convert.ToByte(hex[2..4], 16),
                        Convert.ToByte(hex[4..6], 16)));
                }
                catch { return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)); }
            }
        }

        public EnvListItem(QaEnvironment env) => Env = env;
    }

    public sealed partial class EnvironmentsPage : Page
    {
        private MainViewModel? _vm;
        private QaEnvironment? _selected;
        private readonly EnvironmentHealthService _healthService = new();

        public EnvironmentsPage()
        {
            this.InitializeComponent();
            this.Unloaded += (_, _) => _healthService.Dispose();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                _healthService.StatusChanged += OnHealthStatusChanged;
                StartHealthChecks();
                Refresh();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _healthService.StatusChanged -= OnHealthStatusChanged;
            _healthService.Stop();
        }

        private void OnHealthStatusChanged()
        {
            DispatcherQueue.TryEnqueue(Refresh);
        }

        private void StartHealthChecks()
        {
            if (_vm?.SelectedProject is not { } project) return;
            var envs = project.Environments;
            if (envs.Count > 0)
                _healthService.Start(envs);
        }

        private void Refresh()
        {
            if (_vm?.SelectedProject is not { } project) return;

            var items = new List<EnvListItem>();
            foreach (var env in project.Environments)
            {
                var item = new EnvListItem(env) { Health = _healthService.GetStatus(env.Id) };
                items.Add(item);
            }

            EnvList.ItemsSource = null;
            EnvList.ItemsSource = items;

            if (_selected != null)
            {
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Env.Id == _selected.Id) { EnvList.SelectedIndex = i; break; }
            }
        }

        private void EnvList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EnvList.SelectedItem is EnvListItem item)
            {
                _selected = item.Env;
                LoadEnvIntoEditor(_selected);
                EnvEmptyState.Visibility = Visibility.Collapsed;
                EnvEditorPanel.Visibility = Visibility.Visible;
            }
        }

        private void LoadEnvIntoEditor(QaEnvironment env)
        {
            EnvNameBox.Text = env.Name;
            EnvTypePicker.SelectedIndex = env.Type switch
            {
                EnvironmentType.Development => 0,
                EnvironmentType.Staging => 1,
                EnvironmentType.Production => 2,
                _ => 3
            };
            EnvBaseUrlBox.Text = env.BaseUrl;
            EnvHealthCheckUrlBox.Text = env.HealthCheckUrl;
            EnvHacUrlBox.Text = env.HacUrl;
            EnvBackofficeUrlBox.Text = env.BackofficeUrl;
            EnvStorefrontUrlBox.Text = env.StorefrontUrl;
            EnvSolrUrlBox.Text = env.SolrAdminUrl;
            EnvOccPathBox.Text = env.OccBasePath;
            EnvNotesBox.Text = env.Notes;
            EnvDefaultCheck.IsChecked = env.IsDefault;
            EnvStatusText.Text = string.Empty;

            // Load credentials from Credential Manager
            var id = env.Id.ToString("N");
            EnvUsernameBox.Text = CredentialService.LoadCredential($"Env_{id}_Username") ?? string.Empty;
            EnvPasswordBox.Password = CredentialService.LoadCredential($"Env_{id}_Password") ?? string.Empty;
        }

        private void AddEnv_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            var env = new QaEnvironment { Name = "New Environment" };
            _vm.SelectedProject.Environments.Add(env);
            _ = _vm.SaveAsync();

            _selected = env;
            Refresh();
            EnvEmptyState.Visibility = Visibility.Collapsed;
            EnvEditorPanel.Visibility = Visibility.Visible;
            LoadEnvIntoEditor(env);
        }

        private void SaveEnv_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _vm?.SelectedProject == null) return;

            _selected.Name = EnvNameBox.Text.Trim();
            _selected.Type = (EnvTypePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
            {
                "Development" => EnvironmentType.Development,
                "Staging" => EnvironmentType.Staging,
                "Production" => EnvironmentType.Production,
                _ => EnvironmentType.Custom
            };
            _selected.BaseUrl = EnvBaseUrlBox.Text.Trim();
            _selected.HealthCheckUrl = EnvHealthCheckUrlBox.Text.Trim();
            _selected.HacUrl = EnvHacUrlBox.Text.Trim();
            _selected.BackofficeUrl = EnvBackofficeUrlBox.Text.Trim();
            _selected.StorefrontUrl = EnvStorefrontUrlBox.Text.Trim();
            _selected.SolrAdminUrl = EnvSolrUrlBox.Text.Trim();
            _selected.OccBasePath = EnvOccPathBox.Text.Trim();
            _selected.Notes = EnvNotesBox.Text.Trim();

            if (EnvDefaultCheck.IsChecked == true)
            {
                foreach (var env in _vm.SelectedProject.Environments)
                    env.IsDefault = env.Id == _selected.Id;
            }

            // Save credentials
            var id = _selected.Id.ToString("N");
            if (!string.IsNullOrWhiteSpace(EnvUsernameBox.Text))
                CredentialService.SaveCredential($"Env_{id}_Username", EnvUsernameBox.Text.Trim());
            if (!string.IsNullOrWhiteSpace(EnvPasswordBox.Password))
                CredentialService.SaveCredential($"Env_{id}_Password", EnvPasswordBox.Password);

            _ = _vm.SaveAsync();
            Refresh();
            EnvStatusText.Text = "Saved.";
        }

        private async void TestConn_Click(object sender, RoutedEventArgs e)
        {
            var url = EnvBaseUrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                EnvStatusText.Text = "Enter a Base URL to test.";
                return;
            }

            EnvStatusText.Text = "Testing connection...";
            TestConnBtn.IsEnabled = false;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await client.GetAsync(url);
                EnvStatusText.Text = $"✓ Reachable — HTTP {(int)response.StatusCode} {response.StatusCode}";
            }
            catch (Exception ex)
            {
                EnvStatusText.Text = $"✗ Unreachable: {ex.Message}";
            }
            finally
            {
                TestConnBtn.IsEnabled = true;
            }
        }

        private async void CheckAllHealth_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject is not { } project || project.Environments.Count == 0) return;
            EnvStatusText.Text = "Checking all endpoints...";
            await _healthService.CheckNowAsync(project.Environments);
            Refresh();
            EnvStatusText.Text = "Health check complete.";
        }

        private void SwitchActive_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _vm?.SelectedProject == null) return;

            foreach (var env in _vm.SelectedProject.Environments)
                env.IsDefault = env.Id == _selected.Id;

            _ = _vm.SaveAsync();
            Refresh();
            EnvStatusText.Text = $"✓ Switched active environment to '{_selected.Name}'.";
        }

        private async void DeleteEnv_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _vm?.SelectedProject == null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Environment",
                Content = $"Delete '{_selected.Name}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _vm.SelectedProject.Environments.Remove(_selected);
                _ = _vm.SaveAsync();
                _selected = null;
                EnvEditorPanel.Visibility = Visibility.Collapsed;
                EnvEmptyState.Visibility = Visibility.Visible;
                Refresh();
            }
        }
    }
}
