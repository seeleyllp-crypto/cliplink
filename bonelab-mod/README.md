# ClipLink Media for BONELAB

ClipLink Media is the ClipLink app as a Windows PC BONELAB MelonLoader mod. It turns a copied YouTube link into a public direct MP4 URL, copies that URL to the clipboard, and shows in-game notifications as it finishes.

## Use

1. Open `ClipLink Media` in BoneMenu and choose **Open YouTube**.
2. Copy the YouTube video URL.
3. Return to BoneMenu and turn on **I own / have permission**.
4. Choose **Make public MP4 URL**.
5. Wait for the **Download finished** notification and then the **Public MP4 URL copied** notification.
6. Paste or use the copied direct URL in the mod.io Media Player, including while using Fusion.

The mod does not patch Media Player playback. It downloads an MP4 with `yt-dlp`, anonymously uploads the temporary MP4 to Catbox, copies the public URL, and removes the local temporary file. **Copy last public URL** copies the most recent result again.

## Installation

Import the ZIP with Thunderstore Mod Manager or copy its contents into the BONELAB installation folder. The package contains:

- `Mods/ClipLinkMedia.dll`
- `UserData/yt-dlp.exe`

Required: MelonLoader and BoneLib. The Media Player content pack is separate.

Only download and publicly upload videos you own or have permission to use. Catbox accepts files up to 200 MB. Anonymous Catbox uploads are public and may remain available indefinitely. YouTube account-only, DRM-protected, age-restricted, or unavailable videos may fail.
