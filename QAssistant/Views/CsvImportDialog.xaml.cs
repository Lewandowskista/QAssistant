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
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QAssistant.Helpers;
using QAssistant.Services;
using Windows.UI;

namespace QAssistant.Views
{
    public sealed partial class CsvImportDialog : ContentDialog
    {
        private readonly ParsedCsvData _data;
        private readonly List<(string Header, ComboBox Combo)> _mappingRows = [];

        public string PlanName => PlanNameBox.Text.Trim();

        public Dictionary<string, string> ColumnMapping =>
            _mappingRows.ToDictionary(
                r => r.Header,
                r => (r.Combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "(Ignore)");

        public CsvImportDialog(ParsedCsvData data, string fileName)
        {
            this.InitializeComponent();

            _data = data;

            FileNameText.Text = fileName;
            RowCountText.Text = $"{data.Rows.Count} row(s) detected · {data.Headers.Count} column(s)";
            PlanNameBox.Text = $"CSV Import · {DateTime.Now:MMM d, yyyy}";
            PrimaryButtonText = $"Import {data.Rows.Count} row(s)";

            BuildMappingRows();
            BuildPreview();

            DialogHelper.ApplyDarkTheme(this);
        }

        // ── Column mapping rows ──────────────────────────────────────────────

        private void BuildMappingRows()
        {
            var autoDetect = CsvImportService.AutoDetectMappings(_data.Headers);

            foreach (var header in _data.Headers)
            {
                if (string.IsNullOrWhiteSpace(header)) continue;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // CSV column name
                var label = new TextBlock
                {
                    Text = header,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 226, 232, 240)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(4, 0, 4, 0)
                };
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                // Arrow
                var arrow = new FontIcon
                {
                    Glyph = "\uE76C",
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 107, 114, 128)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(arrow, 1);
                row.Children.Add(arrow);

                // TC field picker
                var combo = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(Color.FromArgb(255, 37, 37, 53)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 226, 232, 240)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 42, 42, 58)),
                    CornerRadius = new CornerRadius(6),
                    FontSize = 12,
                    Margin = new Thickness(4, 0, 0, 0)
                };

                int selectedIdx = 0;
                for (int i = 0; i < CsvImportService.TestCaseFields.Count; i++)
                {
                    var (field, display) = CsvImportService.TestCaseFields[i];
                    combo.Items.Add(new ComboBoxItem { Content = display, Tag = field });
                    if (autoDetect.TryGetValue(header, out var detected) && detected == field)
                        selectedIdx = i;
                }
                combo.SelectedIndex = selectedIdx;

                Grid.SetColumn(combo, 2);
                row.Children.Add(combo);

                MappingContainer.Children.Add(row);
                _mappingRows.Add((header, combo));
            }
        }

        // ── Preview rows ─────────────────────────────────────────────────────

        private void BuildPreview()
        {
            var previewRows = _data.Rows.Take(3).ToList();
            if (previewRows.Count == 0) return;

            // Show at most 5 columns to keep the preview compact.
            var previewHeaders = _data.Headers.Take(5).ToList();

            foreach (var row in previewRows)
            {
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 19, 19, 30)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 42, 42, 58)),
                    BorderThickness = new Thickness(1)
                };

                var rowStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

                foreach (var header in previewHeaders)
                {
                    var val = row.TryGetValue(header, out var v) ? v : string.Empty;

                    var colStack = new StackPanel { MaxWidth = 110 };
                    colStack.Children.Add(new TextBlock
                    {
                        Text = header,
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 107, 114, 128)),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    colStack.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(val) ? "—" : val,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 226, 232, 240)),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxLines = 2
                    });

                    rowStack.Children.Add(colStack);
                }

                if (_data.Headers.Count > 5)
                {
                    rowStack.Children.Add(new TextBlock
                    {
                        Text = $"+{_data.Headers.Count - 5} more",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 107, 114, 128)),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                card.Child = rowStack;
                PreviewContainer.Children.Add(card);
            }
        }
    }
}
