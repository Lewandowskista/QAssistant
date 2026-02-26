using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using QAssistant.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using QAssistant.Models;
using QAssistant.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QAssistant.Views
{
    public sealed partial class FilesPage : Page
    {
        private MainViewModel? _vm;
        private readonly FileStorageService _fileService = new();

        public FilesPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                RefreshFiles();
            }
        }

        private void RefreshFiles()
        {
            if (_vm?.SelectedProject == null) return;
            var files = _vm.SelectedProject.Attachments;
            EmptyState.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FilesList.Items.Clear();
            foreach (var file in files.OrderByDescending(f => f.AddedAt))
                FilesList.Items.Add(BuildFileCard(file));
        }

        private UIElement BuildFileCard(FileAttachment file)
        {
            var border = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 42, 42, 58)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4),
                Tag = file
            };

            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            // Thumbnail or icon
            if (file.IsImage && File.Exists(file.FilePath))
            {
                var img = new Image
                {
                    Width = 40,
                    Height = 40,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
                };
                img.Source = new BitmapImage(new Uri(file.FilePath));
                Grid.SetColumn(img, 0);
                innerGrid.Children.Add(img);
            }
            else
            {
                var icon = new TextBlock
                {
                    Text = GetFileIcon(file.MimeType),
                    FontSize = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(icon, 0);
                innerGrid.Children.Add(icon);
            }

            // File info
            var info = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            info.Children.Add(new TextBlock
            {
                Text = file.FileName,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontSize = 13,
                TextWrapping = TextWrapping.NoWrap
            });
            info.Children.Add(new TextBlock
            {
                Text = $"{file.FileSizeDisplay} · {file.AddedAt:MMM d, yyyy}",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 11
            });
            Grid.SetColumn(info, 1);
            innerGrid.Children.Add(info);

            // Buttons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            var openBtn = new Button
            {
                Content = "Open",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4)
            };
            openBtn.Click += async (s, e) => await _fileService.OpenFileAsync(file);

            var deleteBtn = new Button
            {
                Content = "Delete",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 63, 26, 26)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4)
            };
            deleteBtn.Click += async (s, e) =>
            {
                _fileService.DeleteFile(file);
                _vm?.SelectedProject?.Attachments.Remove(file);
                if (_vm != null) await _vm.SaveAsync();
                RefreshFiles();
            };

            btnPanel.Children.Add(openBtn);
            btnPanel.Children.Add(deleteBtn);
            Grid.SetColumn(btnPanel, 2);
            innerGrid.Children.Add(btnPanel);

            border.Child = innerGrid;
            return border;
        }

        private static string GetFileIcon(string mimeType) => mimeType switch
        {
            var m when m.StartsWith("image/") => "🖼",
            "application/pdf" => "📄",
            var m when m.Contains("word") => "📝",
            var m when m.Contains("sheet") => "📊",
            "text/plain" => "📃",
            "application/zip" => "🗜",
            _ => "📎"
        };

        private async void BrowseFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var attachment = await _fileService.SaveFileAsync(file.Path, AttachmentScope.Project);
            if (attachment != null)
            {
                _vm.SelectedProject.Attachments.Add(attachment);
                await _vm.SaveAsync();
                RefreshFiles();
            }
        }

        private async void PasteScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            var clipboard = Clipboard.GetContent();
            if (!clipboard.Contains(StandardDataFormats.Bitmap))
            {
                var dialog = new ContentDialog
                {
                    Title = "No screenshot found",
                    Content = "Copy an image to your clipboard first (e.g. Win+Shift+S)",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            try
            {
                var bitmapRef = await clipboard.GetBitmapAsync();
                using var stream = await bitmapRef.OpenReadAsync();

                var fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.bmp";
                var destPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "QAssistant", "Files", fileName);

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using var fileStream = File.Create(destPath);
                await stream.AsStreamForRead().CopyToAsync(fileStream);

                var attachment = new FileAttachment
                {
                    FileName = fileName,
                    FilePath = destPath,
                    MimeType = "image/bmp",
                    FileSizeBytes = new FileInfo(destPath).Length,
                    Scope = AttachmentScope.Project
                };

                _vm.SelectedProject.Attachments.Add(attachment);
                await _vm.SaveAsync();
                RefreshFiles();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Paste failed",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop to attach";
            e.DragUIOverride.IsGlyphVisible = true;
        }

        private void Page_DragLeave(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is StorageFile file)
                    {
                        var attachment = await _fileService.SaveFileAsync(
                            file.Path, AttachmentScope.Project);
                        if (attachment != null)
                            _vm.SelectedProject.Attachments.Add(attachment);
                    }
                }
                await _vm.SaveAsync();
                RefreshFiles();
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async void FilesList_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            var reordered = FilesList.Items
                .OfType<Border>()
                .Select(b => b.Tag as FileAttachment)
                .Where(f => f != null)
                .ToList();
            if (reordered.Count == 0) return; // Safety: don't clear if extraction failed
            _vm.SelectedProject.Attachments.Clear();
            foreach (var file in reordered)
                _vm.SelectedProject.Attachments.Add(file!);
            await _vm.SaveAsync();
        }
    }
}