using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoneLib.BoneMenu;
using BoneLib.Notifications;
using LabFusion.Entities;
using LabFusion.UI;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(ClipLinkMedia.Core), "ClipLink Media", "3.0.0", "seeleyllp-crypto")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

namespace ClipLinkMedia;

public sealed class Core : MelonMod
{
    private const ulong OwnerPlatformId = 76561199548494681UL;
    private const string YouTubeHome = "https://www.youtube.com/";
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string LitterboxUploadUrl = "https://litterbox.catbox.moe/resources/internals/api.php";
    private const string DefaultLitterboxRetention = "72h";
    private const int MaximumHistoryEntries = 8;
    private const long MaxUploadBytes = 1_000_000_000L;
    private const long MinimumTemporarySpace = 1200L * 1024 * 1024;
    private static readonly ConcurrentQueue<Action> MainThreadActions = new();
    private static readonly HttpClient UploadHttpClient = CreateUploadHttpClient();
    private static readonly HttpClient YouTubeHttpClient = CreateYouTubeHttpClient();
    private static readonly object JobGate = new();
    private static readonly Dictionary<NetworkPlayer, OwnerTagElement> OwnerTags = new();
    private static readonly List<LinkHistoryEntry> LinkHistory = new();
    private static string _dataDirectory = string.Empty;
    private static string _downloadsDirectory = string.Empty;
    private static string _settingsPath = string.Empty;
    private static string _historyPath = string.Empty;
    private static string _ytDlpPath = string.Empty;
    private static string _lastPublicUrl = string.Empty;
    private static string _searchQuery = "bonelab";
    private static Page? _searchResultsPage;
    private static Page? _historyPage;
    private static ClipLinkSettings _settings = new();
    private static CancellationTokenSource? _jobCancellation;
    private static bool _rightsConfirmed;
    private static int _jobRunning;
    private static int _searchRunning;

    public override void OnInitializeMelon()
    {
        _dataDirectory = Path.Combine(MelonEnvironment.UserDataDirectory, "ClipLinkMedia");
        _downloadsDirectory = Path.Combine(_dataDirectory, "Downloads");
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _historyPath = Path.Combine(_dataDirectory, "history.json");
        Directory.CreateDirectory(_dataDirectory);
        Directory.CreateDirectory(_downloadsDirectory);
        _ytDlpPath = ResolveYtDlpPath();
        LoadPersistentState();

        BuildBoneMenu();
        InitializeFusionOwnerTag();
        MelonLogger.Msg($"All-in-one v3 ready. Public-link expiry is {_settings.LitterboxRetention}; {LinkHistory.Count} saved link(s) loaded.");
        MelonLogger.Msg("Fusion OWNER tag enabled. Players with ClipLink Media installed will see OWNER above the creator's head.");
        MelonLogger.Warning("Litterbox uploads are public and temporary. Upload only videos you own or have permission to share.");
        if (!File.Exists(_ytDlpPath))
            MelonLogger.Error("yt-dlp.exe is not installed. Use the BoneMenu GitHub download and folder buttons.");
    }

    public override void OnUpdate()
    {
        while (MainThreadActions.TryDequeue(out Action? action))
        {
            try { action(); }
            catch (Exception ex) { MelonLogger.Error($"Main-thread action failed: {ex}"); }
        }
    }

    private static void InitializeFusionOwnerTag()
    {
        NetworkPlayer.OnNetworkPlayerRegistered += TryAttachOwnerTag;

        foreach (NetworkPlayer player in NetworkPlayer.Players.ToArray())
            TryAttachOwnerTag(player);
    }

    private static void TryAttachOwnerTag(NetworkPlayer player)
    {
        try
        {
            if (player?.PlayerID == null
                || !player.PlayerID.IsValid
                || player.PlayerID.PlatformID != OwnerPlatformId
                || player.PlayerID.IsMe
                || OwnerTags.ContainsKey(player))
                return;

            var ownerTag = new OwnerTagElement();
            OwnerTags.Add(player, ownerTag);
            player.HeadUI.RegisterElement(ownerTag);
            player.PlayerID.OnDestroyedEvent += () => RemoveOwnerTag(player);
            MelonLogger.Msg($"Attached OWNER tag to Fusion player {player.Username} ({player.PlayerID.PlatformID}).");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not attach Fusion OWNER tag: {ex.Message}");
        }
    }

