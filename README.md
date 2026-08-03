# ClipLink

ClipLink is a one-click Windows desktop app that turns a permitted YouTube video into a public MP4 URL.

Paste a YouTube URL, confirm that you own the video or have permission to download and share it, and click **Make Public MP4 URL**. ClipLink then:

1. Downloads the video as an MP4 with `yt-dlp`.
2. Uploads the temporary MP4 to Catbox.
3. Copies the public URL to the clipboard.
4. Removes the temporary MP4 from the computer.

## Download

Download the current Windows ZIP from the repository's [Releases](https://github.com/seeleyllp-crypto/cliplink/releases) page. Extract the complete folder and run `ClipLink.exe`. Keep `yt-dlp.exe` beside the app.

## Requirements and limits

- Windows 10 or 11.
- Internet access to YouTube and Catbox.
- Catbox currently accepts uploads up to 200 MB.
- At least 500 MB of temporary free space on one drive. ClipLink automatically selects another drive when the system temporary drive is full.
- Only download and publicly upload videos you own or have permission to use.
- Anonymous Catbox uploads may remain available indefinitely and may be difficult to remove.

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

The `bonelab-mod` folder contains the source and Thunderstore metadata for ClipLink Media v1.1.5. It is a Windows PC MelonLoader mod that downloads a copied YouTube URL and plays the resulting local MP4 through the mod.io Media Players content pack.

The ready-to-import package is published with the GitHub v1.1.5 release. In Thunderstore Mod Manager, select BONELAB, open **Settings**, and choose **Import local mod** to install the ZIP into a profile.
