using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Auvryxel;

public partial class MainWindow : Window
{
    // Keep the existing profile path so a rebrand never abandons local browsing data.
    private readonly string _dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StillBrowser");
    private readonly ObservableCollection<BrowserTab> _tabs = new();
    private readonly List<QuickLink> _links = new();
    private CoreWebView2Environment? _environment;
    private BrowserTab? _activeTab;
    private string _themeFile = "";
    private bool _isDark;

    public ObservableCollection<BrowserTab> Tabs => _tabs;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Directory.CreateDirectory(_dataFolder);
        Directory.CreateDirectory(Path.Combine(_dataFolder, "Downloads"));
        _themeFile = Path.Combine(_dataFolder, "theme.json");
        ApplyTheme(ReadDarkPreference(), false);
        StorageLabel.Text = "Profile · local disk";
        StoragePathLabel.Text = _dataFolder;
        LoadLinks();
        RenderLinks();
        Loaded += async (_, _) =>
        {
            try
            {
                var options = new CoreWebView2EnvironmentOptions { EnableTrackingPrevention = true };
                _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _dataFolder, options: options);
                CreateTab();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Browser engine could not start";
                MessageBox.Show(this, "Auvryxel uses the Microsoft WebView2 runtime installed on this PC. Install or repair that runtime, then open Auvryxel again.\n\n" + ex.Message, "Auvryxel could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        PreviewKeyDown += Window_PreviewKeyDown;
        Closing += (_, _) => SaveLinks();
    }

    private BrowserTab? CreateTab(string? address = null)
    {
        if (_environment is null) return null;
        var dark = _isDark;
        var view = new WebView2
        {
            Visibility = Visibility.Collapsed,
            DefaultBackgroundColor = dark ? System.Drawing.Color.FromArgb(17, 23, 20) : System.Drawing.Color.FromArgb(247, 248, 245)
        };
        BrowserHost.Children.Add(view);
        var tab = new BrowserTab(view, address is null ? "New tab" : "Loading…") { IsHome = address is null };
        _tabs.Add(tab);
        UpdateTabStrip();
        if (address is not null)
        {
            HomeView.Visibility = Visibility.Collapsed;
            BrowserHost.Visibility = Visibility.Visible;
        }
        view.CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (!args.IsSuccess || view.CoreWebView2 is null)
            {
                StatusText.Text = "This page could not be opened";
                tab.CoreReady.TrySetException(new InvalidOperationException("The browser engine failed to initialize this tab."));
                return;
            }
            var core = view.CoreWebView2;
            tab.CoreReady.TrySetResult(core);
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.Profile.DefaultDownloadFolderPath = Path.Combine(_dataFolder, "Downloads");
            core.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict;
            core.DocumentTitleChanged += (_, _) => UpdateTabTitle(tab);
            core.SourceChanged += (_, _) => UpdateAddress(tab);
            core.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var candidate) && candidate.Scheme is "http" or "https" or "file" or "about") return;
                e.Cancel = true;
                StatusText.Text = "Auvryxel opens direct web links";
            };
            core.NewWindowRequested += async (_, e) =>
            {
                e.Handled = true;
                var deferral = e.GetDeferral();
                try
                {
                    var popupTab = CreateTab();
                    if (popupTab is null) return;
                    e.NewWindow = await popupTab.CoreReady.Task;
                }
                catch
                {
                    StatusText.Text = "Could not open the link in a new tab";
                }
                finally { deferral.Complete(); }
            };
            core.PermissionRequested += (_, e) =>
            {
                // WebView2 does not display its own prompt for notifications.
                // Preserve the dashboard's "Ask" behavior with a local decision dialog.
                if (e.PermissionKind != CoreWebView2PermissionKind.Notifications
                    || e.State != CoreWebView2PermissionState.Default) return;
                var origin = GetPermissionOrigin(e.Uri);
                var decision = MessageBox.Show(this,
                    $"Allow notifications from {origin}? You can change this later in Auvryxel's privacy dashboard.",
                    "Website notification request", MessageBoxButton.YesNo, MessageBoxImage.Question);
                e.SavesInProfile = false;
                e.State = decision == MessageBoxResult.Yes
                    ? CoreWebView2PermissionState.Allow
                    : CoreWebView2PermissionState.Deny;
            };
            core.NavigationCompleted += (_, e) =>
            {
                // A late completion from a page stopped by the Home button must never
                // switch the start screen back to the empty WebView surface.
                if (tab == _activeTab && !tab.IsHome)
                {
                    HomeView.Visibility = Visibility.Collapsed;
                    BrowserHost.Visibility = Visibility.Visible;
                    StatusText.Text = e.IsSuccess ? "Page ready · strict tracking protection on" : "This page could not be reached";
                }
                else if (tab == _activeTab)
                {
                    HomeView.Visibility = Visibility.Visible;
                    BrowserHost.Visibility = Visibility.Collapsed;
                }
                UpdateAddress(tab);
            };
            if (!string.IsNullOrWhiteSpace(address)) core.Navigate(address);
        };
        _ = InitializeTabAsync(view, tab);
        SelectTab(tab);
        return tab;
    }

    private async Task InitializeTabAsync(WebView2 view, BrowserTab tab)
    {
        try { await view.EnsureCoreWebView2Async(_environment); }
        catch (Exception ex) { tab.CoreReady.TrySetException(ex); }
    }

    private static string GetPermissionOrigin(string address)
    {
        return Uri.TryCreate(address, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : "this website";
    }

    private void SelectTab(BrowserTab tab)
    {
        _activeTab = tab;
        foreach (var item in _tabs)
        {
            item.IsSelected = item == tab;
            item.View.Visibility = item.IsSelected ? Visibility.Visible : Visibility.Collapsed;
            item.TabBackground = item.IsSelected ? (Brush)FindResource("SurfaceBackground") : Brushes.Transparent;
        }
        UpdateTabStrip();
        HomeView.Visibility = tab.IsHome ? Visibility.Visible : Visibility.Collapsed;
        BrowserHost.Visibility = tab.IsHome ? Visibility.Collapsed : Visibility.Visible;
        UpdateAddress(tab);
        StatusText.Text = tab.IsHome ? "Ready when you are" : "Page ready · strict tracking protection on";
    }

    private void UpdateTabTitle(BrowserTab tab)
    {
        if (tab.View.CoreWebView2 is not { } core) return;
        tab.Title = tab.IsHome || string.IsNullOrWhiteSpace(core.DocumentTitle) ? "New tab" : core.DocumentTitle;
        UpdateTabStrip();
    }

    private void UpdateAddress(BrowserTab tab)
    {
        if (tab != _activeTab) return;
        AddressBox.Text = tab.IsHome || tab.View.Source is null || tab.View.Source.Scheme == "about" ? "" : tab.View.Source.ToString();
        BookmarkButton.Content = _links.Any(x => string.Equals(x.Url, AddressBox.Text, StringComparison.OrdinalIgnoreCase)) ? "★" : "☆";
    }

    private void NavigateAddress()
    {
        var input = AddressBox.Text.Trim();
        if (string.IsNullOrEmpty(input) || _activeTab is null) return;
        if (input.Any(char.IsWhiteSpace))
        {
            StatusText.Text = "Enter a direct website address. Auvryxel has no search engine.";
            return;
        }
        if (!input.Contains("://", StringComparison.Ordinal) && !input.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "file"))
        {
            StatusText.Text = "Enter a direct website address";
            return;
        }
        _activeTab.IsHome = false;
        HomeView.Visibility = Visibility.Collapsed;
        BrowserHost.Visibility = Visibility.Visible;
        _activeTab.View.Visibility = Visibility.Visible;
        StatusText.Text = "Opening link…";
        if (_activeTab.View.CoreWebView2 is { } core) core.Navigate(uri.AbsoluteUri);
        else _activeTab.View.Source = uri;
    }

    private void LoadLinks()
    {
        try
        {
            var path = Path.Combine(_dataFolder, "links.json");
            if (File.Exists(path)) _links.AddRange(JsonSerializer.Deserialize<List<QuickLink>>(File.ReadAllText(path)) ?? []);
        }
        catch { _links.Clear(); }
    }

    private void SaveLinks()
    {
        try { File.WriteAllText(Path.Combine(_dataFolder, "links.json"), JsonSerializer.Serialize(_links, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    private void RenderLinks()
    {
        QuickLinksPanel.Children.Clear();
        if (_links.Count == 0)
        {
            QuickLinksPanel.Children.Add(new TextBlock { Text = "Your pinned links will appear here.", FontSize = 12, Foreground = (Brush)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            return;
        }
        foreach (var link in _links)
        {
            var button = new Button { Content = link.Title, Tag = link.Url, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(13, 9, 13, 9), Background = (Brush)FindResource("AccentSoft"), Foreground = (Brush)FindResource("SecondaryText"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = 12 };
            button.Click += (_, _) => CreateTab(link.Url);
            QuickLinksPanel.Children.Add(button);
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => CreateTab();
    private void Tab_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is BrowserTab tab) SelectTab(tab); }
    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTab tab) CloseTab(tab);
    }
    private void CloseCurrentTab_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is not null) CloseTab(_activeTab);
    }
    private void CloseTab(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;
        BrowserHost.Children.Remove(tab.View);
        tab.View.Dispose();
        _tabs.Remove(tab);
        if (_tabs.Count == 0) CreateTab();
        else if (tab == _activeTab) SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
        else UpdateTabStrip();
    }
    private void TabPickerButton_Click(object sender, RoutedEventArgs e) => TabsPopup.IsOpen = !TabsPopup.IsOpen;
    private void TabPickerItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTab tab) SelectTab(tab);
        TabsPopup.IsOpen = false;
    }
    private void UpdateTabStrip()
    {
        TabCountText.Text = _tabs.Count.ToString();
        CurrentTabTitle.Text = _activeTab?.Title ?? "New tab";
        CurrentTabHeader.Background = (Brush)FindResource("SurfaceBackground");
    }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.View.CoreWebView2?.CanGoBack != true) return;
        _activeTab.IsHome = false;
        BrowserHost.Visibility = Visibility.Visible;
        HomeView.Visibility = Visibility.Collapsed;
        _activeTab.View.CoreWebView2.GoBack();
    }
    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.View.CoreWebView2?.CanGoForward != true) return;
        _activeTab.IsHome = false;
        BrowserHost.Visibility = Visibility.Visible;
        HomeView.Visibility = Visibility.Collapsed;
        _activeTab.View.CoreWebView2.GoForward();
    }
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null || _activeTab.IsHome) return;
        _activeTab.View.CoreWebView2?.Reload();
    }
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null) return;
        _activeTab.IsHome = true;
        _activeTab.Title = "New tab";
        _activeTab.View.CoreWebView2?.Stop();
        HomeView.Visibility = Visibility.Visible;
        BrowserHost.Visibility = Visibility.Collapsed;
        AddressBox.Text = "";
        StatusText.Text = "Ready when you are";
        UpdateTabStrip();
    }
    private void AddressBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { NavigateAddress(); Keyboard.ClearFocus(); e.Handled = true; } }
    private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) { if (AddressBox.Text.Length > 0) AddressBox.SelectAll(); }
    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(AddressBox.Text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return;
        var existing = _links.FindIndex(x => string.Equals(x.Url, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _links.RemoveAt(existing);
        else _links.Add(new QuickLink(uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase), uri.AbsoluteUri));
        SaveLinks();
        RenderLinks();
        if (_activeTab is not null) UpdateAddress(_activeTab);
    }
    private void AddLink_Click(object sender, RoutedEventArgs e)
    {
        var input = Microsoft.VisualBasic.Interaction.InputBox("Enter a website address to pin on your start page:", "Add a quick link", "https://").Trim();
        if (string.IsNullOrEmpty(input) || input == "https://") return;
        if (!input.Contains("://", StringComparison.Ordinal)) input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) { StatusText.Text = "That link could not be added"; return; }
        _links.Add(new QuickLink(uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase), uri.AbsoluteUri));
        SaveLinks();
        RenderLinks();
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        ThemeMenuItem.Content = _isDark ? "Switch to light mode" : "Switch to dark mode";
        AppMenuPopup.IsOpen = !AppMenuPopup.IsOpen;
    }

    private bool ReadDarkPreference()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(_themeFile));
            return json.RootElement.TryGetProperty("dark", out var dark) && dark.GetBoolean();
        }
        catch { return false; }
    }

    private void ApplyTheme(bool dark, bool persist = true)
    {
        _isDark = dark;
        var colors = dark
            ? new Dictionary<string, string> { ["AppBackground"] = "#111714", ["ChromeBackground"] = "#191F1B", ["SurfaceBackground"] = "#1D2520", ["CardBackground"] = "#1A211D", ["FieldBackground"] = "#232C26", ["MainText"] = "#E9EFEA", ["SecondaryText"] = "#C4CFC7", ["MutedText"] = "#96A198", ["BorderBrushColor"] = "#303A33", ["AccentBrush"] = "#76C5A4", ["AccentSoft"] = "#263A30", ["ButtonText"] = "#B5C0B8", ["HoverBackground"] = "#2A342D", ["PressedBackground"] = "#354139", ["LogoMark"] = "#C8FFE9" }
            : new Dictionary<string, string> { ["AppBackground"] = "#F7F8F5", ["ChromeBackground"] = "#ECEFEA", ["SurfaceBackground"] = "#FCFDFB", ["CardBackground"] = "#FFFFFF", ["FieldBackground"] = "#F0F2EE", ["MainText"] = "#18211C", ["SecondaryText"] = "#3E4B43", ["MutedText"] = "#818B83", ["BorderBrushColor"] = "#E5E9E3", ["AccentBrush"] = "#267A64", ["AccentSoft"] = "#E6F0EA", ["ButtonText"] = "#59635D", ["HoverBackground"] = "#E4E9E3", ["PressedBackground"] = "#DCE3DC", ["LogoMark"] = "#D7FFF1" };
        foreach (var (key, value) in colors)
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        foreach (var tab in _tabs)
            tab.View.DefaultBackgroundColor = dark ? System.Drawing.Color.FromArgb(17, 23, 20) : System.Drawing.Color.FromArgb(247, 248, 245);
        RenderLinks();
        foreach (var tab in _tabs)
            tab.TabBackground = tab.IsSelected ? (Brush)FindResource("SurfaceBackground") : Brushes.Transparent;
        UpdateTabStrip();
        if (persist)
        {
            try { File.WriteAllText(_themeFile, JsonSerializer.Serialize(new { dark })); } catch { }
        }
    }

    private void MenuNewTab_Click(object sender, RoutedEventArgs e) { AppMenuPopup.IsOpen = false; CreateTab(); }
    private void MenuSaveLink_Click(object sender, RoutedEventArgs e) { AppMenuPopup.IsOpen = false; Bookmark_Click(sender, e); }
    private void ToggleTheme_Click(object sender, RoutedEventArgs e) { AppMenuPopup.IsOpen = false; ApplyTheme(!_isDark); }
    private void OpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        AppMenuPopup.IsOpen = false;
        try { Process.Start(new ProcessStartInfo(Path.Combine(_dataFolder, "Downloads")) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open downloads", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async void ClearData_Click(object sender, RoutedEventArgs e)
    {
        AppMenuPopup.IsOpen = false;
        if (_activeTab?.View.CoreWebView2 is not { } core) return;
        var choice = MessageBox.Show(this, "This clears cookies, cache, history, and stored site data for every Auvryxel tab. Pinned links and downloaded files stay in place. Continue?", "Clear browsing data", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes) return;
        try
        {
            await core.Profile.ClearBrowsingDataAsync();
            StatusText.Text = "Local browsing data cleared";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not clear browsing data", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void About_Click(object sender, RoutedEventArgs e)
    {
        AppMenuPopup.IsOpen = false;
        MessageBox.Show(this, $"Auvryxel is a small, account-free browser for direct links. Strict tracking prevention is enabled using WebView2's built-in tracker list. It blocks many known trackers, though it cannot identify every tracker or stop a site from recording your visit to itself.\n\nYour profile and downloads are stored here:\n{_dataFolder}\n\nPage rendering and tracker protection use the Microsoft WebView2 Runtime installed on this PC.", "About Auvryxel", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PrivacyDashboard_Click(object sender, RoutedEventArgs e)
    {
        AppMenuPopup.IsOpen = false;
        var profile = _activeTab?.View.CoreWebView2?.Profile;
        var currentSite = _activeTab?.View.Source?.ToString();
        var dashboard = new PrivacyDashboardWindow(_dataFolder, profile, currentSite) { Owner = this };
        dashboard.ShowDialog();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { AddressBox.Focus(); AddressBox.SelectAll(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T) { CreateTab(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W && _activeTab is not null) { CloseTab_Click(new Button { DataContext = _activeTab }, new RoutedEventArgs()); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R) { Reload_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Escape && AddressBox.IsKeyboardFocused) { Keyboard.ClearFocus(); e.Handled = true; }
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) Maximize_Click(sender, new RoutedEventArgs()); else DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public sealed record QuickLink(string Title, string Url);
    public sealed class BrowserTab(WebView2 view, string title)
    {
        public WebView2 View { get; } = view;
        public string Title { get; set; } = title;
        public bool IsSelected { get; set; }
        public bool IsHome { get; set; }
        public Brush TabBackground { get; set; } = Brushes.Transparent;
        public TaskCompletionSource<CoreWebView2> CoreReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
