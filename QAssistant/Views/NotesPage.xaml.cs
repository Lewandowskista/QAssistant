using QAssistant.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using QAssistant.Helpers;
using QAssistant.Models;
using QAssistant.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace QAssistant.Views
{
    public sealed partial class NotesPage : Page
    {
        private MainViewModel? _vm;
        private Note? _activeNote;
        private bool _isLoading = false;
        private CancellationTokenSource? _saveCts;
        private Note? _clipboardNote;
        private bool _isCut;
        private readonly FileStorageService _fileService = new();

        public NotesPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                RefreshNotes();
            }
        }

        private void RefreshNotes()
        {
            if (_vm?.SelectedProject == null) return;
            NotesList.ItemsSource = null;
            NotesList.ItemsSource = _vm.SelectedProject.Notes;
            if (NotesList.Items.Count > 0)
                NotesList.SelectedIndex = 0;
            else
                ShowEmptyState();
        }

        private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NotesList.SelectedItem is Note note)
                LoadNote(note);
        }

        private void LoadNote(Note note)
        {
            _isLoading = true;
            _activeNote = note;
            NoteTitleBox.Text = note.Title;
            NoteContentBox.Text = note.Content;
            LastSavedText.Text = $"Last saved: {note.UpdatedAt:MMM d, yyyy h:mm tt}";
            EmptyState.Visibility = Visibility.Collapsed;
            EditorPanel.Visibility = Visibility.Visible;
            RefreshAttachments();
            _isLoading = false;
        }

        private void ShowEmptyState()
        {
            EmptyState.Visibility = Visibility.Visible;
            EditorPanel.Visibility = Visibility.Collapsed;
            _activeNote = null;
        }

        private async void NoteTitle_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || _activeNote == null) return;
            _activeNote.Title = NoteTitleBox.Text;
            _activeNote.UpdatedAt = DateTime.Now;
            LastSavedText.Text = $"Last saved: {_activeNote.UpdatedAt:MMM d, yyyy h:mm tt}";
            NotesList.ItemsSource = null;
            NotesList.ItemsSource = _vm?.SelectedProject?.Notes;
            NotesList.SelectedItem = _activeNote;
            await DebouncedSaveAsync();
        }

        private async void NoteContent_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || _activeNote == null) return;
            _activeNote.Content = NoteContentBox.Text;
            _activeNote.UpdatedAt = DateTime.Now;
            LastSavedText.Text = $"Last saved: {_activeNote.UpdatedAt:MMM d, yyyy h:mm tt}";
            await DebouncedSaveAsync();
        }

        private async System.Threading.Tasks.Task DebouncedSaveAsync()
        {
            _saveCts?.Cancel();
            _saveCts = new CancellationTokenSource();
            var token = _saveCts.Token;
            try
            {
                await System.Threading.Tasks.Task.Delay(500, token);
                if (!token.IsCancellationRequested && _vm != null)
                    await _vm.SaveAsync();
            }
            catch (OperationCanceledException) { }
        }

        private async void AddNote_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            var note = new Note { Title = "New Note", Content = string.Empty };
            _vm.SelectedProject.Notes.Add(note);
            await _vm.SaveAsync();
            RefreshNotes();
            NotesList.SelectedItem = note;
        }

        private async void NotesList_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            var reordered = NotesList.Items.OfType<Note>().ToList();
            _vm.SelectedProject.Notes.Clear();
            foreach (var note in reordered)
                _vm.SelectedProject.Notes.Add(note);
            await _vm.SaveAsync();

            // Rebind to reflect new order
            NotesList.ItemsSource = null;
            NotesList.ItemsSource = _vm.SelectedProject.Notes;
        }

        private async void NotesList_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (!ctrl) return;

            if (e.Key == VirtualKey.C && NotesList.SelectedItem is Note copyNote)
            {
                _clipboardNote = new Note
                {
                    Title = copyNote.Title,
                    Content = copyNote.Content,
                    Attachments = copyNote.Attachments.Select(a => new FileAttachment
                    {
                        FileName = a.FileName,
                        FilePath = a.FilePath,
                        MimeType = a.MimeType,
                        FileSizeBytes = a.FileSizeBytes,
                        Scope = AttachmentScope.Note
                    }).ToList()
                };
                _isCut = false;
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.X && NotesList.SelectedItem is Note cutNote)
            {
                _clipboardNote = new Note
                {
                    Title = cutNote.Title,
                    Content = cutNote.Content,
                    Attachments = new List<FileAttachment>(cutNote.Attachments)
                };
                _isCut = true;
                _vm?.SelectedProject?.Notes.Remove(cutNote);
                await _vm!.SaveAsync();
                RefreshNotes();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.V && _clipboardNote != null && _vm?.SelectedProject != null)
            {
                var newNote = new Note
                {
                    Title = _isCut ? _clipboardNote.Title : $"{_clipboardNote.Title} (Copy)",
                    Content = _clipboardNote.Content,
                    Attachments = _clipboardNote.Attachments.Select(a => new FileAttachment
                    {
                        FileName = a.FileName,
                        FilePath = a.FilePath,
                        MimeType = a.MimeType,
                        FileSizeBytes = a.FileSizeBytes,
                        Scope = AttachmentScope.Note,
                        NoteId = null
                    }).ToList()
                };
                newNote.Attachments.ForEach(a => a.NoteId = newNote.Id);
                _vm.SelectedProject.Notes.Add(newNote);
                await _vm.SaveAsync();
                RefreshNotes();
                NotesList.SelectedItem = newNote;

                if (_isCut)
                    _clipboardNote = null;

                e.Handled = true;
            }
        }

        private async void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null || _activeNote == null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Note",
                Content = $"Are you sure you want to delete \"{_activeNote.Title}\"?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                foreach (var attachment in _activeNote.Attachments)
                    _fileService.DeleteFile(attachment);

                _vm.SelectedProject.Notes.Remove(_activeNote);
                await _vm.SaveAsync();
                RefreshNotes();
            }
        }

        #region Attachments

        private void RefreshAttachments()
        {
            DisposeMediaPlayers(AttachmentItems);
            AttachmentItems.Children.Clear();
            if (_activeNote == null) return;

            foreach (var attachment in _activeNote.Attachments)
                AttachmentItems.Children.Add(BuildAttachmentUI(attachment));
        }

        private static void DisposeMediaPlayers(UIElement element)
        {
            if (element is MediaPlayerElement mpe)
            {
                mpe.MediaPlayer?.Dispose();
                mpe.Source = null;
            }
            else if (element is Panel panel)
            {
                foreach (var child in panel.Children)
                    DisposeMediaPlayers(child);
            }
            else if (element is Border border && border.Child != null)
            {
                DisposeMediaPlayers(border.Child);
            }
        }

        private UIElement BuildAttachmentUI(FileAttachment file)
        {
            if (file.IsImage && File.Exists(file.FilePath))
                return BuildImageAttachment(file);
            if ((file.IsVideo || file.IsAudio) && File.Exists(file.FilePath))
                return BuildMediaAttachment(file);
            return BuildGenericAttachment(file);
        }

        private UIElement BuildImageAttachment(FileAttachment file)
        {
            var container = CreateAttachmentBorder();
            var stack = new StackPanel { Spacing = 6 };

            var image = new Image
            {
                Source = new BitmapImage(new Uri(file.FilePath)),
                MaxHeight = 300,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            image.Tapped += async (s, e) => await ShowImagePreviewAsync(file);
            stack.Children.Add(image);
            stack.Children.Add(BuildAttachmentFooter(file));

            container.Child = stack;
            return container;
        }

        private UIElement BuildMediaAttachment(FileAttachment file)
        {
            var container = CreateAttachmentBorder();
            var stack = new StackPanel { Spacing = 6 };

            var player = new MediaPlayerElement
            {
                Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(file.FilePath)),
                AutoPlay = false,
                AreTransportControlsEnabled = true,
                MaxHeight = file.IsAudio ? 48 : 300,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            player.TransportControls.IsCompact = true;
            stack.Children.Add(player);
            stack.Children.Add(BuildAttachmentFooter(file));

            container.Child = stack;
            return container;
        }

        private UIElement BuildGenericAttachment(FileAttachment file)
        {
            var container = CreateAttachmentBorder();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = GetFileIcon(file.MimeType),
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var info = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            info.Children.Add(new TextBlock
            {
                Text = file.FileName,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontSize = 12
            });
            info.Children.Add(new TextBlock
            {
                Text = $"{file.FileSizeDisplay} · {file.AddedAt:MMM d, yyyy}",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 11
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnPanel.Children.Add(CreateOpenButton(file));
            btnPanel.Children.Add(CreateDeleteButton(file));
            Grid.SetColumn(btnPanel, 2);
            grid.Children.Add(btnPanel);

            container.Child = grid;
            return container;
        }

        private UIElement BuildAttachmentFooter(FileAttachment file)
        {
            var grid = new Grid();
            var info = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            info.Children.Add(new TextBlock
            {
                Text = file.FileName,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                FontSize = 11
            });
            info.Children.Add(new TextBlock
            {
                Text = file.FileSizeDisplay,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 11
            });
            grid.Children.Add(info);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 6
            };
            btnPanel.Children.Add(CreateOpenButton(file));
            btnPanel.Children.Add(CreateDeleteButton(file));
            grid.Children.Add(btnPanel);

            return grid;
        }

        private static Border CreateAttachmentBorder() => new()
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 26, 26, 36)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 4)
        };

        private Button CreateOpenButton(FileAttachment file)
        {
            var btn = new Button
            {
                Content = "Open",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 167, 139, 250)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3)
            };
            btn.Click += async (s, e) => await _fileService.OpenFileAsync(file);
            return btn;
        }

        private Button CreateDeleteButton(FileAttachment file)
        {
            var btn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons") },
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 63, 26, 26)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3)
            };
            btn.Click += async (s, e) =>
            {
                _fileService.DeleteFile(file);
                _activeNote?.Attachments.Remove(file);
                if (_vm != null) await _vm.SaveAsync();
                RefreshAttachments();
            };
            return btn;
        }

        private static string GetFileIcon(string mimeType) => mimeType switch
        {
            var m when m.StartsWith("image/") => "🖼",
            var m when m.StartsWith("video/") => "🎬",
            var m when m.StartsWith("audio/") => "🎵",
            "application/pdf" => "📄",
            var m when m.Contains("word") => "📝",
            var m when m.Contains("sheet") => "📊",
            "text/plain" => "📃",
            "application/zip" => "🗜",
            _ => "📎"
        };

        private async System.Threading.Tasks.Task ShowImagePreviewAsync(FileAttachment file)
        {
            var image = new Image
            {
                Source = new BitmapImage(new Uri(file.FilePath)),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                MaxWidth = 800,
                MaxHeight = 600
            };

            var dialog = new ContentDialog
            {
                Title = file.FileName,
                Content = new ScrollViewer
                {
                    Content = image,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);
            await dialog.ShowAsync();
        }

        private async void AttachFile_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null || _activeNote == null) return;

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            foreach (var file in files)
            {
                var attachment = await _fileService.SaveFileAsync(
                    file.Path, AttachmentScope.Note, _activeNote.Id);
                if (attachment != null)
                    _activeNote.Attachments.Add(attachment);
            }

            _activeNote.UpdatedAt = DateTime.Now;
            await _vm.SaveAsync();
            RefreshAttachments();
            LastSavedText.Text = $"Last saved: {_activeNote.UpdatedAt:MMM d, yyyy h:mm tt}";
        }

        private async void PasteMedia_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null || _activeNote == null) return;

            var clipboard = Clipboard.GetContent();
            if (clipboard.Contains(StandardDataFormats.Bitmap))
            {
                try
                {
                    var bitmapRef = await clipboard.GetBitmapAsync();
                    using var stream = await bitmapRef.OpenReadAsync();

                    var fileName = $"paste_{DateTime.Now:yyyyMMdd_HHmmss}.png";
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
                        MimeType = "image/png",
                        FileSizeBytes = new FileInfo(destPath).Length,
                        Scope = AttachmentScope.Note,
                        NoteId = _activeNote.Id
                    };

                    _activeNote.Attachments.Add(attachment);
                    _activeNote.UpdatedAt = DateTime.Now;
                    await _vm.SaveAsync();
                    RefreshAttachments();
                    LastSavedText.Text = $"Last saved: {_activeNote.UpdatedAt:MMM d, yyyy h:mm tt}";
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
                    DialogHelper.ApplyDarkTheme(dialog);
                    await dialog.ShowAsync();
                }
            }
            else if (clipboard.Contains(StandardDataFormats.StorageItems))
            {
                var items = await clipboard.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is Windows.Storage.StorageFile file)
                    {
                        var attachment = await _fileService.SaveFileAsync(
                            file.Path, AttachmentScope.Note, _activeNote.Id);
                        if (attachment != null)
                            _activeNote.Attachments.Add(attachment);
                    }
                }

                _activeNote.UpdatedAt = DateTime.Now;
                await _vm.SaveAsync();
                RefreshAttachments();
                LastSavedText.Text = $"Last saved: {_activeNote.UpdatedAt:MMM d, yyyy h:mm tt}";
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = "Nothing to paste",
                    Content = "Copy an image or file to your clipboard first.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                DialogHelper.ApplyDarkTheme(dialog);
                await dialog.ShowAsync();
            }
        }

        #endregion
    }
}