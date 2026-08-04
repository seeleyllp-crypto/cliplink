using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoneLib;
using BoneLib.BoneMenu;
using BoneLib.Notifications;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LabFusion.Entities;
using LabFusion.Marrow.Pool;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.RPC;
using LabFusion.SDK.Modules;
using LabFusion.UI;
using LabFusion.Utilities;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(ClipLinkMedia.Core), "ClipLink Media", "5.6.0", "seeleyllp-crypto")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: AssemblyVersion("5.6.0.0")]
[assembly: AssemblyFileVersion("5.6.0.0")]

namespace ClipLinkMedia;

public sealed class Core : MelonMod
{
    private const string ModVersion = "5.6.0";
    private const ulong OwnerPlatformId = 76561199548494681UL;
    private const string YouTubeHome = "https://www.youtube.com/";
    private const string RealYouTubeBrowserFileName = "ClipLinkYouTubeBrowser.exe";
    private const string RealYouTubeBrowserDownloadUrl = "https://github.com/seeleyllp-crypto/cliplink/releases/latest/download/ClipLinkYouTubeBrowser.exe";
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string LitterboxUploadUrl = "https://litterbox.catbox.moe/resources/internals/api.php";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/seeleyllp-crypto/cliplink/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/seeleyllp-crypto/cliplink/releases";
    private const string MediaPlayerPalletFolder = "Elijoe.MediaPlayer";
    private const string MediaPlayerBarcode = "Elijoe.MediaPlayer.Spawnable.MediaPlayer";
    private const string FlatScreenMediaPlayerBarcode = "Elijoe.MediaPlayer.Spawnable.FlatscreenMediaPlayer";
    private const string CrtMediaPlayerBarcode = "Elijoe.MediaPlayer.Spawnable.CRTTV";
    private const string PhoneMediaPlayerBarcode = "Elijoe.MediaPlayer.Spawnable.PhoneMediaPlayer";
    private const string ComputerMediaPlayerBarcode = "Elijoe.MediaPlayer.Spawnable.ComputerMonitor";
    private const string BoomBoxMediaPlayerBarcode = "Elijoe.MediaPlayer.Spawnable.BoomBox";
    private const string DefaultLitterboxRetention = "72h";
    private const int MaximumHistoryEntries = 8;
    private const int MaximumRecentVideos = 12;
    private const int MaximumRecentSearches = 8;
    private const int MaximumFavorites = 20;
    private const int MaximumQueuedVideos = 10;
    private const int MaximumDownloadLibraryEntries = 20;
    private const int MaximumThumbnailCacheEntries = 48;
    private const long MaxUploadBytes = 1_000_000_000L;
    private const long MinimumTemporarySpace = 1200L * 1024 * 1024;
    private static readonly ConcurrentQueue<Action> MainThreadActions = new();
    private static readonly HttpClient YouTubeHttpClient = CreateYouTubeHttpClient();
    private static readonly HttpClient DiagnosticsHttpClient = CreateDiagnosticsHttpClient();
    private static readonly HttpClient BrowserDownloadHttpClient = CreateBrowserDownloadHttpClient();
    private static readonly object JobGate = new();
    private static readonly Stopwatch UtilityStopwatch = new();
    private static readonly Dictionary<NetworkPlayer, OwnerTagElement> OwnerTags = new();
    private static readonly object PresenceGate = new();
    private static readonly Dictionary<byte, ClipLinkPresenceInfo> ClipLinkUsers = new();
    private static readonly object ThumbnailGate = new();
    private static readonly Dictionary<string, Texture2D> ThumbnailCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<FunctionElement>> PendingThumbnailButtons = new(StringComparer.Ordinal);
    private static readonly Queue<string> ThumbnailCacheOrder = new();
    private static readonly SemaphoreSlim ThumbnailDownloadSlots = new(3, 3);
    private static readonly List<LinkHistoryEntry> LinkHistory = new();
    private static string _dataDirectory = string.Empty;
    private static string _downloadsDirectory = string.Empty;
    private static string _settingsPath = string.Empty;
    private static string _historyPath = string.Empty;
    private static string _backupsDirectory = string.Empty;
    private static string _screenshotsDirectory = string.Empty;
    private static string _ytDlpPath = string.Empty;
    private static string _realYouTubeBrowserPath = string.Empty;
    private static string _realYouTubeSelectionPath = string.Empty;
    private static string _realYouTubeMenuBridgeDirectory = string.Empty;
    private static string _realYouTubeMenuCommandPath = string.Empty;
    private static string _lastPublicUrl = string.Empty;
    private static string _searchQuery = "bonelab";
    private static Page? _searchResultsPage;
    private static Page? _historyPage;
    private static Page? _recentVideosPage;
    private static Page? _searchHistoryPage;
    private static Page? _favoritesPage;
    private static Page? _queuePage;
    private static Page? _downloadsPage;
    private static Page? _realYouTubeMenuPage;
    private static Texture2D? _youtubeBackground;
    private static Texture2D? _youtubeLogo;
    private static Texture2D? _realYouTubeMenuTexture;
    private static ClipLinkSettings _settings = new();
    private static CancellationTokenSource? _jobCancellation;
    private static string _jobStatus = "Idle";
    private static string _lastPreviewSummary = string.Empty;
    private static string _draftNote = string.Empty;
    private static string _latestReleaseUrl = ReleasesPageUrl;
    private static DateTimeOffset? _jobStartedUtc;
    private static DateTimeOffset? _countdownEndsUtc;
    private static string _countdownLabel = string.Empty;
    private static float _fpsWindowSeconds;
    private static float _measuredFps;
    private static int _fpsWindowFrames;
    private static bool _queueModeActive;
    private static bool _queueUploadPublicly;
    private static bool _cleanupConfirmed;
    private static bool _rightsConfirmed;
    private static float _lowFpsSeconds;
    private static bool _lowFpsAlertShown;
    private static int _jobRunning;
    private static int _searchRunning;
    private static int _previewRunning;
    private static int _diagnosticRunning;
    private static int _browserInstallRunning;
    private static float _realBrowserPollSeconds;
    private static float _realBrowserMenuPollSeconds;
    private static DateTime _lastRealBrowserSelectionWriteUtc;
    private static DateTime _lastRealBrowserMenuFrameWriteUtc;
    private static bool _realBrowserMenuFrameShown;
    private static bool _launchMenuAfterBrowserInstall;
    private static int _realBrowserMenuFrameReadRunning;

    public override void OnInitializeMelon()
    {
        _dataDirectory = Path.Combine(MelonEnvironment.UserDataDirectory, "ClipLinkMedia");
        _downloadsDirectory = Path.Combine(_dataDirectory, "Downloads");
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _historyPath = Path.Combine(_dataDirectory, "history.json");
        _backupsDirectory = Path.Combine(_dataDirectory, "Backups");
        _screenshotsDirectory = Path.Combine(_dataDirectory, "Screenshots");
        _realYouTubeSelectionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipLinkMedia",
            "selected-youtube-url.txt");
        Directory.CreateDirectory(_dataDirectory);
        Directory.CreateDirectory(_downloadsDirectory);
        Directory.CreateDirectory(_backupsDirectory);
        Directory.CreateDirectory(_screenshotsDirectory);
        _ytDlpPath = ResolveYtDlpPath();
        _realYouTubeBrowserPath = ResolveRealYouTubeBrowserPath();
        InitializeRealYouTubeMenuBridge();
        _realYouTubeSelectionPath = ResolveRealYouTubeSelectionPath();
        if (File.Exists(_realYouTubeSelectionPath))
            _lastRealBrowserSelectionWriteUtc = File.GetLastWriteTimeUtc(_realYouTubeSelectionPath);
        LoadPersistentState();
        _draftNote = _settings.PersonalNote;

        BuildBoneMenu();
        InitializeFusionOwnerTag();
        InitializeFusionPresence();
        MelonLogger.Msg($"All-in-one v{ModVersion} ready with the real signed-out YouTube GUI, Fusion mod-presence detection, synced media-player spawning, and resilient video uploads. Expiry: {_settings.LitterboxRetention}; quality: {_settings.VideoQuality}; {LinkHistory.Count} saved link(s); {_settings.Favorites.Count} favorite(s); {_settings.JobQueue.Count} queued.");
        MelonLogger.Msg("Fusion OWNER tag enabled. Players with ClipLink Media installed will see OWNER above the creator's head.");
        MelonLogger.Warning("Litterbox uploads are public and temporary. Upload only videos you own or have permission to share.");
        if (!File.Exists(_ytDlpPath))
            MelonLogger.Error("yt-dlp.exe is not installed. Use the BoneMenu GitHub download and folder buttons.");
        if (!File.Exists(_realYouTubeBrowserPath))
            MelonLogger.Warning("The real YouTube browser companion is not installed yet. Opening it from BoneMenu will download it from GitHub.");
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

