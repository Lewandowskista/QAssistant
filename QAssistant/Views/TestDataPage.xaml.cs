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
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace QAssistant.Views
{
    internal sealed class GroupListItem
    {
        public TestDataGroup Group { get; }
        public string Name => Group.Name;
        public string CategoryLabel => string.IsNullOrWhiteSpace(Group.Category) ? "Uncategorized" : Group.Category;
        public GroupListItem(TestDataGroup g) => Group = g;
    }

    public sealed partial class TestDataPage : Page
    {
        private MainViewModel? _vm;
        private TestDataGroup? _selectedGroup;

        public TestDataPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                Refresh();
            }
        }

        private void Refresh()
        {
            if (_vm?.SelectedProject is not { } project) return;

            var items = project.TestDataGroups.Select(g => new GroupListItem(g)).ToList();
            GroupList.ItemsSource = null;
            GroupList.ItemsSource = items;

            if (_selectedGroup != null)
            {
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Group.Id == _selectedGroup.Id) { GroupList.SelectedIndex = i; break; }
            }
        }

        private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupList.SelectedItem is GroupListItem item)
            {
                _selectedGroup = item.Group;
                LoadGroup(_selectedGroup);
            }
        }

        private void LoadGroup(TestDataGroup group)
        {
            DataEmptyState.Visibility = Visibility.Collapsed;
            DataEditorPanel.Visibility = Visibility.Visible;

            GroupNameBox.Text = group.Name;

            // Set category picker
            var categories = new[] { "Users", "Products", "Promotions", "Cart", "Orders", "Credentials", "URLs", "Other" };
            GroupCategoryPicker.SelectedIndex = Array.IndexOf(categories, group.Category);
            if (GroupCategoryPicker.SelectedIndex < 0) GroupCategoryPicker.SelectedIndex = 7;

            RenderEntries(group);
        }

        private void RenderEntries(TestDataGroup group)
        {
            EntriesContainer.Children.Clear();
            EntriesEmptyText.Visibility = group.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var entry in group.Entries)
            {
                var card = BuildEntryCard(entry, group);
                EntriesContainer.Children.Add(card);
            }

            DataStatusText.Text = $"{group.Entries.Count} entries";
        }

        private UIElement BuildEntryCard(TestDataEntry entry, TestDataGroup group)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var keyBox = new TextBox
            {
                Text = entry.Key,
                PlaceholderText = "Key",
                FontSize = 12,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            keyBox.TextChanged += (s, _) => entry.Key = keyBox.Text;

            var valueBox = new TextBox
            {
                Text = entry.Value,
                PlaceholderText = "Value",
                FontSize = 12,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            valueBox.TextChanged += (s, _) => entry.Value = valueBox.Text;

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

            var copyBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE8C8", FontSize = 13, FontFamily = new FontFamily("Segoe Fluent Icons") },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6)
            };
            ToolTipService.SetToolTip(copyBtn, "Copy value");
            copyBtn.Click += (s, e2) =>
            {
                var dp = new DataPackage();
                dp.SetText(entry.Value);
                Clipboard.SetContent(dp);
            };

            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 12, FontFamily = new FontFamily("Segoe Fluent Icons") },
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 63, 26, 26)),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(6)
            };
            deleteBtn.Click += (s, e2) =>
            {
                group.Entries.Remove(entry);
                _ = _vm?.SaveAsync();
                RenderEntries(group);
            };

            btnPanel.Children.Add(copyBtn);
            btnPanel.Children.Add(deleteBtn);

            Grid.SetColumn(keyBox, 0);
            Grid.SetColumn(valueBox, 1);
            Grid.SetColumn(btnPanel, 2);
            grid.Children.Add(keyBox);
            grid.Children.Add(valueBox);
            grid.Children.Add(btnPanel);

            border.Child = grid;
            return border;
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            var g = new TestDataGroup { Name = "New Group", Category = "Other" };
            _vm.SelectedProject.TestDataGroups.Add(g);
            _ = _vm.SaveAsync();
            _selectedGroup = g;
            Refresh();
            LoadGroup(g);
        }

        private void AddEntry_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null) return;
            var entry = new TestDataEntry { Key = "key", Value = "value" };
            _selectedGroup.Entries.Add(entry);
            _ = _vm?.SaveAsync();
            RenderEntries(_selectedGroup);
        }

        private void SaveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null) return;
            _selectedGroup.Name = GroupNameBox.Text.Trim();
            _selectedGroup.Category = (GroupCategoryPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Other";
            _ = _vm?.SaveAsync();
            Refresh();
            DataStatusText.Text = "Saved.";
        }

        private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null || _vm?.SelectedProject == null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Group",
                Content = $"Delete group '{_selectedGroup.Name}' and all its entries?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _vm.SelectedProject.TestDataGroups.Remove(_selectedGroup);
                _ = _vm.SaveAsync();
                _selectedGroup = null;
                DataEditorPanel.Visibility = Visibility.Collapsed;
                DataEmptyState.Visibility = Visibility.Visible;
                Refresh();
            }
        }
    }
}