    private static void RemoveOwnerTag(NetworkPlayer player)
    {
        if (!OwnerTags.Remove(player, out OwnerTagElement? ownerTag))
            return;

        try { player.HeadUI.UnregisterElement(ownerTag); }
        catch (Exception ex) { MelonLogger.Warning($"Could not remove Fusion OWNER tag: {ex.Message}"); }
    }

    private static void BuildBoneMenu()
    {
        Page page = Page.Root.CreatePage("ClipLink Media", Color.cyan);
        Page browserPage = page.CreatePage("YouTube browser - no login", Color.red);
        StringElement searchElement = browserPage.CreateString("Search", Color.white, _searchQuery, value => _searchQuery = value.Trim());
        searchElement.SetTooltip("Select the keyboard button, type a search, and press Enter.");
        browserPage.CreateFunction("Search YouTube", Color.red, SearchYouTube);
        _searchResultsPage = browserPage.CreatePage("Video results", Color.cyan);
        _searchResultsPage.CreateFunction("Search first", Color.gray, SearchYouTube);

        page.CreateBool("I own / have permission", Color.yellow, false, value => _rightsConfirmed = value);
        page.CreateFunction("Make public MP4 URL", Color.green, StartClipboardJob);
        page.CreateFunction("Download MP4 only", Color.green, StartLocalDownloadJob);
        page.CreateFunction("Cancel current job", Color.red, CancelCurrentJob);

        Page expiryPage = page.CreatePage("Public-link expiry", Color.yellow);
        expiryPage.CreateFunction("Show current expiry", Color.white, ShowCurrentRetention);
        expiryPage.CreateFunction("Use 1 hour", Color.cyan, () => SetRetention("1h"));
        expiryPage.CreateFunction("Use 12 hours", Color.cyan, () => SetRetention("12h"));
        expiryPage.CreateFunction("Use 24 hours", Color.cyan, () => SetRetention("24h"));
        expiryPage.CreateFunction("Use 72 hours", Color.cyan, () => SetRetention("72h"));

        _historyPage = page.CreatePage("Recent public URLs", Color.cyan);
        RefreshHistoryPage();
        page.CreateFunction("Copy last public URL", Color.cyan, CopyLastPublicUrl);
        page.CreateFunction("Open last public URL", Color.cyan, OpenLastPublicUrl);

        Page toolsPage = page.CreatePage("Setup and folders", Color.gray);
        toolsPage.CreateFunction("Check setup", Color.green, CheckSetup);
        toolsPage.CreateFunction("Open YouTube on desktop", Color.red, OpenYouTube);
        toolsPage.CreateFunction("Get yt-dlp from GitHub", Color.yellow, OpenYtDlpDownload);
        toolsPage.CreateFunction("Open ClipLink folder", Color.yellow, OpenYtDlpFolder);
        toolsPage.CreateFunction("Open downloads folder", Color.cyan, OpenDownloadsFolder);
    }