        if (_settings.LowFpsAlertsEnabled && _measuredFps > 0f && _measuredFps < _settings.LowFpsThreshold)
        {
            _lowFpsSeconds += Time.unscaledDeltaTime;
            if (_lowFpsSeconds >= 10f && !_lowFpsAlertShown)
            {
                _lowFpsAlertShown = true;
                Notify("Low FPS warning", $"FPS stayed below {_settings.LowFpsThreshold} for 10 seconds (now {_measuredFps:0.0}).", NotificationType.Warning, 7f);
            }
        }
        else
        {
            _lowFpsSeconds = 0f;
            if (_measuredFps >= _settings.LowFpsThreshold + 5f)
                _lowFpsAlertShown = false;
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

        _realBrowserPollSeconds += Time.unscaledDeltaTime;
        if (_realBrowserPollSeconds >= 0.75f)
        {
            _realBrowserPollSeconds = 0f;
            PollRealYouTubeSelection();
        }

        _realBrowserMenuPollSeconds += Time.unscaledDeltaTime;
        if (_realBrowserMenuPollSeconds >= 0.5f)
        {
            _realBrowserMenuPollSeconds = 0f;
            PollRealYouTubeMenuFrame();
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

    private static void InitializeFusionPresence()
    {
        try
        {
            if (ModuleMessageManager.GetHandlerByType(typeof(ClipLinkPresenceMessage)) == null)
                ModuleMessageManager.RegisterHandler<ClipLinkPresenceMessage>();

            MultiplayerHooking.OnJoinedServer += OnFusionJoinedServer;
            MultiplayerHooking.OnDisconnected += OnFusionDisconnected;
            MultiplayerHooking.OnPlayerJoined += OnFusionPlayerJoined;
            MultiplayerHooking.OnPlayerLeft += OnFusionPlayerLeft;

            if (NetworkInfo.HasServer)
                OnFusionJoinedServer();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not initialize Fusion ClipLink presence: {ex.Message}");
        }
    }

    private static void OnFusionJoinedServer()
    {
        lock (PresenceGate)
        {
            ClipLinkUsers.Clear();
            if (PlayerIDManager.LocalID != null)
                ClipLinkUsers[PlayerIDManager.LocalSmallID] = new ClipLinkPresenceInfo(ModVersion, DateTimeOffset.UtcNow);
        }

        SendClipLinkPresence(CommonMessageRoutes.ReliableToOtherClients, requestReply: true);
        MelonLogger.Msg("Announced ClipLink presence to the current Fusion lobby.");
    }

    private static void OnFusionDisconnected()
    {
        lock (PresenceGate) ClipLinkUsers.Clear();
    }

    private static void OnFusionPlayerJoined(PlayerID playerId)
    {
        if (playerId == null || playerId.IsMe) return;
        SendClipLinkPresence(new MessageRoute(playerId.SmallID, NetworkChannel.Reliable), requestReply: true);
    }

    private static void OnFusionPlayerLeft(PlayerID playerId)
    {
        if (playerId == null) return;
        lock (PresenceGate) ClipLinkUsers.Remove(playerId.SmallID);
    }

    private static void SendClipLinkPresence(MessageRoute route, bool requestReply)
    {
        if (!NetworkInfo.HasServer) return;
        try
        {
            MessageRelay.RelayModule<ClipLinkPresenceMessage, ClipLinkPresenceData>(
                new ClipLinkPresenceData(ModVersion, requestReply),
                route);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not send ClipLink presence: {ex.Message}");
        }
    }

    internal static void ReceiveClipLinkPresence(ReceivedMessage received)
    {
        if (!received.Sender.HasValue) return;

        ClipLinkPresenceData data = received.ReadData<ClipLinkPresenceData>();
        byte sender = received.Sender.Value;
        bool firstDetection;
        lock (PresenceGate)
        {
            firstDetection = !ClipLinkUsers.ContainsKey(sender);
            ClipLinkUsers[sender] = new ClipLinkPresenceInfo(
                string.IsNullOrWhiteSpace(data.Version) ? "unknown" : data.Version,
                DateTimeOffset.UtcNow);
        }

        string playerName = GetFusionPlayerName(sender);
        MelonLogger.Msg($"ClipLink presence received from {playerName} (Fusion ID {sender}, v{data.Version}).");
        if (firstDetection)
        {
            MainThreadActions.Enqueue(() => Notify(
                "ClipLink user detected",
                $"{playerName} is using ClipLink Media v{data.Version}.",
                NotificationType.Success,
                5f));
        }

        if (data.RequestReply)
            SendClipLinkPresence(new MessageRoute(sender, NetworkChannel.Reliable), requestReply: false);
    }

    private static string GetFusionPlayerName(byte smallId)
    {
        return NetworkPlayerManager.TryGetPlayer(smallId, out NetworkPlayer? player)
            && !string.IsNullOrWhiteSpace(player.Username)
                ? player.Username
                : $"Fusion player {smallId}";
    }

    private static void ApplyYouTubeTheme(Page page, bool videoGrid = false)
    {
        EnsureYouTubeTextures();
        page.Color = new Color(1f, 0f, 0f);
        page.Background = _youtubeBackground;
        page.BackgroundOpacity = 0.96f;
        page.Logo = _youtubeLogo;
        page.ElementSpacing = videoGrid ? 108f : 62f;
    }

    private static void EnsureYouTubeTextures()
    {
        if (_youtubeBackground == null)
        {
            _youtubeBackground = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                name = "ClipLink YouTube Background",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    bool header = y >= 56;
                    bool subtleRow = !header && ((y / 8) % 2 == 0);
                    Color color = header
                        ? new Color(0.85f, 0.02f, 0.02f, 1f)
                        : subtleRow
                            ? new Color(0.055f, 0.055f, 0.06f, 1f)
                            : new Color(0.035f, 0.035f, 0.04f, 1f);
                    _youtubeBackground.SetPixel(x, y, color);
                }
            }
            _youtubeBackground.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        if (_youtubeLogo == null)
        {
            _youtubeLogo = new Texture2D(96, 64, TextureFormat.RGBA32, false)
            {
                name = "ClipLink YouTube Logo",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 96; x++)
                {
                    bool redBody = x >= 4 && x <= 91 && y >= 7 && y <= 56;
                    bool whitePlay = x >= 38 && x <= 70 && Math.Abs(y - 32) <= (x - 38) * 0.72f;
                    Color color = whitePlay
                        ? Color.white
                        : redBody
                            ? new Color(1f, 0f, 0f, 1f)
                            : new Color(0f, 0f, 0f, 0f);
                    _youtubeLogo.SetPixel(x, y, color);
                }
            }
            _youtubeLogo.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }
    }

    private static void SetYouTubeSearchAndRun(string query)
    {
        _searchQuery = query;
        SearchYouTube();
    }

