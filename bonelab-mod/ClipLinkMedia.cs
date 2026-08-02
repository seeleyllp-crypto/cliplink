using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using BoneLib.BoneMenu;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Video;

[assembly: MelonInfo(typeof(ClipLinkMedia.Core), "ClipLink Media", "1.1.2", "seeleyllp-crypto")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]

namespace ClipLinkMedia;

public sealed class Core : MelonMod
{
    private const string YouTubeHome = "https://www.youtube.com/";
    private static readonly ConcurrentQueue<Action> MainThreadActions = new();
    private static readonly ConcurrentDictionary<int, byte> ActivePlayers = new();
    private static readonly ConcurrentDictionary<string, string> CachedVideos = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();
    private static bool _bypassUrlPatch;
    private static string _dataDirectory = string.Empty;
    private static string _cacheDirectory = string.Empty;
    private static string _ytDlpPath = string.Empty;

    public override void OnInitializeMelon()
    {
        _dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "ClipLinkMedia");
        Directory.CreateDirectory(_dataDirectory);
        _cacheDirectory = ChooseCacheDirectory();
        Directory.CreateDirectory(_cacheDirectory);
        _ytDlpPath = ResolveYtDlpPath();

        HarmonyInstance.PatchAll();
        BuildBoneMenu();
        MelonLogger.Msg($"Ready. Cache: {_cacheDirectory}");
        if (!File.Exists(_ytDlpPath))
            MelonLogger.Error("yt-dlp.exe is missing from the ClipLink Media package.");
    }

    public override void OnUpdate()
    {
        while (MainThreadActions.TryDequeue(out Action? action))
        {
            try { action(); }
            catch (Exception ex) { MelonLogger.Error($"Main-thread action failed: {ex}"); }
        }
    }

    private static void BuildBoneMenu()
    {
        Page page = Page.Root.CreatePage("ClipLink Media", Color.cyan);
        page.CreateFunction("Open YouTube", Color.red, OpenYouTube);
        page.CreateFunction("Download copied link", Color.green, DownloadClipboardVideo);
        page.CreateFunction("Clear downloaded videos", Color.yellow, ClearCache);
    }

    private static void OpenYouTube()
    {
        try
        {
            Application.OpenURL(YouTubeHome);
            MelonLogger.Msg("Opened YouTube. Copy a video URL, return to BONELAB, hold a Media Player, and press B.");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Could not open YouTube: {ex.Message}");
        }
    }

    private static void DownloadClipboardVideo()
    {
        string url = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        if (!IsYouTubeUrl(url))
        {
            MelonLogger.Warning("Clipboard does not contain a youtube.com or youtu.be video URL.");
            return;
        }

        MelonLogger.Msg("Downloading the copied YouTube video. Press B on a Media Player after it finishes.");
        _ = Task.Run(() => DownloadVideo(url));
    }

    private static void ClearCache()
    {
        try
        {
            lock (CacheLock)
            {
                foreach (string file in Directory.EnumerateFiles(_cacheDirectory, "*.mp4"))
                    File.Delete(file);
                CachedVideos.Clear();
            }
            MelonLogger.Msg("Downloaded video cache cleared.");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Could not clear cache: {ex.Message}");
        }
    }

    internal static bool ShouldIntercept(string? value, out string youtubeUrl)
    {
        youtubeUrl = value?.Trim() ?? string.Empty;
        if (IsYouTubeUrl(youtubeUrl)) return true;

        string clipboard = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        if (IsYouTubeUrl(clipboard))
        {
            youtubeUrl = clipboard;
            return true;
        }
        return false;
    }

    internal static bool IsBypassing => _bypassUrlPatch;

    internal static void QueueDownloadAndPlay(VideoPlayer player, string youtubeUrl)
    {
        int playerId = player.GetInstanceID();
        if (!ActivePlayers.TryAdd(playerId, 0))
        {
            MelonLogger.Warning("This Media Player is already downloading a video.");
            return;
        }

        MelonLogger.Msg($"Downloading for Media Player: {youtubeUrl}");
        _ = Task.Run(() =>
        {
            string? localPath = DownloadVideo(youtubeUrl);
            MainThreadActions.Enqueue(() =>
            {
                try
                {
                    if (player == null || string.IsNullOrEmpty(localPath) || !File.Exists(localPath)) return;
                    string fileUrl = new Uri(localPath).AbsoluteUri;
                    _bypassUrlPatch = true;
                    player.source = VideoSource.Url;
                    player.url = fileUrl;
                    player.Play();
                    MelonLogger.Msg($"Playing downloaded video: {Path.GetFileName(localPath)}");
                }
                finally
                {
                    _bypassUrlPatch = false;
                    ActivePlayers.TryRemove(playerId, out _);
                }
            });
        });
    }

    private static string? DownloadVideo(string youtubeUrl)
    {
        if (CachedVideos.TryGetValue(youtubeUrl, out string? cached) && File.Exists(cached))
        {
            MelonLogger.Msg($"Using cached video: {Path.GetFileName(cached)}");
            return cached;
        }
        if (!File.Exists(_ytDlpPath))
        {
            MelonLogger.Error($"yt-dlp.exe is missing: {_ytDlpPath}");
            return null;
        }

        try
        {
            string resultFile = Path.Combine(_dataDirectory, "last-download.txt");
            if (File.Exists(resultFile)) File.Delete(resultFile);

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _dataDirectory,
            };
            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add("--windows-filenames");
            startInfo.ArgumentList.Add("--newline");
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add("youtube:player_client=android_vr,android,ios");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("b[ext=mp4]/b");
            startInfo.ArgumentList.Add("--print-to-file");
            startInfo.ArgumentList.Add("after_move:%(filepath)s");
            startInfo.ArgumentList.Add(resultFile);
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(Path.Combine(_cacheDirectory, "%(title).100s [%(id)s].%(ext)s"));
            startInfo.ArgumentList.Add(youtubeUrl);

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("yt-dlp did not start.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            if (process.ExitCode != 0)
            {
                MelonLogger.Error($"yt-dlp failed ({process.ExitCode}): {stderr.Result.Trim()}");
                return null;
            }

            string? path = File.Exists(resultFile)
                ? File.ReadLines(resultFile).LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
                : null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MelonLogger.Error("yt-dlp finished but did not return a playable MP4 path.");
                return null;
            }

            CachedVideos[youtubeUrl] = path;
            MelonLogger.Msg($"Download complete: {Path.GetFileName(path)}");
            return path;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Download failed: {ex}");
            return null;
        }
    }

    private static bool IsYouTubeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return false;
        string host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal) || host == "youtu.be";
    }

    private static string ResolveYtDlpPath()
    {
        string legacyPath = Path.Combine(_dataDirectory, "yt-dlp.exe");
        if (File.Exists(legacyPath)) return legacyPath;

        string gameDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string userDataDirectory = Path.Combine(gameDirectory, "UserData");
        if (Directory.Exists(userDataDirectory))
        {
            try
            {
                string? packagedPath = Directory
                    .EnumerateFiles(userDataDirectory, "yt-dlp.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(path => path.Contains("ClipLinkMedia", StringComparison.OrdinalIgnoreCase)
                                         || path.Contains("ClipLink_Media", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(packagedPath)) return packagedPath;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not search UserData for yt-dlp: {ex.Message}");
            }
        }

        return Path.Combine(userDataDirectory, "yt-dlp.exe");
    }

    private static string ChooseCacheDirectory()
    {
        string preferred = Path.Combine(_dataDirectory, "Cache");
        try
        {
            string root = Path.GetPathRoot(preferred) ?? string.Empty;
            if (!string.IsNullOrEmpty(root) && new DriveInfo(root).AvailableFreeSpace >= 1024L * 1024 * 1024)
                return preferred;
        }
        catch { }

        DriveInfo? fallback = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.AvailableFreeSpace >= 1024L * 1024 * 1024)
            .OrderByDescending(d => d.AvailableFreeSpace)
            .FirstOrDefault();
        return fallback == null ? preferred : Path.Combine(fallback.RootDirectory.FullName, "ClipLinkMediaCache");
    }
}
[HarmonyPatch(typeof(VideoPlayer), "set_url")]
internal static class VideoPlayerUrlPatch
{
    [HarmonyPrefix]
    private static bool Prefix(VideoPlayer __instance, ref string value)
    {
        if (Core.IsBypassing) return true;
        if (!Core.ShouldIntercept(value, out string youtubeUrl)) return true;
        Core.QueueDownloadAndPlay(__instance, youtubeUrl);
        return false;
    }
}
