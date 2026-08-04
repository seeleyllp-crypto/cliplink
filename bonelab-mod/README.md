# ClipLink Media for BONELAB

ClipLink Media v5.6.0 is an all-in-one Windows PC BONELAB MelonLoader mod. It renders YouTube's real signed-out webpage as a live BoneMenu background, controls that page from the menu, automatically copies the video you select, keeps the complete YouTube-to-MP4 workflow, spawns the Elijoe Media Player through Fusion, identifies other ClipLink users in the current lobby, and provides practical troubleshooting and utility tools.

## All-in-one controls

- **YouTube IN MENU** starts YouTube's actual signed-out webpage and renders continuously updated webpage frames behind the BoneMenu controls.
- Search, Previous, Next, Select, Scroll Up/Down, Back, Home, and Reload control the real page without leaving BoneMenu.
- Selecting a YouTube video in that page automatically copies its normal watch URL, records it in the mod, and shows an in-game notification.
- **Open separate YouTube window** remains available when a desktop-style window is preferred.
- **Thumbnail browser fallback** keeps the native VR search and clickable thumbnails for systems where the companion cannot run.
- **Make public MP4 URL** downloads the copied video, uploads it to Litterbox, and automatically copies the direct MP4 URL.
- **Download MP4 only** saves the file under `UserData/ClipLinkMedia/Downloads` without uploading it.
- **Retry saved failed upload** retries a completed MP4 if Litterbox was temporarily unavailable, without downloading the video again.
- **Public-link expiry** selects 1, 12, 24, or 72 hours. The selection is saved for next time.
- **Recent public URLs** saves up to eight unexpired results with their expiry times. Select one to copy it again.
- **Copy last public URL** and **Open last public URL** reuse the newest result.
- **Cancel current job** stops the active yt-dlp download or Litterbox upload.
- **MP4 quality** saves a Best, 720p, 480p, or 360p choice for future jobs.
- **Recent searches** reruns saved YouTube searches, and **Recently selected videos** copies earlier selections again.
- **Repeat last video** recreates a public URL or local download without searching again.
- **Setup and folders** checks yt-dlp/Fusion, opens the ClipLink and downloads folders, opens YouTube, and opens the official yt-dlp download.
- Clipboard tools inspect or open the copied URL, and statistics/setup-report controls help diagnose a problem.
- **Favorite videos** stores up to 20 favorite YouTube links.
- **Batch video queue** processes up to 10 videos as public links or local downloads.
- **Preview copied video** loads title, channel, duration, selected format, resolution, and estimated size without downloading it.
- **Downloaded MP4 library** lists recent local MP4 files and copies a selected file path.
- **Spawn media player** places the installed Elijoe Media Player in front of your headset without opening the spawn gun menu.
- **Media player spawner** also offers flatscreen, CRT, phone, computer-monitor, and boom-box variants.
- **Use last MP4 URL + spawn** copies the latest generated direct MP4 URL before spawning the player.
- In a Fusion lobby, ClipLink sends the spawn through LabFusion's network asset spawner so the Media Player is replicated for the lobby. In single-player it spawns locally.
- **Show ClipLink users**, **Copy ClipLink user list**, and **Refresh ClipLink detection** use a lobby-only handshake to confirm which current players also have ClipLink Media v5.6.0 or newer.
- Progress, success, cancellation, and failure notifications appear in game.

## General BONELAB utility toolbox

- Session dashboard: clock, scene, uptime, measured FPS, headset position, session report, and device report.
- Stopwatch and timers: start/pause/reset stopwatch plus 1/5/10/15-minute countdowns.
- Saved support notes: keep reproduction steps or troubleshooting notes across launches.
- Mod health and support: dependency checks, duplicate/empty DLL detection, installed-mod export, recent error extraction, and a complete support report.
- Fusion diagnostics: current player count, player-name list, confirmed ClipLink users and versions, and a session report without copying platform IDs.
- Connectivity and updates: test YouTube/Litterbox/GitHub and check the current GitHub release.
- Maintenance: screenshots, settings/history backups, disk-space checks, and shortcuts to active mod/log/data folders.
- Safe cleanup: requires confirmation and only removes non-link ClipLinkMediaJobs subfolders older than 24 hours.
- Local audio and FPS: volume, target FPS, VSync, and optional sustained-low-FPS warnings at 45/60/72 FPS.
- Clipboard helpers: copy local or UTC timestamps, the current scene, headset position, or a complete session report.

