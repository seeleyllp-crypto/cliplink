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

## BONELAB mod

The `bonelab-mod` folder contains the source and Thunderstore metadata for ClipLink Media v2.1.3. It adds a signed-out YouTube search to BoneMenu: select a video title to copy its YouTube link, then download a permitted video, upload the temporary MP4 to Litterbox, and copy the 72-hour direct URL for the mod.io Media Player. It shows in-game notifications and does not patch Media Player playback. The Thunderstore package does not bundle yt-dlp; download the official [yt-dlp.exe from GitHub](https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe) and place it in the folder opened by the mod.

The ready-to-import package is published with the GitHub v2.1.3 release. That release also keeps a verified copy of [yt-dlp.exe as a separate GitHub asset](https://github.com/seeleyllp-crypto/cliplink/releases/download/v2.1.3/yt-dlp.exe). Do not add the EXE to the Thunderstore ZIP. In Thunderstore Mod Manager, select BONELAB, open **Settings**, and choose **Import local mod** to install the ZIP into a profile.
