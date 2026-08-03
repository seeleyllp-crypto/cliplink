using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoneLib.BoneMenu;
using BoneLib.Notifications;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(ClipLinkMedia.Core), "ClipLink Media", "2.1.2", "seeleyllp-crypto")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: AssemblyVersion("2.1.2.0")]
[assembly: AssemblyFileVersion("2.1.2.0")]

namespace ClipLinkMedia;

public sealed class Core : MelonMod
{
    private const string YouTubeHome = "https://www.youtube.com/";
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string CatboxUploadUrl = "https://catbox.moe/user/api.php";
    private const long MaxUploadBytes = 200L * 1024 * 1024;
    private const long MinimumTemporarySpace = 500L * 1024 * 1024;
    private static readonly ConcurrentQueue<Action> MainThreadActions = new();
    private static readonly HttpClient UploadHttpClient = CreateUploadHttpClient();
    private static readonly HttpClient YouTubeHttpClient = CreateYouTubeHttpClient();
    private static string _dataDirectory = string.Empty;
    private static string _ytDlpPath = string.Empty;
    private static string _lastPublicUrl = string.Empty;
    private static string _searchQuery = "bonelab";
    private static Page? _searchResultsPage;
    private static bool _rightsConfirmed;
    private static int _jobRunning;
    private static int _searchRunning;

    public override void OnInitializeMelon()
    {
        _dataDirectory = Path.Combine(MelonEnvironment.UserDataDirectory, "ClipLinkMedia");
        Directory.CreateDirectory(_dataDirectory);
        _ytDlpPath = ResolveYtDlpPath();

        BuildBoneMenu();
        MelonLogger.Msg("Ready. Browse YouTube without login in BoneMenu, select a video to copy its link, then choose Make public MP4 URL.");
        MelonLogger.Warning("Catbox uploads are public. Upload only videos you own or have permission to share.");
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

    private static void BuildBoneMenu()
    {
        Page page = Page.Root.CreatePage("ClipLink Media", Color.cyan);
        Page browserPage = page.CreatePage("YouTube browser - no login", Color.red);
        StringElement searchElement = browserPage.CreateString("Search", Color.white, _searchQuery, value => _searchQuery = value.Trim());
        searchElement.SetTooltip("Select the keyboard button, type a search, and press Enter.");
        browserPage.CreateFunction("Search YouTube", Color.red, SearchYouTube);
        _searchResultsPage = browserPage.CreatePage("Video results", Color.cyan);
        _searchResultsPage.CreateFunction("Search first", Color.gray, SearchYouTube);

        page.CreateFunction("Open YouTube on desktop", Color.red, OpenYouTube);
        page.CreateFunction("Get yt-dlp from GitHub", Color.yellow, OpenYtDlpDownload);
        page.CreateFunction("Open yt-dlp folder", Color.yellow, OpenYtDlpFolder);
        page.CreateBool("I own / have permission", Color.yellow, false, value => _rightsConfirmed = value);
        page.CreateFunction("Make public MP4 URL", Color.green, StartClipboardJob);
        page.CreateFunction("Copy last public URL", Color.cyan, CopyLastPublicUrl);
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
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _dataDirectory,
                UseShellExecute = true,
            });
            MelonLogger.Msg($"Opened yt-dlp folder: {_dataDirectory}");
        }
        catch (Exception ex)
        {
            FailOnMainThread($"Could not open the yt-dlp folder: {ex.Message}");
        }
    }

    private static void StartClipboardJob()
    {
        if (!_rightsConfirmed)
        {
            Warn("Turn on 'I own / have permission' before uploading.");
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

        MelonLogger.Msg($"Starting download: {url}");
        Notify("ClipLink Media", "Downloading copied YouTube video...", NotificationType.Information, 3f);
        _ = Task.Run(() => CreatePublicUrl(url));
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

    private static void CreatePublicUrl(string youtubeUrl)
    {
        string? jobDirectory = null;
        try
        {
            jobDirectory = CreateJobDirectory();
            string mp4Path = DownloadVideo(youtubeUrl, jobDirectory);

            MainThreadActions.Enqueue(() =>
            {
                MelonLogger.Msg($"Download finished: {Path.GetFileName(mp4Path)}. Uploading to Catbox...");
                Notify("Download finished", "Uploading the MP4 to make a direct URL...", NotificationType.Information, 4f);
            });

            string publicUrl = UploadToCatbox(mp4Path);
            MainThreadActions.Enqueue(() =>
            {
                _lastPublicUrl = publicUrl;
                GUIUtility.systemCopyBuffer = publicUrl;
                MelonLogger.Msg($"Public MP4 URL copied: {publicUrl}");
                Notify("ClipLink Media finished", "Public MP4 URL copied to your clipboard.", NotificationType.Success, 6f);
            });
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Could not create public MP4 URL: {ex}");
            FailOnMainThread(ex.Message);
        }
        finally
        {
            if (!string.IsNullOrEmpty(jobDirectory))
            {
                try { Directory.Delete(jobDirectory, recursive: true); }
                catch (Exception ex) { MelonLogger.Warning($"Could not remove temporary video: {ex.Message}"); }
            }
            Interlocked.Exchange(ref _jobRunning, 0);
        }
    }

    private static string DownloadVideo(string youtubeUrl, string jobDirectory)
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
        startInfo.ArgumentList.Add("200M");
        startInfo.ArgumentList.Add("--print-to-file");
        startInfo.ArgumentList.Add("after_move:%(filepath)s");
        startInfo.ArgumentList.Add(resultFile);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(Path.Combine(jobDirectory, "%(id)s.%(ext)s"));
        startInfo.ArgumentList.Add(youtubeUrl);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("yt-dlp did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Download failed: {LastPart(stderr.Result.Trim(), 500)}");

        string? mp4Path = File.Exists(resultFile)
            ? File.ReadLines(resultFile).LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
            : null;
        if (string.IsNullOrEmpty(mp4Path) || !File.Exists(mp4Path))
            throw new InvalidOperationException("The download finished but no MP4 file was found.");

        long fileSize = new FileInfo(mp4Path).Length;
        if (fileSize > MaxUploadBytes)
            throw new InvalidOperationException("The MP4 is larger than Catbox's 200 MB upload limit.");

        return mp4Path;
    }

    private static string UploadToCatbox(string mp4Path)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("fileupload"), "reqtype");

        var fileContent = new StreamContent(File.OpenRead(mp4Path));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(fileContent, "fileToUpload", Path.GetFileName(mp4Path));

        using HttpResponseMessage response = UploadHttpClient.PostAsync(CatboxUploadUrl, form).GetAwaiter().GetResult();
        string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Trim();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Upload failed ({(int)response.StatusCode}): {LastPart(responseBody, 300)}");
        if (!Uri.TryCreate(responseBody, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Catbox returned an invalid URL: {LastPart(responseBody, 300)}");

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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClipLinkMedia/2.1.2");
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