    private static void LoadPersistentState()
    {
        try
        {
            if (File.Exists(_settingsPath))
                _settings = JsonSerializer.Deserialize<ClipLinkSettings>(File.ReadAllText(_settingsPath)) ?? new ClipLinkSettings();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not load settings: {ex.Message}");
            _settings = new ClipLinkSettings();
        }

        if (!IsSupportedRetention(_settings.LitterboxRetention))
            _settings.LitterboxRetention = DefaultLitterboxRetention;

        try
        {
            if (File.Exists(_historyPath))
            {
                List<LinkHistoryEntry>? saved = JsonSerializer.Deserialize<List<LinkHistoryEntry>>(File.ReadAllText(_historyPath));
                if (saved != null)
                {
                    LinkHistory.AddRange(saved
                        .Where(entry => entry.ExpiresUtc > DateTimeOffset.UtcNow && IsHttpsUrl(entry.DirectUrl))
                        .OrderByDescending(entry => entry.CreatedUtc)
                        .Take(MaximumHistoryEntries));
                }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not load recent links: {ex.Message}");
        }

        _lastPublicUrl = LinkHistory.FirstOrDefault()?.DirectUrl ?? string.Empty;
        SaveSettings();
        SaveHistory();
    }

    private static void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not save settings: {ex.Message}");
        }
    }

    private static void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(LinkHistory, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not save recent links: {ex.Message}");
        }
    }

    private static void SetRetention(string retention)
    {
        if (!IsSupportedRetention(retention)) return;
        _settings.LitterboxRetention = retention;
        SaveSettings();
        string display = RetentionDisplay(retention);
        MelonLogger.Msg($"Public-link expiry set to {display}.");
        Notify("ClipLink Media", $"New public links will last {display}.", NotificationType.Success, 4f);
    }

    private static void ShowCurrentRetention()
    {
        Notify("Public-link expiry", $"Current setting: {RetentionDisplay(_settings.LitterboxRetention)}.", NotificationType.Information, 4f);
    }

    private static void RefreshHistoryPage()
    {
        if (_historyPage == null) return;

        _historyPage.RemoveAll();
        LinkHistory.RemoveAll(entry => entry.ExpiresUtc <= DateTimeOffset.UtcNow);
        if (LinkHistory.Count == 0)
        {
            _historyPage.CreateFunction("No saved public URLs", Color.gray, () => Warn("Create a public MP4 URL first."));
        }
        else
        {
            foreach (LinkHistoryEntry entry in LinkHistory.ToArray())
            {
                LinkHistoryEntry selectedEntry = entry;
                string sourceId = GetYouTubeVideoId(entry.SourceUrl);
                string label = $"{entry.ExpiresUtc.LocalDateTime:g} | {sourceId}";
                FunctionElement button = _historyPage.CreateFunction(ShortMenuText(label, 64), Color.white, () => CopyHistoryEntry(selectedEntry));
                button.SetTooltip($"Expires: {entry.ExpiresUtc.LocalDateTime:F}\nSource: {entry.SourceUrl}\nDirect URL: {entry.DirectUrl}");
            }
        }

        _historyPage.CreateFunction("Remove expired URLs", Color.yellow, RemoveExpiredHistory);
        _historyPage.CreateFunction("Clear all saved URLs", Color.red, ClearHistory);
        SaveHistory();
    }

    private static void CopyHistoryEntry(LinkHistoryEntry entry)
    {
        GUIUtility.systemCopyBuffer = entry.DirectUrl;
        _lastPublicUrl = entry.DirectUrl;
        Notify("Recent URL copied", $"Expires {entry.ExpiresUtc.LocalDateTime:g}.", NotificationType.Success, 4f);
    }

    private static void RemoveExpiredHistory()
    {
        int removed = LinkHistory.RemoveAll(entry => entry.ExpiresUtc <= DateTimeOffset.UtcNow);
        RefreshHistoryPage();
        Notify("Recent URLs", removed == 1 ? "Removed 1 expired URL." : $"Removed {removed} expired URLs.", NotificationType.Success, 3f);
    }

    private static void ClearHistory()
    {
        LinkHistory.Clear();
        _lastPublicUrl = string.Empty;
        RefreshHistoryPage();
        Notify("Recent URLs", "Saved public URLs cleared.", NotificationType.Success, 3f);
    }

    private static void SearchYouTube()
    {
        string query = _searchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            Warn("Type something in the YouTube search box first.");
            return;
        }

        if (Interlocked.CompareExchange(ref _searchRunning, 1, 0) != 0)
        {
            Warn("A YouTube search is already running.");
            return;
        }

        MelonLogger.Msg($"Searching signed-out YouTube results for: {query}");
        Notify("YouTube browser", $"Searching for {ShortMenuText(query, 60)}...", NotificationType.Information, 3f);
        _ = Task.Run(() => LoadYouTubeResults(query));
    }

    private static void LoadYouTubeResults(string query)
    {
        try
        {
            string url = "https://www.youtube.com/results?hl=en&sp=EgIQAQ%3D%3D&search_query=" + Uri.EscapeDataString(query);
            string html = YouTubeHttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            List<YouTubeSearchResult> results = YouTubeSearchParser.Parse(html, 12);
            if (results.Count == 0)
                throw new InvalidOperationException("YouTube returned no video results. Try another search.");

            MainThreadActions.Enqueue(() => DisplayYouTubeResults(query, results));
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"YouTube search failed: {ex}");
            FailOnMainThread($"YouTube search failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _searchRunning, 0);
        }
    }

    private static void DisplayYouTubeResults(string query, List<YouTubeSearchResult> results)
    {
        if (_searchResultsPage == null) return;

        _searchResultsPage.RemoveAll();
        foreach (YouTubeSearchResult choice in results)
        {
            YouTubeSearchResult selectedChoice = choice;
            FunctionElement button = _searchResultsPage.CreateFunction(
                ShortMenuText(choice.Title, 64),
                Color.white,
                () => CopyYouTubeChoice(selectedChoice));
            button.SetTooltip(choice.Title);
        }

        MelonLogger.Msg($"Loaded {results.Count} signed-out YouTube results for: {query}");
        Notify("YouTube browser", $"Loaded {results.Count} videos. Select one to copy its link.", NotificationType.Success, 5f);
        Menu.OpenPage(_searchResultsPage);
    }

    private static void CopyYouTubeChoice(YouTubeSearchResult choice)
    {
        GUIUtility.systemCopyBuffer = choice.Url;
        MelonLogger.Msg($"YouTube link copied: {choice.Url} ({choice.Title})");
        Notify("YouTube link copied", ShortMenuText(choice.Title, 100), NotificationType.Success, 5f);
    }

    private static void OpenYouTube()
    {
        try
        {
            Application.OpenURL(YouTubeHome);
            MelonLogger.Msg("Opened YouTube. Copy a video URL, then return to ClipLink Media in BoneMenu.");
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not open YouTube: {ex.Message}");
        }
    }

    private static void OpenYtDlpDownload()
    {
        try
        {
            Application.OpenURL(YtDlpDownloadUrl);
            MelonLogger.Msg("Opened the official yt-dlp.exe download on GitHub.");
            Notify("ClipLink Media", "Download yt-dlp.exe, then put it in the folder opened by the next menu button.", NotificationType.Information, 7f);
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not open the yt-dlp download: {ex.Message}");
        }
    }

    private static void OpenYtDlpFolder()
    {
        OpenFolder(_dataDirectory, "ClipLink folder");
    }

    private static void OpenDownloadsFolder()
    {
        OpenFolder(_downloadsDirectory, "downloads folder");
    }

    private static void OpenFolder(string path, string label)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            MelonLogger.Msg($"Opened {label}: {path}");
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not open the {label}: {ex.Message}");
        }
    }

    private static void StartClipboardJob()
    {
        TryStartClipboardJob(uploadPublicly: true);
    }

    private static void StartLocalDownloadJob()
    {
        TryStartClipboardJob(uploadPublicly: false);
    }

    private static void TryStartClipboardJob(bool uploadPublicly)
    {
        if (!_rightsConfirmed)
        {
            Warn("Turn on 'I own / have permission' before downloading or uploading.");
            return;
        }

        string url = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        if (!IsYouTubeUrl(url))
        {
            Warn("Copy a youtube.com or youtu.be video URL first.");
            return;
        }

        if (Interlocked.CompareExchange(ref _jobRunning, 1, 0) != 0)
        {
            Warn("ClipLink Media is already working on a video.");
            return;
        }

        _ytDlpPath = ResolveYtDlpPath();
        var cancellation = new CancellationTokenSource();
        lock (JobGate) _jobCancellation = cancellation;

        string retention = _settings.LitterboxRetention;
        string mode = uploadPublicly ? $"public {RetentionDisplay(retention)} link" : "local MP4";
        MelonLogger.Msg($"Starting {mode} job: {url}");
        Notify("ClipLink Media", $"Downloading video for a {mode}...", NotificationType.Information, 4f);
        _ = Task.Run(() => ProcessVideoJob(url, uploadPublicly, retention, cancellation));
    }

    private static void CancelCurrentJob()
    {
        lock (JobGate)
        {
            if (Volatile.Read(ref _jobRunning) == 0 || _jobCancellation == null)
            {
                Warn("There is no download or upload to cancel.");
                return;
            }

            _jobCancellation.Cancel();
        }

        MelonLogger.Msg("Cancellation requested for the current video job.");
        Notify("ClipLink Media", "Cancelling the current job...", NotificationType.Warning, 4f);
    }

    private static void CopyLastPublicUrl()
    {
        if (string.IsNullOrWhiteSpace(_lastPublicUrl))
        {
            Warn("No public URL is ready yet.");
            return;
        }

        GUIUtility.systemCopyBuffer = _lastPublicUrl;
        MelonLogger.Msg($"Copied public URL: {_lastPublicUrl}");
        Notify("ClipLink Media", "Public MP4 URL copied again.", NotificationType.Success, 3f);
    }

    private static void OpenLastPublicUrl()
    {
        if (string.IsNullOrWhiteSpace(_lastPublicUrl))
        {
            Warn("No public URL is ready yet.");
            return;
        }

        try
        {
            Application.OpenURL(_lastPublicUrl);
            Notify("ClipLink Media", "Opened the last public MP4 URL.", NotificationType.Information, 3f);
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not open the public URL: {ex.Message}");
        }
    }

    private static void CheckSetup()
    {
        _ytDlpPath = ResolveYtDlpPath();
        Directory.CreateDirectory(_dataDirectory);
        Directory.CreateDirectory(_downloadsDirectory);

        bool ytDlpReady = File.Exists(_ytDlpPath);
        bool fusionReady = typeof(NetworkPlayer).Assembly != null;
        string status = $"yt-dlp: {(ytDlpReady ? "ready" : "missing")}; Fusion: {(fusionReady ? "ready" : "missing")}; expiry: {RetentionDisplay(_settings.LitterboxRetention)}.";
        MelonLogger.Msg($"Setup check - {status} Data: {_dataDirectory}");
        Notify("ClipLink setup check", status, ytDlpReady && fusionReady ? NotificationType.Success : NotificationType.Warning, 7f);
    }

    private static void ProcessVideoJob(string youtubeUrl, bool uploadPublicly, string retention, CancellationTokenSource cancellation)
    {
        string? jobDirectory = null;
        CancellationToken token = cancellation.Token;
        try
        {
            jobDirectory = CreateJobDirectory();
            string mp4Path = DownloadVideo(youtubeUrl, jobDirectory, token);
            token.ThrowIfCancellationRequested();
            string fileSize = FormatBytes(new FileInfo(mp4Path).Length);

            if (!uploadPublicly)
            {
                string savedPath = SaveDownloadedVideo(mp4Path, token);
                token.ThrowIfCancellationRequested();
                MainThreadActions.Enqueue(() =>
                {
                    MelonLogger.Msg($"Local MP4 saved: {savedPath}");
                    Notify("Download complete", $"Saved {Path.GetFileName(savedPath)} ({fileSize}) in ClipLink Downloads.", NotificationType.Success, 7f);
                });
                return;
            }

            MainThreadActions.Enqueue(() =>
            {
                MelonLogger.Msg($"Download finished: {Path.GetFileName(mp4Path)} ({fileSize}). Uploading for {RetentionDisplay(retention)}...");
                Notify("Download finished", $"{fileSize} MP4 ready. Uploading for {RetentionDisplay(retention)}...", NotificationType.Information, 5f);
            });

            string publicUrl = UploadToLitterbox(mp4Path, retention, token);
            token.ThrowIfCancellationRequested();
            DateTimeOffset createdUtc = DateTimeOffset.UtcNow;
            DateTimeOffset expiresUtc = createdUtc.Add(RetentionDuration(retention));
            MainThreadActions.Enqueue(() =>
            {
                _lastPublicUrl = publicUrl;
                GUIUtility.systemCopyBuffer = publicUrl;
                LinkHistory.RemoveAll(entry => string.Equals(entry.DirectUrl, publicUrl, StringComparison.OrdinalIgnoreCase));
                LinkHistory.Insert(0, new LinkHistoryEntry
                {
                    DirectUrl = publicUrl,
                    SourceUrl = youtubeUrl,
                    CreatedUtc = createdUtc,
                    ExpiresUtc = expiresUtc,
                });
                while (LinkHistory.Count > MaximumHistoryEntries)
                    LinkHistory.RemoveAt(LinkHistory.Count - 1);
                RefreshHistoryPage();
                MelonLogger.Msg($"Public MP4 URL copied: {publicUrl}");
                Notify("ClipLink Media finished", $"{RetentionDisplay(retention)} MP4 URL copied. Expires {expiresUtc.LocalDateTime:g}.", NotificationType.Success, 7f);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            MelonLogger.Msg("Video job cancelled by the user.");
            MainThreadActions.Enqueue(() => Notify("ClipLink Media", "Download/upload job cancelled.", NotificationType.Warning, 4f));
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Video job failed: {ex}");
            FailOnMainThread(ex.Message);
        }
        finally
        {
            if (!string.IsNullOrEmpty(jobDirectory))
            {
                try { Directory.Delete(jobDirectory, recursive: true); }
                catch (Exception ex) { MelonLogger.Warning($"Could not remove temporary video: {ex.Message}"); }
            }

            lock (JobGate)
            {
                if (ReferenceEquals(_jobCancellation, cancellation))
                    _jobCancellation = null;
            }
            cancellation.Dispose();
            Interlocked.Exchange(ref _jobRunning, 0);
        }
    }

    private static string DownloadVideo(string youtubeUrl, string jobDirectory, CancellationToken token)
    {
        if (!File.Exists(_ytDlpPath))
            throw new FileNotFoundException("yt-dlp.exe is missing. Download it from the official GitHub link in BoneMenu and put it in the ClipLinkMedia folder.", _ytDlpPath);

        string resultFile = Path.Combine(jobDirectory, "download-result.txt");
        var startInfo = new ProcessStartInfo
        {
            FileName = _ytDlpPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = jobDirectory,
        };
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--windows-filenames");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtube:player_client=android_vr,android,ios");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("b[ext=mp4]/b");
        startInfo.ArgumentList.Add("--max-filesize");
        startInfo.ArgumentList.Add("1000M");
        startInfo.ArgumentList.Add("--print-to-file");
        startInfo.ArgumentList.Add("after_move:%(filepath)s");
        startInfo.ArgumentList.Add(resultFile);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(Path.Combine(jobDirectory, "%(title).80B [%(id)s].%(ext)s"));
        startInfo.ArgumentList.Add(youtubeUrl);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("yt-dlp did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenRegistration registration = token.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { }
        });
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        token.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Download failed: {LastPart(stderr.Result.Trim(), 500)}");

        string? mp4Path = File.Exists(resultFile)
            ? File.ReadLines(resultFile).LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
            : null;
        if (string.IsNullOrEmpty(mp4Path) || !File.Exists(mp4Path))
            throw new InvalidOperationException("The download finished but no MP4 file was found.");
        if (!string.Equals(Path.GetExtension(mp4Path), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("YouTube did not provide a compatible MP4 for this video.");

        long fileSize = new FileInfo(mp4Path).Length;
        if (fileSize > MaxUploadBytes)
            throw new InvalidOperationException("The MP4 is larger than ClipLink Media's 1 GB safety limit.");

        return mp4Path;
    }

    private static string SaveDownloadedVideo(string mp4Path, CancellationToken token)
    {
        Directory.CreateDirectory(_downloadsDirectory);
        string destination = UniqueFilePath(_downloadsDirectory, Path.GetFileName(mp4Path));
        try
        {
            using FileStream source = File.OpenRead(mp4Path);
            using FileStream target = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyToAsync(target, 1024 * 1024, token).GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested();
            return destination;
        }
        catch
        {
            try { if (File.Exists(destination)) File.Delete(destination); }
            catch { }
            throw;
        }
    }

    private static string UniqueFilePath(string directory, string fileName)
    {
        string safeName = string.IsNullOrWhiteSpace(fileName) ? $"ClipLink-{DateTime.Now:yyyyMMdd-HHmmss}.mp4" : fileName;
        string destination = Path.Combine(directory, safeName);
        if (!File.Exists(destination)) return destination;

        string stem = Path.GetFileNameWithoutExtension(safeName);
        string extension = Path.GetExtension(safeName);
        for (int number = 2; number < 1000; number++)
        {
            destination = Path.Combine(directory, $"{stem} ({number}){extension}");
            if (!File.Exists(destination)) return destination;
        }
        return Path.Combine(directory, $"{stem}-{Guid.NewGuid():N}{extension}");
    }

    private static string UploadToLitterbox(string mp4Path, string retention, CancellationToken token)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("fileupload"), "reqtype");
        form.Add(new StringContent(retention), "time");

        var fileContent = new StreamContent(File.OpenRead(mp4Path));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(fileContent, "fileToUpload", Path.GetFileName(mp4Path));

        using HttpResponseMessage response = UploadHttpClient.PostAsync(LitterboxUploadUrl, form, token).GetAwaiter().GetResult();
        string responseBody = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult().Trim();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Upload failed ({(int)response.StatusCode}): {LastPart(responseBody, 300)}");
        if (!Uri.TryCreate(responseBody, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Litterbox returned an invalid URL: {LastPart(responseBody, 300)}");

        return uri.AbsoluteUri;
    }

    private static void Warn(string message)
    {
        MelonLogger.Warning(message);
        Notify("ClipLink Media", message, NotificationType.Warning, 5f);
    }

    private static void FailOnMainThread(string message)
    {
        string shortMessage = LastPart(message, 180);
        MainThreadActions.Enqueue(() => Notify("ClipLink Media error", shortMessage, NotificationType.Error, 7f));
    }

    private static void Notify(string title, string message, NotificationType type, float seconds)
    {
        Notifier.Send(new Notification
        {
            Title = new NotificationText(title),
            Message = new NotificationText(message),
            ShowTitleOnPopup = true,
            PopupLength = seconds,
            Type = type,
        });
    }

    private static bool IsYouTubeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return false;
        string host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal) || host == "youtu.be";
    }

    private static bool IsHttpsUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    private static bool IsSupportedRetention(string? value)
    {
        return value is "1h" or "12h" or "24h" or "72h";
    }

    private static string RetentionDisplay(string retention)
    {
        return retention switch
        {
            "1h" => "1 hour",
            "12h" => "12 hours",
            "24h" => "24 hours",
            _ => "72 hours",
        };
    }

    private static TimeSpan RetentionDuration(string retention)
    {
        return retention switch
        {
            "1h" => TimeSpan.FromHours(1),
            "12h" => TimeSpan.FromHours(12),
            "24h" => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(72),
        };
    }

    private static string GetYouTubeVideoId(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri)) return "video";
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Trim('/');

        Match match = Regex.Match(uri.Query, "(?:^|[?&])v=(?<id>[A-Za-z0-9_-]{11})(?:&|$)");
        return match.Success ? match.Groups["id"].Value : "video";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):0.00} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024d * 1024):0.0} MB";
        if (bytes >= 1024L) return $"{bytes / 1024d:0.0} KB";
        return $"{bytes} B";
    }

    private static string ResolveYtDlpPath()
    {
        string legacyPath = Path.Combine(_dataDirectory, "yt-dlp.exe");
        if (File.Exists(legacyPath)) return legacyPath;

        string userDataDirectory = MelonEnvironment.UserDataDirectory;
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

    private static string CreateJobDirectory()
    {
        string jobsRoot = ChooseJobsRoot();
        Directory.CreateDirectory(jobsRoot);
        string jobDirectory = Path.Combine(jobsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDirectory);
        return jobDirectory;
    }

    private static string ChooseJobsRoot()
    {
        string preferred = Path.Combine(Path.GetTempPath(), "ClipLinkMediaJobs");
        try
        {
            string root = Path.GetPathRoot(preferred) ?? string.Empty;
            if (!string.IsNullOrEmpty(root) && new DriveInfo(root).AvailableFreeSpace >= MinimumTemporarySpace)
                return preferred;
        }
        catch { }

        DriveInfo? fallback = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed && drive.AvailableFreeSpace >= MinimumTemporarySpace)
            .OrderByDescending(drive => drive.AvailableFreeSpace)
            .FirstOrDefault();
        return fallback == null ? preferred : Path.Combine(fallback.RootDirectory.FullName, "ClipLinkMediaJobs");
    }

    private static HttpClient CreateUploadHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClipLinkMedia/3.0.0");
        return client;
    }

    private static HttpClient CreateYouTubeHttpClient()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/127 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }

    private static string ShortMenuText(string value, int maxLength)
    {
        string clean = Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim();
        if (clean.Length <= maxLength) return clean;
        return clean[..Math.Max(1, maxLength - 3)] + "...";
    }

    private static string LastPart(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown error.";
        return value.Length <= maxLength ? value : value[^maxLength..];
    }
}

