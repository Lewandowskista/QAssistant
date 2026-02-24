using QAssistant.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using QAssistant.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QAssistant.Views
{
    public sealed partial class LinksPage : Page
    {
        private MainViewModel? _vm;
        private bool _webViewInitialized = false;

        public LinksPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _vm = vm;
                RefreshLinks();
            }
        }

        private void RefreshLinks()
        {
            if (_vm?.SelectedProject == null) return;
            LinksList.ItemsSource = null;
            LinksList.ItemsSource = _vm.SelectedProject.Links;
            if (LinksList.Items.Count > 0)
                LinksList.SelectedIndex = 0;
        }

        private bool IsValidUri(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            return Uri.TryCreate(url, UriKind.Absolute, out _);
        }

        private async void LinksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LinksList.SelectedItem is not EmbedLink link) return;

            if (!IsValidUri(link.Url))
            {
                EmptyState.Visibility = Visibility.Visible;
                WebViewContainer.Visibility = Visibility.Collapsed;
                return;
            }

            WebViewContainer.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;

            await InitializeWebViewAsync();
            EmbedWebView.Source = new Uri(link.Url);
        }

        private async void LinksList_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;
            var reordered = LinksList.Items.OfType<EmbedLink>().ToList();
            _vm.SelectedProject.Links.Clear();
            foreach (var l in reordered)
                _vm.SelectedProject.Links.Add(l);
            await _vm.SaveAsync();

            // Rebind to reflect new order
            LinksList.ItemsSource = null;
            LinksList.ItemsSource = _vm.SelectedProject.Links;
        }

        private async void LinksList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (LinksList.SelectedItem is not EmbedLink link) return;

            var titleBox = new TextBox { Text = link.Title, PlaceholderText = "Title..." };
            var urlBox = new TextBox { Text = link.Url, PlaceholderText = "https://..." };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "Title", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(titleBox);
            panel.Children.Add(new TextBlock { Text = "URL", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(urlBox);

            var dialog = new ContentDialog
            {
                Title = "Edit Link",
                Content = panel,
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(titleBox.Text))
            {
                if (!IsValidUri(urlBox.Text.Trim()))
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Invalid URL",
                        Content = "Please enter a valid URL (e.g., https://example.com)",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                    return;
                }

                link.Title = titleBox.Text.Trim();
                link.Url = urlBox.Text.Trim();
                await _vm!.SaveAsync();
                RefreshLinks();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                _vm?.SelectedProject?.Links.Remove(link);
                await _vm!.SaveAsync();
                RefreshLinks();
                EmptyState.Visibility = Visibility.Visible;
                WebViewContainer.Visibility = Visibility.Collapsed;
            }
        }

        private async Task InitializeWebViewAsync()
        {
            if (_webViewInitialized) return;

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QAssistant", "WebView2Data");

            var options = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions();
            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty, userDataFolder, options);

            await EmbedWebView.EnsureCoreWebView2Async(environment);

            EmbedWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
            EmbedWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            EmbedWebView.CoreWebView2.AddWebResourceRequestedFilter("*",
                Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
            EmbedWebView.CoreWebView2.WebResourceRequested += (s, args) =>
            {
                args.Request.Headers.SetHeader("Upgrade-Insecure-Requests", "1");
            };

            _webViewInitialized = true;
        }

        private async void AddLink_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedProject == null) return;

            var titleBox = new TextBox { PlaceholderText = "Title (e.g. Figma Designs)" };
            var urlBox = new TextBox { PlaceholderText = "https://..." };
            var typePicker = new ComboBox
            {
                ItemsSource = Enum.GetValues(typeof(LinkType)),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "Title", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(titleBox);
            panel.Children.Add(new TextBlock { Text = "URL", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(urlBox);
            panel.Children.Add(new TextBlock { Text = "Type", Foreground = new SolidColorBrush(Colors.White) });
            panel.Children.Add(typePicker);

            var dialog = new ContentDialog
            {
                Title = "Add Link",
                Content = panel,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary
                && !string.IsNullOrWhiteSpace(titleBox.Text)
                && !string.IsNullOrWhiteSpace(urlBox.Text))
            {
                if (!IsValidUri(urlBox.Text.Trim()))
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Invalid URL",
                        Content = "Please enter a valid URL (e.g., https://example.com)",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                    return;
                }

                var link = new EmbedLink
                {
                    Title = titleBox.Text.Trim(),
                    Url = urlBox.Text.Trim(),
                    Type = (LinkType)typePicker.SelectedItem!
                };
                _vm.SelectedProject.Links.Add(link);
                await _vm.SaveAsync();
                RefreshLinks();
                LinksList.SelectedItem = link;
            }
        }
    }
}