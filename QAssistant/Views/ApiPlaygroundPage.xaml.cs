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
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using QAssistant.Helpers;
using QAssistant.Models;
using QAssistant.Services;
using QAssistant.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace QAssistant.Views
{
    internal sealed class RequestListItem
    {
        public SavedApiRequest Req { get; }
        public string Name => Req.Name;
        public string Method => Req.Method;
        public string CategoryLabel => Req.Category;
        public Color MethodColor => Req.Method switch
        {
            "GET" => Color.FromArgb(255, 16, 185, 129),
            "POST" => Color.FromArgb(255, 59, 130, 246),
            "PUT" => Color.FromArgb(255, 245, 158, 11),
            "PATCH" => Color.FromArgb(255, 167, 139, 250),
            "DELETE" => Color.FromArgb(255, 239, 68, 68),
            _ => Color.FromArgb(255, 107, 114, 128)
        };
        public RequestListItem(SavedApiRequest r) => Req = r;
    }

    public sealed partial class ApiPlaygroundPage : Page
    {
        private MainViewModel? _vm;
        private SavedApiRequest? _selected;
        private string _activeRespTab = "Body";
        private static readonly HttpClient s_client = new() { Timeout = TimeSpan.FromSeconds(30) };

        public ApiPlaygroundPage()
        {
            this.InitializeComponent();
            ReqBodyBox.PlaceholderText = "{\"key\": \"value\"}";
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm) { _vm = vm; Refresh(); }
        }

        private void Refresh()
        {
            if (_vm?.SelectedProject is not { } project) return;
            var items = project.ApiRequests.Select(r => new RequestListItem(r)).ToList();
            RequestList.ItemsSource = null;
            RequestList.ItemsSource = items;
            if (_selected != null)
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Req.Id == _selected.Id) { RequestList.SelectedIndex = i; break; }
        }

        private void RequestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RequestList.SelectedItem is RequestListItem item) LoadRequest(item.Req);
        }

        private void LoadRequest(SavedApiRequest req)
        {
            _selected = req;
            ApiEmptyState.Visibility = Visibility.Collapsed;
            ApiEditorPanel.Visibility = Visibility.Visible;

            ReqNameBox.Text = req.Name;
            var cats = new[] { "OCC", "HAC", "Jira", "Linear", "Custom" };
            ReqCategoryPicker.SelectedIndex = Math.Max(0, Array.IndexOf(cats, req.Category));

            var methods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" };
            ReqMethodPicker.SelectedIndex = Math.Max(0, Array.IndexOf(methods, req.Method));

            ReqUrlBox.Text = req.Url;
            ReqHeadersBox.Text = req.Headers;
            ReqBodyBox.Text = req.Body;

            RespStatusText.Text = string.Empty;
            RespDurationText.Text = string.Empty;
            RespHeadersBox.Text = string.Empty;

            if (req.History.Count > 0)
            {
                var last = req.History[^1];
                RespBodyBox.Text = last.ResponseBody;
                RespHeadersBox.Text = last.ResponseHeaders;
            }
            else
            {
                RespBodyBox.Text = string.Empty;
            }

            RefreshHistoryList();
            RefreshComparePickers();
            SetRespTab("Body");
        }

        private void AddRequest_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            var req = new SavedApiRequest { Name = "New Request", Method = "GET", Category = "Custom" };
            _vm.SelectedProject.ApiRequests.Add(req);
            _ = _vm.SaveAsync();
            _selected = req;
            Refresh();
            LoadRequest(req);
        }

        private void LoadOccTemplates_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            foreach (var tmpl in OccTemplates.All)
            {
                if (_vm.SelectedProject.ApiRequests.Any(r => r.Name == tmpl.Name)) continue;
                _vm.SelectedProject.ApiRequests.Add(tmpl);
            }
            _ = _vm.SaveAsync();
            Refresh();
        }

        private void LoadHacTemplates_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            foreach (var tmpl in HacTemplates.All)
            {
                if (_vm.SelectedProject.ApiRequests.Any(r => r.Name == tmpl.Name)) continue;
                _vm.SelectedProject.ApiRequests.Add(tmpl);
            }
            _ = _vm.SaveAsync();
            Refresh();
        }

        private void SaveRequest_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            _selected.Name = ReqNameBox.Text.Trim();
            _selected.Category = (ReqCategoryPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Custom";
            _selected.Method = (ReqMethodPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
            _selected.Url = ReqUrlBox.Text.Trim();
            _selected.Headers = ReqHeadersBox.Text.Trim();
            _selected.Body = ReqBodyBox.Text.Trim();
            _ = _vm?.SaveAsync();
            Refresh();
        }

        private async void DeleteRequest_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _vm?.SelectedProject == null) return;
            var dialog = new ContentDialog
            {
                Title = "Delete Request",
                Content = $"Delete '{_selected.Name}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _vm.SelectedProject.ApiRequests.Remove(_selected);
                _ = _vm.SaveAsync();
                _selected = null;
                ApiEditorPanel.Visibility = Visibility.Collapsed;
                ApiEmptyState.Visibility = Visibility.Visible;
                Refresh();
            }
        }

        private async void SendRequest_Click(object sender, RoutedEventArgs e)
        {
            var url = ReqUrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url)) return;

            var method = (ReqMethodPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
            var headersText = ReqHeadersBox.Text.Trim();
            var body = ReqBodyBox.Text.Trim();

            RespStatusText.Text = "Sending…";
            RespBodyBox.Text = string.Empty;
            RespDurationText.Text = string.Empty;

            var sw = Stopwatch.StartNew();
            try
            {
                var request = new HttpRequestMessage(new HttpMethod(method), url);

                if (!string.IsNullOrWhiteSpace(body) && method is "POST" or "PUT" or "PATCH")
                    request.Content = new StringContent(body, Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));

                // Parse headers (one per line, key: value)
                // Must come after Content assignment so content-level headers (e.g. Content-Type) can be set.
                foreach (var line in headersText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var idx = line.IndexOf(':');
                    if (idx > 0)
                    {
                        var key = line[..idx].Trim();
                        var val = line[(idx + 1)..].Trim();
                        if (!request.Headers.TryAddWithoutValidation(key, val))
                            request.Content?.Headers.TryAddWithoutValidation(key, val);
                    }
                }

                var response = await s_client.SendAsync(request);
                sw.Stop();

                var responseBody = await response.Content.ReadAsStringAsync();
                var statusCode = (int)response.StatusCode;

                // Pretty-print JSON
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    responseBody = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                }
                catch { /* leave as-is */ }

                Color statusColor = statusCode < 300
                    ? Color.FromArgb(255, 16, 185, 129)
                    : statusCode < 400
                        ? Color.FromArgb(255, 245, 158, 11)
                        : Color.FromArgb(255, 239, 68, 68);

                RespStatusText.Text = $"{statusCode} {response.StatusCode}";
                RespStatusText.Foreground = new SolidColorBrush(statusColor);
                RespDurationText.Text = $"{sw.ElapsedMilliseconds} ms";
                RespBodyBox.Text = responseBody;

                // Capture response headers as a single string
                var respHeaders = new StringBuilder();
                foreach (var h in response.Headers)
                    respHeaders.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
                if (response.Content?.Headers != null)
                    foreach (var h in response.Content.Headers)
                        respHeaders.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
                var headersSnapshot = respHeaders.ToString().TrimEnd();

                RespHeadersBox.Text = headersSnapshot;

                // Persist to history
                if (_selected != null)
                {
                    _selected.History.Add(new ApiRequestHistoryEntry
                    {
                        StatusCode = statusCode,
                        ResponseBody = responseBody.Length > 10_000 ? responseBody[..10_000] + "\n…truncated" : responseBody,
                        ResponseHeaders = headersSnapshot,
                        DurationMs = sw.ElapsedMilliseconds
                    });
                    if (_selected.History.Count > 20)
                        _selected.History.RemoveAt(0);
                    _ = _vm?.SaveAsync();
                    RefreshHistoryList();
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                RespStatusText.Text = "Error";
                RespStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
                RespBodyBox.Text = ex.Message;
                RespDurationText.Text = $"{sw.ElapsedMilliseconds} ms";
            }
        }

        private void CopyResponse_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RespBodyBox.Text)) return;
            var dp = new DataPackage();
            dp.SetText(RespBodyBox.Text);
            Clipboard.SetContent(dp);
        }

        // ── Auto-populate auth headers ────────────────────────────────

        private void AutoAuth_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject is not { } project) return;

            var env = project.Environments.FirstOrDefault(e => e.IsDefault)
                  ?? project.Environments.FirstOrDefault();
            if (env == null) return;

            var id = env.Id.ToString("N");
            var user = CredentialService.LoadCredential($"Env_{id}_Username") ?? string.Empty;
            var pass = CredentialService.LoadCredential($"Env_{id}_Password") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(pass)) return;

            var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
            var authLine = $"Authorization: Basic {basicToken}";

            var existing = ReqHeadersBox.Text.Trim();
            var lines = existing.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            var authIdx = lines.FindIndex(l => l.TrimStart().StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase));
            if (authIdx >= 0)
                lines[authIdx] = authLine;
            else
                lines.Insert(0, authLine);

            ReqHeadersBox.Text = string.Join("\n", lines);
        }

        // ── Response sub-tabs ─────────────────────────────────────────

        private void RespTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
                SetRespTab(tag);
        }

        private void SetRespTab(string tab)
        {
            _activeRespTab = tab;
            var tabBtns = new[] { RespTabBody, RespTabHeaders, RespTabHistory, RespTabCompare };
            var tags = new[] { "Body", "Headers", "History", "Compare" };
            for (int i = 0; i < tabBtns.Length; i++)
            {
                bool active = tags[i] == tab;
                tabBtns[i].Background = active
                    ? (Brush)Application.Current.Resources["ListAccentLowBrush"]
                    : new SolidColorBrush(Colors.Transparent);
                tabBtns[i].Foreground = active
                    ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextSecondaryBrush"];
            }

            RespBodyPanel.Visibility = tab == "Body" ? Visibility.Visible : Visibility.Collapsed;
            RespHeadersPanel.Visibility = tab == "Headers" ? Visibility.Visible : Visibility.Collapsed;
            RespHistoryPanel.Visibility = tab == "History" ? Visibility.Visible : Visibility.Collapsed;
            RespComparePanel.Visibility = tab == "Compare" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── History list ──────────────────────────────────────────────

        private void RefreshHistoryList()
        {
            HistoryContainer.Children.Clear();
            if (_selected == null || _selected.History.Count == 0)
            {
                HistoryContainer.Children.Add(new TextBlock
                {
                    Text = "No history yet. Send a request to record a snapshot.",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 12,
                    Margin = new Thickness(16)
                });
                return;
            }

            foreach (var entry in _selected.History.AsEnumerable().Reverse())
            {
                Color statusColor = entry.StatusCode < 300
                    ? Color.FromArgb(255, 16, 185, 129)
                    : entry.StatusCode < 400
                        ? Color.FromArgb(255, 245, 158, 11)
                        : Color.FromArgb(255, 239, 68, 68);

                var row = new Grid { Padding = new Thickness(16, 8, 16, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var statusTb = new TextBlock
                {
                    Text = entry.StatusCode.ToString(),
                    Foreground = new SolidColorBrush(statusColor),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var timeTb = new TextBlock
                {
                    Text = entry.ExecutedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 226, 232, 240)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var durationTb = new TextBlock
                {
                    Text = $"{entry.DurationMs} ms",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 107, 114, 128)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var viewBtn = new Button
                {
                    Content = "View",
                    Background = new SolidColorBrush(Colors.Transparent),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 167, 139, 250)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8, 3, 8, 3),
                    FontSize = 11,
                    Tag = entry
                };
                viewBtn.Click += HistoryView_Click;

                Grid.SetColumn(statusTb, 0);
                Grid.SetColumn(timeTb, 1);
                Grid.SetColumn(durationTb, 2);
                Grid.SetColumn(viewBtn, 3);
                row.Children.Add(statusTb);
                row.Children.Add(timeTb);
                row.Children.Add(durationTb);
                row.Children.Add(viewBtn);

                var border = new Border
                {
                    Child = row,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 42, 42, 58)),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                HistoryContainer.Children.Add(border);
            }
        }

        private void HistoryView_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ApiRequestHistoryEntry entry })
            {
                RespBodyBox.Text = entry.ResponseBody;
                RespHeadersBox.Text = entry.ResponseHeaders;
                RespStatusText.Text = entry.StatusCode.ToString();
                RespDurationText.Text = $"{entry.DurationMs} ms";
                SetRespTab("Body");
            }
        }

        // ── Snapshot comparison ───────────────────────────────────────

        private void RefreshComparePickers()
        {
            ComparePickerA.Items.Clear();
            ComparePickerB.Items.Clear();
            if (_selected == null) return;

            for (int i = 0; i < _selected.History.Count; i++)
            {
                var h = _selected.History[i];
                var label = $"Run {i + 1} — {h.StatusCode} — {h.ExecutedAt:HH:mm:ss}";
                ComparePickerA.Items.Add(new ComboBoxItem { Content = label, Tag = i });
                ComparePickerB.Items.Add(new ComboBoxItem { Content = label, Tag = i });
            }

            if (_selected.History.Count >= 2)
            {
                ComparePickerA.SelectedIndex = _selected.History.Count - 2;
                ComparePickerB.SelectedIndex = _selected.History.Count - 1;
            }
        }

        private void ComparePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selected == null) return;
            if (ComparePickerA.SelectedItem is not ComboBoxItem itemA || itemA.Tag is not int idxA) return;
            if (ComparePickerB.SelectedItem is not ComboBoxItem itemB || itemB.Tag is not int idxB) return;
            if (idxA < 0 || idxA >= _selected.History.Count || idxB < 0 || idxB >= _selected.History.Count) return;

            RenderDiff(_selected.History[idxA], _selected.History[idxB]);
        }

        private void RenderDiff(ApiRequestHistoryEntry a, ApiRequestHistoryEntry b)
        {
            CompareResultContainer.Children.Clear();

            // Metadata summary
            var summaryColor = a.StatusCode == b.StatusCode
                ? Color.FromArgb(255, 16, 185, 129)
                : Color.FromArgb(255, 239, 68, 68);
            CompareResultContainer.Children.Add(new TextBlock
            {
                Text = $"Status: {a.StatusCode} → {b.StatusCode}    Duration: {a.DurationMs}ms → {b.DurationMs}ms",
                Foreground = new SolidColorBrush(summaryColor),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var linesA = a.ResponseBody.Split('\n');
            var linesB = b.ResponseBody.Split('\n');
            int maxLines = Math.Max(linesA.Length, linesB.Length);

            for (int i = 0; i < Math.Min(maxLines, 500); i++)
            {
                var lineA = i < linesA.Length ? linesA[i] : string.Empty;
                var lineB = i < linesB.Length ? linesB[i] : string.Empty;

                if (lineA == lineB)
                {
                    CompareResultContainer.Children.Add(MakeDiffLine($"  {lineB}", Color.FromArgb(255, 107, 114, 128)));
                }
                else
                {
                    if (!string.IsNullOrEmpty(lineA))
                        CompareResultContainer.Children.Add(MakeDiffLine($"- {lineA}", Color.FromArgb(255, 239, 68, 68)));
                    if (!string.IsNullOrEmpty(lineB))
                        CompareResultContainer.Children.Add(MakeDiffLine($"+ {lineB}", Color.FromArgb(255, 16, 185, 129)));
                }
            }

            if (maxLines > 500)
            {
                CompareResultContainer.Children.Add(MakeDiffLine($"… {maxLines - 500} more lines", Color.FromArgb(255, 107, 114, 128)));
            }
        }

        private static TextBlock MakeDiffLine(string text, Color color) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap
        };
    }

    // ── Built-in API templates ─────────────────────────────────────────
    internal static class OccTemplates
    {
        public static IReadOnlyList<SavedApiRequest> All { get; } =
        [
            new SavedApiRequest
            {
                Name = "Get Product",
                Method = "GET",
                Url = "https://{baseSite}/occ/v2/{baseSite}/products/{productCode}?fields=FULL",
                Headers = "Authorization: Bearer {ACCESS_TOKEN}",
                Category = "OCC"
            },
            new SavedApiRequest
            {
                Name = "Create Anonymous Cart",
                Method = "POST",
                Url = "https://{baseSite}/occ/v2/{baseSite}/users/anonymous/carts",
                Headers = "Authorization: Bearer {ACCESS_TOKEN}",
                Category = "OCC"
            },
            new SavedApiRequest
            {
                Name = "Add Entry to Cart",
                Method = "POST",
                Url = "https://{baseSite}/occ/v2/{baseSite}/users/{userId}/carts/{cartId}/entries",
                Headers = "Authorization: Bearer {ACCESS_TOKEN}\nContent-Type: application/json",
                Body = "{\"product\":{\"code\":\"{productCode}\"},\"quantity\":1}",
                Category = "OCC"
            },
            new SavedApiRequest
            {
                Name = "Get Cart",
                Method = "GET",
                Url = "https://{baseSite}/occ/v2/{baseSite}/users/{userId}/carts/{cartId}?fields=FULL",
                Headers = "Authorization: Bearer {ACCESS_TOKEN}",
                Category = "OCC"
            },
            new SavedApiRequest
            {
                Name = "Search Products",
                Method = "GET",
                Url = "https://{baseSite}/occ/v2/{baseSite}/products/search?query={query}&pageSize=20",
                Headers = "Authorization: Bearer {ACCESS_TOKEN}",
                Category = "OCC"
            },
            new SavedApiRequest
            {
                Name = "Get OAuth Token",
                Method = "POST",
                Url = "https://{baseSite}/authorizationserver/oauth/token",
                Headers = "Content-Type: application/x-www-form-urlencoded",
                Body = "grant_type=password&client_id=mobile_android&client_secret=secret&username={email}&password={password}",
                Category = "OCC"
            },
            new SavedApiRequest
            {
                Name = "Get Categories",
                Method = "GET",
                Url = "https://{baseSite}/occ/v2/{baseSite}/catalogs/{catalogId}/{catalogVersion}/categories/{categoryId}",
                Headers = "Authorization: Bearer {ACCESS_TOKEN}",
                Category = "OCC"
            },
            new SavedApiRequest
            {
                Name = "Place Order",
                Method = "POST",
                Url = "https://{baseSite}/occ/v2/{baseSite}/users/{userId}/orders?cartId={cartId}",
                Headers = "Authorization: Bearer {ACCESS_TOKEN}\nContent-Type: application/json",
                Category = "OCC"
            }
        ];
    }

    internal static class HacTemplates
    {
        public static IReadOnlyList<SavedApiRequest> All { get; } =
        [
            new SavedApiRequest
            {
                Name = "HAC – ImpEx Import",
                Method = "POST",
                Url = "https://{hacHost}/hac/console/impex/import",
                Headers = "Content-Type: application/x-www-form-urlencoded",
                Body = "scriptContent=INSERT_UPDATE+Product%3Bcode%5Bunique%3Dtrue%5D%3Bname%5Ben%5D%0A%3Btest123%3BTest+Product&maxThreads=4&encoding=UTF-8&validationEnum=IMPORT_STRICT",
                Category = "HAC"
            },
            new SavedApiRequest
            {
                Name = "HAC – ImpEx Validate",
                Method = "POST",
                Url = "https://{hacHost}/hac/console/impex/import/validate",
                Headers = "Content-Type: application/x-www-form-urlencoded",
                Body = "scriptContent=INSERT_UPDATE+Product%3Bcode%5Bunique%3Dtrue%5D%3Bname%5Ben%5D%0A%3Btest123%3BTest+Product&maxThreads=1&encoding=UTF-8&validationEnum=IMPORT_STRICT",
                Category = "HAC"
            },
            new SavedApiRequest
            {
                Name = "HAC – FlexibleSearch",
                Method = "POST",
                Url = "https://{hacHost}/hac/console/flexsearch/execute",
                Headers = "Content-Type: application/x-www-form-urlencoded",
                Body = "flexibleSearchQuery=SELECT+%7Bpk%7D%2C+%7Bcode%7D+FROM+%7BProduct%7D+WHERE+%7Bcode%7D+%3D+%27{productCode}%27&sqlQuery=&maxCount=200&user=admin&locale=en&commit=false",
                Category = "HAC"
            },
            new SavedApiRequest
            {
                Name = "HAC – CronJobs Status",
                Method = "GET",
                Url = "https://{hacHost}/hac/monitoring/cronjobs",
                Headers = "Accept: text/html",
                Category = "HAC"
            },
            new SavedApiRequest
            {
                Name = "Solr – Core Status",
                Method = "GET",
                Url = "https://{solrHost}:8983/solr/admin/cores?action=STATUS&wt=json",
                Category = "HAC"
            },
            new SavedApiRequest
            {
                Name = "Solr – Select Query",
                Method = "GET",
                Url = "https://{solrHost}:8983/solr/{coreName}/select?q={query}&rows=10&wt=json",
                Category = "HAC"
            }
        ];
    }
}
