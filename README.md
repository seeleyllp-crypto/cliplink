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

The `bonelab-mod` folder contains ClipLink Media v5.6.0. **YouTube IN MENU** renders continuously updated frames from YouTube's actual signed-out webpage behind its BoneMenu controls. Search, Previous, Next, Select, scrolling, Back, Home, and Reload control that real page without leaving the menu, and selecting a video copies its normal URL back into the mod. The earlier thumbnail interface and separate window remain as fallbacks. The media hub also provides selectable quality, temporary links, local downloads, saved failed-upload retries, favorites, a ten-video queue, metadata previews, saved histories, job controls, statistics, an MP4 library, Fusion Media Player spawning, current-lobby ClipLink detection, practical troubleshooting tools, and the installed-client-only cosmetic `OWNER` label.

The Thunderstore ZIP remains EXE-free. **Start REAL YouTube in menu** downloads the separate `ClipLinkYouTubeBrowser.exe` asset from the latest GitHub release and notifies you when it finishes. Also download the official [yt-dlp.exe from GitHub](https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe) and place it in the folder opened by the mod. The ready-to-import package is published with the GitHub v5.6.0 release, which keeps both executables as separate GitHub assets. In Thunderstore Mod Manager, select BONELAB, open **Settings**, and choose **Import local mod** to install the ZIP into a profile.
