# Auvryxel

Auvryxel is a small Windows browser for opening direct links. It has no account, search provider, or sign-in flow. Pinned links are optional and stay on this PC.

## Run it

Install the .NET 8 SDK and the Microsoft Edge WebView2 Runtime, then run dotnet run --project Auvryxel.csproj. The app's profile remains at %LOCALAPPDATA%\StillBrowser so existing local browsing data stays available after the rebrand.

The Windows executable and window use Assets/Auvryxel.ico. Rebuild its 7-size icon from the vector mark with dotnet run --project tools/AuvryxelIcon/AuvryxelIcon.csproj -c Release -- Assets/Auvryxel.ico.

## Local data and privacy

The WebView2 profile stores cache, cookies, site storage, and history locally in the profile folder. Downloads go in its Downloads subfolder. Auvryxel enables strict built-in tracking prevention; it blocks many known trackers, though no list can identify every tracker or stop a site from recording your visit to that site.

The interface and Auvryxel logo are project assets. Website rendering and the tracker list are provided by the Microsoft WebView2 Runtime installed on the PC.
