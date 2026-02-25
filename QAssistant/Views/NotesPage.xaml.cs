using QAssistant.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using QAssistant.Helpers;
using QAssistant.Models;
using System;
using System.Linq;
using System.Threading;

namespace QAssistant.Views
{
    public sealed partial class NotesPage : Page
    {
        private MainViewModel? _vm;
        private Note? _activeNote;
        private bool _isLoading = false;
        private CancellationTokenSource? _saveCts;

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
                _vm.SelectedProject.Notes.Remove(_activeNote);
                await _vm.SaveAsync();
                RefreshNotes();
            }
        }
    }
}