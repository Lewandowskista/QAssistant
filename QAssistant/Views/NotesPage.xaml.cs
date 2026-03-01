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
    internal sealed class RunbookListItem
    {
        public Runbook Runbook { get; }
        public string Title => Runbook.Title;
        public string CategoryLabel => Runbook.Category switch
        {
            RunbookCategory.GoLive => "Go-Live",
            _ => Runbook.Category.ToString()
        };
        public string StepsLabel
        {
            get
            {
                int total = Runbook.Steps.Count;
                if (total == 0) return "No steps";
                int done = Runbook.Steps.Count(s => s.Status is RunbookStepStatus.Done or RunbookStepStatus.Skipped);
                return $"{done}/{total} done";
            }
        }
        public string UpdatedAtLabel => Runbook.UpdatedAt.ToString("MMM d, yyyy");
        public RunbookListItem(Runbook r) => Runbook = r;
    }

    public sealed partial class NotesPage : Page
    {
        private MainViewModel? _vm;
        private Note? _activeNote;
        private Runbook? _activeRunbook;
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
                Text = $"{file.FileSizeDisplay} Â· {file.AddedAt:MMM d, yyyy}",
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
            var m when m.StartsWith("image/") => "ðŸ–¼",
            var m when m.StartsWith("video/") => "ðŸŽ¬",
            var m when m.StartsWith("audio/") => "ðŸŽµ",
            "application/pdf" => "ðŸ“„",
            var m when m.Contains("word") => "ðŸ“",
            var m when m.Contains("sheet") => "ðŸ“Š",
            "text/plain" => "ðŸ“ƒ",
            "application/zip" => "ðŸ—œ",
            _ => "ðŸ“Ž"
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

        #region Tab switching

        private void UpdateTabStyles(bool notesActive)
        {
            var accent = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
            var hover = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["HoverBrush"];
            var textPrimary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextPrimaryBrush"];
            var textSecondary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextSecondaryBrush"];
            NotesTabBtn.Background = notesActive ? accent : hover;
            NotesTabBtn.Foreground = notesActive ? textPrimary : textSecondary;
            RunbooksTabBtn.Background = notesActive ? hover : accent;
            RunbooksTabBtn.Foreground = notesActive ? textSecondary : textPrimary;
        }

        private void NotesTab_Click(object sender, RoutedEventArgs e)
        {
            if (NotesPanel == null || RunbooksPanel == null) return;
            UpdateTabStyles(notesActive: true);
            NotesPanel.Visibility = Visibility.Visible;
            RunbooksPanel.Visibility = Visibility.Collapsed;
            NotesEditorArea.Visibility = Visibility.Visible;
            RunbooksEditorArea.Visibility = Visibility.Collapsed;
        }

        private void RunbooksTab_Click(object sender, RoutedEventArgs e)
        {
            if (NotesPanel == null || RunbooksPanel == null) return;
            UpdateTabStyles(notesActive: false);
            RunbooksPanel.Visibility = Visibility.Visible;
            NotesPanel.Visibility = Visibility.Collapsed;
            RunbooksEditorArea.Visibility = Visibility.Visible;
            NotesEditorArea.Visibility = Visibility.Collapsed;
            if (_vm != null)
                RefreshRunbooks();
        }

        #endregion

        #region Runbooks

        private void RefreshRunbooks()
        {
            if (_vm?.SelectedProject == null) return;
            RefreshRunbooksList();
            if (_vm.SelectedProject.Runbooks.Count > 0 && RunbooksList.SelectedIndex < 0)
                RunbooksList.SelectedIndex = 0;
            else if (_vm.SelectedProject.Runbooks.Count == 0)
                ShowRunbookEmptyState();
        }

        private void RefreshRunbooksList()
        {
            if (_vm?.SelectedProject == null) return;
            var items = _vm.SelectedProject.Runbooks.Select(r => new RunbookListItem(r)).ToList();
            RunbooksList.ItemsSource = null;
            RunbooksList.ItemsSource = items;
            if (_activeRunbook != null)
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Runbook.Id == _activeRunbook.Id) { RunbooksList.SelectedIndex = i; break; }
        }

        private void RunbooksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (RunbooksList.SelectedItem is RunbookListItem item)
                LoadRunbook(item.Runbook);
        }

        private void LoadRunbook(Runbook runbook)
        {
            _isLoading = true;
            _activeRunbook = runbook;
            RunbookTitleBox.Text = runbook.Title;
            RunbookDescBox.Text = runbook.Description;
            RunbookCategoryBox.SelectedIndex = (int)runbook.Category;
            RunbookCategoryLabel.Text = GetCategoryLabel(runbook.Category);
            RunbookLastSavedText.Text = $"Last saved: {runbook.UpdatedAt:MMM d, yyyy h:mm tt}";
            RunbookEmptyState.Visibility = Visibility.Collapsed;
            RunbookEditorPanel.Visibility = Visibility.Visible;
            RefreshSteps();
            _isLoading = false;
        }

        private void ShowRunbookEmptyState()
        {
            RunbookEmptyState.Visibility = Visibility.Visible;
            RunbookEditorPanel.Visibility = Visibility.Collapsed;
            _activeRunbook = null;
        }

        private static string GetCategoryLabel(RunbookCategory category) => category switch
        {
            RunbookCategory.GoLive => "Go-Live",
            _ => category.ToString()
        };

        private async void AddRunbook_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            var runbook = new Runbook { Title = "New Runbook" };
            _vm.SelectedProject.Runbooks.Add(runbook);
            await _vm.SaveAsync();
            RefreshRunbooksList();
            RunbooksList.SelectedItem = RunbooksList.Items.OfType<RunbookListItem>()
                .FirstOrDefault(i => i.Runbook.Id == runbook.Id);
        }

        private async void AddRunbookStep_Click(object sender, RoutedEventArgs e)
        {
            if (_activeRunbook == null) return;
            var step = new RunbookStep { Title = $"Step {_activeRunbook.Steps.Count + 1}" };
            _activeRunbook.Steps.Add(step);
            _activeRunbook.UpdatedAt = DateTime.Now;
            RefreshSteps();
            RunbookLastSavedText.Text = $"Last saved: {_activeRunbook.UpdatedAt:MMM d, yyyy h:mm tt}";
            await DebouncedSaveAsync();
        }

        private async void ResetRunbookSteps_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null || _activeRunbook == null) return;
            var dialog = new ContentDialog
            {
                Title = "Reset Steps",
                Content = "Set all steps back to Pending?",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                foreach (var step in _activeRunbook.Steps)
                    step.Status = RunbookStepStatus.Pending;
                _activeRunbook.UpdatedAt = DateTime.Now;
                await _vm.SaveAsync();
                RefreshSteps();
                RunbookLastSavedText.Text = $"Last saved: {_activeRunbook.UpdatedAt:MMM d, yyyy h:mm tt}";
            }
        }

        private async void DeleteRunbook_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null || _activeRunbook == null) return;
            var dialog = new ContentDialog
            {
                Title = "Delete Runbook",
                Content = $"Are you sure you want to delete \"{_activeRunbook.Title}\"?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            DialogHelper.ApplyDarkTheme(dialog);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _vm.SelectedProject.Runbooks.Remove(_activeRunbook);
                _activeRunbook = null;
                await _vm.SaveAsync();
                RefreshRunbooks();
            }
        }

        private async void RunbookTitle_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || _activeRunbook == null) return;
            _activeRunbook.Title = RunbookTitleBox.Text;
            _activeRunbook.UpdatedAt = DateTime.Now;
            RunbookLastSavedText.Text = $"Last saved: {_activeRunbook.UpdatedAt:MMM d, yyyy h:mm tt}";
            RefreshRunbooksList();
            await DebouncedSaveAsync();
        }

        private async void RunbookDesc_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || _activeRunbook == null) return;
            _activeRunbook.Description = RunbookDescBox.Text;
            _activeRunbook.UpdatedAt = DateTime.Now;
            RunbookLastSavedText.Text = $"Last saved: {_activeRunbook.UpdatedAt:MMM d, yyyy h:mm tt}";
            await DebouncedSaveAsync();
        }

        private async void RunbookCategory_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || _activeRunbook == null) return;
            if (RunbookCategoryBox.SelectedItem is ComboBoxItem item)
            {
                _activeRunbook.Category = Enum.Parse<RunbookCategory>(item.Tag!.ToString()!);
                RunbookCategoryLabel.Text = GetCategoryLabel(_activeRunbook.Category);
                _activeRunbook.UpdatedAt = DateTime.Now;
                await DebouncedSaveAsync();
            }
        }

        private void RefreshSteps()
        {
            StepItems.Children.Clear();
            if (_activeRunbook == null) return;
            for (int i = 0; i < _activeRunbook.Steps.Count; i++)
                StepItems.Children.Add(BuildStepUI(_activeRunbook.Steps[i], i));
            UpdateRunbookProgress();
        }

        private void UpdateRunbookProgress()
        {
            if (_activeRunbook == null) return;
            int total = _activeRunbook.Steps.Count;
            int done = _activeRunbook.Steps.Count(s => s.Status is RunbookStepStatus.Done or RunbookStepStatus.Skipped);
            RunbookProgressText.Text = total == 0 ? "No steps yet" : $"{done}/{total} steps done";
            RunbookProgressBar.Value = total == 0 ? 0 : (double)done / total * 100;
            RefreshRunbooksList();
        }

        private UIElement BuildStepUI(RunbookStep step, int index)
        {
            var statusColor = GetStepStatusColor(step.Status);

            var container = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 26, 26, 36)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Step number badge
            var badgeBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(statusColor);
            var badge = new Border
            {
                Width = 26, Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = badgeBrush,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = (index + 1).ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 255, 255, 255))
                }
            };
            Grid.SetColumn(badge, 0);
            outerGrid.Children.Add(badge);

            // Content column
            var content = new StackPanel { Spacing = 6, Margin = new Thickness(10, 0, 10, 0) };

            // Title row: title textbox + status combo
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBox = new TextBox
            {
                Text = step.Title,
                PlaceholderText = "Step title...",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleBox.TextChanged += async (s, e) =>
            {
                step.Title = titleBox.Text;
                _activeRunbook!.UpdatedAt = DateTime.Now;
                RunbookLastSavedText.Text = $"Last saved: {_activeRunbook.UpdatedAt:MMM d, yyyy h:mm tt}";
                await DebouncedSaveAsync();
            };
            Grid.SetColumn(titleBox, 0);
            titleRow.Children.Add(titleBox);

            var statusComboForeground = new Microsoft.UI.Xaml.Media.SolidColorBrush(statusColor);
            var statusCombo = new ComboBox
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                Foreground = statusComboForeground,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 110
            };
            // Items must match RunbookStepStatus enum order: Pending=0, InProgress=1, Done=2, Skipped=3, Blocked=4
            statusCombo.Items.Add(new ComboBoxItem { Content = "Pending", Tag = "Pending" });
            statusCombo.Items.Add(new ComboBoxItem { Content = "In Progress", Tag = "InProgress" });
            statusCombo.Items.Add(new ComboBoxItem { Content = "Done", Tag = "Done" });
            statusCombo.Items.Add(new ComboBoxItem { Content = "Skipped", Tag = "Skipped" });
            statusCombo.Items.Add(new ComboBoxItem { Content = "Blocked", Tag = "Blocked" });
            statusCombo.SelectedIndex = (int)step.Status;
            statusCombo.SelectionChanged += async (s, e) =>
            {
                step.Status = (RunbookStepStatus)statusCombo.SelectedIndex;
                var newColor = GetStepStatusColor(step.Status);
                badgeBrush.Color = newColor;
                statusComboForeground.Color = newColor;
                _activeRunbook!.UpdatedAt = DateTime.Now;
                UpdateRunbookProgress();
                RunbookLastSavedText.Text = $"Last saved: {_activeRunbook.UpdatedAt:MMM d, yyyy h:mm tt}";
                await DebouncedSaveAsync();
            };
            Grid.SetColumn(statusCombo, 1);
            titleRow.Children.Add(statusCombo);
            content.Children.Add(titleRow);

            // Details textbox (monospace for commands/URLs)
            var detailsBox = new TextBox
            {
                Text = step.Details,
                PlaceholderText = "Details, commands, URLs...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 20, 20, 30)),
                BorderThickness = new Thickness(1),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 55, 55, 75)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 156, 163, 175)),
                FontSize = 12,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(4),
                MinHeight = 48
            };
            detailsBox.TextChanged += async (s, e) =>
            {
                step.Details = detailsBox.Text;
                _activeRunbook!.UpdatedAt = DateTime.Now;
                await DebouncedSaveAsync();
            };
            content.Children.Add(detailsBox);

            // Notes + Assigned-to row
            var notesRow = new Grid();
            notesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            notesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            var notesBox = new TextBox
            {
                Text = step.Notes,
                PlaceholderText = "Notes...",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            notesBox.TextChanged += async (s, e) =>
            {
                step.Notes = notesBox.Text;
                _activeRunbook!.UpdatedAt = DateTime.Now;
                await DebouncedSaveAsync();
            };
            Grid.SetColumn(notesBox, 0);
            notesRow.Children.Add(notesBox);

            var assignedBox = new TextBox
            {
                Text = step.AssignedTo,
                PlaceholderText = "Assigned to...",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            assignedBox.TextChanged += async (s, e) =>
            {
                step.AssignedTo = assignedBox.Text;
                _activeRunbook!.UpdatedAt = DateTime.Now;
                await DebouncedSaveAsync();
            };
            Grid.SetColumn(assignedBox, 1);
            notesRow.Children.Add(assignedBox);
            content.Children.Add(notesRow);

            Grid.SetColumn(content, 1);
            outerGrid.Children.Add(content);

            // Up / Down / Delete buttons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Top
            };

            var upBtn = new Button
            {
                Content = "↑",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                FontSize = 12
            };
            upBtn.Click += async (s, e) =>
            {
                if (_activeRunbook == null) return;
                int idx = _activeRunbook.Steps.IndexOf(step);
                if (idx <= 0) return;
                _activeRunbook.Steps.RemoveAt(idx);
                _activeRunbook.Steps.Insert(idx - 1, step);
                _activeRunbook.UpdatedAt = DateTime.Now;
                await DebouncedSaveAsync();
                RefreshSteps();
            };
            btnPanel.Children.Add(upBtn);

            var downBtn = new Button
            {
                Content = "↓",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 37, 37, 53)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 107, 114, 128)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                FontSize = 12
            };
            downBtn.Click += async (s, e) =>
            {
                if (_activeRunbook == null) return;
                int idx = _activeRunbook.Steps.IndexOf(step);
                if (idx >= _activeRunbook.Steps.Count - 1) return;
                _activeRunbook.Steps.RemoveAt(idx);
                _activeRunbook.Steps.Insert(idx + 1, step);
                _activeRunbook.UpdatedAt = DateTime.Now;
                await DebouncedSaveAsync();
                RefreshSteps();
            };
            btnPanel.Children.Add(downBtn);

            var deleteBtn = new Button
            {
                Content = new FontIcon
                {
                    Glyph = "\uE711",
                    FontSize = 10,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons")
                },
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 63, 26, 26)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 248, 113, 113)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3)
            };
            deleteBtn.Click += async (s, e) =>
            {
                if (_activeRunbook == null) return;
                _activeRunbook.Steps.Remove(step);
                _activeRunbook.UpdatedAt = DateTime.Now;
                await DebouncedSaveAsync();
                RefreshSteps();
            };
            btnPanel.Children.Add(deleteBtn);

            Grid.SetColumn(btnPanel, 2);
            outerGrid.Children.Add(btnPanel);

            container.Child = outerGrid;
            return container;
        }

        private static Windows.UI.Color GetStepStatusColor(RunbookStepStatus status) => status switch
        {
            RunbookStepStatus.Done => Windows.UI.Color.FromArgb(255, 16, 185, 129),
            RunbookStepStatus.InProgress => Windows.UI.Color.FromArgb(255, 245, 158, 11),
            RunbookStepStatus.Blocked => Windows.UI.Color.FromArgb(255, 239, 68, 68),
            RunbookStepStatus.Skipped => Windows.UI.Color.FromArgb(255, 107, 114, 128),
            _ => Windows.UI.Color.FromArgb(255, 75, 85, 99)
        };

        #endregion
    }
}