    private static void BuildBoneMenu()
    {
        Page page = Page.Root.CreatePage("ClipLink Media", Color.cyan);
        _realYouTubeMenuPage = page.CreatePage("YouTube IN MENU", new Color(1f, 0f, 0f));
        ApplyYouTubeTheme(_realYouTubeMenuPage);
        _realYouTubeMenuPage.BackgroundOpacity = 1f;
        _realYouTubeMenuPage.ElementSpacing = 50f;
        _realYouTubeMenuPage.CreateFunction("Start REAL YouTube in menu", Color.red, StartRealYouTubeInMenu);
        _realYouTubeMenuPage.CreateString("YouTube search", Color.white, _searchQuery, value => _searchQuery = value.Trim());
        _realYouTubeMenuPage.CreateFunction("Search real YouTube", Color.red, SearchRealYouTubeInMenu);
        _realYouTubeMenuPage.CreateFunction("Previous YouTube control", Color.white, () => SendRealYouTubeMenuCommand("previous"));
        _realYouTubeMenuPage.CreateFunction("Next YouTube control", Color.white, () => SendRealYouTubeMenuCommand("next"));
        _realYouTubeMenuPage.CreateFunction("SELECT focused control", Color.green, () => SendRealYouTubeMenuCommand("select"));
        _realYouTubeMenuPage.CreateFunction("Scroll YouTube up", Color.cyan, () => SendRealYouTubeMenuCommand("scrollup"));
        _realYouTubeMenuPage.CreateFunction("Scroll YouTube down", Color.cyan, () => SendRealYouTubeMenuCommand("scrolldown"));
        _realYouTubeMenuPage.CreateFunction("YouTube back", Color.yellow, () => SendRealYouTubeMenuCommand("back"));
        _realYouTubeMenuPage.CreateFunction("YouTube home", Color.red, () => SendRealYouTubeMenuCommand("home"));
        _realYouTubeMenuPage.CreateFunction("Reload YouTube", Color.yellow, () => SendRealYouTubeMenuCommand("reload"));
        _realYouTubeMenuPage.CreateFunction("Copy last clicked video", Color.white, CopyLastRealYouTubeSelection);
        _realYouTubeMenuPage.CreateFunction("Use clicked video + spawn player", Color.cyan, SpawnMediaPlayerWithRealYouTubeSelection);
        _realYouTubeMenuPage.CreateFunction("Stop in-menu YouTube", Color.gray, StopRealYouTubeInMenu);
        _realYouTubeMenuPage.CreateFunction("Open separate YouTube window", Color.gray, OpenRealYouTubeBrowser);
        _realYouTubeMenuPage.CreateFunction("Install / update browser", Color.yellow, InstallRealYouTubeBrowser);

        Page browserPage = page.CreatePage("Thumbnail browser fallback", Color.gray);
        ApplyYouTubeTheme(browserPage);
        FunctionElement youtubeHeader = browserPage.CreateFunction(
            "Native VR fallback",
            Color.white,
            () => Notify("YouTube", "This is the native VR fallback. Use YouTube IN MENU for YouTube's actual webpage.", NotificationType.Information, 7f));
        youtubeHeader.Logo = _youtubeLogo;
        youtubeHeader.SetTooltip("Fallback thumbnail search. YouTube IN MENU renders the actual webpage as its background.");
        StringElement searchElement = browserPage.CreateString("Search", Color.white, _searchQuery, value => _searchQuery = value.Trim());
        searchElement.SetTooltip("Select the keyboard button, type a search, and press Enter.");
        browserPage.CreateFunction("Search YouTube", Color.red, SearchYouTube);
        Page explorePage = browserPage.CreatePage("Explore", Color.red);
        ApplyYouTubeTheme(explorePage);
        explorePage.CreateFunction("BONELAB", Color.white, () => SetYouTubeSearchAndRun("BONELAB VR gameplay"));
        explorePage.CreateFunction("VR gaming", Color.white, () => SetYouTubeSearchAndRun("VR gaming"));
        explorePage.CreateFunction("Gaming", Color.white, () => SetYouTubeSearchAndRun("gaming"));
        explorePage.CreateFunction("Music", Color.white, () => SetYouTubeSearchAndRun("music"));
        explorePage.CreateFunction("Trending videos", Color.white, () => SetYouTubeSearchAndRun("trending videos"));
        explorePage.CreateFunction("Media Player tutorials", Color.white, () => SetYouTubeSearchAndRun("BONELAB Media Player mod tutorial"));
        _searchResultsPage = browserPage.CreatePage("Videos", Color.red);
        ApplyYouTubeTheme(_searchResultsPage, videoGrid: true);
        _searchResultsPage.CreateFunction("Search first", Color.gray, SearchYouTube);
        _recentVideosPage = browserPage.CreatePage("Recently selected videos", Color.cyan);
        ApplyYouTubeTheme(_recentVideosPage, videoGrid: true);
        RefreshRecentVideosPage();
        _searchHistoryPage = browserPage.CreatePage("Recent searches", Color.yellow);
        ApplyYouTubeTheme(_searchHistoryPage);
        RefreshSearchHistoryPage();
        _favoritesPage = browserPage.CreatePage("Favorite videos", Color.magenta);
        ApplyYouTubeTheme(_favoritesPage, videoGrid: true);
        RefreshFavoritesPage();
        browserPage.CreateFunction("Add copied video to favorites", Color.magenta, AddCopiedFavorite);
        browserPage.CreateFunction("Preview copied video", Color.green, PreviewCopiedVideo);
        browserPage.CreateFunction("Copy last preview info", Color.white, CopyLastPreview);
        browserPage.CreateFunction("Open REAL YouTube GUI", Color.red, OpenRealYouTubeBrowser);

        page.CreateBool("I own / have permission", Color.yellow, false, value => _rightsConfirmed = value);
        page.CreateFunction("Make public MP4 URL", Color.green, StartClipboardJob);
        page.CreateFunction("Download MP4 only", Color.green, StartLocalDownloadJob);
        page.CreateFunction("Retry saved failed upload", Color.yellow, RetryLastFailedUpload);
        page.CreateFunction("Cancel current job", Color.red, CancelCurrentJob);

        _queuePage = page.CreatePage("Batch video queue", Color.yellow);
        RefreshQueuePage();

        _downloadsPage = page.CreatePage("Downloaded MP4 library", Color.cyan);
        RefreshDownloadsPage();

        page.CreateFunction("Spawn media player", Color.blue, () => SpawnMediaPlayer(MediaPlayerBarcode, "Media Player"));
        Page mediaPlayerPage = page.CreatePage("Media player spawner", Color.blue);
        mediaPlayerPage.CreateFunction("Spawn media player", Color.green, () => SpawnMediaPlayer(MediaPlayerBarcode, "Media Player"));
        mediaPlayerPage.CreateFunction("Use last MP4 URL + spawn", Color.cyan, SpawnMediaPlayerWithLastUrl);
        mediaPlayerPage.CreateFunction("Spawn flatscreen TV", Color.cyan, () => SpawnMediaPlayer(FlatScreenMediaPlayerBarcode, "Flatscreen Media Player"));
        mediaPlayerPage.CreateFunction("Spawn CRT TV", Color.yellow, () => SpawnMediaPlayer(CrtMediaPlayerBarcode, "CRT TV"));
        mediaPlayerPage.CreateFunction("Spawn phone player", Color.magenta, () => SpawnMediaPlayer(PhoneMediaPlayerBarcode, "Phone Media Player"));
        mediaPlayerPage.CreateFunction("Spawn computer monitor", Color.white, () => SpawnMediaPlayer(ComputerMediaPlayerBarcode, "Computer Monitor"));
        mediaPlayerPage.CreateFunction("Spawn boom box", Color.green, () => SpawnMediaPlayer(BoomBoxMediaPlayerBarcode, "Boom Box"));
        mediaPlayerPage.CreateFunction("Check media-player setup", Color.white, CheckMediaPlayerSetup);

        BuildUtilityToolbox(page);

        Page repeatPage = page.CreatePage("Repeat last video", Color.green);
        repeatPage.CreateFunction("Make public URL again", Color.green, StartLastPublicJob);
        repeatPage.CreateFunction("Download MP4 again", Color.cyan, StartLastLocalJob);
        repeatPage.CreateFunction("Copy last YouTube link", Color.white, CopyLastSourceUrl);
        repeatPage.CreateFunction("Open last YouTube video", Color.red, OpenLastSourceUrl);
        repeatPage.CreateFunction("Copy MP4 URL + spawn player", Color.blue, SpawnMediaPlayerWithLastUrl);

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
        toolsPage.CreateFunction("Open REAL YouTube GUI", Color.red, OpenRealYouTubeBrowser);
        toolsPage.CreateFunction("Install REAL YouTube GUI", Color.yellow, InstallRealYouTubeBrowser);
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

    private static void SpawnMediaPlayerWithLastUrl()
    {
        if (string.IsNullOrWhiteSpace(_lastPublicUrl))
        {
            Warn("Create a public MP4 URL first, then use this button.");
            return;
        }

        GUIUtility.systemCopyBuffer = _lastPublicUrl;
        SpawnMediaPlayer(MediaPlayerBarcode, "Media Player");
    }

    private static void SpawnMediaPlayer(string barcode, string displayName)
    {
        string palletPath = GetMediaPlayerPalletPath();
        if (!File.Exists(palletPath))
        {
            Warn("The Elijoe Media Player content mod is not installed or enabled.");
            MelonLogger.Warning($"Media Player pallet was not found at {palletPath}");
            return;
        }

        Transform? head = BoneLib.Player.Head;
        if (head == null)
        {
            Warn("The player rig is not ready. Enter a level and try again.");
            return;
        }

        Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = head.forward;
        flatForward.Normalize();

        Vector3 spawnPosition = head.position + (flatForward * 1.75f) - (Vector3.up * 1.15f);
        Quaternion spawnRotation = Quaternion.LookRotation(-flatForward, Vector3.up);

        try
        {
            if (NetworkInfo.HasServer)
                SpawnFusionMediaPlayer(barcode, displayName, spawnPosition, spawnRotation);
            else
                SpawnLocalMediaPlayer(barcode, displayName, spawnPosition, spawnRotation);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Could not spawn {displayName}: {ex}");
            Warn($"Could not spawn {displayName}: {LastPart(ex.Message, 100)}");
        }
    }

    private static void SpawnFusionMediaPlayer(string barcode, string displayName, Vector3 position, Quaternion rotation)
    {
        var spawnable = LocalAssetSpawner.CreateSpawnable(barcode);
        NetworkAssetSpawner.Spawn(new NetworkAssetSpawner.SpawnRequestInfo
        {
            Spawnable = spawnable,
            Position = position,
            Rotation = rotation,
            SpawnEffect = true,
            SpawnSource = EntitySource.Player,
            SpawnCallback = info =>
            {
                if (info.Spawned == null)
                {
                    Warn($"Fusion did not return a spawned {displayName}.");
                    return;
                }

                string entity = info.Entity == null ? "unknown" : info.Entity.ID.ToString();
                MelonLogger.Msg($"Fusion network spawn completed for {displayName}; entity {entity}.");
                Notify(
                    "Fusion media player synced",
                    $"{displayName} spawned for the lobby. Grab it and press B to use the copied direct MP4 URL.",
                    NotificationType.Success,
                    8f);
            },
        });

        MelonLogger.Msg($"Requested Fusion network spawn for {displayName} with barcode {barcode} at {position}.");
        Notify("Fusion spawn requested", $"Syncing {displayName} to the lobby...", NotificationType.Information, 4f);
    }

    private static void SpawnLocalMediaPlayer(string barcode, string displayName, Vector3 position, Quaternion rotation)
    {
        HelperMethods.SpawnCrate(
            barcode,
            position,
            rotation,
            Vector3.one,
            false,
            spawned =>
            {
                if (spawned == null) return;
                Notify("Media player spawned", $"{displayName} is in front of you. Grab it and press B to use the copied direct MP4 URL.", NotificationType.Success, 7f);
            });
        MelonLogger.Msg($"Requested local {displayName} spawn with barcode {barcode} at {position}.");
    }

    private static void CheckMediaPlayerSetup()
    {
        string palletPath = GetMediaPlayerPalletPath();
        if (!File.Exists(palletPath))
        {
            Warn("Elijoe Media Player is missing or disabled in BONELAB's content mods.");
            return;
        }

        string spawnMode = NetworkInfo.HasServer
            ? "Fusion network spawning is active. Lobby players need the Media Player content pack."
            : "Single-player local spawning is active.";
        Notify(
            "Media Player ready",
            $"The Elijoe Media Player pallet is installed. {spawnMode}",
            NotificationType.Success,
            8f);
    }

    private static string GetMediaPlayerPalletPath() =>
        Path.Combine(Application.persistentDataPath, "Mods", MediaPlayerPalletFolder, $"{MediaPlayerPalletFolder}.pallet.json");

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

        Page notes = utilities.CreatePage("Saved support notes", Color.green);
        StringElement noteElement = notes.CreateString("Personal note", Color.white, _draftNote, value => _draftNote = value);
        noteElement.SetTooltip("Save reproduction steps or troubleshooting notes across launches.");
        notes.CreateFunction("Save note", Color.green, SavePersonalNote);
        notes.CreateFunction("Copy saved note", Color.cyan, CopyPersonalNote);
        notes.CreateFunction("Clear saved note", Color.red, ClearPersonalNote);

        Page localSettings = utilities.CreatePage("Local audio and FPS", Color.blue);
        localSettings.CreateFunction("Show audio and FPS", Color.white, ShowLocalSettings);
        localSettings.CreateBool("Low-FPS warnings", Color.yellow, _settings.LowFpsAlertsEnabled, SetLowFpsAlerts);
        localSettings.CreateFunction("Warn below 45 FPS", Color.yellow, () => SetLowFpsThreshold(45));
        localSettings.CreateFunction("Warn below 60 FPS", Color.yellow, () => SetLowFpsThreshold(60));
        localSettings.CreateFunction("Warn below 72 FPS", Color.yellow, () => SetLowFpsThreshold(72));
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

        Page health = utilities.CreatePage("Mod health and support", Color.green);
        health.CreateFunction("Run mod health check", Color.green, RunModHealthCheck);
        health.CreateFunction("Copy installed mod list", Color.cyan, CopyInstalledModList);
        health.CreateFunction("Copy recent log errors", Color.yellow, CopyRecentLogErrors);
        health.CreateFunction("Copy complete support report", Color.magenta, CopyCompleteSupportReport);
        health.CreateFunction("Open Mods folder", Color.white, OpenModsFolder);
        health.CreateFunction("Open MelonLoader folder", Color.white, OpenMelonLoaderFolder);
        health.CreateFunction("Open UserData folder", Color.white, OpenUserDataFolder);
        health.CreateFunction("Open BONELAB folder", Color.white, OpenGameFolder);

        Page connectivity = utilities.CreatePage("Connectivity and updates", Color.cyan);
        connectivity.CreateFunction("Test YouTube/Litterbox/GitHub", Color.green, StartConnectivityTest);
        connectivity.CreateFunction("Check ClipLink update", Color.cyan, CheckForClipLinkUpdate);
        connectivity.CreateFunction("Open latest release", Color.white, OpenLatestRelease);

        Page fusion = utilities.CreatePage("Fusion session diagnostics", Color.magenta);
        fusion.CreateFunction("Show Fusion player count", Color.white, ShowFusionPlayerCount);
        fusion.CreateFunction("Show ClipLink users", Color.green, ShowClipLinkUsers);
        fusion.CreateFunction("Copy ClipLink user list", Color.cyan, CopyClipLinkUserList);
        fusion.CreateFunction("Refresh ClipLink detection", Color.yellow, RefreshClipLinkDetection);
        fusion.CreateFunction("Copy Fusion player list", Color.cyan, CopyFusionPlayerList);
        fusion.CreateFunction("Copy Fusion session report", Color.green, CopyFusionSessionReport);

        Page maintenance = utilities.CreatePage("Maintenance and backups", Color.yellow);
        maintenance.CreateFunction("Show disk space", Color.white, ShowDiskSpace);
        maintenance.CreateFunction("Take BONELAB screenshot", Color.cyan, TakeScreenshot);
        maintenance.CreateFunction("Open screenshots folder", Color.white, OpenScreenshotsFolder);
        maintenance.CreateFunction("Backup ClipLink data", Color.green, BackupClipLinkData);
        maintenance.CreateFunction("Open backups folder", Color.white, OpenBackupsFolder);
        maintenance.CreateBool("Confirm old-temp cleanup", Color.red, false, value => _cleanupConfirmed = value);
        maintenance.CreateFunction("Clean old ClipLink temp jobs", Color.red, CleanOldClipLinkTemps);
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
            $"ClipLink Media: {ModVersion}",
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

    private static void SetLowFpsAlerts(bool enabled)
    {
        _settings.LowFpsAlertsEnabled = enabled;
        _lowFpsSeconds = 0f;
        _lowFpsAlertShown = false;
        SaveSettings();
        Notify("Low-FPS warnings", enabled ? $"Enabled below {_settings.LowFpsThreshold} FPS." : "Disabled.", NotificationType.Success, 4f);
    }

    private static void SetLowFpsThreshold(int threshold)
    {
        _settings.LowFpsThreshold = threshold;
        _lowFpsSeconds = 0f;
        _lowFpsAlertShown = false;
        SaveSettings();
        Notify("Low-FPS threshold", $"Warnings will trigger below {threshold} FPS for 10 seconds.", NotificationType.Success, 4f);
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

    private static string GetMelonBaseDirectory()
    {
        return Directory.GetParent(MelonEnvironment.UserDataDirectory)?.FullName ?? MelonEnvironment.UserDataDirectory;
    }

    private static string GetModsDirectory()
    {
        return Path.Combine(GetMelonBaseDirectory(), "Mods");
    }

    private static string GetMelonLoaderDirectory()
    {
        return Path.Combine(GetMelonBaseDirectory(), "MelonLoader");
    }

    private static string GetLatestLogPath()
    {
        string profileLog = Path.Combine(GetMelonLoaderDirectory(), "Latest.log");
        if (File.Exists(profileLog)) return profileLog;
        string gameLog = Path.Combine(GetGameDirectory(), "MelonLoader", "Latest.log");
        return File.Exists(gameLog) ? gameLog : profileLog;
    }

    private static string GetGameDirectory()
    {
        string current = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(current, "BONELAB_Steam_Windows64.exe"))) return current;
        string applicationBase = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return File.Exists(Path.Combine(applicationBase, "BONELAB_Steam_Windows64.exe")) ? applicationBase : current;
    }

