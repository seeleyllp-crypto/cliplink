# ClipLink Media for BONELAB

ClipLink Media v5.1.0 is an all-in-one Windows PC BONELAB MelonLoader mod. It keeps the complete YouTube-to-MP4 workflow, adds an in-menu spawner for the Elijoe Media Player content mod, and includes practical troubleshooting, support, maintenance, Fusion diagnostics, monitoring, screenshot, backup, and connectivity tools.

## All-in-one controls

- **YouTube browser - no login** searches public video titles and copies the selected normal YouTube URL.
- **Make public MP4 URL** downloads the copied video, uploads it to Litterbox, and automatically copies the direct MP4 URL.
- **Download MP4 only** saves the file under `UserData/ClipLinkMedia/Downloads` without uploading it.
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
- Progress, success, cancellation, and failure notifications appear in game.

## General BONELAB utility toolbox

- Session dashboard: clock, scene, uptime, measured FPS, headset position, session report, and device report.
- Stopwatch and timers: start/pause/reset stopwatch plus 1/5/10/15-minute countdowns.
- Saved support notes: keep reproduction steps or troubleshooting notes across launches.
- Mod health and support: dependency checks, duplicate/empty DLL detection, installed-mod export, recent error extraction, and a complete support report.
- Fusion diagnostics: current player count, player-name list, and a session report without copying platform IDs.
- Connectivity and updates: test YouTube/Litterbox/GitHub and check the current GitHub release.
- Maintenance: screenshots, settings/history backups, disk-space checks, and shortcuts to active mod/log/data folders.
- Safe cleanup: requires confirmation and only removes non-link ClipLinkMediaJobs subfolders older than 24 hours.
- Local audio and FPS: volume, target FPS, VSync, and optional sustained-low-FPS warnings at 45/60/72 FPS.
- Clipboard helpers: copy local or UTC timestamps, the current scene, headset position, or a complete session report.

The utility controls are local. They do not grant Fusion permissions, affect other players, or change network ownership.

## Use with Media Player

1. Open `ClipLink Media` in BoneMenu and choose **YouTube browser - no login**.
2. Select **Search**, type a search, press Enter, and choose **Search YouTube**.
3. Select a video title. Its YouTube link is copied automatically.
4. Return to the main ClipLink page and turn on **I own / have permission**.
5. Optionally choose **MP4 quality** and **Public-link expiry**.
6. Choose **Make public MP4 URL**.
7. Wait for the completion notification. The direct MP4 URL is now in the Windows clipboard.
8. Select **Spawn media player**, grab the spawned player, and press **B** to load the copied direct link. You can instead use **Use last MP4 URL + spawn** to recopy the newest link automatically.

The browser uses signed-out public requests: no Google login, cookies, or API key are used. It shows clickable video titles instead of embedding the complete YouTube site. **Open YouTube on desktop** is available as a fallback.

The mod does not patch or redistribute Media Player. It uses the installed `Elijoe.MediaPlayer` content pallet's real barcode and shows a warning if that content mod is unavailable.

## Fusion OWNER tag

Players who install ClipLink Media see a gold `OWNER` label above the creator's avatar in Fusion. The label is a separate local visual: it does not change the creator's Fusion nickname, and players without this mod installed will not see it.

The OWNER label is cosmetic only. It does not grant server ownership, host controls, moderation permissions, or any gameplay advantage.

## Install yt-dlp from GitHub

The Thunderstore package does not bundle another project's executable. Download the official Windows file from GitHub: **[Download yt-dlp.exe](https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe)**. The ClipLink v5.1.0 GitHub release also keeps the same verified file as a [separate yt-dlp.exe asset](https://github.com/seeleyllp-crypto/cliplink/releases/download/v5.1.0/yt-dlp.exe). Do not add the EXE to the Thunderstore ZIP.

In BoneMenu, choose **Setup and folders**, then **Open ClipLink folder**. Move the downloaded `yt-dlp.exe` into that folder. The full path normally ends in `UserData/ClipLinkMedia/yt-dlp.exe`. **Get yt-dlp from GitHub** opens the official download directly, and **Check setup** confirms when it is found.

## Installation

Import the ZIP with Thunderstore Mod Manager or copy its contents into the BONELAB installation folder. The package contains the mod DLL and required Thunderstore metadata; it contains no EXE.

Required: MelonLoader, BoneLib, Fusion, and a separately downloaded `yt-dlp.exe`. The Media Player content pack is separate.

Only download and publicly upload videos you own or have permission to use. ClipLink Media has a 1 GB safety limit, and temporary Litterbox links expire at the selected time. YouTube account-only, DRM-protected, age-restricted, or unavailable videos may fail.