The utility controls are local. They do not grant Fusion permissions, affect other players, or change network ownership.

ClipLink presence detection is also session-local: it exchanges a small version message only with the current Fusion lobby and never uploads a player list to an external server. Players on older ClipLink versions or without the mod cannot answer and will not be marked as confirmed users.

## Use with Media Player

1. Open `ClipLink Media` in BoneMenu, choose **YouTube IN MENU**, then select **Start REAL YouTube in menu**.
2. The first use downloads the separate companion from this repository's latest GitHub release; wait for the in-game completion notification.
3. The actual YouTube webpage appears behind the menu controls. Type in **YouTube search**, then choose **Search real YouTube**.
4. Use **Previous YouTube control** and **Next YouTube control** to move focus on the real page, then use **SELECT focused control** to click it. Scroll and navigation controls are on the same page.
5. Selecting a video copies its normal YouTube link automatically.
6. Return to the main ClipLink page and turn on **I own / have permission**.
7. Optionally choose **MP4 quality** and **Public-link expiry**, then choose **Make public MP4 URL**.
8. Wait for the completion notification, spawn Media Player, grab it, and press **B** to load the copied direct MP4 link.

The companion uses a private WebView2 profile and blocks navigation to Google account-login pages. It does not need an API key. Because BoneMenu has no native web element, ClipLink captures the actual webpage and refreshes it as the page background while sending menu actions back to WebView2. The native thumbnail browser remains available as a fallback.

The mod does not patch or redistribute Media Player. It uses the installed `Elijoe.MediaPlayer` content pallet's real barcode and shows a warning if that content mod is unavailable. Other Fusion players need the same Media Player content pack (or compatible automatic spawnable downloading) to resolve and display the networked barcode.

## Fusion OWNER tag

Players who install ClipLink Media see a gold `OWNER` label above the creator's avatar in Fusion. The label is a separate local visual: it does not change the creator's Fusion nickname, and players without this mod installed will not see it.

The OWNER label is cosmetic only. It does not grant server ownership, host controls, moderation permissions, or any gameplay advantage.

## Install yt-dlp from GitHub

The Thunderstore package remains EXE-free. **Start REAL YouTube in menu** downloads `ClipLinkYouTubeBrowser.exe` from the latest ClipLink GitHub release and notifies you when it finishes. Also download the official Windows file from GitHub: **[Download yt-dlp.exe](https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe)**. The ClipLink v5.6.0 release keeps both executables as separate assets. Do not add either EXE to the Thunderstore ZIP.

In BoneMenu, choose **Setup and folders**, then **Open ClipLink folder**. Move the downloaded `yt-dlp.exe` into that folder. The full path normally ends in `UserData/ClipLinkMedia/yt-dlp.exe`. **Get yt-dlp from GitHub** opens the official download directly, and **Check setup** confirms when it is found.

## Installation

Import the ZIP with Thunderstore Mod Manager or copy its contents into the BONELAB installation folder. The package contains the mod DLL and required Thunderstore metadata; it contains no EXE.

Required: MelonLoader, BoneLib, Fusion, the separately downloaded `yt-dlp.exe`, and Microsoft Edge WebView2 Runtime for the real GUI companion. The Media Player content pack is separate.

Only download and publicly upload videos you own or have permission to use. ClipLink Media has a 1 GB safety limit, and temporary Litterbox links expire at the selected time. YouTube account-only, DRM-protected, age-restricted, or unavailable videos may fail.

If Litterbox returns a temporary server error, ClipLink tries three times, saves the completed MP4 in Downloads, and shows a notification. Use **Retry saved failed upload** later; the file is kept until you remove it yourself.
