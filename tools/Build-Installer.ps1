param(
    [string]$Dotnet = 'dotnet',
    [string]$InnoCompiler = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repo 'artifacts'
$publishDir = Join-Path $artifacts 'publish'
$bootstrapper = Join-Path $artifacts 'MicrosoftEdgeWebView2Setup.exe'
$installerScript = Join-Path $repo 'installer\Auvryxel.iss'

if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Inno Setup compiler was not found. Install the signed compiler from https://jrsoftware.org/isdl.php, then rerun this script.'
}

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

& $Dotnet publish (Join-Path $repo 'Auvryxel.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# XML files beside package assemblies are developer documentation, not runtime dependencies.
Get-ChildItem -LiteralPath $publishDir -Filter '*.xml' -File | Remove-Item -Force

$bootstrapperCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Package Cache\100CBFBFB0916E3658D5F8FF748C1C2368EA52F51E3C828425A9F9F2C366BDAF\MicrosoftEdgeWebview2Setup.exe')
)
$cachedBootstrapper = $bootstrapperCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($cachedBootstrapper) {
    Copy-Item -LiteralPath $cachedBootstrapper -Destination $bootstrapper -Force
} else {
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper
}
$signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation') {
    Remove-Item -LiteralPath $bootstrapper -Force
    throw 'The WebView2 bootstrapper signature was not valid and signed by Microsoft Corporation.'
}

& $InnoCompiler $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE" }

$setup = Get-ChildItem -LiteralPath (Join-Path $artifacts 'installer') -Filter 'Auvryxel-Setup-*-win-x64.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) { throw 'The installer compiler completed but no setup file was created.' }
Write-Host ("Installer: {0} ({1:N1} MiB)" -f $setup.FullName, ($setup.Length / 1MB))
