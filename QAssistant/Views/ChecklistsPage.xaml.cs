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
using QAssistant.Helpers;
using QAssistant.Models;
using QAssistant.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace QAssistant.Views
{
    internal sealed class ChecklistListItem
    {
        public ChecklistTemplate Template { get; }
        public string Name => Template.Name;
        public string CategoryLabel => Template.Category;
        public string ProgressLabel
        {
            get
            {
                int total = Template.Items.Count;
                if (total == 0) return "No items";
                int done = Template.Items.Count(i => i.IsChecked);
                return $"{done}/{total} checked";
            }
        }
        public ChecklistListItem(ChecklistTemplate t) => Template = t;
    }

    public sealed partial class ChecklistsPage : Page
    {
        private MainViewModel? _vm;
        private ChecklistTemplate? _selected;

        public ChecklistsPage() { this.InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm) { _vm = vm; Refresh(); }
        }

        private void Refresh()
        {
            if (_vm?.SelectedProject is not { } project) return;
            var items = project.Checklists.Select(c => new ChecklistListItem(c)).ToList();
            ChecklistList.ItemsSource = null;
            ChecklistList.ItemsSource = items;
            if (_selected != null)
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Template.Id == _selected.Id) { ChecklistList.SelectedIndex = i; break; }
        }

        private void ChecklistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChecklistList.SelectedItem is ChecklistListItem item)
            {
                _selected = item.Template;
                LoadIntoEditor(_selected);
            }
        }

        private void LoadIntoEditor(ChecklistTemplate t)
        {
            CLEmptyState.Visibility = Visibility.Collapsed;
            CLEditorPanel.Visibility = Visibility.Visible;
            CLNameBox.Text = t.Name;
            var cats = new[] { "Pre-Deployment", "Release Sign-off", "SAP Commerce", "OCC Contract", "Smoke Test", "Custom" };
            CLCategoryPicker.SelectedIndex = Math.Max(0, Array.IndexOf(cats, t.Category));
            RenderItems(t);
        }

        private void RenderItems(ChecklistTemplate t)
        {
            // keep only item controls; remove old ones
            ItemsContainer.Children.Clear();

            CLEmptyItemsText.Visibility = t.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (t.Items.Count > 0) ItemsContainer.Children.Add(CLEmptyItemsText);

            foreach (var item in t.Items)
                ItemsContainer.Children.Add(BuildItemRow(item, t));

            UpdateProgress(t);
        }

        private UIElement BuildItemRow(ChecklistItem item, ChecklistTemplate t)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Opacity = item.IsChecked ? 0.6 : 1.0
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new CheckBox
            {
                IsChecked = item.IsChecked,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            check.Checked += (_, _) => { item.IsChecked = true; border.Opacity = 0.6; UpdateProgress(t); _ = _vm?.SaveAsync(); Refresh(); };
            check.Unchecked += (_, _) => { item.IsChecked = false; border.Opacity = 1.0; UpdateProgress(t); _ = _vm?.SaveAsync(); Refresh(); };

            var textBox = new TextBox
            {
                Text = item.Text,
                PlaceholderText = "Checklist item",
                FontSize = 13,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                TextWrapping = TextWrapping.Wrap
            };
            textBox.TextChanged += (_, _) => { item.Text = textBox.Text; _ = _vm?.SaveAsync(); };

            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 12, FontFamily = new FontFamily("Segoe Fluent Icons") },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                Padding = new Thickness(6)
            };
            deleteBtn.Click += (_, _) => { t.Items.Remove(item); _ = _vm?.SaveAsync(); RenderItems(t); Refresh(); };

            Grid.SetColumn(check, 0);
            Grid.SetColumn(textBox, 1);
            Grid.SetColumn(deleteBtn, 2);
            grid.Children.Add(check);
            grid.Children.Add(textBox);
            grid.Children.Add(deleteBtn);
            border.Child = grid;
            return border;
        }

        private void UpdateProgress(ChecklistTemplate t)
        {
            int total = t.Items.Count;
            int done = t.Items.Count(i => i.IsChecked);
            double pct = total > 0 ? (double)done / total * 100 : 0;
            CLProgressBar.Value = pct;
            CLProgressText.Text = $"{done}/{total} ({pct:F0}%)";
        }

        private void AddChecklist_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            var t = new ChecklistTemplate { Name = "New Checklist", Category = "Custom" };
            _vm.SelectedProject.Checklists.Add(t);
            _ = _vm.SaveAsync();
            _selected = t;
            Refresh();
            LoadIntoEditor(t);
        }

        private void LoadBuiltins_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            foreach (var tmpl in BuiltInChecklists.All)
            {
                if (_vm.SelectedProject.Checklists.Any(c => c.Name == tmpl.Name && c.IsBuiltIn)) continue;
                _vm.SelectedProject.Checklists.Add(tmpl);
            }
            _ = _vm.SaveAsync();
            Refresh();
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            _selected.Items.Add(new ChecklistItem { Text = "New item" });
            _ = _vm?.SaveAsync();
            RenderItems(_selected);
        }

        private void SaveChecklist_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            _selected.Name = CLNameBox.Text.Trim();
            _selected.Category = (CLCategoryPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Custom";
            _ = _vm?.SaveAsync();
            Refresh();
        }

        private void ResetChecklist_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            foreach (var item in _selected.Items) item.IsChecked = false;
            _ = _vm?.SaveAsync();
            RenderItems(_selected);
            Refresh();
        }

        private async void DeleteChecklist_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _vm?.SelectedProject == null) return;
            var dialog = new ContentDialog
            {
                Title = "Delete Checklist",
                Content = $"Delete '{_selected.Name}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _vm.SelectedProject.Checklists.Remove(_selected);
                _ = _vm.SaveAsync();
                _selected = null;
                CLEditorPanel.Visibility = Visibility.Collapsed;
                CLEmptyState.Visibility = Visibility.Visible;
                Refresh();
            }
        }
    }

    // ── Built-in SAP Commerce checklists ─────────────────────────────
    internal static class BuiltInChecklists
    {
        public static IReadOnlyList<ChecklistTemplate> All { get; } = BuildAll();

        private static List<ChecklistTemplate> BuildAll()
        {
            return
            [
                new ChecklistTemplate
                {
                    Name = "Pre-Deployment QA",
                    Category = "Pre-Deployment",
                    IsBuiltIn = true,
                    Items =
                    [
                        new ChecklistItem { Text = "All critical/blocker bugs resolved or deferred" },
                        new ChecklistItem { Text = "Regression test suite executed and pass rate ≥ 90%" },
                        new ChecklistItem { Text = "Performance tests run on key flows (checkout, PDP, search)" },
                        new ChecklistItem { Text = "Security scan completed (OWASP Top 10 basics)" },
                        new ChecklistItem { Text = "Smoke test on staging environment passed" },
                        new ChecklistItem { Text = "Rollback plan documented and tested" },
                        new ChecklistItem { Text = "Release notes reviewed by QA lead" }
                    ]
                },
                new ChecklistTemplate
                {
                    Name = "Catalog Sync Verification",
                    Category = "SAP Commerce",
                    IsBuiltIn = true,
                    Items =
                    [
                        new ChecklistItem { Text = "Staged catalog item count matches expected count" },
                        new ChecklistItem { Text = "Online catalog item count matches Staged after sync" },
                        new ChecklistItem { Text = "No errors in CatalogSyncJob execution log" },
                        new ChecklistItem { Text = "Spot-check 5 products: price, stock, images, categories" },
                        new ChecklistItem { Text = "Solr re-index triggered after catalog sync" },
                        new ChecklistItem { Text = "Search results return newly synced products" },
                        new ChecklistItem { Text = "Storefront product detail pages load without errors" }
                    ]
                },
                new ChecklistTemplate
                {
                    Name = "OCC API Contract Validation",
                    Category = "OCC Contract",
                    IsBuiltIn = true,
                    Items =
                    [
                        new ChecklistItem { Text = "GET /products/{productCode} returns correct fields" },
                        new ChecklistItem { Text = "POST /users/{userId}/carts creates cart successfully" },
                        new ChecklistItem { Text = "POST /users/{userId}/carts/{cartId}/entries adds item to cart" },
                        new ChecklistItem { Text = "GET /users/{userId}/carts/{cartId} returns correct totals" },
                        new ChecklistItem { Text = "POST /users/{userId}/orders places order successfully" },
                        new ChecklistItem { Text = "Error responses match API contract (400/401/403/404/500)" },
                        new ChecklistItem { Text = "OAuth2 token flow works for guest and registered user" },
                        new ChecklistItem { Text = "Pagination works correctly (pageSize, currentPage)" }
                    ]
                },
                new ChecklistTemplate
                {
                    Name = "CronJob Health Check",
                    Category = "SAP Commerce",
                    IsBuiltIn = true,
                    Items =
                    [
                        new ChecklistItem { Text = "SolrIndexerJob: last run FINISHED (not ERROR)" },
                        new ChecklistItem { Text = "CatalogSyncJob: last run FINISHED" },
                        new ChecklistItem { Text = "CleanUpCronJob: running on schedule" },
                        new ChecklistItem { Text = "ProcessOrdersCronJob: no stuck orders" },
                        new ChecklistItem { Text = "No CronJob in RUNNING state for > 4 hours" },
                        new ChecklistItem { Text = "Trigger intervals match expected schedule" }
                    ]
                },
                new ChecklistTemplate
                {
                    Name = "ImpEx Import Validation",
                    Category = "SAP Commerce",
                    IsBuiltIn = true,
                    Items =
                    [
                        new ChecklistItem { Text = "ImpEx script validated against data model" },
                        new ChecklistItem { Text = "Test import run on development environment succeeded" },
                        new ChecklistItem { Text = "No ERROR lines in import log" },
                        new ChecklistItem { Text = "Imported items visible in Backoffice after import" },
                        new ChecklistItem { Text = "Rollback ImpEx (REMOVE) tested and ready" },
                        new ChecklistItem { Text = "Character encoding verified (UTF-8)" }
                    ]
                }
            ];
        }
    }
}
