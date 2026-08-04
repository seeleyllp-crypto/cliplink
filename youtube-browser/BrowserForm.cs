using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClipLinkYouTubeBrowser;

internal sealed class BrowserForm : Form
{
    private const string YouTubeHome = "https://www.youtube.com/?hl=en";
    private const string SmokeVideo = "https://www.youtube.com/watch?v=ujUXRqh-tko&hl=en";
    private const string RuntimeDownloadUrl = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly TextBox _address = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom,
        Height = 27,
        Padding = new Padding(10, 5, 10, 0),
        BackColor = Color.FromArgb(31, 31, 31),
        ForeColor = Color.Gainsboro,
        Text = "Starting the real YouTube website...",
    };
    private readonly bool _smokeTest;
    private readonly string _statusPath;
    private readonly System.Windows.Forms.Timer _smokeTimeout = new() { Interval = 60_000 };
    private string _runtimeVersion = "unknown";
    private string _lastCopied = string.Empty;
    private string _lastSelectionFile = string.Empty;
    private bool _finishedSmokeTest;

    public BrowserForm(bool smokeTest, string? statusPath)
    {
        _smokeTest = smokeTest;
        _statusPath = string.IsNullOrWhiteSpace(statusPath)
            ? Path.Combine(Path.GetTempPath(), "cliplink-youtube-browser-smoke.json")
            : Path.GetFullPath(statusPath);

        Text = "ClipLink - Real YouTube (Signed Out)";
        Icon = SystemIcons.Application;
        BackColor = Color.FromArgb(15, 15, 15);
        ForeColor = Color.White;
        MinimumSize = new Size(900, 600);
        ClientSize = new Size(1280, 820);
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(_webView);
        Controls.Add(BuildToolbar());
        Controls.Add(_status);

        _address.KeyDown += AddressOnKeyDown;
        Shown += async (_, _) => await InitializeBrowserAsync();
        FormClosed += (_, _) => _smokeTimeout.Stop();
        _smokeTimeout.Tick += (_, _) => FinishSmokeTest(false, "Timed out while loading the real YouTube website.");

        if (_smokeTest)
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20_000, -20_000);
            Size = new Size(960, 640);
            Opacity = 0.01;
            _smokeTimeout.Start();
        }
    }

    private Control BuildToolbar()
    {
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Color.FromArgb(33, 33, 33),
            Padding = new Padding(5),
            ColumnCount = 7,
            RowCount = 1,
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));

        toolbar.Controls.Add(MakeButton("<", (_, _) => { if (_webView.CanGoBack) _webView.GoBack(); }), 0, 0);
        toolbar.Controls.Add(MakeButton(">", (_, _) => { if (_webView.CanGoForward) _webView.GoForward(); }), 1, 0);
        toolbar.Controls.Add(MakeButton("Reload", (_, _) => _webView.Reload()), 2, 0);
        toolbar.Controls.Add(MakeButton("Home", (_, _) => Navigate(YouTubeHome)), 3, 0);
        toolbar.Controls.Add(_address, 4, 0);
        toolbar.Controls.Add(MakeButton("Go", (_, _) => NavigateFromAddress()), 5, 0);
        toolbar.Controls.Add(MakeButton("Copy URL", (_, _) => CopyCurrentAddress()), 6, 0);
        return toolbar;
    }

    private static Button MakeButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Dock = DockStyle.Fill,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = Color.White,
            Margin = new Padding(2),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(85, 85, 85);
        button.Click += click;
        return button;
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            _runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            string profileDirectory = Path.Combine(BrowserDataRoot(), "YouTubeBrowser");
            Directory.CreateDirectory(profileDirectory);

            _webView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = profileDirectory,
                ProfileName = "ClipLinkSignedOut",
                IsInPrivateModeEnabled = true,
                AdditionalBrowserArguments = "--disable-features=msEdgeAutofill",
            };
            await _webView.EnsureCoreWebView2Async();

            CoreWebView2 core = _webView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NavigationStarting += CoreOnNavigationStarting;
            core.NavigationCompleted += CoreOnNavigationCompleted;
            core.SourceChanged += (_, _) => UpdateAddressAndCopyVideo();
            core.NewWindowRequested += CoreOnNewWindowRequested;
            core.DocumentTitleChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(core.DocumentTitle))
                    Text = $"{core.DocumentTitle} - ClipLink (Signed Out)";
            };

            Navigate(_smokeTest ? SmokeVideo : YouTubeHome);
        }
        catch (Exception ex)
        {
            string message = $"WebView2 could not start: {ex.Message}";
            _status.Text = message;
            if (_smokeTest)
            {
                FinishSmokeTest(false, message);
                return;
            }

            MessageBox.Show(
                this,
                message + "\n\nInstall the Microsoft Edge WebView2 Runtime, then reopen ClipLink.",
                "ClipLink YouTube Browser",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Process.Start(new ProcessStartInfo { FileName = RuntimeDownloadUrl, UseShellExecute = true });
        }
    }

    private void CoreOnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri)) return;
        if (IsLoginHost(uri.Host))
        {
            e.Cancel = true;
            _status.Text = "Login blocked - ClipLink keeps YouTube signed out.";
            return;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != "about")
        {
            e.Cancel = true;
            _status.Text = "Blocked a non-secure page.";
        }
    }

    private void CoreOnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        UpdateAddressAndCopyVideo();
        if (!e.IsSuccess)
        {
            string message = $"YouTube navigation failed: {e.WebErrorStatus}";
            _status.Text = message;
            if (_smokeTest) FinishSmokeTest(false, message);
            return;
        }

        if (!string.IsNullOrEmpty(_lastCopied))
        {
            _status.Text = "Video URL copied automatically. Return to BONELAB and paste it into Media Player.";
            if (_smokeTest) FinishSmokeTest(true, "The real YouTube page loaded and its video URL was copied.");
        }
        else
        {
            _status.Text = "Real YouTube loaded in signed-out mode. Click a video and ClipLink copies its URL.";
        }
    }

    private void CoreOnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri) && IsLoginHost(uri.Host))
        {
            _status.Text = "Login blocked - ClipLink keeps YouTube signed out.";
            return;
        }
        Navigate(e.Uri);
    }

    private void UpdateAddressAndCopyVideo()
    {
        string source = _webView.Source?.AbsoluteUri ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(source)) _address.Text = source;
        if (!TryNormalizeVideoUrl(source, out string normalized) || normalized == _lastCopied) return;

        _lastCopied = normalized;
        try
        {
            Clipboard.SetText(normalized);
            _lastSelectionFile = WriteSelectionFile(normalized);
            _status.Text = "Copied video URL: " + normalized;
        }
        catch (Exception ex)
        {
            _status.Text = "Selected video, but copying failed: " + ex.Message;
            if (_smokeTest) FinishSmokeTest(false, _status.Text);
        }
    }

    private void AddressOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        NavigateFromAddress();
    }

    private void NavigateFromAddress()
    {
        string value = _address.Text.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
            Navigate(uri.AbsoluteUri);
        else
            Navigate("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(value));
    }

    private void Navigate(string address)
    {
        if (_webView.CoreWebView2 == null) return;
        _webView.CoreWebView2.Navigate(address);
    }

    private void CopyCurrentAddress()
    {
        string source = _webView.Source?.AbsoluteUri ?? _address.Text.Trim();
        if (string.IsNullOrWhiteSpace(source)) return;
        Clipboard.SetText(source);
        _status.Text = "Current URL copied.";
    }

    private void FinishSmokeTest(bool success, string message)
    {
        if (!_smokeTest || _finishedSmokeTest) return;
        _finishedSmokeTest = true;
        _smokeTimeout.Stop();
        try
        {
            string? parent = Path.GetDirectoryName(_statusPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(_statusPath, JsonSerializer.Serialize(new
            {
                success,
                message,
                runtimeVersion = _runtimeVersion,
                source = _webView.Source?.AbsoluteUri ?? string.Empty,
                normalizedVideoUrl = _lastCopied,
                selectionFile = _lastSelectionFile,
                checkedAtUtc = DateTimeOffset.UtcNow,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // The process still returns a failure code if the status file cannot be written.
            success = false;
        }
        Environment.ExitCode = success ? 0 : 1;
        BeginInvoke(Close);
    }

    private static string SelectionFilePath()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("CLIPLINK_BROWSER_DATA");
        string root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipLinkMedia")
            : Path.GetFullPath(overrideRoot);
        return Path.Combine(root, "selected-youtube-url.txt");
    }

    private static string WriteSelectionFile(string url)
    {
        string primaryPath = SelectionFilePath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(primaryPath)!);
            File.WriteAllText(primaryPath, url);
            return primaryPath;
        }
        catch
        {
            string fallbackPath = Path.Combine(AppContext.BaseDirectory, "selected-youtube-url.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath)!);
            File.WriteAllText(fallbackPath, url);
            return fallbackPath;
        }
    }

    private static string BrowserDataRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("CLIPLINK_BROWSER_DATA");
        if (!string.IsNullOrWhiteSpace(overrideRoot)) return Path.GetFullPath(overrideRoot);

        string localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipLinkMedia");
        try
        {
            string driveRoot = Path.GetPathRoot(localRoot) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(driveRoot) && new DriveInfo(driveRoot).AvailableFreeSpace >= 512L * 1024 * 1024)
                return localRoot;
        }
        catch
        {
            // Fall through to storage beside the executable.
        }
        return Path.Combine(AppContext.BaseDirectory, "ClipLinkBrowserData");
    }

    private static bool IsLoginHost(string host)
    {
        return host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".accounts.google.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeVideoUrl(string source, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)) return false;
        string host = uri.Host.TrimEnd('.').ToLowerInvariant();
        string videoId = string.Empty;

        if (host == "youtu.be")
        {
            videoId = uri.AbsolutePath.Trim('/').Split('/')[0];
        }
        else if (host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal))
        {
            if (uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
                videoId = QueryValue(uri.Query, "v");
            else if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.StartsWith("/live/", StringComparison.OrdinalIgnoreCase))
                videoId = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() ?? string.Empty;
        }

        if (videoId.Length != 11 || videoId.Any(character => !char.IsLetterOrDigit(character) && character != '_' && character != '-'))
            return false;
        normalized = "https://www.youtube.com/watch?v=" + videoId;
        return true;
    }

    private static string QueryValue(string query, string key)
    {
        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            if (pair.Length == 2 && Uri.UnescapeDataString(pair[0]).Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[1]);
        }
        return string.Empty;
    }
}
