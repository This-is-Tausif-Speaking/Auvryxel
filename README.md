# Auvryxel

Auvryxel is a small Windows browser for opening direct links. It has no account, search provider, or sign-in flow. Pinned links are optional and stay on this PC.

## Install and use

Open the repository's **Releases** page and download `Auvryxel-Setup-*-win-x64.exe`. Run the setup, then open Auvryxel from the Start menu (or choose the optional desktop shortcut). The installer is per-user and does not need administrator access. It includes Auvryxel's .NET runtime and checks for Microsoft's WebView2 Runtime; if missing, it installs it from Microsoft over Wi-Fi before finishing setup. WebView2 is Microsoft's web page engine and receives its own updates.

The setup is for 64-bit Windows and requires internet access only if WebView2 needs installing. This is a small online installer, not a fully offline bundle. The browser's profile remains at `%LOCALAPPDATA%\StillBrowser`, preserving existing local browsing data after the rebrand.

## Build from source

Building is separate from installing the ready-to-use app. To build from source, install the .NET 8 SDK and Inno Setup 7, then run `powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1`. The script publishes a self-contained x64 app, includes Microsoft's signed WebView2 bootstrapper after validating its signature, and creates the setup executable in `artifacts\installer`. End users do not need the SDK or Inno Setup.

The Windows executable and window use Assets/Auvryxel.ico. Rebuild its 7-size icon from the vector mark with dotnet run --project tools/AuvryxelIcon/AuvryxelIcon.csproj -c Release -- Assets/Auvryxel.ico.

## Local data and privacy

The WebView2 profile stores cache, cookies, site storage, and history locally in the profile folder. Downloads go in its Downloads subfolder. Auvryxel enables strict built-in tracking prevention; it blocks many known trackers, though no list can identify every tracker or stop a site from recording your visit to that site.

The interface and Auvryxel logo are project assets. Website rendering and the tracker list are provided by Microsoft's WebView2 Runtime. The installer checks its official runtime registry keys and installs the signed Microsoft Evergreen Runtime only if it is missing.