    private static void OpenModsFolder() => OpenFolder(GetModsDirectory(), "Mods folder");
    private static void OpenMelonLoaderFolder() => OpenFolder(GetMelonLoaderDirectory(), "MelonLoader folder");
    private static void OpenUserDataFolder() => OpenFolder(MelonEnvironment.UserDataDirectory, "UserData folder");
    private static void OpenGameFolder() => OpenFolder(GetGameDirectory(), "BONELAB folder");
    private static void OpenScreenshotsFolder() => OpenFolder(_screenshotsDirectory, "screenshots folder");
    private static void OpenBackupsFolder() => OpenFolder(_backupsDirectory, "backups folder");

    private static string DllVersion(string path)
    {
        if (!File.Exists(path)) return "missing";
        string? version = FileVersionInfo.GetVersionInfo(path).FileVersion;
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
    }

    private static string BuildModHealthReport(out int issueCount)
    {
        var issues = new List<string>();
        var details = new List<string>();
        string modsDirectory = GetModsDirectory();
        string melonLoaderDirectory = GetMelonLoaderDirectory();
        string latestLog = GetLatestLogPath();
        _ytDlpPath = ResolveYtDlpPath();

        if (!Directory.Exists(modsDirectory)) issues.Add("Mods folder is missing");
        if (!Directory.Exists(melonLoaderDirectory)) issues.Add("MelonLoader folder is missing");
        if (!File.Exists(latestLog)) issues.Add("Latest.log is missing");
        if (!File.Exists(_ytDlpPath)) issues.Add("yt-dlp.exe is missing");

        string boneLibPath = Path.Combine(modsDirectory, "BoneLib.dll");
        string fusionPath = Path.Combine(modsDirectory, "LabFusion.dll");
        string clipLinkPath = Path.Combine(modsDirectory, "ClipLinkMedia.dll");
        if (!File.Exists(boneLibPath)) issues.Add("BoneLib.dll is missing");
        if (!File.Exists(fusionPath)) issues.Add("LabFusion.dll is missing");
        if (!File.Exists(clipLinkPath)) issues.Add("ClipLinkMedia.dll is missing from the active Mods folder");

        int modCount = 0;
        try
        {
            FileInfo[] dlls = new DirectoryInfo(modsDirectory).GetFiles("*.dll", SearchOption.AllDirectories);
            modCount = dlls.Length;
            foreach (FileInfo empty in dlls.Where(file => file.Length == 0))
                issues.Add($"Zero-byte mod DLL: {empty.Name}");
            foreach (IGrouping<string, FileInfo> duplicate in dlls.GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                issues.Add($"Duplicate DLL name: {duplicate.Key} ({duplicate.Count()} copies)");
        }
        catch (Exception ex)
        {
            issues.Add($"Could not scan mod DLLs: {ex.Message}");
        }

        try
        {
            string root = Path.GetPathRoot(GetMelonBaseDirectory()) ?? string.Empty;
            if (!string.IsNullOrEmpty(root))
            {
                long free = new DriveInfo(root).AvailableFreeSpace;
                details.Add($"Free disk space: {FormatBytes(free)}");
                if (free < 2L * 1024 * 1024 * 1024) issues.Add("Less than 2 GB free disk space");
            }
        }
        catch (Exception ex)
        {
            details.Add($"Disk check failed: {ex.Message}");
        }

        details.Add($"Active Mods folder: {modsDirectory}");
        details.Add($"Installed DLL count: {modCount}");
        details.Add($"ClipLink Media: {DllVersion(clipLinkPath)}");
        details.Add($"BoneLib: {DllVersion(boneLibPath)}");
        details.Add($"Fusion: {DllVersion(fusionPath)}");
        details.Add($"yt-dlp: {GetYtDlpVersion()}");
        details.Add($"Latest log: {latestLog}");

        issueCount = issues.Count;
        var lines = new List<string>
        {
            "ClipLink Media mod health report",
            $"Status: {(issueCount == 0 ? "READY" : $"{issueCount} ISSUE(S)")}",
            $"Checked: {DateTime.Now:F}",
        };
        lines.AddRange(details);
        if (issues.Count > 0)
        {
            lines.Add("Issues:");
            lines.AddRange(issues.Select(issue => $"- {issue}"));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static void RunModHealthCheck()
    {
        string report = BuildModHealthReport(out int issues);
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("Mod health check", issues == 0 ? "Ready. Full report copied." : $"Found {issues} issue(s). Full report copied.", issues == 0 ? NotificationType.Success : NotificationType.Warning, 7f);
    }

    private static string BuildInstalledModList(int maximumEntries = 200)
    {
        string modsDirectory = GetModsDirectory();
        if (!Directory.Exists(modsDirectory)) return $"Mods folder missing: {modsDirectory}";
        try
        {
            FileInfo[] dlls = new DirectoryInfo(modsDirectory)
                .GetFiles("*.dll", SearchOption.AllDirectories)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Take(maximumEntries)
                .ToArray();
            var lines = new List<string> { $"Installed BONELAB mod DLLs ({dlls.Length} shown)", $"Folder: {modsDirectory}" };
            foreach (FileInfo file in dlls)
                lines.Add($"{file.Name} | v{DllVersion(file.FullName)} | {FormatBytes(file.Length)}");
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return $"Could not list installed mods: {ex.Message}";
        }
    }

    private static void CopyInstalledModList()
    {
        string report = BuildInstalledModList();
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("Installed mod list", "Installed DLL names, versions, and sizes copied.", NotificationType.Success, 5f);
    }

    private static string GetRecentLogErrors(int maximumEntries = 30)
    {
        string path = GetLatestLogPath();
        if (!File.Exists(path)) return $"Latest log not found: {path}";
        try
        {
            string[] errors = File.ReadLines(path)
                .TakeLast(2500)
                .Where(line => line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase)
                            || line.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                            || line.Contains("missing dependency", StringComparison.OrdinalIgnoreCase))
                .TakeLast(maximumEntries)
                .ToArray();
            if (errors.Length == 0) return $"No recent error-like lines found in {path}";
            return $"Recent BONELAB error-like log lines ({errors.Length}){Environment.NewLine}Log: {path}{Environment.NewLine}{string.Join(Environment.NewLine, errors)}";
        }
        catch (Exception ex)
        {
            return $"Could not read recent log errors: {ex.Message}";
        }
    }

    private static void CopyRecentLogErrors()
    {
        string report = GetRecentLogErrors();
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("Recent log errors", "Recent error-like log lines copied for troubleshooting.", NotificationType.Success, 5f);
    }

    private static string BuildFusionPlayerList()
    {
        NetworkPlayer[] players = NetworkPlayer.Players.ToArray();
        if (players.Length == 0) return "No Fusion players are currently registered.";
        var lines = new List<string> { $"Fusion players ({players.Length})" };
        foreach (NetworkPlayer player in players)
        {
            string local = player.PlayerID != null && player.PlayerID.IsMe ? " [LOCAL]" : string.Empty;
            string owner = player.PlayerID != null && player.PlayerID.PlatformID == OwnerPlatformId ? " [OWNER]" : string.Empty;
            lines.Add($"- {player.Username}{local}{owner}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static void ShowFusionPlayerCount()
    {
        int count = NetworkPlayer.Players.Count;
        int clipLinkCount;
        lock (PresenceGate) clipLinkCount = ClipLinkUsers.Count;
        Notify("Fusion session", $"{count} registered player(s); {clipLinkCount} confirmed ClipLink user(s); {OwnerTags.Count} remote OWNER tag(s).", NotificationType.Information, 6f);
    }

    private static string BuildClipLinkUserList()
    {
        List<KeyValuePair<byte, ClipLinkPresenceInfo>> users;
        lock (PresenceGate)
            users = ClipLinkUsers.OrderBy(entry => entry.Key).ToList();

        if (!NetworkInfo.HasServer)
            return "Not connected to a Fusion lobby.";
        if (users.Count == 0)
            return "No confirmed ClipLink users have answered yet.";

        var lines = new List<string> { $"Confirmed ClipLink Media users ({users.Count})" };
        foreach ((byte smallId, ClipLinkPresenceInfo presence) in users)
        {
            string local = smallId == PlayerIDManager.LocalSmallID ? " [YOU]" : string.Empty;
            lines.Add($"- {GetFusionPlayerName(smallId)}{local} | v{presence.Version}");
        }
        lines.Add($"Only players with ClipLink Media v{ModVersion} or newer can answer this check.");
        return string.Join(Environment.NewLine, lines);
    }

    private static void ShowClipLinkUsers()
    {
        if (!NetworkInfo.HasServer)
        {
            Warn("Join a Fusion lobby before checking ClipLink users.");
            return;
        }

        int confirmed;
        lock (PresenceGate) confirmed = ClipLinkUsers.Count;
        int total = Math.Max(PlayerIDManager.PlayerCount, NetworkPlayer.Players.Count);
        Notify("ClipLink users", $"{confirmed} of {total} Fusion player(s) confirmed. Use 'Copy ClipLink user list' for names and versions.", NotificationType.Information, 7f);
    }

    private static void CopyClipLinkUserList()
    {
        string report = BuildClipLinkUserList();
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("ClipLink user list", "Confirmed ClipLink player names and versions copied.", NotificationType.Success, 5f);
    }

    private static void RefreshClipLinkDetection()
    {
        if (!NetworkInfo.HasServer)
        {
            Warn("Join a Fusion lobby before refreshing ClipLink detection.");
            return;
        }

        lock (PresenceGate)
        {
            ClipLinkUsers.Clear();
            if (PlayerIDManager.LocalID != null)
                ClipLinkUsers[PlayerIDManager.LocalSmallID] = new ClipLinkPresenceInfo(ModVersion, DateTimeOffset.UtcNow);
        }
        SendClipLinkPresence(CommonMessageRoutes.ReliableToOtherClients, requestReply: true);
        Notify("ClipLink detection", "Presence request sent to the Fusion lobby.", NotificationType.Information, 5f);
    }

    private static void CopyFusionPlayerList()
    {
        string report = BuildFusionPlayerList();
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("Fusion player list", "Current Fusion player names copied.", NotificationType.Success, 4f);
    }

    private static void CopyFusionSessionReport()
    {
        string report = string.Join(Environment.NewLine, new[]
        {
            "Fusion session diagnostics",
            $"Fusion assembly: {typeof(NetworkPlayer).Assembly.GetName().Version}",
            $"Local OWNER tags attached: {OwnerTags.Count}",
            BuildClipLinkUserList(),
            BuildFusionPlayerList(),
        });
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("Fusion diagnostics", "Fusion session report copied.", NotificationType.Success, 4f);
    }

    private static void CopyCompleteSupportReport()
    {
        string health = BuildModHealthReport(out _);
        string session = string.Join(Environment.NewLine, new[]
        {
            $"Scene: {SceneManager.GetActiveScene().name}",
            $"Measured FPS: {_measuredFps:0.0}",
            $"Target FPS: {Application.targetFrameRate}",
            $"VSync: {QualitySettings.vSyncCount}",
            $"Unity: {Application.unityVersion}",
            $"OS: {SystemInfo.operatingSystem}",
            $"CPU: {SystemInfo.processorType}",
            $"GPU: {SystemInfo.graphicsDeviceName}",
            $"RAM: {SystemInfo.systemMemorySize} MB",
        });
        string report = string.Join(Environment.NewLine + Environment.NewLine, new[]
        {
            $"CLIPLINK MEDIA COMPLETE SUPPORT REPORT v{ModVersion}",
            health,
            session,
            BuildFusionPlayerList(),
            BuildInstalledModList(100),
            GetRecentLogErrors(40),
        });
        GUIUtility.systemCopyBuffer = report;
        MelonLogger.Msg(report);
        Notify("Support report", "Health, session, Fusion, mod-list, and log report copied.", NotificationType.Success, 6f);
    }

    private static void StartConnectivityTest()
    {
        if (Interlocked.CompareExchange(ref _diagnosticRunning, 1, 0) != 0)
        {
            Warn("A connectivity or update check is already running.");
            return;
        }
        Notify("Connectivity test", "Checking YouTube, Litterbox, and GitHub...", NotificationType.Information, 4f);
        _ = Task.Run(RunConnectivityTest);
    }

    private static void RunConnectivityTest()
    {
        try
        {
            string[] results =
            {
                TestEndpoint("YouTube", "https://www.youtube.com/generate_204"),
                TestEndpoint("Litterbox", "https://litterbox.catbox.moe/"),
                TestEndpoint("GitHub", "https://api.github.com/"),
            };
            string report = $"ClipLink connectivity test - {DateTime.Now:F}{Environment.NewLine}{string.Join(Environment.NewLine, results)}";
            MainThreadActions.Enqueue(() =>
            {
                GUIUtility.systemCopyBuffer = report;
                MelonLogger.Msg(report);
                Notify("Connectivity test finished", "YouTube/Litterbox/GitHub results copied.", NotificationType.Success, 6f);
            });
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Connectivity test failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _diagnosticRunning, 0);
        }
    }

    private static string TestEndpoint(string name, string url)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            using HttpResponseMessage response = DiagnosticsHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            stopwatch.Stop();
            return $"{name}: HTTP {(int)response.StatusCode} in {stopwatch.ElapsedMilliseconds} ms";
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return $"{name}: FAILED after {stopwatch.ElapsedMilliseconds} ms ({ex.Message})";
        }
    }

    private static void CheckForClipLinkUpdate()
    {
        if (Interlocked.CompareExchange(ref _diagnosticRunning, 1, 0) != 0)
        {
            Warn("A connectivity or update check is already running.");
            return;
        }
        Notify("ClipLink update check", "Checking the latest GitHub release...", NotificationType.Information, 4f);
        _ = Task.Run(LoadLatestRelease);
    }

    private static void LoadLatestRelease()
    {
        try
        {
            string json = DiagnosticsHttpClient.GetStringAsync(LatestReleaseApiUrl).GetAwaiter().GetResult();
            using JsonDocument document = JsonDocument.Parse(json);
            string tag = JsonText(document.RootElement, "tag_name", "unknown");
            string url = JsonText(document.RootElement, "html_url", ReleasesPageUrl);
            MainThreadActions.Enqueue(() =>
            {
                _latestReleaseUrl = url;
                bool current = Version.TryParse(tag.TrimStart('v'), out Version? latestVersion)
                            && Version.TryParse(ModVersion, out Version? currentVersion)
                            && currentVersion.CompareTo(latestVersion) >= 0;
                Notify("ClipLink update check", current ? $"You are current ({tag})." : $"Latest release: {tag}. Open latest release to update.", current ? NotificationType.Success : NotificationType.Warning, 7f);
            });
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Update check failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _diagnosticRunning, 0);
        }
    }

    private static void OpenLatestRelease()
    {
        Application.OpenURL(_latestReleaseUrl);
        Notify("ClipLink Media", "Opened the latest GitHub release page.", NotificationType.Information, 3f);
    }

    private static void ShowDiskSpace()
    {
        try
        {
            string dataRoot = Path.GetPathRoot(_dataDirectory) ?? string.Empty;
            string jobsRoot = Path.GetPathRoot(ChooseJobsRoot()) ?? string.Empty;
            string dataFree = string.IsNullOrEmpty(dataRoot) ? "unknown" : FormatBytes(new DriveInfo(dataRoot).AvailableFreeSpace);
            string jobsFree = string.IsNullOrEmpty(jobsRoot) ? "unknown" : FormatBytes(new DriveInfo(jobsRoot).AvailableFreeSpace);
            Notify("Disk space", $"ClipLink data drive: {dataFree} free; temporary-job drive: {jobsFree} free.", NotificationType.Information, 6f);
        }
        catch (Exception ex)
        {
            Warn($"Could not read disk space: {ex.Message}");
        }
    }

    private static void TakeScreenshot()
    {
        try
        {
            Directory.CreateDirectory(_screenshotsDirectory);
            string path = Path.Combine(_screenshotsDirectory, $"BONELAB-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            ScreenCapture.CaptureScreenshot(path, 1);
            MelonLogger.Msg($"Screenshot requested: {path}");
            Notify("BONELAB screenshot", $"Saving {Path.GetFileName(path)} in ClipLink Screenshots.", NotificationType.Success, 5f);
        }
        catch (Exception ex)
        {
            Warn($"Could not take screenshot: {ex.Message}");
        }
    }

    private static void BackupClipLinkData()
    {
        try
        {
            string backup = Path.Combine(_backupsDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            int copied = 0;
            foreach (string source in new[] { _settingsPath, _historyPath })
            {
                if (!File.Exists(source)) continue;
                File.Copy(source, Path.Combine(backup, Path.GetFileName(source)), overwrite: false);
                copied++;
            }
            MelonLogger.Msg($"Backed up {copied} ClipLink data file(s) to {backup}");
            Notify("ClipLink backup", $"Backed up {copied} data file(s).", NotificationType.Success, 5f);
        }
        catch (Exception ex)
        {
            Warn($"Backup failed: {ex.Message}");
        }
    }

    private static void CleanOldClipLinkTemps()
    {
        if (!_cleanupConfirmed)
        {
            Warn("Turn on 'Confirm old-temp cleanup' before deleting old ClipLink job folders.");
            return;
        }
        _cleanupConfirmed = false;
        Notify("ClipLink maintenance", "Scanning only ClipLinkMediaJobs folders older than 24 hours...", NotificationType.Information, 4f);
        _ = Task.Run(CleanOldClipLinkTempsWorker);
    }

    private static void CleanOldClipLinkTempsWorker()
    {
        int deletedDirectories = 0;
        long deletedBytes = 0;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Path.GetTempPath(), "ClipLinkMediaJobs"),
        };
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "ClipLinkMediaJobs"));

