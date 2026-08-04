# Changelog

## 5.2.0

- Media Players spawned from ClipLink now use LabFusion's `NetworkAssetSpawner` while connected to a Fusion server.
- Network spawns use `EntitySource.Player`, a visible spawn effect, and a completion callback that confirms the replicated entity was created.
- Single-player sessions retain the existing local BoneLib spawn path.
- The setup check now reports whether Fusion-networked or single-player spawning is active and reminds lobby members that the Media Player content pack is required.

## 5.1.0

- Added a one-click BoneMenu button that spawns the installed Elijoe Media Player in front of the player's headset.
- Added a Media Player spawner page with the standard player, flatscreen TV, CRT TV, phone, computer monitor, and boom box variants.
- Added a shortcut that copies the last generated direct MP4 URL and spawns the standard player, ready for the player to grab and press B.
- Added an in-game setup check with a clear warning when the Elijoe Media Player content pallet is missing or disabled.

## 5.0.0

- Replaced the dice/random/tally/notification-test filler with practical troubleshooting and maintenance tools.
- Added dependency/mod health checks, duplicate and zero-byte DLL detection, installed-mod export, recent log-error extraction, and a complete support report.
- Added Fusion player-count, player-list, and session-report diagnostics without exposing platform IDs.
- Added YouTube/Litterbox/GitHub connectivity tests and a GitHub release update checker.
- Added screenshot capture, ClipLink settings/history backups, disk-space checks, and shortcuts to the active Mods, MelonLoader, UserData, BONELAB, screenshot, and backup folders.
- Added optional sustained-low-FPS warnings with 45, 60, and 72 FPS thresholds.
- Added confirmed cleanup restricted to non-reparse-point ClipLinkMediaJobs subfolders older than 24 hours.
- Kept the useful session dashboard, stopwatch/countdowns, support notes, local audio/FPS controls, clipboard helpers, complete media workflow, and Fusion OWNER label.

## 4.0.0

- Added a separate general BONELAB utility toolbox unrelated to the media workflow.
- Added a session dashboard with clock, scene, uptime, measured FPS, headset position, session reports, and device reports.
- Added a stopwatch plus 1, 5, 10, and 15-minute countdown timers with completion notifications.
- Added persistent personal notes and a persistent tally counter.
- Added coin flips, D6/D10/D20 rolls, random 1-100, and yes/no selection tools.
- Added local audio levels, target FPS choices, VSync controls, clipboard helpers, and notification tests.
- Added favorite YouTube videos, a persistent ten-video batch queue, metadata previews, job status, and a downloaded-MP4 library.
- Preserved signed-out YouTube search, temporary links, local downloads, history, cancellation, diagnostics, and the installed-client-only Fusion OWNER label.

## 3.1.0

- Added persistent Best, 720p, 480p, and 360p MP4 quality choices.
- Added recent YouTube searches that can be run again with one selection.
- Added recently selected videos that can be copied again across game launches.
- Added one-click public-link and local-download retries for the last video.
- Added clipboard URL inspection, open-copied-video, copy-downloads-path, and last-source controls.
- Added persistent usage statistics and a detailed copyable setup report.
- Preserved the complete v3.0 workflow and installed-client-only Fusion OWNER label.

## 3.0.0

- Expanded ClipLink Media into an all-in-one BoneMenu hub.
- Added selectable Litterbox expiry: 1, 12, 24, or 72 hours.
- Added a local MP4 download mode and a button that opens the downloads folder.
- Added saved recent public URLs with expiry times, re-copy controls, and cleanup controls.
- Added copy/open-last-link, current-job cancellation, setup checks, and clearer progress notifications.
- Kept signed-out YouTube search, clipboard link selection, EXE-free Thunderstore packaging, and separate yt-dlp installation.
- Kept the installed-client-only Fusion OWNER label without changing the creator's Fusion nickname.

## 2.2.0

- Added a gold floating OWNER label above the creator's Fusion avatar.
- The label is client-side and only appears for viewers who have ClipLink Media installed.
- The label does not change the creator's Fusion nickname.
- The OWNER label is cosmetic and does not grant host or moderator permissions.

## 2.1.4

- Bumped the package version for a new Thunderstore submission.
- Kept the 72-hour Litterbox upload workflow from v2.1.3.

## 2.1.3

- Switched MP4 uploads from permanent Catbox storage to Litterbox.
- Litterbox links expire after 72 hours.
- Increased the supported upload limit to 1 GB.
- Kept yt-dlp.exe as a separate GitHub release asset, outside the Thunderstore ZIP.

## 2.1.2

- Prepared a review-clean Thunderstore package with a nonblank 256x256 icon.
- The package contains no bundled third-party executable.
- Kept the official GitHub link for users to download yt-dlp separately.
- Kept a verified yt-dlp.exe as a separate GitHub release asset, outside the Thunderstore ZIP.

## 2.1.1

- Added signed-out YouTube title search in BoneMenu.
- Removed the bundled yt-dlp executable from the Thunderstore package.