public sealed class ClipLinkSettings
{
    public string LitterboxRetention { get; set; } = "72h";
}

public sealed class LinkHistoryEntry
{
    public string DirectUrl { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
}

public sealed class OwnerTagElement : IPopupLayoutElement
{
    private readonly RigNameTag _tag = new()
    {
        Username = "<color=#FFD700>OWNER</color>",
        Color = Color.white,
        CrownVisible = false,
        Visible = true,
    };

    public int Priority => -100;
    public Transform Transform => _tag.Transform;

    public bool Visible
    {
        get => _tag.Visible;
        set => _tag.Visible = value;
    }

    public void Spawn(Transform parent) => _tag.Spawn(parent);
    public void Despawn() => _tag.Despawn();
}

public sealed class YouTubeSearchResult
{
    public YouTubeSearchResult(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }
    public string Title { get; }
    public string Url => $"https://www.youtube.com/watch?v={Id}";
}

public static class YouTubeSearchParser
{
    private static readonly Regex VideoResultPattern = new(
        "\\\"videoRenderer\\\":\\{\\\"videoId\\\":\\\"(?<id>[A-Za-z0-9_-]{11})\\\".*?\\\"title\\\":\\{\\\"runs\\\":\\[\\{\\\"text\\\":\\\"(?<title>(?:\\\\\\\\.|[^\\\"\\\\\\\\])*)\\\"",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static List<YouTubeSearchResult> Parse(string html, int limit)
    {
        var results = new List<YouTubeSearchResult>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in VideoResultPattern.Matches(html))
        {
            string id = match.Groups["id"].Value;
            if (!seenIds.Add(id)) continue;

            string rawTitle = match.Groups["title"].Value;
            string title;
            try { title = JsonSerializer.Deserialize<string>($"\"{rawTitle}\"") ?? rawTitle; }
            catch { title = rawTitle; }
            title = Regex.Replace(title, "\\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(title)) title = id;
            results.Add(new YouTubeSearchResult(id, title));
            if (results.Count >= limit) break;
        }
        return results;
    }
}
