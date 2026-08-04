# ClipLink

ClipLink is a one-click Windows desktop app that turns a permitted YouTube video into a temporary 72-hour MP4 URL.

Paste a YouTube URL, confirm that you own the video or have permission to download and share it, and click **Make Public MP4 URL**. ClipLink then:

1. Downloads the video as an MP4 with `yt-dlp`.
2. Uploads the temporary MP4 to Litterbox for 72 hours.
3. Copies the temporary public URL to the clipboard.
4. Removes the temporary MP4 from the computer.

## Download

Download the current Windows ZIP from the repository's [Releases](https://github.com/seeleyllp-crypto/cliplink/releases) page. Extract the complete folder and run `ClipLink.exe`. Keep `yt-dlp.exe` beside the app.

## Requirements and limits

- Windows 10 or 11.
- Internet access to YouTube and Litterbox.
- Litterbox currently accepts temporary uploads up to 1 GB.
- At least 1.2 GB of temporary free space on one drive. ClipLink automatically selects another drive when the system temporary drive is full.
- Only download and publicly upload videos you own or have permission to use.
- Anonymous Litterbox links created by ClipLink expire after 72 hours.

## Run from source

Install Python 3 with Tk support, place `yt-dlp.exe` beside `cliplink.py` or on `PATH`, then run:

```powershell
python cliplink.py
```

## Build the Windows app

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install pyinstaller
.\.venv\Scripts\pyinstaller.exe --noconfirm --clean --windowed --name ClipLink cliplink.py
```

Place a current `yt-dlp.exe` beside the generated `ClipLink.exe` before distributing the folder.

## BONELAB all-in-one mod

The `bonelab-mod` folder contains ClipLink Media v5.5.0. Its main browser control launches YouTube's actual live website in a private signed-out WebView2 companion and automatically copies the selected video URL back into the mod. The earlier clickable-thumbnail interface remains available as a native VR fallback. The media hub also provides selectable quality, temporary links, local downloads, saved failed-upload retries, favorites, a ten-video queue, metadata previews, saved histories, job controls, statistics, and an MP4 library. It can spawn the installed Elijoe Media Player or several player variants directly from BoneMenu, including a shortcut that copies the last direct MP4 URL first. Media Players use LabFusion's replicated network-asset path in a lobby and a local fallback in single-player. A session-local Fusion handshake shows which current lobby players also have ClipLink v5.5.0 or newer, without external tracking. The practical BONELAB toolbox adds mod/dependency health checks, installed-mod and recent-error exports, support reports, Fusion session diagnostics, connectivity/update checks, screenshots, data backups, disk and folder tools, sustained-low-FPS warnings, confirmed old-temp cleanup, session reports, timers, support notes, and local audio/FPS/VSync controls. Viewers who install the mod also see a separate cosmetic `OWNER` label above the creator's Fusion avatar. It does not change the creator's Fusion nickname or grant permissions.

The Thunderstore ZIP remains EXE-free. **Open REAL YouTube GUI** downloads the separate `ClipLinkYouTubeBrowser.exe` asset from the latest GitHub release and notifies you when it finishes. Also download the official [yt-dlp.exe from GitHub](https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe) and place it in the folder opened by the mod. The ready-to-import package is published with the GitHub v5.5.0 release, which keeps both executables as separate GitHub assets. In Thunderstore Mod Manager, select BONELAB, open **Settings**, and choose **Import local mod** to install the ZIP into a profile.
