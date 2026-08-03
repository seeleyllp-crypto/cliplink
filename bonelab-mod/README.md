# ClipLink Media for BONELAB

ClipLink Media is the ClipLink app as a Windows PC BONELAB MelonLoader mod. It includes a signed-out YouTube search inside BoneMenu, copies the selected video's link, turns that link into a temporary 72-hour direct MP4 URL, and shows in-game notifications as it works.

## Use

1. Open `ClipLink Media` in BoneMenu and choose **YouTube browser - no login**.
2. Select the **Search** keyboard button, type a search, and press Enter.
3. Choose **Search YouTube**, then select a video title. Its normal YouTube link is copied automatically.
4. Return to `ClipLink Media` and turn on **I own / have permission**.
5. Choose **Make public MP4 URL**.
6. Wait for the **Download finished** notification and then the **72-hour MP4 URL copied** notification.
7. Paste or use the copied direct URL in the mod.io Media Player, including while using Fusion.

## Fusion OWNER tag

Players who install ClipLink Media see a gold `OWNER` label above the creator's avatar in Fusion. The label is a separate local visual: it does not change the creator's Fusion nickname, and players without this mod installed will not see it.

The OWNER label is cosmetic only. It does not grant server ownership, host controls, moderation permissions, or any gameplay advantage.

The YouTube browser makes signed-out public search requests and does not use a Google login, cookies, or an API key. It displays clickable video titles rather than the complete YouTube webpage or thumbnails. **Open YouTube on desktop** remains available as a fallback.

The mod does not patch Media Player playback. It downloads an MP4 with `yt-dlp`, anonymously uploads the temporary MP4 to Litterbox for 72 hours, copies the direct URL, and removes the local temporary file. **Copy last public URL** copies the most recent result again.

## Install yt-dlp from GitHub

This package does not bundle another project's executable. Download the official Windows file from GitHub: **[Download yt-dlp.exe](https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe)**. The ClipLink v2.2.0 GitHub release also keeps the same verified file as a [separate yt-dlp.exe asset](https://github.com/seeleyllp-crypto/cliplink/releases/download/v2.2.0/yt-dlp.exe). Do not add the EXE to the Thunderstore ZIP.

In BoneMenu, choose **Open yt-dlp folder**, then move the downloaded `yt-dlp.exe` into that folder. The full path normally ends in `UserData/ClipLinkMedia/yt-dlp.exe`. You can also choose **Get yt-dlp from GitHub** in BoneMenu to open the official download directly.

## Installation

Import the ZIP with Thunderstore Mod Manager or copy its contents into the BONELAB installation folder. The package contains:

- `Mods/ClipLinkMedia.dll`

Required: MelonLoader, BoneLib, Fusion, and a separately downloaded `yt-dlp.exe`. The Media Player content pack is separate.

Only download and publicly upload videos you own or have permission to use. Litterbox accepts temporary files up to 1 GB. Links created by the mod expire after 72 hours. YouTube account-only, DRM-protected, age-restricted, or unavailable videos may fail.
