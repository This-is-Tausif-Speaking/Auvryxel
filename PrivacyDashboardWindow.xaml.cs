using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Auvryxel;

public partial class PrivacyDashboardWindow : Window
{
    private readonly string _profilePath;
    private readonly CoreWebView2Profile? _profile;
    private readonly string? _siteOrigin;
    private readonly Dictionary<ComboBox, CoreWebView2PermissionKind> _permissionKinds = new();
    private bool _isRefreshing;
    private bool _isLoadingPermissions;
    private bool _isSavingPermission;

    public PrivacyDashboardWindow(string profilePath, CoreWebView2Profile? profile, string? currentSite)
    {
        InitializeComponent();
        _profilePath = profilePath;
        _profile = profile;
        _siteOrigin = GetOrigin(currentSite);
        ConfigurePermissionPicker(LocationChoice, CoreWebView2PermissionKind.Geolocation);
        ConfigurePermissionPicker(CameraChoice, CoreWebView2PermissionKind.Camera);
        ConfigurePermissionPicker(MicrophoneChoice, CoreWebView2PermissionKind.Microphone);
        ConfigurePermissionPicker(NotificationsChoice, CoreWebView2PermissionKind.Notifications);
        ProfilePathText.Text = "Profile: " + profilePath + Environment.NewLine
            + "Downloads: " + Path.Combine(profilePath, "Downloads");
        TrackingLevelText.Text = profile?.PreferredTrackingPreventionLevel == CoreWebView2TrackingPreventionLevel.Strict
            ? "Strict protection is on"
            : $"Tracking prevention: {profile?.PreferredTrackingPreventionLevel.ToString() ?? "Starting"}";
        if (_siteOrigin is null)
        {
            SiteOriginText.Text = "No website selected";
            SitePermissionHint.Text = "Open an HTTP or HTTPS website to manage its location, camera, microphone, and notification permissions.";
        }
        else
        {
            SiteOriginText.Text = _siteOrigin;
        }
        SetPermissionControlsEnabled(_siteOrigin is not null && _profile is not null);
        Loaded += async (_, _) =>
        {
            await RefreshProfileSizeAsync();
            await LoadSitePermissionsAsync();
        };
    }

    private void ConfigurePermissionPicker(ComboBox picker, CoreWebView2PermissionKind kind)
    {
        picker.ItemsSource = new[]
        {
            new PermissionOption("Ask", CoreWebView2PermissionState.Default),
            new PermissionOption("Allow", CoreWebView2PermissionState.Allow),
            new PermissionOption("Block", CoreWebView2PermissionState.Deny)
        };
        picker.DisplayMemberPath = nameof(PermissionOption.Label);
        picker.SelectedValuePath = nameof(PermissionOption.State);
        picker.SelectedValue = CoreWebView2PermissionState.Default;
        _permissionKinds[picker] = kind;
    }

    private static string? GetOrigin(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }

    private void SetPermissionControlsEnabled(bool enabled)
    {
        foreach (var picker in _permissionKinds.Keys) picker.IsEnabled = enabled;
    }

    private async Task LoadSitePermissionsAsync()
    {
        if (_profile is null || _siteOrigin is null) return;
        _isLoadingPermissions = true;
        try
        {
            var saved = await _profile.GetNonDefaultPermissionSettingsAsync();
            foreach (var (picker, kind) in _permissionKinds)
            {
                var setting = saved.FirstOrDefault(item => item.PermissionKind == kind
                    && string.Equals(GetOrigin(item.PermissionOrigin), _siteOrigin, StringComparison.OrdinalIgnoreCase));
                picker.SelectedValue = setting?.PermissionState ?? CoreWebView2PermissionState.Default;
            }
        }
        catch (Exception ex)
        {
            SitePermissionHint.Text = "Could not read this site's saved permissions: " + ex.Message;
        }
        finally { _isLoadingPermissions = false; }
    }

    private async void PermissionChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingPermissions || _isSavingPermission || sender is not ComboBox picker
            || _profile is null || _siteOrigin is null
            || !_permissionKinds.TryGetValue(picker, out var kind)
            || picker.SelectedValue is not CoreWebView2PermissionState state) return;

        _isSavingPermission = true;
        SetPermissionControlsEnabled(false);
        try
        {
            await _profile.SetPermissionStateAsync(kind, _siteOrigin, state);
            StatusText.Text = $"{kind} permission for this site: {state switch { CoreWebView2PermissionState.Default => "Ask", CoreWebView2PermissionState.Allow => "Allowed", _ => "Blocked" }}.";
            StatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not update this site's permission", MessageBoxButton.OK, MessageBoxImage.Error);
            await LoadSitePermissionsAsync();
        }
        finally
        {
            _isSavingPermission = false;
            SetPermissionControlsEnabled(_siteOrigin is not null && _profile is not null);
        }
    }

    private sealed record PermissionOption(string Label, CoreWebView2PermissionState State);

    private async void RefreshSize_Click(object sender, RoutedEventArgs e) => await RefreshProfileSizeAsync();

    private async Task RefreshProfileSizeAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        RefreshButton.IsEnabled = false;
        ProfileSizeText.Text = "Checking…";
        try
        {
            var bytes = await Task.Run(() => GetDirectorySize(_profilePath));
            ProfileSizeText.Text = FormatSize(bytes);
        }
        catch
        {
            ProfileSizeText.Text = "Unavailable";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            _isRefreshing = false;
        }
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var file in Directory.EnumerateFiles(path, "*", options))
        {
            try { total += new FileInfo(file).Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return total;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    private void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_profilePath) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open profile folder", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ClearBrowsingData_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null)
        {
            MessageBox.Show(this, "The browser profile is still starting. Try again in a moment.", "Auvryxel", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            "Clear cookies, cached files, browsing history, and stored site data from this PC? Downloads, pinned links, and your theme will stay.",
            "Clear browsing data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var kinds = CoreWebView2BrowsingDataKinds.AllSite
                | CoreWebView2BrowsingDataKinds.DiskCache
                | CoreWebView2BrowsingDataKinds.BrowsingHistory;
            await _profile.ClearBrowsingDataAsync(kinds);
            StatusText.Text = "Local browsing data cleared.";
            StatusText.Visibility = Visibility.Visible;
            await RefreshProfileSizeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not clear browsing data", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
