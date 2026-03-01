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

    internal sealed record ImpExTemplate(string Name, string Category, string Description, string Code);

    public sealed partial class TestDataPage : Page
    {
        private MainViewModel? _vm;
        private TestDataGroup? _selectedGroup;
        private string _impExFilter = "All";

        // ── SAP Commerce ImpEx snippet library ────────────────────────────
        private static readonly IReadOnlyList<ImpExTemplate> s_impExTemplates =
        [
            new("Create Product (Staged)", "Products",
                "Inserts or updates a basic product in the Staged catalog version.",
                "$catalogVersion=catalogVersion(catalog(id[default='electronicsProductCatalog']),version[default='Staged'])\n\n" +
                "INSERT_UPDATE Product;code[unique=true];$catalogVersion;name[lang=en];unit(code);approvalStatus(code)\n" +
                ";{{productCode}};{{catalogVersion}};{{productName}};pieces;approved"),

            new("Set Product Price", "Products",
                "Assigns a base price to a product via the Europe1Prices translator.",
                "$catalogVersion=catalogVersion(catalog(id[default='electronicsProductCatalog']),version[default='Staged'])\n" +
                "$Europe1Prices=Europe1Prices[translator=de.hybris.platform.europe1.jalo.impex.Europe1PricesTranslator]\n\n" +
                "INSERT_UPDATE Product;code[unique=true];$catalogVersion;$Europe1Prices\n" +
                ";{{productCode}};{{catalogVersion}};{{price}}::{{currencyIsoCode}}::1"),

            new("Create B2C Customer", "Customers",
                "Creates a standard B2C customer account with a hashed password.",
                "INSERT_UPDATE Customer;uid[unique=true];name;password[default='12341234'];groups(uid)\n" +
                ";{{email}};{{fullName}};;customergroup"),

            new("Create B2B Customer", "Customers",
                "Creates a B2B customer linked to a B2B organisational unit.",
                "INSERT_UPDATE B2BCustomer;uid[unique=true];name;email;password[default='12341234'];groups(uid);defaultB2BUnit(uid)\n" +
                ";{{email}};{{fullName}};{{email}};;b2bcustomergroup;{{b2bUnitUid}}"),

            new("Create B2B Unit", "Customers",
                "Creates a B2B organisational unit (company).",
                "INSERT_UPDATE B2BUnit;uid[unique=true];name;locName[lang=en];active[default=true]\n" +
                ";{{b2bUnitUid}};{{unitName}};{{unitName}};true"),

            new("Create User Group", "Customers",
                "Creates a custom user group and assigns it to the customer group.",
                "INSERT_UPDATE UserGroup;uid[unique=true];name[lang=en];groups(uid)\n" +
                ";{{groupUid}};{{groupName}};customergroup"),

            new("Customer Delivery Address", "Customers",
                "Adds a delivery and billing address to an existing customer.",
                "INSERT_UPDATE Address;owner(Customer.uid)[unique=true];streetname[unique=true];streetnumber;postalcode;town;country(isocode);phone1;shippingAddress;billingAddress\n" +
                ";{{customerEmail}};{{streetName}};{{streetNumber}};{{postalCode}};{{city}};{{countryCode}};{{phone}};true;true"),

            new("Set Stock Level", "Stock",
                "Sets available stock quantity for a product at a given warehouse.",
                "INSERT_UPDATE StockLevel;productCode[unique=true];warehouse(code)[unique=true];inStockStatus(code);available;overSelling;preOrder;reserved\n" +
                ";{{productCode}};{{warehouseCode}};forceInStock;{{stockQty}};0;0;0"),

            new("Force Out Of Stock", "Stock",
                "Forces a product out of stock at a given warehouse regardless of quantity.",
                "INSERT_UPDATE StockLevel;productCode[unique=true];warehouse(code)[unique=true];inStockStatus(code);available\n" +
                ";{{productCode}};{{warehouseCode}};forceOutOfStock;0"),

            new("Percentage Discount Promotion", "Promotions",
                "Creates a percentage discount promotion active for a date range.",
                "$promotionGroup=promotionGroup(Identifier[default='electronicsPromoGroup'])\n\n" +
                "INSERT_UPDATE PercentageDiscountPromotion;code[unique=true];$promotionGroup;enabled[default=true];title[lang=en];percentageDiscount;startDate[dateformat=dd.MM.yyyy HH:mm:ss];endDate[dateformat=dd.MM.yyyy HH:mm:ss]\n" +
                ";{{promoCode}};;true;{{promoTitle}};{{discountPercent}};{{startDate}};{{endDate}}"),

            new("Fixed Amount Discount Promotion", "Promotions",
                "Creates a fixed monetary discount promotion for a product.",
                "$promotionGroup=promotionGroup(Identifier[default='electronicsPromoGroup'])\n\n" +
                "INSERT_UPDATE ProductFixedDiscountPromotion;code[unique=true];$promotionGroup;enabled[default=true];title[lang=en];absoluteDiscount;startDate[dateformat=dd.MM.yyyy HH:mm:ss];endDate[dateformat=dd.MM.yyyy HH:mm:ss]\n" +
                ";{{promoCode}};;true;{{promoTitle}};{{discountAmount}};{{startDate}};{{endDate}}"),

            new("Free Gift Promotion", "Promotions",
                "Creates a buy-one-get-one-free promotion for a specific product.",
                "$promotionGroup=promotionGroup(Identifier[default='electronicsPromoGroup'])\n" +
                "$catalogVersion=catalogVersion(catalog(id[default='electronicsProductCatalog']),version[default='Staged'])\n\n" +
                "INSERT_UPDATE ProductBuyOneGetOneFreePromotion;code[unique=true];$promotionGroup;enabled[default=true];title[lang=en];product(code,$catalogVersion);startDate[dateformat=dd.MM.yyyy HH:mm:ss];endDate[dateformat=dd.MM.yyyy HH:mm:ss]\n" +
                ";{{promoCode}};;true;{{promoTitle}};{{productCode}}:{{catalogVersion}};{{startDate}};{{endDate}}"),

            new("Single-Use Coupon Code", "Promotions",
                "Creates a single-use coupon code with a configurable redemption limit.",
                "INSERT_UPDATE SingleCodeCoupon;couponId[unique=true];maxRedemptionsPerCustomer;maxTotalRedemptions;active[default=true]\n" +
                ";{{couponCode}};1;{{maxUsage}};true"),

            new("Catalog Sync Job (Staged → Online)", "Catalog",
                "Defines a catalog version synchronisation job from Staged to Online.",
                "INSERT_UPDATE CatalogVersionSyncJob;code[unique=true];sourceVersion(catalog(id),version);targetVersion(catalog(id),version);createNewItems[default=true];copyAllTranslations[default=true]\n" +
                ";{{jobCode}};{{catalogId}}:Staged;{{catalogId}}:Online;true;true"),
        ];

        // ── Category badge colours ────────────────────────────────────────
        private static Windows.UI.Color CategoryColor(string cat) => cat switch
        {
            "Products"   => Windows.UI.Color.FromArgb(255, 34, 197, 94),
            "Customers"  => Windows.UI.Color.FromArgb(255, 96, 165, 250),
            "Promotions" => Windows.UI.Color.FromArgb(255, 251, 191, 36),
            "Stock"      => Windows.UI.Color.FromArgb(255, 248, 113, 113),
            "Catalog"    => Windows.UI.Color.FromArgb(255, 167, 139, 250),
            _            => Windows.UI.Color.FromArgb(255, 107, 114, 128),
        };

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
                HideImpExPanel();
                LoadGroup(_selectedGroup);
            }
        }

        private void LoadGroup(TestDataGroup group)
        {
            DataEmptyState.Visibility = Visibility.Collapsed;
            DataEditorPanel.Visibility = Visibility.Visible;

            GroupNameBox.Text = group.Name;

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
                EntriesContainer.Children.Add(BuildEntryCard(entry, group));

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

            var outer = new StackPanel { Spacing = 6 };

            // ── Row 1: Key | Value | copy | delete ──────────────────
            var row1 = new Grid();
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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
            row1.Children.Add(keyBox);
            row1.Children.Add(valueBox);
            row1.Children.Add(btnPanel);

            // ── Row 2: Description | Tags | Environment ──────────────
            var row2 = new Grid();
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            var descBox = new TextBox
            {
                Text = entry.Description,
                PlaceholderText = "Description (optional)",
                FontSize = 11,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            descBox.TextChanged += (s, _) => entry.Description = descBox.Text;

            var tagsBox = new TextBox
            {
                Text = entry.Tags,
                PlaceholderText = "Tags (b2b,staging)",
                FontSize = 11,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            tagsBox.TextChanged += (s, _) => entry.Tags = tagsBox.Text;

            var envPicker = new ComboBox
            {
                FontSize = 11,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 19)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            foreach (var env in new[] { "All", "Staging", "Production", "B2B", "B2C", "Dev" })
                envPicker.Items.Add(new ComboBoxItem { Content = env });

            var envMatch = envPicker.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Content?.ToString() == entry.Environment);
            envPicker.SelectedItem = envMatch ?? envPicker.Items[0];
            envPicker.SelectionChanged += (s, _) =>
            {
                entry.Environment = (envPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            };

            Grid.SetColumn(descBox, 0);
            Grid.SetColumn(tagsBox, 1);
            Grid.SetColumn(envPicker, 2);
            row2.Children.Add(descBox);
            row2.Children.Add(tagsBox);
            row2.Children.Add(envPicker);

            outer.Children.Add(row1);
            outer.Children.Add(row2);
            border.Child = outer;
            return border;
        }

        // ── Group CRUD ────────────────────────────────────────────────────
        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            var g = new TestDataGroup { Name = "New Group", Category = "Other" };
            _vm.SelectedProject.TestDataGroups.Add(g);
            _ = _vm.SaveAsync();
            _selectedGroup = g;
            HideImpExPanel();
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

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null || _selectedGroup.Entries.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var en in _selectedGroup.Entries)
                sb.AppendLine($"{en.Key}={en.Value}");
            var dp = new DataPackage();
            dp.SetText(sb.ToString().TrimEnd());
            Clipboard.SetContent(dp);
            DataStatusText.Text = $"Copied {_selectedGroup.Entries.Count} entries to clipboard.";
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

        // ── ImpEx Templates ───────────────────────────────────────────────
        private void ShowImpEx_Click(object sender, RoutedEventArgs e)
        {
            GroupList.SelectedItem = null;
            _selectedGroup = null;

            DataEmptyState.Visibility = Visibility.Collapsed;
            DataEditorPanel.Visibility = Visibility.Collapsed;
            ImpExPanel.Visibility = Visibility.Visible;

            _impExFilter = "All";
            UpdateFilterButtons();
            RenderImpExTemplates();
        }

        private void HideImpEx_Click(object sender, RoutedEventArgs e) => HideImpExPanel();

        private void HideImpExPanel()
        {
            ImpExPanel.Visibility = Visibility.Collapsed;
            if (_selectedGroup != null)
                DataEditorPanel.Visibility = Visibility.Visible;
            else
                DataEmptyState.Visibility = Visibility.Visible;
        }

        private void ImpExFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _impExFilter = btn.Tag?.ToString() ?? "All";
                UpdateFilterButtons();
                RenderImpExTemplates();
            }
        }

        private void UpdateFilterButtons()
        {
            var filterButtons = new[]
            {
                (ImpExFilterAll, "All"),
                (ImpExFilterProducts, "Products"),
                (ImpExFilterCustomers, "Customers"),
                (ImpExFilterPromotions, "Promotions"),
                (ImpExFilterStock, "Stock"),
                (ImpExFilterCatalog, "Catalog"),
            };

            foreach (var (btn, tag) in filterButtons)
            {
                bool active = tag == _impExFilter;
                btn.Background = active
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 42));
                btn.Foreground = active
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184));
            }
        }

        private void RenderImpExTemplates()
        {
            ImpExContainer.Children.Clear();

            var filtered = _impExFilter == "All"
                ? s_impExTemplates
                : s_impExTemplates.Where(t => t.Category == _impExFilter).ToList();

            foreach (var tpl in filtered)
                ImpExContainer.Children.Add(BuildImpExCard(tpl));
        }

        private UIElement BuildImpExCard(ImpExTemplate tpl)
        {
            var catColor = CategoryColor(tpl.Category);

            var border = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 22, 34)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 14)
            };

            var stack = new StackPanel { Spacing = 8 };

            // Header row: name + category badge + copy button
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            var catBadge = new Border
            {
                Background = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(40, catColor.R, catColor.G, catColor.B)),
                BorderBrush = new SolidColorBrush(catColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2)
            };
            catBadge.Child = new TextBlock
            {
                Text = tpl.Category,
                FontSize = 10,
                Foreground = new SolidColorBrush(catColor),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };

            var nameBlock = new TextBlock
            {
                Text = tpl.Name,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                VerticalAlignment = VerticalAlignment.Center
            };

            namePanel.Children.Add(catBadge);
            namePanel.Children.Add(nameBlock);

            var copyBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8C8", FontSize = 12, FontFamily = new FontFamily("Segoe Fluent Icons") },
                        new TextBlock { Text = "Copy", FontSize = 12 }
                    }
                },
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 46)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 5, 12, 5)
            };
            copyBtn.Click += (s, e) =>
            {
                var dp = new DataPackage();
                dp.SetText(tpl.Code);
                Clipboard.SetContent(dp);
            };

            Grid.SetColumn(namePanel, 0);
            Grid.SetColumn(copyBtn, 1);
            headerGrid.Children.Add(namePanel);
            headerGrid.Children.Add(copyBtn);

            // Description
            var descBlock = new TextBlock
            {
                Text = tpl.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                TextWrapping = TextWrapping.Wrap
            };

            // Code viewer
            var codeViewer = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 10, 16)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1)
            };
            codeViewer.Child = new TextBlock
            {
                Text = tpl.Code,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 134, 239, 172)),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };

            stack.Children.Add(headerGrid);
            stack.Children.Add(descBlock);
            stack.Children.Add(codeViewer);
            border.Child = stack;
            return border;
        }
    }
}
