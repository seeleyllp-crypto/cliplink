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
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(ClipLinkMedia.Core), "ClipLink Media", "4.0.0", "seeleyllp-crypto")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: AssemblyVersion("4.0.0.0")]
[assembly: AssemblyFileVersion("4.0.0.0")]

namespace ClipLinkMedia;

public sealed class Core : MelonMod
{
    private const ulong OwnerPlatformId = 76561199548494681UL;
    private const string YouTubeHome = "https://www.youtube.com/";
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string LitterboxUploadUrl = "https://litterbox.catbox.moe/resources/internals/api.php";
    private const string DefaultLitterboxRetention = "72h";
    private const int MaximumHistoryEntries = 8;
    private const int MaximumRecentVideos = 12;
    private const int MaximumRecentSearches = 8;
    private const int MaximumFavorites = 20;
    private const int MaximumQueuedVideos = 10;
    private const int MaximumDownloadLibraryEntries = 20;
    private const long MaxUploadBytes = 1_000_000_000L;
    private const long MinimumTemporarySpace = 1200L * 1024 * 1024;
    private static readonly ConcurrentQueue<Action> MainThreadActions = new();
    private static readonly HttpClient UploadHttpClient = CreateUploadHttpClient();
    private static readonly HttpClient YouTubeHttpClient = CreateYouTubeHttpClient();
    private static readonly object JobGate = new();
    private static readonly Stopwatch UtilityStopwatch = new();
    private static readonly System.Random UtilityRandom = new();
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
    private static Page? _recentVideosPage;
    private static Page? _searchHistoryPage;
    private static Page? _favoritesPage;
    private static Page? _queuePage;
    private static Page? _downloadsPage;
    private static ClipLinkSettings _settings = new();
    private static CancellationTokenSource? _jobCancellation;
    private static string _jobStatus = "Idle";
    private static string _lastPreviewSummary = string.Empty;
    private static string _draftNote = string.Empty;
    private static DateTimeOffset? _jobStartedUtc;
    private static DateTimeOffset? _countdownEndsUtc;
    private static string _countdownLabel = string.Empty;
    private static float _fpsWindowSeconds;
    private static float _measuredFps;
    private static int _fpsWindowFrames;
    private static bool _queueModeActive;
    private static bool _queueUploadPublicly;
    private static bool _rightsConfirmed;
    private static int _jobRunning;
    private static int _searchRunning;
    private static int _previewRunning;

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
        _draftNote = _settings.PersonalNote;

