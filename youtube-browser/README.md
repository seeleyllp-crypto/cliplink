# ClipLink Real YouTube Browser

This Windows companion displays YouTube's actual live website using Microsoft Edge WebView2. It starts with a private signed-out profile, blocks Google account-login navigation, and automatically copies a standard `youtube.com/watch?v=...` URL when a watch video, Short, live video, or `youtu.be` link is selected.

ClipLink Media for BONELAB launches and installs this executable. Its `--menu-mode` renders continuously updated webpage frames to a local bridge directory and accepts Search, Previous, Next, Select, scrolling, history, reload, and stop commands from BoneMenu. Separate-window mode remains available. The executable is published as a separate GitHub release asset and is not bundled inside the Thunderstore ZIP.

## Build

```powershell
dotnet publish .\ClipLinkYouTubeBrowser.csproj -c Release -r win-x64
```

The default project settings create a self-contained single-file Windows executable. Microsoft Edge WebView2 Runtime must be installed; it is included with current Windows 11 installations and is also available from Microsoft.