        DateTime cutoff = DateTime.UtcNow.AddHours(-24);
        foreach (string root in roots)
        {
            try
            {
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(Path.GetFileName(fullRoot), "ClipLinkMediaJobs", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(fullRoot))
                    continue;
                var rootInfo = new DirectoryInfo(fullRoot);
                if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                foreach (DirectoryInfo job in rootInfo.GetDirectories())
                {
                    if (job.LastWriteTimeUtc >= cutoff || (job.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (job.GetDirectories().Length != 0)
                    {
                        MelonLogger.Warning($"Skipped unexpected nested ClipLink temp folder: {job.FullName}");
                        continue;
                    }
                    try { deletedBytes += job.GetFiles().Sum(file => file.Length); }
                    catch { }
                    job.Delete(recursive: true);
                    deletedDirectories++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not clean a ClipLink temp root: {ex.Message}");
            }
        }

        MainThreadActions.Enqueue(() => Notify("ClipLink cleanup finished", $"Deleted {deletedDirectories} old job folder(s), freeing {FormatBytes(deletedBytes)}.", NotificationType.Success, 6f));
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
        _settings.LastFailedUploadPath ??= string.Empty;
        _settings.LastFailedUploadSourceUrl ??= string.Empty;
        _settings.LastFailedUploadRetention ??= string.Empty;
        _settings.PersonalNote ??= string.Empty;
        if (_settings.LowFpsThreshold is not (45 or 60 or 72))
            _settings.LowFpsThreshold = 45;
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
                button.SetTooltip($"{entry.Title}\nSelected: {entry.SelectedUtc.LocalDateTime:g}\nSelect the thumbnail to copy:\n{entry.Url}");
                AttachYouTubeThumbnail(button, GetYouTubeVideoId(entry.Url));
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
                button.SetTooltip($"{entry.Title}\nSelect the thumbnail to copy:\n{entry.Url}");
                AttachYouTubeThumbnail(button, GetYouTubeVideoId(entry.Url));
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
        _searchResultsPage.Name = $"YouTube | {ShortMenuText(query, 34)}";
        foreach (YouTubeSearchResult choice in results)
        {
            YouTubeSearchResult selectedChoice = choice;
            FunctionElement button = _searchResultsPage.CreateFunction(
                ShortMenuText(choice.Title, 64),
                Color.white,
                () => CopyYouTubeChoice(selectedChoice));
            button.SetTooltip($"{choice.Title}\nSelect this thumbnail to copy:\n{choice.Url}");
            AttachYouTubeThumbnail(button, choice.Id);
        }

        MelonLogger.Msg($"Loaded {results.Count} signed-out YouTube results for: {query}");
        Notify("YouTube", $"Loaded {results.Count} thumbnail cards. Select a video to copy its link.", NotificationType.Success, 6f);
        Menu.OpenPage(_searchResultsPage);
    }

    private static void AttachYouTubeThumbnail(FunctionElement button, string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return;

        bool startDownload = false;
        lock (ThumbnailGate)
        {
            if (ThumbnailCache.TryGetValue(videoId, out Texture2D? cached) && cached != null)
            {
                button.Logo = cached;
                return;
            }

            if (!PendingThumbnailButtons.TryGetValue(videoId, out List<FunctionElement>? buttons))
            {
                buttons = new List<FunctionElement>();
                PendingThumbnailButtons[videoId] = buttons;
                startDownload = true;
            }
            buttons.Add(button);
        }

        if (startDownload)
            _ = Task.Run(() => DownloadYouTubeThumbnail(videoId));
    }

    private static async Task DownloadYouTubeThumbnail(string videoId)
    {
        await ThumbnailDownloadSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            string thumbnailUrl = $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";
            byte[] bytes = await YouTubeHttpClient.GetByteArrayAsync(thumbnailUrl).ConfigureAwait(false);
            if (bytes.Length < 256)
                throw new InvalidOperationException("YouTube returned an empty thumbnail.");
            MainThreadActions.Enqueue(() => CompleteYouTubeThumbnail(videoId, bytes));
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not load YouTube thumbnail {videoId}: {ex.Message}");
            MainThreadActions.Enqueue(() => CompleteYouTubeThumbnail(videoId, null));
        }
        finally
        {
            ThumbnailDownloadSlots.Release();
        }
    }

    private static void CompleteYouTubeThumbnail(string videoId, byte[]? bytes)
    {
        List<FunctionElement> buttons;
        lock (ThumbnailGate)
        {
            if (!PendingThumbnailButtons.Remove(videoId, out List<FunctionElement>? pending))
                return;
            buttons = pending;
        }

        if (bytes == null) return;

        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false)
            {
                name = $"YouTube Thumbnail {videoId}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Il2CppStructArray<byte> imageBytes = bytes;
            bool loaded = ImageConversion.LoadImage(texture, imageBytes, markNonReadable: true);
            if (!loaded)
            {
                UnityEngine.Object.Destroy(texture);
                return;
            }

            lock (ThumbnailGate)
            {
                if (ThumbnailCache.Count >= MaximumThumbnailCacheEntries && ThumbnailCacheOrder.Count > 0)
                {
                    string oldest = ThumbnailCacheOrder.Dequeue();
                    ThumbnailCache.Remove(oldest);
                }
                ThumbnailCache[videoId] = texture;
                ThumbnailCacheOrder.Enqueue(videoId);
            }

            foreach (FunctionElement button in buttons)
                button.Logo = texture;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not render YouTube thumbnail {videoId}: {ex.Message}");
        }
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

    private static void InitializeRealYouTubeMenuBridge()
    {
        string browserDirectory = File.Exists(_realYouTubeBrowserPath)
            ? Path.GetDirectoryName(_realYouTubeBrowserPath) ?? _dataDirectory
            : _dataDirectory;
        _realYouTubeMenuBridgeDirectory = Path.Combine(browserDirectory, "ClipLinkMenuBridge");
        _realYouTubeMenuCommandPath = Path.Combine(_realYouTubeMenuBridgeDirectory, "command.json");
        try
        {
            Directory.CreateDirectory(_realYouTubeMenuBridgeDirectory);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not initialize the in-menu YouTube bridge: {ex.Message}");
        }
    }

    private static void StartRealYouTubeInMenu()
    {
        _realYouTubeBrowserPath = ResolveRealYouTubeBrowserPath();
        InitializeRealYouTubeMenuBridge();
        if (!File.Exists(_realYouTubeBrowserPath))
        {
            _launchMenuAfterBrowserInstall = true;
            InstallRealYouTubeBrowser();
            return;
        }

        if (IsRealYouTubeMenuRunning())
        {
            Notify("YouTube is already in the menu", "The real webpage feed is running. Use the menu controls to browse it.", NotificationType.Information, 5f);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _realYouTubeBrowserPath,
                WorkingDirectory = Path.GetDirectoryName(_realYouTubeBrowserPath) ?? _dataDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--menu-mode");
            startInfo.ArgumentList.Add("--bridge-dir");
            startInfo.ArgumentList.Add(_realYouTubeMenuBridgeDirectory);
            Process.Start(startInfo);
            _lastRealBrowserMenuFrameWriteUtc = DateTime.MinValue;
            _realBrowserMenuFrameShown = false;
            Notify("Loading real YouTube in menu", "The actual signed-out webpage is starting behind these controls.", NotificationType.Information, 7f);
            MelonLogger.Msg($"Started in-menu YouTube bridge: {_realYouTubeMenuBridgeDirectory}");
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not start YouTube in the menu: {ex.Message}");
        }
    }

    private static bool IsRealYouTubeMenuRunning()
    {
        string statusPath = Path.Combine(_realYouTubeMenuBridgeDirectory, "status.json");
        try
        {
            if (!File.Exists(statusPath)) return false;
            using JsonDocument status = JsonDocument.Parse(File.ReadAllText(statusPath));
            if (!status.RootElement.TryGetProperty("processId", out JsonElement processIdElement)
                || !processIdElement.TryGetInt32(out int processId))
                return false;
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void SearchRealYouTubeInMenu()
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            Warn("Type a YouTube search first.");
            return;
        }
        SendRealYouTubeMenuCommand("search", _searchQuery);
    }

    private static void SendRealYouTubeMenuCommand(string action, string value = "")
    {
        if (!IsRealYouTubeMenuRunning())
            StartRealYouTubeInMenu();
        try
        {
            Directory.CreateDirectory(_realYouTubeMenuBridgeDirectory);
            File.WriteAllText(_realYouTubeMenuCommandPath, JsonSerializer.Serialize(new
            {
                id = Guid.NewGuid().ToString("N"),
                action,
                value,
                sentAtUtc = DateTimeOffset.UtcNow,
            }));
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not control the in-menu YouTube page: {ex.Message}");
        }
    }

    private static void StopRealYouTubeInMenu()
    {
        if (!IsRealYouTubeMenuRunning())
        {
            Warn("The in-menu YouTube page is not running.");
            return;
        }
        SendRealYouTubeMenuCommand("stop");
        Notify("YouTube menu", "Closing the real webpage feed.", NotificationType.Information, 4f);
    }

    private static void PollRealYouTubeMenuFrame()
    {
        if (_realYouTubeMenuPage == null
            || string.IsNullOrWhiteSpace(_realYouTubeMenuBridgeDirectory)
            || !Directory.Exists(_realYouTubeMenuBridgeDirectory)
            || Volatile.Read(ref _realBrowserMenuFrameReadRunning) != 0)
            return;

        try
        {
            FileInfo? newestFrame = new DirectoryInfo(_realYouTubeMenuBridgeDirectory)
                .GetFiles("youtube-frame-*.png", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newestFrame == null || newestFrame.LastWriteTimeUtc <= _lastRealBrowserMenuFrameWriteUtc) return;
            _lastRealBrowserMenuFrameWriteUtc = newestFrame.LastWriteTimeUtc;
            string framePath = newestFrame.FullName;
            Interlocked.Exchange(ref _realBrowserMenuFrameReadRunning, 1);
            _ = Task.Run(() =>
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(framePath);
                    MainThreadActions.Enqueue(() => DisplayRealYouTubeMenuFrame(bytes));
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not read the in-menu YouTube frame: {ex.Message}");
                    Interlocked.Exchange(ref _realBrowserMenuFrameReadRunning, 0);
                }
            });
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not poll the in-menu YouTube frame: {ex.Message}");
        }
    }

    private static void DisplayRealYouTubeMenuFrame(byte[] bytes)
    {
        try
        {
            if (bytes.Length < 10_000 || _realYouTubeMenuPage == null) return;
            if (_realYouTubeMenuTexture == null)
            {
                _realYouTubeMenuTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "ClipLink Real YouTube Menu",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _realYouTubeMenuPage.Background = _realYouTubeMenuTexture;
                _realYouTubeMenuPage.BackgroundOpacity = 1f;
            }
            if (!ImageConversion.LoadImage(_realYouTubeMenuTexture, bytes, markNonReadable: false))
                throw new InvalidDataException("Unity could not decode the YouTube webpage frame.");

            if (!_realBrowserMenuFrameShown)
            {
                _realBrowserMenuFrameShown = true;
                Notify("Real YouTube is in the menu", "Use Previous/Next and SELECT to click the actual webpage.", NotificationType.Success, 7f);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not display the in-menu YouTube frame: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _realBrowserMenuFrameReadRunning, 0);
        }
    }

    private static void OpenRealYouTubeBrowser()
    {
        _realYouTubeBrowserPath = ResolveRealYouTubeBrowserPath();
        if (!File.Exists(_realYouTubeBrowserPath))
        {
            InstallRealYouTubeBrowser();
            return;
        }

        LaunchRealYouTubeBrowser();
    }

    private static void LaunchRealYouTubeBrowser()
    {
        try
        {
            string workingDirectory = Path.GetDirectoryName(_realYouTubeBrowserPath) ?? _dataDirectory;
            Process.Start(new ProcessStartInfo
            {
                FileName = _realYouTubeBrowserPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
            MelonLogger.Msg($"Opened the real signed-out YouTube GUI: {_realYouTubeBrowserPath}");
            Notify("Real YouTube opened", "Use the SteamVR desktop panel to view it. Clicking a video copies its URL automatically.", NotificationType.Success, 7f);
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not open the real YouTube GUI: {ex.Message}");
        }
    }

    private static void InstallRealYouTubeBrowser()
    {
        if (Interlocked.CompareExchange(ref _browserInstallRunning, 1, 0) != 0)
        {
            Warn("The real YouTube browser is already downloading.");
            return;
        }

        Notify("Installing real YouTube GUI", "Downloading the signed-out browser from this mod's GitHub release...", NotificationType.Information, 6f);
        _ = Task.Run(DownloadRealYouTubeBrowser);
    }

    private static async Task DownloadRealYouTubeBrowser()
    {
        string targetPath = Path.Combine(_dataDirectory, RealYouTubeBrowserFileName);
        string partialPath = targetPath + ".download";
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            using HttpResponseMessage response = await BrowserDownloadHttpClient
                .GetAsync(RealYouTubeBrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using (var destination = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
                await source.CopyToAsync(destination).ConfigureAwait(false);

            long length = new FileInfo(partialPath).Length;
            if (length < 1_000_000)
                throw new InvalidDataException($"GitHub returned an incomplete browser file ({FormatBytes(length)}).");
            File.Move(partialPath, targetPath, overwrite: true);
            _realYouTubeBrowserPath = targetPath;
            MainThreadActions.Enqueue(() =>
            {
                InitializeRealYouTubeMenuBridge();
                Notify("Real YouTube GUI installed", $"Download complete ({FormatBytes(length)}). Opening the actual YouTube website now.", NotificationType.Success, 7f);
                if (_launchMenuAfterBrowserInstall)
                {
                    _launchMenuAfterBrowserInstall = false;
                    StartRealYouTubeInMenu();
                }
                else
                {
                    LaunchRealYouTubeBrowser();
                }
            });
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Real YouTube browser installation failed: {ex}");
            FailOnMainThread($"Browser download failed: {ex.Message}. You can download it from the GitHub release instead.");
        }
        finally
        {
            Interlocked.Exchange(ref _browserInstallRunning, 0);
        }
    }

    private static void CheckRealYouTubeBrowser()
    {
        _realYouTubeBrowserPath = ResolveRealYouTubeBrowserPath();
        if (!File.Exists(_realYouTubeBrowserPath))
        {
            Warn("The real YouTube GUI is not installed. Select Start REAL YouTube in menu or Install / update browser to download it.");
            return;
        }

        string version = DllVersion(_realYouTubeBrowserPath);
        Notify("Real YouTube GUI ready", $"Installed v{version}. It uses YouTube's actual signed-out website and auto-copies selected videos.", NotificationType.Success, 8f);
        MelonLogger.Msg($"Real YouTube browser ready: {_realYouTubeBrowserPath} (v{version})");
    }

    private static void OpenRealYouTubeBrowserFolder()
    {
        _realYouTubeBrowserPath = ResolveRealYouTubeBrowserPath();
        string folder = File.Exists(_realYouTubeBrowserPath)
            ? Path.GetDirectoryName(_realYouTubeBrowserPath) ?? _dataDirectory
            : _dataDirectory;
        OpenFolder(folder, "real YouTube browser folder");
    }

    private static void CopyLastRealYouTubeSelection()
    {
        if (!TryReadRealYouTubeSelection(out string url))
        {
            Warn("No video has been clicked in the real YouTube GUI yet.");
            return;
        }

        GUIUtility.systemCopyBuffer = url;
        AddRecentVideo($"Real YouTube {GetYouTubeVideoId(url)}", url);
        Notify("YouTube URL copied", "The last video clicked in the real YouTube GUI is ready to paste.", NotificationType.Success, 5f);
    }

    private static void SpawnMediaPlayerWithRealYouTubeSelection()
    {
        if (!TryReadRealYouTubeSelection(out string url))
        {
            Warn("Click a video in the real YouTube GUI first.");
            return;
        }

        GUIUtility.systemCopyBuffer = url;
        AddRecentVideo($"Real YouTube {GetYouTubeVideoId(url)}", url);
        SpawnMediaPlayer(MediaPlayerBarcode, "Media Player");
    }

    private static bool TryReadRealYouTubeSelection(out string url)
    {
        url = string.Empty;
        try
        {
            _realYouTubeSelectionPath = ResolveRealYouTubeSelectionPath();
            if (!File.Exists(_realYouTubeSelectionPath)) return false;
            string selected = File.ReadAllText(_realYouTubeSelectionPath).Trim();
            if (!IsYouTubeUrl(selected)) return false;
            url = selected;
            return true;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not read the real YouTube selection: {ex.Message}");
            return false;
        }
    }

    private static void PollRealYouTubeSelection()
    {
        try
        {
            _realYouTubeSelectionPath = ResolveRealYouTubeSelectionPath();
            if (!File.Exists(_realYouTubeSelectionPath)) return;
            DateTime writeUtc = File.GetLastWriteTimeUtc(_realYouTubeSelectionPath);
            if (writeUtc <= _lastRealBrowserSelectionWriteUtc) return;
            _lastRealBrowserSelectionWriteUtc = writeUtc;
            if (!TryReadRealYouTubeSelection(out string url)) return;

            GUIUtility.systemCopyBuffer = url;
            AddRecentVideo($"Real YouTube {GetYouTubeVideoId(url)}", url);
            Notify("YouTube video selected", "URL copied automatically. It is ready for ClipLink or Media Player.", NotificationType.Success, 6f);
            MelonLogger.Msg($"Real YouTube GUI selected and copied: {url}");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not poll the real YouTube browser selection: {ex.Message}");
        }
    }

    private static string ResolveRealYouTubeSelectionPath()
    {
        string standardPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipLinkMedia",
            "selected-youtube-url.txt");
        string companionPath = string.IsNullOrWhiteSpace(_realYouTubeBrowserPath)
            ? string.Empty
            : Path.Combine(Path.GetDirectoryName(_realYouTubeBrowserPath) ?? string.Empty, "selected-youtube-url.txt");

        return new[] { standardPath, companionPath }
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? standardPath;
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

    private static void RetryLastFailedUpload()
    {
        if (!_rightsConfirmed)
        {
            Warn("Turn on 'I own / have permission' before retrying an upload.");
            return;
        }

        string savedPath = _settings.LastFailedUploadPath;
        if (string.IsNullOrWhiteSpace(savedPath) || !File.Exists(savedPath))
        {
            Warn("There is no saved failed upload to retry.");
            return;
        }

        if (Interlocked.CompareExchange(ref _jobRunning, 1, 0) != 0)
        {
            Warn("ClipLink Media is already working on a video.");
            return;
        }

        string retention = IsSupportedRetention(_settings.LastFailedUploadRetention)
            ? _settings.LastFailedUploadRetention
            : _settings.LitterboxRetention;
        string sourceUrl = _settings.LastFailedUploadSourceUrl;
        var cancellation = new CancellationTokenSource();
        lock (JobGate) _jobCancellation = cancellation;
        _jobStartedUtc = DateTimeOffset.UtcNow;
        _jobStatus = $"Retrying saved upload: {Path.GetFileName(savedPath)}";
        Notify("ClipLink upload retry", $"Uploading the saved MP4 for {RetentionDisplay(retention)}...", NotificationType.Information, 5f);
        _ = Task.Run(() => ProcessSavedUploadJob(savedPath, sourceUrl, retention, cancellation));
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

    private static void ProcessSavedUploadJob(string savedPath, string sourceUrl, string retention, CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;
        try
        {
            long fileBytes = new FileInfo(savedPath).Length;
            string publicUrl = UploadToLitterbox(savedPath, retention, token);
            token.ThrowIfCancellationRequested();
            QueuePublicUploadCompletion(publicUrl, sourceUrl, retention, fileBytes, clearFailedUpload: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _jobStatus = "Saved upload retry cancelled";
            MainThreadActions.Enqueue(() => Notify("ClipLink Media", "Saved upload retry cancelled. The MP4 is still in Downloads.", NotificationType.Warning, 5f));
        }
        catch (Exception ex)
        {
            _jobStatus = $"Saved upload retry failed: {LastPart(ex.Message, 120)}";
            MelonLogger.Error($"Saved upload retry failed: {ex}");
            MainThreadActions.Enqueue(() => Notify("Upload still unavailable", $"{LastPart(ex.Message, 130)} The MP4 remains saved; retry later.", NotificationType.Error, 8f));
        }
        finally
        {
            lock (JobGate)
            {
                if (ReferenceEquals(_jobCancellation, cancellation))
                    _jobCancellation = null;
            }
            cancellation.Dispose();
            Interlocked.Exchange(ref _jobRunning, 0);
        }
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
            $"Version: {ModVersion}",
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
            $"Personal note saved: {!string.IsNullOrWhiteSpace(_settings.PersonalNote)}",
            $"Low-FPS warnings: {_settings.LowFpsAlertsEnabled} below {_settings.LowFpsThreshold}",
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
        string? mp4Path = null;
        bool uploadStarted = false;
        CancellationToken token = cancellation.Token;
        try
        {
            jobDirectory = CreateJobDirectory();
            mp4Path = DownloadVideo(youtubeUrl, jobDirectory, quality, token);
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

            uploadStarted = true;
            string publicUrl = UploadToLitterbox(mp4Path, retention, token);
            token.ThrowIfCancellationRequested();
            QueuePublicUploadCompletion(publicUrl, youtubeUrl, retention, fileBytes, clearFailedUpload: false);
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
            if (uploadPublicly && uploadStarted && !string.IsNullOrWhiteSpace(mp4Path) && File.Exists(mp4Path))
            {
                try
                {
                    string savedPath = SaveDownloadedVideo(mp4Path, CancellationToken.None);
                    MainThreadActions.Enqueue(() =>
                    {
                        _settings.LastFailedUploadPath = savedPath;
                        _settings.LastFailedUploadSourceUrl = youtubeUrl;
                        _settings.LastFailedUploadRetention = retention;
                        SaveSettings();
                        RefreshDownloadsPage();
                        _jobStatus = $"Upload failed; MP4 saved: {Path.GetFileName(savedPath)}";
                        Notify("Upload failed - MP4 saved", $"{LastPart(ex.Message, 105)} Use 'Retry saved failed upload'; no re-download needed.", NotificationType.Error, 10f);
                    });
                    MelonLogger.Warning($"Upload failed, so the downloaded MP4 was preserved at {savedPath}");
                }
                catch (Exception saveEx)
                {
                    MelonLogger.Error($"Could not preserve the downloaded MP4 after upload failure: {saveEx}");
                    FailOnMainThread($"{ex.Message} The downloaded MP4 also could not be saved: {saveEx.Message}");
                }
            }
            else
            {
                FailOnMainThread(ex.Message);
            }
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

    private static void QueuePublicUploadCompletion(string publicUrl, string sourceUrl, string retention, long fileBytes, bool clearFailedUpload)
    {
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
                SourceUrl = sourceUrl,
                CreatedUtc = createdUtc,
                ExpiresUtc = expiresUtc,
            });
            while (LinkHistory.Count > MaximumHistoryEntries)
                LinkHistory.RemoveAt(LinkHistory.Count - 1);
            _settings.TotalPublicLinks++;
            _settings.TotalBytesProcessed += fileBytes;
            _settings.LastJobCompletedUtc = createdUtc;
            if (clearFailedUpload)
            {
                _settings.LastFailedUploadPath = string.Empty;
                _settings.LastFailedUploadSourceUrl = string.Empty;
                _settings.LastFailedUploadRetention = string.Empty;
            }
            SaveSettings();
            RefreshHistoryPage();
            _jobStatus = $"Completed public link: {publicUrl}";
            MelonLogger.Msg($"Public MP4 URL copied: {publicUrl}");
            Notify("ClipLink Media finished", $"{RetentionDisplay(retention)} MP4 URL copied. Expires {expiresUtc.LocalDateTime:g}.", NotificationType.Success, 8f);
        });
    }

    private static string DownloadVideo(string youtubeUrl, string jobDirectory, string quality, CancellationToken token)
    {
        if (!File.Exists(_ytDlpPath))
            throw new FileNotFoundException("yt-dlp.exe is missing. Download it from the official GitHub link in BoneMenu and put it in the ClipLinkMedia folder.", _ytDlpPath);

        (string? path, string error) first = RunYtDlpAttempt(youtubeUrl, Path.Combine(jobDirectory, "default-client"), quality, null, token);
        if (!string.IsNullOrWhiteSpace(first.path))
            return ValidateDownloadedMp4(first.path);

        MelonLogger.Warning($"Default yt-dlp client attempt failed; retrying with Android VR and Safari clients. {LastPart(first.error, 350)}");
        (string? path, string error) fallback = RunYtDlpAttempt(
            youtubeUrl,
            Path.Combine(jobDirectory, "fallback-client"),
            quality,
            "youtube:player_client=android_vr,web_safari",
            token);
        if (!string.IsNullOrWhiteSpace(fallback.path))
            return ValidateDownloadedMp4(fallback.path);

        throw new InvalidOperationException($"Download failed after two YouTube client attempts: {LastPart(fallback.error, 420)}");
    }

    private static (string? path, string error) RunYtDlpAttempt(string youtubeUrl, string attemptDirectory, string quality, string? extractorArgs, CancellationToken token)
    {
        Directory.CreateDirectory(attemptDirectory);
        string resultFile = Path.Combine(attemptDirectory, "download-result.txt");
        var startInfo = new ProcessStartInfo
        {
            FileName = _ytDlpPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = attemptDirectory,
        };
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--windows-filenames");
        startInfo.ArgumentList.Add("--newline");
        if (!string.IsNullOrWhiteSpace(extractorArgs))
        {
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add(extractorArgs);
        }
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(VideoFormatSelector(quality));
        startInfo.ArgumentList.Add("--max-filesize");
        startInfo.ArgumentList.Add("1000M");
        startInfo.ArgumentList.Add("--print-to-file");
        startInfo.ArgumentList.Add("after_move:%(filepath)s");
        startInfo.ArgumentList.Add(resultFile);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(Path.Combine(attemptDirectory, "%(title).80B [%(id)s].%(ext)s"));
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
            return (null, string.IsNullOrWhiteSpace(stderr.Result) ? stdout.Result.Trim() : stderr.Result.Trim());

        string? mp4Path = File.Exists(resultFile)
            ? File.ReadLines(resultFile).LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
            : null;
        if (string.IsNullOrEmpty(mp4Path) || !File.Exists(mp4Path))
            return (null, "The download finished but no MP4 file was found.");

        return (mp4Path, string.Empty);
    }

    private static string ValidateDownloadedMp4(string mp4Path)
    {
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
        string safeUploadName = $"cliplink-{Guid.NewGuid():N}.mp4";
        string lastError = "Litterbox did not return a URL.";

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using HttpClient client = CreateUploadHttpClient();
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent("fileupload"), "reqtype");
                form.Add(new StringContent(retention), "time");

                using var fileContent = new StreamContent(File.OpenRead(mp4Path));
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
                fileContent.Headers.ContentLength = new FileInfo(mp4Path).Length;
                form.Add(fileContent, "fileToUpload", safeUploadName);

                using HttpResponseMessage response = client.PostAsync(LitterboxUploadUrl, form, token).GetAwaiter().GetResult();
                string responseBody = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult().Trim();
                if (response.IsSuccessStatusCode
                    && Uri.TryCreate(responseBody, UriKind.Absolute, out Uri? uri)
                    && uri.Scheme == Uri.UriSchemeHttps
                    && (uri.Host.Equals("catbox.moe", StringComparison.OrdinalIgnoreCase)
                        || uri.Host.EndsWith(".catbox.moe", StringComparison.OrdinalIgnoreCase)))
                {
                    return uri.AbsoluteUri;
                }

                string bodySummary = responseBody.StartsWith("<", StringComparison.Ordinal)
                    ? "Litterbox returned an HTML server/WAF error instead of a URL."
                    : LastPart(responseBody, 220);
                lastError = $"attempt {attempt}/3, HTTP {(int)response.StatusCode}: {bodySummary}";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = $"attempt {attempt}/3: {LastPart(ex.Message, 240)}";
            }

            MelonLogger.Warning($"Litterbox upload {lastError}");
            if (attempt < 3)
                Task.Delay(TimeSpan.FromSeconds(attempt * 2), token).GetAwaiter().GetResult();
        }

        throw new InvalidOperationException($"Litterbox upload failed after 3 attempts ({lastError}).");
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

    private static string ResolveRealYouTubeBrowserPath()
    {
        string installedPath = Path.Combine(_dataDirectory, RealYouTubeBrowserFileName);
        if (File.Exists(installedPath)) return installedPath;

        string pathPointer = Path.Combine(_dataDirectory, "youtube-browser-path.txt");
        try
        {
            if (File.Exists(pathPointer))
            {
                string configuredPath = Environment.ExpandEnvironmentVariables(File.ReadAllText(pathPointer).Trim().Trim('"'));
                if (File.Exists(configuredPath)) return configuredPath;
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Could not read YouTube browser path: {ex.Message}");
        }

        string userDataDirectory = MelonEnvironment.UserDataDirectory;
        if (Directory.Exists(userDataDirectory))
        {
            try
            {
                string? foundPath = Directory
                    .EnumerateFiles(userDataDirectory, RealYouTubeBrowserFileName, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(foundPath)) return foundPath;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not search UserData for the real YouTube browser: {ex.Message}");
            }
        }

        string downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            RealYouTubeBrowserFileName);
        return File.Exists(downloadsPath) ? downloadsPath : installedPath;
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
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/127 Safari/537.36 ClipLinkMedia/{ModVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        client.DefaultRequestHeaders.Referrer = new Uri("https://litterbox.catbox.moe/");
        client.DefaultRequestHeaders.ExpectContinue = false;
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://litterbox.catbox.moe");
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

    private static HttpClient CreateDiagnosticsHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ClipLinkMedia/{ModVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static HttpClient CreateBrowserDownloadHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ClipLinkMedia/{ModVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
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
    public string LastFailedUploadPath { get; set; } = string.Empty;
    public string LastFailedUploadSourceUrl { get; set; } = string.Empty;
    public string LastFailedUploadRetention { get; set; } = string.Empty;
    public List<string> RecentSearches { get; set; } = new();
    public List<RecentVideoEntry> RecentVideos { get; set; } = new();
    public List<RecentVideoEntry> Favorites { get; set; } = new();
    public List<RecentVideoEntry> JobQueue { get; set; } = new();
    public string PersonalNote { get; set; } = string.Empty;
    public bool LowFpsAlertsEnabled { get; set; }
    public int LowFpsThreshold { get; set; } = 45;
    public int TotalPublicLinks { get; set; }
    public int TotalLocalDownloads { get; set; }
    public long TotalBytesProcessed { get; set; }
    public DateTimeOffset? LastJobCompletedUtc { get; set; }
}

public sealed class ClipLinkPresenceInfo
{
    public ClipLinkPresenceInfo(string version, DateTimeOffset lastSeenUtc)
    {
        Version = version;
        LastSeenUtc = lastSeenUtc;
    }

    public string Version { get; }
    public DateTimeOffset LastSeenUtc { get; }
}

public sealed class ClipLinkPresenceData : INetSerializable
{
    public ClipLinkPresenceData()
    {
    }

    public ClipLinkPresenceData(string version, bool requestReply)
    {
        Version = version;
        RequestReply = requestReply;
    }

    public string Version { get; private set; } = string.Empty;
    public bool RequestReply { get; private set; }

    public void Serialize(INetSerializer serializer)
    {
        string version = Version;
        bool requestReply = RequestReply;
        serializer.SerializeValue(ref version);
        serializer.SerializeValue(ref requestReply);
        if (serializer.IsReader)
        {
            Version = version;
            RequestReply = requestReply;
        }
    }
}

public sealed class ClipLinkPresenceMessage : ModuleMessageHandler
{
    protected override void OnHandleMessage(ReceivedMessage received) => Core.ReceiveClipLinkPresence(received);
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