        BuildBoneMenu();
        InitializeFusionOwnerTag();
        MelonLogger.Msg($"All-in-one v4 ready with media tools and the BONELAB utility toolbox. Expiry: {_settings.LitterboxRetention}; quality: {_settings.VideoQuality}; {LinkHistory.Count} saved link(s); {_settings.Favorites.Count} favorite(s); {_settings.JobQueue.Count} queued.");
        MelonLogger.Msg("Fusion OWNER tag enabled. Players with ClipLink Media installed will see OWNER above the creator's head.");
        MelonLogger.Warning("Litterbox uploads are public and temporary. Upload only videos you own or have permission to share.");
        if (!File.Exists(_ytDlpPath))
            MelonLogger.Error("yt-dlp.exe is not installed. Use the BoneMenu GitHub download and folder buttons.");
    }

    public override void OnUpdate()
    {
        _fpsWindowFrames++;
        _fpsWindowSeconds += Time.unscaledDeltaTime;
        if (_fpsWindowSeconds >= 0.75f)
        {
            _measuredFps = _fpsWindowFrames / _fpsWindowSeconds;
            _fpsWindowFrames = 0;
            _fpsWindowSeconds = 0f;
        }

        if (_countdownEndsUtc.HasValue && DateTimeOffset.UtcNow >= _countdownEndsUtc.Value)
        {
            string label = _countdownLabel;
            _countdownEndsUtc = null;
            _countdownLabel = string.Empty;
            Notify("Countdown finished", $"{label} timer is complete.", NotificationType.Success, 8f);
            MelonLogger.Msg($"Countdown finished: {label}");
        }

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
        _recentVideosPage = browserPage.CreatePage("Recently selected videos", Color.cyan);
        RefreshRecentVideosPage();
        _searchHistoryPage = browserPage.CreatePage("Recent searches", Color.yellow);
        RefreshSearchHistoryPage();
        _favoritesPage = browserPage.CreatePage("Favorite videos", Color.magenta);
        RefreshFavoritesPage();
        browserPage.CreateFunction("Add copied video to favorites", Color.magenta, AddCopiedFavorite);
        browserPage.CreateFunction("Preview copied video", Color.green, PreviewCopiedVideo);
        browserPage.CreateFunction("Copy last preview info", Color.white, CopyLastPreview);

        page.CreateBool("I own / have permission", Color.yellow, false, value => _rightsConfirmed = value);
        page.CreateFunction("Make public MP4 URL", Color.green, StartClipboardJob);
        page.CreateFunction("Download MP4 only", Color.green, StartLocalDownloadJob);
        page.CreateFunction("Cancel current job", Color.red, CancelCurrentJob);

        _queuePage = page.CreatePage("Batch video queue", Color.yellow);
        RefreshQueuePage();

        _downloadsPage = page.CreatePage("Downloaded MP4 library", Color.cyan);
        RefreshDownloadsPage();

        BuildUtilityToolbox(page);

        Page repeatPage = page.CreatePage("Repeat last video", Color.green);
        repeatPage.CreateFunction("Make public URL again", Color.green, StartLastPublicJob);
        repeatPage.CreateFunction("Download MP4 again", Color.cyan, StartLastLocalJob);
        repeatPage.CreateFunction("Copy last YouTube link", Color.white, CopyLastSourceUrl);
        repeatPage.CreateFunction("Open last YouTube video", Color.red, OpenLastSourceUrl);

        Page qualityPage = page.CreatePage("MP4 quality", Color.magenta);
        qualityPage.CreateFunction("Show current quality", Color.white, ShowCurrentQuality);
        qualityPage.CreateFunction("Use best MP4", Color.cyan, () => SetVideoQuality("Best"));
        qualityPage.CreateFunction("Use 720p", Color.cyan, () => SetVideoQuality("720p"));
        qualityPage.CreateFunction("Use 480p", Color.cyan, () => SetVideoQuality("480p"));
        qualityPage.CreateFunction("Use 360p", Color.cyan, () => SetVideoQuality("360p"));

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
        toolsPage.CreateFunction("Copy downloads path", Color.white, CopyDownloadsPath);
        toolsPage.CreateFunction("Inspect copied URL", Color.white, InspectCopiedUrl);
        toolsPage.CreateFunction("Open copied YouTube URL", Color.red, OpenCopiedYouTubeUrl);
        toolsPage.CreateFunction("Show usage statistics", Color.magenta, ShowStatistics);
        toolsPage.CreateFunction("Show job and queue status", Color.yellow, ShowJobStatus);
        toolsPage.CreateFunction("Copy setup report", Color.green, CopySetupReport);
    }

    private static void BuildUtilityToolbox(Page rootPage)
    {
        Page utilities = rootPage.CreatePage("BONELAB utility toolbox", Color.magenta);

        Page dashboard = utilities.CreatePage("Session dashboard", Color.cyan);
        dashboard.CreateFunction("Show clock", Color.white, ShowClock);
        dashboard.CreateFunction("Show scene and uptime", Color.green, ShowSceneAndUptime);
        dashboard.CreateFunction("Show measured FPS", Color.yellow, ShowMeasuredFps);
        dashboard.CreateFunction("Copy headset position", Color.cyan, CopyHeadsetPosition);
        dashboard.CreateFunction("Copy session report", Color.green, CopySessionReport);
        dashboard.CreateFunction("Copy device report", Color.magenta, CopyDeviceReport);

        Page timers = utilities.CreatePage("Stopwatch and timers", Color.yellow);
        timers.CreateFunction("Start / resume stopwatch", Color.green, StartUtilityStopwatch);
        timers.CreateFunction("Pause stopwatch", Color.yellow, PauseUtilityStopwatch);
        timers.CreateFunction("Show stopwatch", Color.white, ShowUtilityStopwatch);
        timers.CreateFunction("Reset stopwatch", Color.red, ResetUtilityStopwatch);
        timers.CreateFunction("Start 1-minute timer", Color.cyan, () => StartCountdown(1));
        timers.CreateFunction("Start 5-minute timer", Color.cyan, () => StartCountdown(5));
        timers.CreateFunction("Start 10-minute timer", Color.cyan, () => StartCountdown(10));
        timers.CreateFunction("Start 15-minute timer", Color.cyan, () => StartCountdown(15));
        timers.CreateFunction("Show countdown", Color.white, ShowCountdown);
        timers.CreateFunction("Cancel countdown", Color.red, CancelCountdown);

        Page notes = utilities.CreatePage("Notes and tally counter", Color.green);
        StringElement noteElement = notes.CreateString("Personal note", Color.white, _draftNote, value => _draftNote = value);
        noteElement.SetTooltip("Use the keyboard, type a note, press Enter, then choose Save note.");
        notes.CreateFunction("Save note", Color.green, SavePersonalNote);
        notes.CreateFunction("Copy saved note", Color.cyan, CopyPersonalNote);
        notes.CreateFunction("Clear saved note", Color.red, ClearPersonalNote);
        notes.CreateFunction("Show tally", Color.white, ShowTally);
        notes.CreateFunction("Tally +1", Color.green, () => ChangeTally(1));
        notes.CreateFunction("Tally -1", Color.yellow, () => ChangeTally(-1));
        notes.CreateFunction("Reset tally", Color.red, ResetTally);

        Page random = utilities.CreatePage("Dice and random tools", Color.magenta);
        random.CreateFunction("Flip a coin", Color.yellow, FlipCoin);
        random.CreateFunction("Roll D6", Color.cyan, () => RollDice(6));
        random.CreateFunction("Roll D10", Color.cyan, () => RollDice(10));
        random.CreateFunction("Roll D20", Color.cyan, () => RollDice(20));
        random.CreateFunction("Random 1 to 100", Color.green, RandomOneToHundred);
        random.CreateFunction("Pick yes or no", Color.white, PickYesOrNo);

        Page localSettings = utilities.CreatePage("Local audio and FPS", Color.blue);
        localSettings.CreateFunction("Show audio and FPS", Color.white, ShowLocalSettings);
        localSettings.CreateFunction("Mute local audio", Color.red, () => SetAudioVolume(0f));
        localSettings.CreateFunction("Audio 25%", Color.yellow, () => SetAudioVolume(0.25f));
        localSettings.CreateFunction("Audio 50%", Color.yellow, () => SetAudioVolume(0.5f));
        localSettings.CreateFunction("Audio 75%", Color.green, () => SetAudioVolume(0.75f));
        localSettings.CreateFunction("Audio 100%", Color.green, () => SetAudioVolume(1f));
        localSettings.CreateFunction("Target 72 FPS", Color.cyan, () => SetTargetFrameRate(72));
        localSettings.CreateFunction("Target 90 FPS", Color.cyan, () => SetTargetFrameRate(90));
        localSettings.CreateFunction("Target 120 FPS", Color.cyan, () => SetTargetFrameRate(120));
        localSettings.CreateFunction("Unlimited target FPS", Color.magenta, () => SetTargetFrameRate(-1));
        localSettings.CreateFunction("VSync off", Color.yellow, () => SetVSync(false));
        localSettings.CreateFunction("VSync on", Color.green, () => SetVSync(true));

        Page clipboard = utilities.CreatePage("Clipboard helpers", Color.gray);
        clipboard.CreateFunction("Copy local timestamp", Color.white, CopyLocalTimestamp);
        clipboard.CreateFunction("Copy UTC timestamp", Color.white, CopyUtcTimestamp);
        clipboard.CreateFunction("Copy current scene", Color.cyan, CopyCurrentScene);
        clipboard.CreateFunction("Copy headset position", Color.cyan, CopyHeadsetPosition);
        clipboard.CreateFunction("Copy session report", Color.green, CopySessionReport);

        Page notificationTests = utilities.CreatePage("Notification tester", Color.yellow);
        notificationTests.CreateFunction("Information notification", Color.cyan, () => Notify("Information test", "ClipLink Media notifications are working.", NotificationType.Information, 4f));
        notificationTests.CreateFunction("Success notification", Color.green, () => Notify("Success test", "The success notification is working.", NotificationType.Success, 4f));
        notificationTests.CreateFunction("Warning notification", Color.yellow, () => Notify("Warning test", "The warning notification is working.", NotificationType.Warning, 4f));
        notificationTests.CreateFunction("Error notification", Color.red, () => Notify("Error test", "The error notification is working.", NotificationType.Error, 4f));
    }

    private static void ShowClock()
    {
        Notify("Current time", $"Local: {DateTime.Now:F} | UTC: {DateTime.UtcNow:u}", NotificationType.Information, 7f);
    }

    private static void ShowSceneAndUptime()
    {
        string scene = SceneManager.GetActiveScene().name;
        string uptime = FormatDuration(TimeSpan.FromSeconds(Math.Max(0f, Time.realtimeSinceStartup)));
        Notify("BONELAB session", $"Scene: {scene}; game uptime: {uptime}.", NotificationType.Information, 6f);
    }

    private static void ShowMeasuredFps()
    {
        string target = Application.targetFrameRate < 0 ? "unlimited" : Application.targetFrameRate.ToString();
        Notify("Local FPS", $"Measured: {_measuredFps:0.0} FPS; target: {target}; VSync: {(QualitySettings.vSyncCount > 0 ? "on" : "off")}.", NotificationType.Information, 6f);
    }

    private static bool TryGetHeadsetPosition(out Vector3 position)
    {
        Camera? camera = Camera.main;
        if (camera == null)
        {
            position = Vector3.zero;
            return false;
        }
        position = camera.transform.position;
        return true;
    }

    private static string PositionText(Vector3 position)
    {
        return $"X {position.x:0.000}, Y {position.y:0.000}, Z {position.z:0.000}";
    }

    private static void CopyHeadsetPosition()
    {
        if (!TryGetHeadsetPosition(out Vector3 position))
        {
            Warn("The active headset camera is not ready yet.");
            return;
        }
        string text = PositionText(position);
        GUIUtility.systemCopyBuffer = text;
        Notify("Headset position copied", text, NotificationType.Success, 5f);
    }

    private static void CopySessionReport()
    {
        string position = TryGetHeadsetPosition(out Vector3 value) ? PositionText(value) : "Unavailable";
        string target = Application.targetFrameRate < 0 ? "Unlimited" : Application.targetFrameRate.ToString();
        string report = string.Join(Environment.NewLine, new[]
        {
            "BONELAB local session report",
            $"Local time: {DateTime.Now:F}",
            $"Scene: {SceneManager.GetActiveScene().name}",
            $"Game uptime: {FormatDuration(TimeSpan.FromSeconds(Math.Max(0f, Time.realtimeSinceStartup)))}",
            $"Headset position: {position}",
            $"Measured FPS: {_measuredFps:0.0}",
            $"Target FPS: {target}",
            $"VSync: {(QualitySettings.vSyncCount > 0 ? "On" : "Off")}",
            $"Local audio: {AudioListener.volume * 100f:0}%",
            $"Unity: {Application.unityVersion}",
            $"ClipLink Media: 4.0.0",
        });
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("Session report", "Local BONELAB session report copied.", NotificationType.Success, 5f);
    }

    private static void CopyDeviceReport()
    {
        string report = string.Join(Environment.NewLine, new[]
        {
            "BONELAB device report",
            $"Device: {SystemInfo.deviceModel}",
            $"Operating system: {SystemInfo.operatingSystem}",
            $"Processor: {SystemInfo.processorType} ({SystemInfo.processorCount} logical cores)",
            $"System memory: {SystemInfo.systemMemorySize} MB",
            $"Graphics: {SystemInfo.graphicsDeviceName}",
            $"Graphics memory: {SystemInfo.graphicsMemorySize} MB",
            $"Graphics API: {SystemInfo.graphicsDeviceType}",
            $"Unity: {Application.unityVersion}",
        });
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("Device report", "Local device and graphics report copied.", NotificationType.Success, 5f);
    }

    private static void StartUtilityStopwatch()
    {
        UtilityStopwatch.Start();
        Notify("Stopwatch", $"Running from {FormatDuration(UtilityStopwatch.Elapsed)}.", NotificationType.Success, 3f);
    }

    private static void PauseUtilityStopwatch()
    {
        UtilityStopwatch.Stop();
        Notify("Stopwatch paused", FormatDuration(UtilityStopwatch.Elapsed), NotificationType.Information, 4f);
    }

    private static void ShowUtilityStopwatch()
    {
        Notify("Stopwatch", $"{FormatDuration(UtilityStopwatch.Elapsed)} ({(UtilityStopwatch.IsRunning ? "running" : "paused")}).", NotificationType.Information, 4f);
    }

    private static void ResetUtilityStopwatch()
    {
        UtilityStopwatch.Reset();
        Notify("Stopwatch", "Reset to zero.", NotificationType.Success, 3f);
    }

    private static void StartCountdown(int minutes)
    {
        _countdownEndsUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
        _countdownLabel = $"{minutes}-minute";
        Notify("Countdown started", $"{minutes} minute(s); ends at {_countdownEndsUtc.Value.LocalDateTime:T}.", NotificationType.Success, 5f);
    }

    private static void ShowCountdown()
    {
        if (!_countdownEndsUtc.HasValue)
        {
            Warn("No countdown is running.");
            return;
        }
        TimeSpan remaining = _countdownEndsUtc.Value - DateTimeOffset.UtcNow;
        Notify("Countdown", $"{FormatDuration(remaining)} remaining; ends at {_countdownEndsUtc.Value.LocalDateTime:T}.", NotificationType.Information, 5f);
    }

    private static void CancelCountdown()
    {
        _countdownEndsUtc = null;
        _countdownLabel = string.Empty;
        Notify("Countdown", "Countdown cancelled.", NotificationType.Success, 3f);
    }

    private static void SavePersonalNote()
    {
        _settings.PersonalNote = _draftNote.Trim();
        SaveSettings();
        Notify("Personal note", "Note saved for future game launches.", NotificationType.Success, 4f);
    }

    private static void CopyPersonalNote()
    {
        if (string.IsNullOrWhiteSpace(_settings.PersonalNote))
        {
            Warn("No personal note is saved.");
            return;
        }
        GUIUtility.systemCopyBuffer = _settings.PersonalNote;
        Notify("Personal note", "Saved note copied.", NotificationType.Success, 3f);
    }

    private static void ClearPersonalNote()
    {
        _draftNote = string.Empty;
        _settings.PersonalNote = string.Empty;
        SaveSettings();
        Notify("Personal note", "Saved note cleared.", NotificationType.Success, 3f);
    }

    private static void ShowTally()
    {
        Notify("Tally counter", _settings.TallyCount.ToString(), NotificationType.Information, 4f);
    }

    private static void ChangeTally(int change)
    {
        _settings.TallyCount += change;
        SaveSettings();
        Notify("Tally counter", _settings.TallyCount.ToString(), NotificationType.Success, 3f);
    }

    private static void ResetTally()
    {
        _settings.TallyCount = 0;
        SaveSettings();
        Notify("Tally counter", "Reset to 0.", NotificationType.Success, 3f);
    }

    private static void FlipCoin()
    {
        Notify("Coin flip", UtilityRandom.Next(2) == 0 ? "Heads" : "Tails", NotificationType.Information, 4f);
    }

    private static void RollDice(int sides)
    {
        Notify($"D{sides} roll", UtilityRandom.Next(1, sides + 1).ToString(), NotificationType.Information, 4f);
    }

    private static void RandomOneToHundred()
    {
        Notify("Random 1-100", UtilityRandom.Next(1, 101).ToString(), NotificationType.Information, 4f);
    }

    private static void PickYesOrNo()
    {
        Notify("Yes or no", UtilityRandom.Next(2) == 0 ? "Yes" : "No", NotificationType.Information, 4f);
    }

    private static void ShowLocalSettings()
    {
        string target = Application.targetFrameRate < 0 ? "unlimited" : Application.targetFrameRate.ToString();
        Notify("Local audio and FPS", $"Audio: {AudioListener.volume * 100f:0}%; measured: {_measuredFps:0.0} FPS; target: {target}; VSync: {(QualitySettings.vSyncCount > 0 ? "on" : "off")}.", NotificationType.Information, 7f);
    }

    private static void SetAudioVolume(float volume)
    {
        AudioListener.volume = Mathf.Clamp01(volume);
        Notify("Local audio", $"Volume set to {AudioListener.volume * 100f:0}%.", NotificationType.Success, 3f);
    }

    private static void SetTargetFrameRate(int target)
    {
        Application.targetFrameRate = target;
        Notify("Target FPS", target < 0 ? "Set to unlimited." : $"Set to {target} FPS.", NotificationType.Success, 3f);
    }

    private static void SetVSync(bool enabled)
    {
        QualitySettings.vSyncCount = enabled ? 1 : 0;
        Notify("Local VSync", enabled ? "VSync enabled." : "VSync disabled.", NotificationType.Success, 3f);
    }

    private static void CopyLocalTimestamp()
    {
        GUIUtility.systemCopyBuffer = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        Notify("Clipboard helper", "Local timestamp copied.", NotificationType.Success, 3f);
    }

    private static void CopyUtcTimestamp()
    {
        GUIUtility.systemCopyBuffer = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        Notify("Clipboard helper", "UTC timestamp copied.", NotificationType.Success, 3f);
    }

    private static void CopyCurrentScene()
    {
        GUIUtility.systemCopyBuffer = SceneManager.GetActiveScene().name;
        Notify("Clipboard helper", "Current scene name copied.", NotificationType.Success, 3f);
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
        if (!IsSupportedVideoQuality(_settings.VideoQuality))
            _settings.VideoQuality = "Best";
        _settings.LastSourceUrl ??= string.Empty;
        _settings.PersonalNote ??= string.Empty;
        _settings.RecentSearches ??= new List<string>();
        _settings.RecentVideos ??= new List<RecentVideoEntry>();
        _settings.Favorites ??= new List<RecentVideoEntry>();
        _settings.JobQueue ??= new List<RecentVideoEntry>();
        _settings.RecentSearches = _settings.RecentSearches
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumRecentSearches)
            .ToList();
        _settings.RecentVideos = _settings.RecentVideos
            .Where(entry => IsYouTubeUrl(entry.Url))
            .OrderByDescending(entry => entry.SelectedUtc)
            .Take(MaximumRecentVideos)
            .ToList();
        _settings.Favorites = _settings.Favorites
            .Where(entry => IsYouTubeUrl(entry.Url))
            .GroupBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(entry => entry.SelectedUtc)
            .Take(MaximumFavorites)
            .ToList();
        _settings.JobQueue = _settings.JobQueue
            .Where(entry => IsYouTubeUrl(entry.Url))
            .Take(MaximumQueuedVideos)
            .ToList();

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

    private static void SetVideoQuality(string quality)
    {
        if (!IsSupportedVideoQuality(quality)) return;
        _settings.VideoQuality = quality;
        SaveSettings();
        MelonLogger.Msg($"MP4 quality set to {quality}.");
        Notify("MP4 quality", $"New jobs will use {quality} quality.", NotificationType.Success, 4f);
    }

    private static void ShowCurrentQuality()
    {
        Notify("MP4 quality", $"Current setting: {_settings.VideoQuality}.", NotificationType.Information, 4f);
    }

    private static void AddRecentSearch(string query)
    {
        _settings.RecentSearches.RemoveAll(saved => string.Equals(saved, query, StringComparison.OrdinalIgnoreCase));
        _settings.RecentSearches.Insert(0, query);
        while (_settings.RecentSearches.Count > MaximumRecentSearches)
            _settings.RecentSearches.RemoveAt(_settings.RecentSearches.Count - 1);
        SaveSettings();
        RefreshSearchHistoryPage();
    }

    private static void RefreshSearchHistoryPage()
    {
        if (_searchHistoryPage == null) return;

        _searchHistoryPage.RemoveAll();
        if (_settings.RecentSearches.Count == 0)
        {
            _searchHistoryPage.CreateFunction("No recent searches", Color.gray, () => Warn("Run a YouTube search first."));
        }
        else
        {
            foreach (string query in _settings.RecentSearches.ToArray())
            {
                string savedQuery = query;
                _searchHistoryPage.CreateFunction(ShortMenuText(query, 64), Color.white, () => RunSavedSearch(savedQuery));
            }
        }

        _searchHistoryPage.CreateFunction("Clear search history", Color.red, ClearSearchHistory);
    }

    private static void RunSavedSearch(string query)
    {
        _searchQuery = query;
        SearchYouTube();
    }

    private static void ClearSearchHistory()
    {
        _settings.RecentSearches.Clear();
        SaveSettings();
        RefreshSearchHistoryPage();
        Notify("YouTube browser", "Search history cleared.", NotificationType.Success, 3f);
    }

    private static void AddRecentVideo(string title, string url)
    {
        _settings.RecentVideos.RemoveAll(entry => string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase));
        _settings.RecentVideos.Insert(0, new RecentVideoEntry
        {
            Title = title,
            Url = url,
            SelectedUtc = DateTimeOffset.UtcNow,
        });
        while (_settings.RecentVideos.Count > MaximumRecentVideos)
            _settings.RecentVideos.RemoveAt(_settings.RecentVideos.Count - 1);
        _settings.LastSourceUrl = url;
        SaveSettings();
        RefreshRecentVideosPage();
    }

    private static void RefreshRecentVideosPage()
    {
        if (_recentVideosPage == null) return;

        _recentVideosPage.RemoveAll();
        if (_settings.RecentVideos.Count == 0)
        {
            _recentVideosPage.CreateFunction("No selected videos", Color.gray, () => Warn("Select a YouTube search result first."));
        }
        else
        {
            foreach (RecentVideoEntry entry in _settings.RecentVideos.ToArray())
            {
                RecentVideoEntry selectedEntry = entry;
                FunctionElement button = _recentVideosPage.CreateFunction(ShortMenuText(entry.Title, 64), Color.white, () => CopyRecentVideo(selectedEntry));
                button.SetTooltip($"Selected: {entry.SelectedUtc.LocalDateTime:g}\n{entry.Url}");
            }
        }

        _recentVideosPage.CreateFunction("Clear selected videos", Color.red, ClearRecentVideos);
    }

    private static void CopyRecentVideo(RecentVideoEntry entry)
    {
        GUIUtility.systemCopyBuffer = entry.Url;
        _settings.LastSourceUrl = entry.Url;
        SaveSettings();
        Notify("YouTube link copied", ShortMenuText(entry.Title, 100), NotificationType.Success, 4f);
    }

    private static void ClearRecentVideos()
    {
        _settings.RecentVideos.Clear();
        _settings.LastSourceUrl = string.Empty;
        SaveSettings();
        RefreshRecentVideosPage();
        Notify("YouTube browser", "Selected-video history cleared.", NotificationType.Success, 3f);
    }

    private static void AddCopiedFavorite()
    {
        string url = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        if (!IsYouTubeUrl(url))
        {
            Warn("Copy or select a YouTube video before adding a favorite.");
            return;
        }

        RecentVideoEntry? recent = _settings.RecentVideos.FirstOrDefault(entry => string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase));
        string title = recent?.Title ?? $"YouTube {GetYouTubeVideoId(url)}";
        _settings.Favorites.RemoveAll(entry => string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase));
        _settings.Favorites.Insert(0, new RecentVideoEntry { Title = title, Url = url, SelectedUtc = DateTimeOffset.UtcNow });
        while (_settings.Favorites.Count > MaximumFavorites)
            _settings.Favorites.RemoveAt(_settings.Favorites.Count - 1);
        _settings.LastSourceUrl = url;
        SaveSettings();
        RefreshFavoritesPage();
        Notify("Favorite saved", ShortMenuText(title, 100), NotificationType.Success, 4f);
    }

    private static void RefreshFavoritesPage()
    {
        if (_favoritesPage == null) return;

        _favoritesPage.RemoveAll();
        if (_settings.Favorites.Count == 0)
        {
            _favoritesPage.CreateFunction("No favorite videos", Color.gray, () => Warn("Copy a video, then choose Add copied video to favorites."));
        }
        else
        {
            foreach (RecentVideoEntry entry in _settings.Favorites.ToArray())
            {
                RecentVideoEntry selectedEntry = entry;
                FunctionElement button = _favoritesPage.CreateFunction(ShortMenuText(entry.Title, 64), Color.white, () => CopyFavorite(selectedEntry));
                button.SetTooltip(entry.Url);
            }
        }
        _favoritesPage.CreateFunction("Remove copied favorite", Color.yellow, RemoveCopiedFavorite);
        _favoritesPage.CreateFunction("Clear all favorites", Color.red, ClearFavorites);
    }

    private static void CopyFavorite(RecentVideoEntry entry)
    {
        GUIUtility.systemCopyBuffer = entry.Url;
        _settings.LastSourceUrl = entry.Url;
        SaveSettings();
        Notify("Favorite link copied", ShortMenuText(entry.Title, 100), NotificationType.Success, 4f);
    }

    private static void RemoveCopiedFavorite()
    {
        string url = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        int removed = _settings.Favorites.RemoveAll(entry => string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            Warn("The copied URL is not in favorites.");
            return;
        }
        SaveSettings();
        RefreshFavoritesPage();
        Notify("Favorite removed", "Removed the copied video from favorites.", NotificationType.Success, 3f);
    }

    private static void ClearFavorites()
    {
        _settings.Favorites.Clear();
        SaveSettings();
        RefreshFavoritesPage();
        Notify("Favorites", "All favorite videos cleared.", NotificationType.Success, 3f);
    }

    private static void RefreshQueuePage()
    {
        if (_queuePage == null) return;

        _queuePage.RemoveAll();
        _queuePage.CreateFunction("Add copied video", Color.green, AddCopiedToQueue);
        _queuePage.CreateFunction("Show queue status", Color.white, ShowJobStatus);
        _queuePage.CreateFunction("Start public-link queue", Color.green, () => StartQueue(uploadPublicly: true));
        _queuePage.CreateFunction("Start local-download queue", Color.cyan, () => StartQueue(uploadPublicly: false));
        _queuePage.CreateFunction("Remove next queued video", Color.yellow, RemoveNextQueuedVideo);
        _queuePage.CreateFunction("Clear pending queue", Color.red, ClearQueue);

        foreach (RecentVideoEntry entry in _settings.JobQueue.ToArray())
        {
            RecentVideoEntry selectedEntry = entry;
            FunctionElement button = _queuePage.CreateFunction($"Queued: {ShortMenuText(entry.Title, 54)}", Color.white, () => CopyQueuedVideo(selectedEntry));
            button.SetTooltip(entry.Url);
        }
    }

    private static void AddCopiedToQueue()
    {
        string url = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        if (!IsYouTubeUrl(url))
        {
            Warn("Copy or select a YouTube video before adding it to the queue.");
            return;
        }
        if (_settings.JobQueue.Count >= MaximumQueuedVideos)
        {
            Warn($"The batch queue is full ({MaximumQueuedVideos} videos).");
            return;
        }
        if (_settings.JobQueue.Any(entry => string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase)))
        {
            Warn("That video is already in the queue.");
            return;
        }

        RecentVideoEntry? recent = _settings.RecentVideos.FirstOrDefault(entry => string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase));
        string title = recent?.Title ?? $"YouTube {GetYouTubeVideoId(url)}";
        _settings.JobQueue.Add(new RecentVideoEntry { Title = title, Url = url, SelectedUtc = DateTimeOffset.UtcNow });
        SaveSettings();
        RefreshQueuePage();
        Notify("Batch queue", $"Added {ShortMenuText(title, 90)} ({_settings.JobQueue.Count}/{MaximumQueuedVideos}).", NotificationType.Success, 4f);
    }

    private static void CopyQueuedVideo(RecentVideoEntry entry)
    {
        GUIUtility.systemCopyBuffer = entry.Url;
        _settings.LastSourceUrl = entry.Url;
        SaveSettings();
        Notify("Queued link copied", ShortMenuText(entry.Title, 100), NotificationType.Success, 3f);
    }

    private static void RemoveNextQueuedVideo()
    {
        if (_settings.JobQueue.Count == 0)
        {
            Warn("The batch queue is empty.");
            return;
        }
        string title = _settings.JobQueue[0].Title;
        _settings.JobQueue.RemoveAt(0);
        SaveSettings();
        RefreshQueuePage();
        Notify("Batch queue", $"Removed {ShortMenuText(title, 90)}.", NotificationType.Success, 3f);
    }

    private static void ClearQueue()
    {
        _queueModeActive = false;
        _settings.JobQueue.Clear();
        SaveSettings();
        RefreshQueuePage();
        Notify("Batch queue", "Pending videos cleared. The current job was not cancelled.", NotificationType.Success, 4f);
    }

    private static void StartQueue(bool uploadPublicly)
    {
        if (!_rightsConfirmed)
        {
            Warn("Turn on 'I own / have permission' before starting the batch queue.");
            return;
        }
        if (Volatile.Read(ref _jobRunning) != 0)
        {
            Warn("Wait for the current video job to finish before starting the queue.");
            return;
        }
        if (_settings.JobQueue.Count == 0)
        {
            Warn("Add at least one copied YouTube video to the queue first.");
            return;
        }

        _queueUploadPublicly = uploadPublicly;
        _queueModeActive = true;
        Notify("Batch queue started", $"Processing {_settings.JobQueue.Count} video(s) as {(uploadPublicly ? "public links" : "local downloads")}.", NotificationType.Information, 5f);
        ContinueQueue();
    }

    private static void ContinueQueue()
    {
        if (!_queueModeActive || Volatile.Read(ref _jobRunning) != 0) return;
        if (_settings.JobQueue.Count == 0)
        {
            _queueModeActive = false;
            _jobStatus = "Batch queue completed";
            RefreshQueuePage();
            Notify("Batch queue finished", "All queued videos have been processed.", NotificationType.Success, 6f);
            return;
        }

        RecentVideoEntry next = _settings.JobQueue[0];
        _settings.JobQueue.RemoveAt(0);
        SaveSettings();
        RefreshQueuePage();
        TryStartJob(next.Url, _queueUploadPublicly, fromQueue: true);
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

        AddRecentSearch(query);
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
        AddRecentVideo(choice.Title, choice.Url);
        MelonLogger.Msg($"YouTube link copied: {choice.Url} ({choice.Title})");
        Notify("YouTube link copied", ShortMenuText(choice.Title, 100), NotificationType.Success, 5f);
    }

    private static void PreviewCopiedVideo()
    {
        string url = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        if (!IsYouTubeUrl(url))
        {
            Warn("Copy or select a YouTube video before previewing its information.");
            return;
        }
        if (Interlocked.CompareExchange(ref _previewRunning, 1, 0) != 0)
        {
            Warn("A video preview is already loading.");
            return;
        }

        _ytDlpPath = ResolveYtDlpPath();
        string quality = _settings.VideoQuality;
        Notify("Video preview", $"Loading title, duration, channel, size, and {quality} format...", NotificationType.Information, 4f);
        _ = Task.Run(() => LoadVideoPreview(url, quality));
    }

    private static void LoadVideoPreview(string url, string quality)
    {
        try
        {
            if (!File.Exists(_ytDlpPath))
                throw new FileNotFoundException("yt-dlp.exe is missing. Open Setup and folders to install it.", _ytDlpPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add("--no-warnings");
            startInfo.ArgumentList.Add("--skip-download");
            startInfo.ArgumentList.Add("--dump-single-json");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(VideoFormatSelector(quality));
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(url);

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("yt-dlp preview did not start.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("The video information preview timed out.");
            }
            Task.WaitAll(stdout, stderr);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Preview failed: {LastPart(stderr.Result.Trim(), 400)}");

            using JsonDocument document = JsonDocument.Parse(stdout.Result);
            JsonElement root = document.RootElement;
            string title = JsonText(root, "title", GetYouTubeVideoId(url));
            string channel = JsonText(root, "channel", JsonText(root, "uploader", "Unknown channel"));
            string duration = JsonText(root, "duration_string", "Unknown duration");
            string resolution = JsonText(root, "resolution", quality);
            long bytes = JsonInt64(root, "filesize_approx") ?? JsonInt64(root, "filesize") ?? 0;
            string size = bytes > 0 ? FormatBytes(bytes) : "Unknown size";
            string summary = string.Join(Environment.NewLine, new[]
            {
                title,
                $"Channel: {channel}",
                $"Duration: {duration}",
                $"Selected format: {resolution} ({quality})",
                $"Estimated size: {size}",
                $"URL: {url}",
            });

            MainThreadActions.Enqueue(() =>
            {
                _lastPreviewSummary = summary;
                _settings.LastSourceUrl = url;
                SaveSettings();
                MelonLogger.Msg($"Video preview loaded:\n{summary}");
                Notify(ShortMenuText(title, 70), $"{duration} | {resolution} | {size} | {ShortMenuText(channel, 40)}", NotificationType.Success, 8f);
            });
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Video preview failed: {ex}");
            FailOnMainThread(ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _previewRunning, 0);
        }
    }

    private static void CopyLastPreview()
    {
        if (string.IsNullOrWhiteSpace(_lastPreviewSummary))
        {
            Warn("Preview a copied YouTube video first.");
            return;
        }
        GUIUtility.systemCopyBuffer = _lastPreviewSummary;
        Notify("Video preview", "The last video information report was copied.", NotificationType.Success, 4f);
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

    private static void RefreshDownloadsPage()
    {
        if (_downloadsPage == null) return;

        _downloadsPage.RemoveAll();
        _downloadsPage.CreateFunction("Refresh MP4 library", Color.green, RefreshAndOpenDownloadsPage);
        _downloadsPage.CreateFunction("Open downloads folder", Color.cyan, OpenDownloadsFolder);
        try
        {
            Directory.CreateDirectory(_downloadsDirectory);
            FileInfo[] files = new DirectoryInfo(_downloadsDirectory)
                .GetFiles("*.mp4", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(MaximumDownloadLibraryEntries)
                .ToArray();
            if (files.Length == 0)
            {
                _downloadsPage.CreateFunction("No downloaded MP4 files", Color.gray, () => Warn("Use Download MP4 only first."));
                return;
            }

            long totalBytes = files.Sum(file => file.Length);
            _downloadsPage.CreateFunction($"Showing {files.Length} MP4s ({FormatBytes(totalBytes)})", Color.white, () => Notify("MP4 library", $"{files.Length} recent files use {FormatBytes(totalBytes)}.", NotificationType.Information, 4f));
            foreach (FileInfo file in files)
            {
                string path = file.FullName;
                FunctionElement button = _downloadsPage.CreateFunction(ShortMenuText($"{file.Name} ({FormatBytes(file.Length)})", 64), Color.white, () => CopyDownloadedPath(path));
                button.SetTooltip($"Modified: {file.LastWriteTime:F}\n{path}");
            }
        }
        catch (Exception ex)
        {
            _downloadsPage.CreateFunction("Could not scan downloads", Color.red, () => Warn(ex.Message));
            MelonLogger.Warning($"Could not refresh MP4 library: {ex.Message}");
        }
    }

    private static void RefreshAndOpenDownloadsPage()
    {
        RefreshDownloadsPage();
        if (_downloadsPage != null) Menu.OpenPage(_downloadsPage);
    }

    private static void CopyDownloadedPath(string path)
    {
        if (!File.Exists(path))
        {
            Warn("That downloaded MP4 no longer exists. Refresh the library.");
            return;
        }
        GUIUtility.systemCopyBuffer = path;
        Notify("MP4 file path copied", ShortMenuText(Path.GetFileName(path), 100), NotificationType.Success, 4f);
    }

    private static void CopyDownloadsPath()
    {
        Directory.CreateDirectory(_downloadsDirectory);
        GUIUtility.systemCopyBuffer = _downloadsDirectory;
        Notify("ClipLink Media", "Downloads folder path copied.", NotificationType.Success, 3f);
    }

    private static void InspectCopiedUrl()
    {
        string copied = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        if (IsYouTubeUrl(copied))
        {
            Notify("Copied YouTube URL", $"Video ID: {GetYouTubeVideoId(copied)}", NotificationType.Success, 5f);
            return;
        }
        if (IsHttpsUrl(copied))
        {
            Uri uri = new(copied);
            Notify("Copied HTTPS URL", $"Host: {uri.Host}", NotificationType.Information, 5f);
            return;
        }
        Warn("The clipboard does not contain a supported web URL.");
    }

    private static void OpenCopiedYouTubeUrl()
    {
        string copied = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        if (!IsYouTubeUrl(copied))
        {
            Warn("Copy a youtube.com or youtu.be video URL first.");
            return;
        }

        try
        {
            Application.OpenURL(copied);
            Notify("ClipLink Media", "Opened the copied YouTube video.", NotificationType.Information, 3f);
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not open the copied video: {ex.Message}");
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
        string url = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        TryStartJob(url, uploadPublicly);
    }

    private static void StartLastPublicJob()
    {
        TryStartJob(_settings.LastSourceUrl, uploadPublicly: true);
    }

    private static void StartLastLocalJob()
    {
        TryStartJob(_settings.LastSourceUrl, uploadPublicly: false);
    }

    private static void TryStartJob(string url, bool uploadPublicly, bool fromQueue = false)
    {
        if (!_rightsConfirmed)
        {
            if (fromQueue) _queueModeActive = false;
            Warn("Turn on 'I own / have permission' before downloading or uploading.");
            return;
        }

        if (!IsYouTubeUrl(url))
        {
            if (fromQueue) _queueModeActive = false;
            Warn("No saved YouTube video is ready. Copy or select one first.");
            return;
        }

        if (Interlocked.CompareExchange(ref _jobRunning, 1, 0) != 0)
        {
            if (fromQueue) _queueModeActive = false;
            Warn("ClipLink Media is already working on a video.");
            return;
        }

        _ytDlpPath = ResolveYtDlpPath();
        var cancellation = new CancellationTokenSource();
        lock (JobGate) _jobCancellation = cancellation;

        string retention = _settings.LitterboxRetention;
        string quality = _settings.VideoQuality;
        _settings.LastSourceUrl = url;
        SaveSettings();
        string mode = uploadPublicly ? $"public {RetentionDisplay(retention)} link" : "local MP4";
        _jobStartedUtc = DateTimeOffset.UtcNow;
        _jobStatus = $"Downloading {quality} video {GetYouTubeVideoId(url)} for {mode}";
        MelonLogger.Msg($"Starting {quality} {mode} job: {url}");
        Notify("ClipLink Media", $"Downloading {quality} video for a {mode}...", NotificationType.Information, 4f);
        _ = Task.Run(() => ProcessVideoJob(url, uploadPublicly, retention, quality, cancellation, fromQueue));
    }

    private static void CopyLastSourceUrl()
    {
        if (!IsYouTubeUrl(_settings.LastSourceUrl))
        {
            Warn("No saved YouTube video is ready yet.");
            return;
        }

        GUIUtility.systemCopyBuffer = _settings.LastSourceUrl;
        Notify("ClipLink Media", "Last YouTube link copied.", NotificationType.Success, 3f);
    }

    private static void OpenLastSourceUrl()
    {
        if (!IsYouTubeUrl(_settings.LastSourceUrl))
        {
            Warn("No saved YouTube video is ready yet.");
            return;
        }

        try
        {
            Application.OpenURL(_settings.LastSourceUrl);
            Notify("ClipLink Media", "Opened the last YouTube video.", NotificationType.Information, 3f);
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not open the last YouTube video: {ex.Message}");
        }
    }

    private static void CancelCurrentJob()
    {
        bool stoppedQueue = _queueModeActive;
        _queueModeActive = false;
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
        Notify("ClipLink Media", stoppedQueue ? "Cancelling the current job and stopping the batch queue..." : "Cancelling the current job...", NotificationType.Warning, 4f);
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
        string status = $"yt-dlp: {(ytDlpReady ? "ready" : "missing")}; Fusion: {(fusionReady ? "ready" : "missing")}; quality: {_settings.VideoQuality}; expiry: {RetentionDisplay(_settings.LitterboxRetention)}.";
        MelonLogger.Msg($"Setup check - {status} Data: {_dataDirectory}");
        Notify("ClipLink setup check", status, ytDlpReady && fusionReady ? NotificationType.Success : NotificationType.Warning, 7f);
    }

    private static void ShowStatistics()
    {
        string completed = _settings.LastJobCompletedUtc.HasValue
            ? _settings.LastJobCompletedUtc.Value.LocalDateTime.ToString("g")
            : "never";
        string message = $"Public links: {_settings.TotalPublicLinks}; local downloads: {_settings.TotalLocalDownloads}; processed: {FormatBytes(_settings.TotalBytesProcessed)}; last: {completed}.";
        MelonLogger.Msg($"Usage statistics - {message}");
        Notify("ClipLink statistics", message, NotificationType.Information, 8f);
    }

    private static void ShowJobStatus()
    {
        string elapsed = Volatile.Read(ref _jobRunning) != 0 && _jobStartedUtc.HasValue
            ? $"; elapsed: {FormatDuration(DateTimeOffset.UtcNow - _jobStartedUtc.Value)}"
            : string.Empty;
        string queue = _queueModeActive
            ? $"active {_settings.JobQueue.Count} remaining"
            : $"stopped {_settings.JobQueue.Count} pending";
        string message = $"Job: {_jobStatus}{elapsed}; queue: {queue}.";
        MelonLogger.Msg(message);
        Notify("ClipLink job status", LastPart(message, 220), Volatile.Read(ref _jobRunning) != 0 ? NotificationType.Information : NotificationType.Success, 8f);
    }

    private static void CopySetupReport()
    {
        _ytDlpPath = ResolveYtDlpPath();
        string ytDlpVersion = GetYtDlpVersion();
        string report = string.Join(Environment.NewLine, new[]
        {
            "ClipLink Media setup report",
            "Version: 4.0.0",
            $"yt-dlp: {ytDlpVersion}",
            $"yt-dlp path: {_ytDlpPath}",
            $"Fusion assembly: {typeof(NetworkPlayer).Assembly.GetName().Version}",
            $"MP4 quality: {_settings.VideoQuality}",
            $"Public-link expiry: {RetentionDisplay(_settings.LitterboxRetention)}",
            $"Recent videos: {_settings.RecentVideos.Count}",
            $"Recent searches: {_settings.RecentSearches.Count}",
            $"Favorite videos: {_settings.Favorites.Count}",
            $"Queued videos: {_settings.JobQueue.Count}",
            $"Queue active: {_queueModeActive}",
            $"Job status: {_jobStatus}",
            $"Utility tally: {_settings.TallyCount}",
            $"Personal note saved: {!string.IsNullOrWhiteSpace(_settings.PersonalNote)}",
            $"Measured FPS: {_measuredFps:0.0}",
            $"Scene: {SceneManager.GetActiveScene().name}",
            $"Saved public URLs: {LinkHistory.Count}",
            $"Local downloads completed: {_settings.TotalLocalDownloads}",
            $"Public links completed: {_settings.TotalPublicLinks}",
            $"Total data processed: {FormatBytes(_settings.TotalBytesProcessed)}",
            $"Data folder: {_dataDirectory}",
            $"Downloads folder: {_downloadsDirectory}",
        });
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("ClipLink setup report", "Detailed setup report copied to the clipboard.", NotificationType.Success, 5f);
    }

    private static string GetYtDlpVersion()
    {
        if (!File.Exists(_ytDlpPath)) return "missing";
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--version");
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("yt-dlp did not start.");
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return "timeout";
            }
            string version = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
        }
        catch (Exception ex)
        {
            return $"error ({ex.Message})";
        }
    }

    private static void ProcessVideoJob(string youtubeUrl, bool uploadPublicly, string retention, string quality, CancellationTokenSource cancellation, bool fromQueue)
    {
        string? jobDirectory = null;
        CancellationToken token = cancellation.Token;
        try
        {
            jobDirectory = CreateJobDirectory();
            string mp4Path = DownloadVideo(youtubeUrl, jobDirectory, quality, token);
            token.ThrowIfCancellationRequested();
            long fileBytes = new FileInfo(mp4Path).Length;
            string fileSize = FormatBytes(fileBytes);

            if (!uploadPublicly)
            {
                _jobStatus = "Saving downloaded MP4";
                string savedPath = SaveDownloadedVideo(mp4Path, token);
                token.ThrowIfCancellationRequested();
                MainThreadActions.Enqueue(() =>
                {
                    _settings.TotalLocalDownloads++;
                    _settings.TotalBytesProcessed += fileBytes;
                    _settings.LastJobCompletedUtc = DateTimeOffset.UtcNow;
                    SaveSettings();
                    _jobStatus = $"Completed local download: {Path.GetFileName(savedPath)}";
                    RefreshDownloadsPage();
                    MelonLogger.Msg($"Local MP4 saved: {savedPath}");
                    Notify("Download complete", $"Saved {quality} {Path.GetFileName(savedPath)} ({fileSize}) in ClipLink Downloads.", NotificationType.Success, 7f);
                });
                return;
            }

            MainThreadActions.Enqueue(() =>
            {
                _jobStatus = $"Uploading {fileSize} MP4 for {RetentionDisplay(retention)}";
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
                _settings.TotalPublicLinks++;
                _settings.TotalBytesProcessed += fileBytes;
                _settings.LastJobCompletedUtc = createdUtc;
                SaveSettings();
                RefreshHistoryPage();
                _jobStatus = $"Completed public link: {publicUrl}";
                MelonLogger.Msg($"Public MP4 URL copied: {publicUrl}");
                Notify("ClipLink Media finished", $"{RetentionDisplay(retention)} MP4 URL copied. Expires {expiresUtc.LocalDateTime:g}.", NotificationType.Success, 7f);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _jobStatus = "Job cancelled";
            MelonLogger.Msg("Video job cancelled by the user.");
            MainThreadActions.Enqueue(() => Notify("ClipLink Media", "Download/upload job cancelled.", NotificationType.Warning, 4f));
        }
        catch (Exception ex)
        {
            _jobStatus = $"Job failed: {LastPart(ex.Message, 120)}";
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
            if (fromQueue && _queueModeActive)
                MainThreadActions.Enqueue(ContinueQueue);
        }
    }

    private static string DownloadVideo(string youtubeUrl, string jobDirectory, string quality, CancellationToken token)
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
        startInfo.ArgumentList.Add(VideoFormatSelector(quality));
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

    private static bool IsSupportedVideoQuality(string? value)
    {
        return value is "Best" or "720p" or "480p" or "360p";
    }

    private static string VideoFormatSelector(string quality)
    {
        return quality switch
        {
            "720p" => "b[ext=mp4][height<=720]",
            "480p" => "b[ext=mp4][height<=480]",
            "360p" => "b[ext=mp4][height<=360]",
            _ => "b[ext=mp4]",
        };
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

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.TotalMinutes >= 1) return $"{duration.Minutes}m {duration.Seconds}s";
        return $"{Math.Max(0, duration.Seconds)}s";
    }

    private static string JsonText(JsonElement root, string propertyName, string fallback)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return fallback;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString();
    }

    private static long? JsonInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            return null;
        if (value.TryGetInt64(out long integer)) return integer;
        return value.TryGetDouble(out double number) ? (long)Math.Max(0, number) : null;
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClipLinkMedia/4.0.0");
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
    public string VideoQuality { get; set; } = "Best";
    public string LastSourceUrl { get; set; } = string.Empty;
    public List<string> RecentSearches { get; set; } = new();
    public List<RecentVideoEntry> RecentVideos { get; set; } = new();
    public List<RecentVideoEntry> Favorites { get; set; } = new();
    public List<RecentVideoEntry> JobQueue { get; set; } = new();
    public string PersonalNote { get; set; } = string.Empty;
    public int TallyCount { get; set; }
    public int TotalPublicLinks { get; set; }
    public int TotalLocalDownloads { get; set; }
    public long TotalBytesProcessed { get; set; }
    public DateTimeOffset? LastJobCompletedUtc { get; set; }
}

public sealed class RecentVideoEntry
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset SelectedUtc { get; set; }
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
