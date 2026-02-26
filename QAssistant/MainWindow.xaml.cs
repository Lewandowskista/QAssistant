using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QAssistant.ViewModels;
using QAssistant.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QAssistant.Models;
using QAssistant.Services;
using WinRT.Interop;
using System.Runtime.InteropServices;
using QAssistant.Helpers;

namespace QAssistant
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        public MainViewModel ViewModel { get; } = new();
        private AppWindow? _appWindow;
        public ReminderService ReminderService { get; } = new();

        public MainWindow()
        {
            this.InitializeComponent();

            // Force dark theme in code - ensures it applies in all environments
            // (GitHub Actions runners default to Light theme which makes items invisible)
            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = ElementTheme.Dark;
            }

            SetupWindow();

            // Load data after UI is fully initialized (low priority ensures layout completes first)
            this.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                async () => await LoadDataAsync());

            this.Closed += (s, e) =>
            {
                if (App.MinimizeToTray)
                {
                    e.Handled = true;
                    App.HideMainWindow();
                }
                else
                {
                    ReminderService.Stop();
                    NotificationService.Instance.Unregister();
                    ((App)Application.Current).RemoveTrayIcon();
                }
            };
        }

        private async Task LoadDataAsync()
        {
            try
            {
                ViewModel.SaveFailed += OnViewModelSaveFailed;
                await ViewModel.InitializeAsync();
                RefreshProjectList();

                ReminderService.Start(
                    () => ViewModel.Projects.ToList(),
                    (title, message) => ShowNotificationBanner(title, message)
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDataAsync error: {ex.Message}");
            }
        }

        // ── Window setup ─────────────────────────────────────────────
        private void SetupWindow()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var wndId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(wndId);

            IntPtr hInstance = GetModuleHandle(null);

            IntPtr hIcon = LoadIcon(hInstance, new IntPtr(32512));

            if (hIcon != IntPtr.Zero)
            {
                var iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon);
                _appWindow.SetIcon(iconId);
            }

            if (_appWindow == null) return;

            _appWindow.Resize(new Windows.Graphics.SizeInt32(1200, 780));
            _appWindow.Move(new Windows.Graphics.PointInt32(100, 100));

            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = true;
            _appWindow.SetPresenter(presenter);

            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = _appWindow.TitleBar;
                titleBar.ExtendsContentIntoTitleBar = true;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                titleBar.ButtonForegroundColor = Colors.Transparent;
                titleBar.ButtonInactiveForegroundColor = Colors.Transparent;
                titleBar.ButtonHoverBackgroundColor = Colors.Transparent;
                titleBar.ButtonHoverForegroundColor = Colors.Transparent;
                titleBar.ButtonPressedBackgroundColor = Colors.Transparent;
                titleBar.ButtonPressedForegroundColor = Colors.Transparent;
            }

            this.Activated += OnFirstActivated;
        }

        private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
        {
            this.Activated -= OnFirstActivated;
            SetAlwaysOnTop(true);
            SetDragRegion();
        }

        private void SetAlwaysOnTop(bool onTop)
        {
            if (_appWindow?.Presenter is OverlappedPresenter presenter)
                presenter.IsAlwaysOnTop = onTop;
        }

        private void SetDragRegion()
        {
            if (_appWindow == null) return;
            if (!AppWindowTitleBar.IsCustomizationSupported()) return;

            _appWindow.TitleBar.SetDragRectangles(new[]
            {
                new Windows.Graphics.RectInt32(220, 0,
                    (int)(_appWindow.Size.Width - 300), 48)
            });
        }

        // ── Notifications ────────────────────────────────────────────
        private void ShowNotificationBanner(string? title, string? message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (string.IsNullOrEmpty(title))
                {
                    NotificationBanner.Visibility = Visibility.Collapsed;
                    return;
                }

                NotificationTitle.Text = title;
                NotificationMessage.Text = message;
                NotificationBanner.Visibility = Visibility.Visible;

                if (title.Contains("Overdue"))
                {
                    NotificationBanner.Background = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 60, 20, 20));
                    NotificationBanner.BorderBrush = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    NotificationTitle.Foreground = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 248, 113, 113));
                }
                else if (title.Contains("Due Today"))
                {
                    NotificationBanner.Background = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 50, 35, 10));
                    NotificationBanner.BorderBrush = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 251, 191, 36));
                    NotificationTitle.Foreground = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 251, 191, 36));
                }
                else
                {
                    NotificationBanner.Background = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 20, 50, 30));
                    NotificationBanner.BorderBrush = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    NotificationTitle.Foreground = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 52, 211, 153));
                }
            });
        }

        private void DismissNotification_Click(object sender, RoutedEventArgs e)
        {
            NotificationBanner.Visibility = Visibility.Collapsed;
        }

        private void OnViewModelSaveFailed(Exception ex)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = "Save Failed",
                    Content = $"Your changes could not be saved.\n\n{ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await dialog.ShowAsync();
            });
        }

        // ── Events ───────────────────────────────────────────────────
        private void PinToggle_Checked(object sender, RoutedEventArgs e)
        {
            SetAlwaysOnTop(true);
            PinToggle.Background = (Brush)Application.Current.Resources["AccentBrush"];
        }

        private void PinToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            SetAlwaysOnTop(false);
            PinToggle.Background = (Brush)Application.Current.Resources["HoverBrush"];
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_appWindow?.Presenter is OverlappedPresenter presenter)
                presenter.Minimize();
        }

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_appWindow?.Presenter is OverlappedPresenter presenter)
            {
                if (presenter.State == OverlappedPresenterState.Maximized)
                    presenter.Restore();
                else
                    presenter.Maximize();
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (App.MinimizeToTray)
                App.HideMainWindow();
            else
                App.ExitApp();
        }

        private void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectList.ItemsSource == null) return;
            if (ProjectList.SelectedItem is Project p)
            {
                ViewModel.SelectedProject = p;
                NavigateToCurrentTab();
            }
        }

        private async void ProjectList_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
        {
            var reordered = ProjectList.Items.OfType<Project>().ToList();
            for (int i = 0; i < reordered.Count; i++)
            {
                var existing = ViewModel.Projects.IndexOf(reordered[i]);
                if (existing != i)
                    ViewModel.Projects.Move(existing, i);
            }
            await ViewModel.SaveAsync();
        }

        private async void ProjectList_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            // Select the right-tapped item
            if ((e.OriginalSource as FrameworkElement)?.DataContext is Project project)
            {
                ProjectList.SelectedItem = project;
                ViewModel.SelectedProject = project;
            }

            if (ViewModel.SelectedProject == null) return;

            var nameBox = new TextBox
            {
                Text = ViewModel.SelectedProject.Name,
                PlaceholderText = "Project name..."
            };

            var colors = new[]
            {
                ("#A78BFA", "Purple"),
                ("#60A5FA", "Blue"),
                ("#34D399", "Green"),
                ("#F87171", "Red"),
                ("#FBBF24", "Yellow"),
                ("#F472B6", "Pink"),
                ("#94A3B8", "Gray"),
                ("#FB923C", "Orange")
            };

            var colorPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0)
            };

            string selectedColor = ViewModel.SelectedProject.Color;

            foreach (var (hex, name) in colors)
            {
                var btn = new Button
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255,
                            Convert.ToByte(hex[1..3], 16),
                            Convert.ToByte(hex[3..5], 16),
                            Convert.ToByte(hex[5..7], 16))),
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(hex == selectedColor ? 2 : 0),
                    BorderBrush = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                    Tag = hex
                };

                btn.Click += (s, args) =>
                {
                    selectedColor = (string)((Button)s).Tag;
                    foreach (var child in colorPanel.Children)
                        if (child is Button b)
                            b.BorderThickness = new Thickness(
                                (string)b.Tag == selectedColor ? 2 : 0);
                };

                colorPanel.Children.Add(btn);
            }

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock
            {
                Text = "Project Name",
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                FontSize = 12
            });
            panel.Children.Add(nameBox);
            panel.Children.Add(new TextBlock
            {
                Text = "Color",
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0)
            });
            panel.Children.Add(colorPanel);

            var dialog = new ContentDialog
            {
                Title = "Edit Project",
                Content = panel,
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = ContentFrame.XamlRoot
            };
            // DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                ViewModel.SelectedProject.Name = nameBox.Text.Trim();
                ViewModel.SelectedProject.Color = selectedColor;
                await ViewModel.SaveAsync();
                RefreshProjectList();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "Delete Project",
                    Content = $"Are you sure you want to delete '{ViewModel.SelectedProject.Name}'? This will delete all notes, tasks and files in this project.",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = ContentFrame.XamlRoot
                };
                // DialogHelper.ApplyDarkTheme(confirmDialog);

                var confirmResult = await confirmDialog.ShowAsync();
                if (confirmResult == ContentDialogResult.Primary)
                {
                    ViewModel.Projects.Remove(ViewModel.SelectedProject);
                    ViewModel.SelectedProject = ViewModel.Projects.FirstOrDefault();
                    await ViewModel.SaveAsync();
                    RefreshProjectList();
                }
            }
        }

        private async void AddProject_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox
            {
                PlaceholderText = "Project name..."
            };

            var dialog = new ContentDialog
            {
                Title = "New Project",
                Content = nameBox,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = ContentFrame.XamlRoot
            };
            // DialogHelper.ApplyDarkTheme(dialog);

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                var p = new Project { Name = nameBox.Text.Trim() };
                ViewModel.Projects.Add(p);
                ViewModel.SelectedProject = p;
                await ViewModel.SaveAsync();
                RefreshProjectList();
                System.Diagnostics.Debug.WriteLine($"Project created: {p.Name}");
            }
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                ViewModel.ActiveTab = btn.Tag.ToString()!;
                UpdateTabStyles();
                NavigateToCurrentTab();
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            var query = sender.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                sender.ItemsSource = null;
                return;
            }

            var results = new List<SearchResult>();

            foreach (var project in ViewModel.Projects)
            {
                foreach (var note in project.Notes)
                {
                    if (note.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        note.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new SearchResult
                        {
                            Title = note.Title,
                            Subtitle = note.Content.Length > 60 ? note.Content[..60] + "..." : note.Content,
                            ProjectName = project.Name,
                            Type = SearchResultType.Note,
                            Item = note,
                            Project = project
                        });
                    }
                }

                foreach (var task in project.Tasks)
                {
                    if (task.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        task.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new SearchResult
                        {
                            Title = task.Title,
                            Subtitle = $"{task.Status} · {task.Priority}",
                            ProjectName = project.Name,
                            Type = SearchResultType.Task,
                            Item = task,
                            Project = project
                        });
                    }
                }
            }

            sender.ItemsSource = results.Count > 0 ? results : null;
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is not SearchResult result) return;

            sender.Text = string.Empty;
            sender.ItemsSource = null;

            ViewModel.SelectedProject = result.Project;
            RefreshProjectList();

            if (result.Type == SearchResultType.Note)
            {
                ViewModel.ActiveTab = "Notes";
                UpdateTabStyles();
                ContentFrame.Navigate(typeof(NotesPage), ViewModel);
            }
            else if (result.Type == SearchResultType.Task)
            {
                ViewModel.ActiveTab = "Tasks";
                UpdateTabStyles();
                ContentFrame.Navigate(typeof(TasksPage), ViewModel);
            }
        }

        // ── Navigation ───────────────────────────────────────────────
        private void NavigateToCurrentTab()
        {
            if (ViewModel.SelectedProject == null) return;

            switch (ViewModel.ActiveTab)
            {
                case "Links":
                    ContentFrame.Navigate(typeof(LinksPage), ViewModel);
                    break;
                case "Notes":
                    ContentFrame.Navigate(typeof(NotesPage), ViewModel);
                    break;
                case "Tasks":
                    ContentFrame.Navigate(typeof(TasksPage), ViewModel);
                    break;
                case "Files":
                    ContentFrame.Navigate(typeof(FilesPage), ViewModel);
                    break;
                case "Settings":
                    ContentFrame.Navigate(typeof(SettingsPage));
                    break;
            }
        }

        private void UpdateTabStyles()
        {
            var tabs = new[] { TabLinks, TabNotes, TabTasks, TabFiles, TabSettings };
            foreach (var tab in tabs)
            {
                bool active = tab.Tag.ToString() == ViewModel.ActiveTab;
                tab.Background = active
                    ? (Brush)Application.Current.Resources["ListAccentLowBrush"]
                    : new SolidColorBrush(Colors.Transparent);
                tab.Foreground = active
                    ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextSecondaryBrush"];
            }
        }

        private void RefreshProjectList()
        {
            try
            {
                ProjectList.ItemsSource = null;

                if (ViewModel.Projects?.Count > 0)
                {
                    ProjectList.ItemsSource = ViewModel.Projects;

                    if (ViewModel.SelectedProject != null)
                        ProjectList.SelectedItem = ViewModel.SelectedProject;
                    else if (ViewModel.Projects.Count > 0)
                        ProjectList.SelectedIndex = 0;

                    ProjectList.UpdateLayout();
                }

                NavigateToCurrentTab();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshProjectList error: {ex.Message}");
            }
        }

        /// <summary>
        /// Public method to force a refresh of the project list UI.
        /// Can be called from other pages/components if needed.
        /// </summary>
        public void ForceRefreshProjectList()
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                RefreshProjectList();
            });
        }
    }
}