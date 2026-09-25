param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,
    [string]$FirstBrowseUrl = 'https://example.com/'
)

$ErrorActionPreference = 'Stop'

function Get-WebView2Versions {
    $keys = @(
        'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'Registry::HKEY_CURRENT_USER\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    )
    foreach ($key in $keys) {
        $version = (Get-ItemProperty -LiteralPath $key -Name pv -ErrorAction SilentlyContinue).pv
        if ($version -and $version -ne '0.0.0.0') { $version }
    }
}

if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) { throw "Installer not found: $InstallerPath" }
if ([Environment]::Is64BitOperatingSystem -ne $true) { throw 'This verification requires a 64-bit Windows VM.' }

$webViewRoot = Join-Path ${env:ProgramFiles(x86)} 'Microsoft\EdgeWebView\Application'
$installedRuntimes = @(Get-WebView2Versions)
if ($installedRuntimes.Count -gt 0) {
    $uninstallers = @(Get-ChildItem -LiteralPath $webViewRoot -Filter setup.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'Installer' } |
        Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending)
    if ($uninstallers.Count -eq 0) {
        throw "WebView2 $($installedRuntimes -join ', ') is present but its supported uninstaller was not found; refusing to fake an absent-runtime test."
    }
    Get-Process msedgewebview2 -ErrorAction SilentlyContinue | Stop-Process -Force
    $uninstaller = $uninstallers[0].FullName
    $remove = Start-Process -FilePath $uninstaller -ArgumentList @('--uninstall', '--msedgewebview', '--system-level', '--verbose-logging', '--force-uninstall') -Wait -PassThru
    if ($remove.ExitCode -ne 0) { throw "WebView2 uninstaller exited with code $($remove.ExitCode)." }
}

$remaining = @(Get-WebView2Versions)
if ($remaining.Count -gt 0) { throw "WebView2 is still registered before setup: $($remaining -join ', ')." }
Write-Host 'Verified: no registered WebView2 Runtime before setup.'

$install = Start-Process -FilePath $InstallerPath -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Auvryxel Setup exited with code $($install.ExitCode)." }

$appPath = Join-Path $env:LOCALAPPDATA 'Programs\Auvryxel\Auvryxel.exe'
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) { throw "Setup completed without installing Auvryxel at $appPath." }
$installedRuntimes = @(Get-WebView2Versions)
if ($installedRuntimes.Count -eq 0) { throw 'Setup completed but did not install/register WebView2 Runtime.' }
Write-Host "Verified: setup installed WebView2 Runtime $($installedRuntimes -join ', ')."

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
$app = Start-Process -FilePath $appPath -PassThru
$deadline = (Get-Date).AddSeconds(60)
$window = $null
while ((Get-Date) -lt $deadline -and -not $window) {
    Start-Sleep -Milliseconds 500
    $app.Refresh()
    if ($app.HasExited) { throw "Auvryxel exited during startup with code $($app.ExitCode)." }
    if ($app.MainWindowHandle -ne [IntPtr]::Zero) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NativeWindowHandleProperty,
            [int]$app.MainWindowHandle)
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condition)
    }
}
if (-not $window) { throw 'Auvryxel did not expose its main window within 60 seconds.' }

$addressCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'AddressBox')
$addressBox = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $addressCondition)
if (-not $addressBox) { throw 'The Auvryxel address bar was not available through Windows UI Automation.' }
$window.SetFocus()
$addressBox.SetFocus()
$addressBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($FirstBrowseUrl)
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

$pageCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::NameProperty, 'Example Domain')
$pageDeadline = (Get-Date).AddSeconds(60)
$pageHeading = $null
while ((Get-Date) -lt $pageDeadline -and -not $pageHeading) {
    Start-Sleep -Milliseconds 750
    $app.Refresh()
    if ($app.HasExited) { throw "Auvryxel exited while browsing with code $($app.ExitCode)." }
    $pageHeading = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $pageCondition)
}
if (-not $pageHeading) { throw "Auvryxel opened, but the first direct link '$FirstBrowseUrl' did not render its expected page heading within 60 seconds." }

try {
    New-Item -ItemType Directory -Force -Path 'artifacts\verification' | Out-Null
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    if ($screen.Width -gt 0 -and $screen.Height -gt 0) {
        $bitmap = [System.Drawing.Bitmap]::new($screen.Width, $screen.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
            $bitmap.Save((Join-Path (Resolve-Path 'artifacts\verification').Path 'first-browse.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
catch { Write-Warning "Optional screenshot capture was unavailable: $($_.Exception.Message)" }

Write-Host "PASS: Auvryxel installed and browsed $FirstBrowseUrl with WebView2 absent before setup."
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
