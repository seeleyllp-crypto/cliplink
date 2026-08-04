namespace ClipLinkYouTubeBrowser;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool smokeTest = args.Any(arg => arg.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));
        bool menuMode = args.Any(arg => arg.Equals("--menu-mode", StringComparison.OrdinalIgnoreCase));
        string? statusPath = null;
        string? bridgeDirectory = null;
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals("--status-file", StringComparison.OrdinalIgnoreCase))
                statusPath = args[index + 1];
            if (args[index].Equals("--bridge-dir", StringComparison.OrdinalIgnoreCase))
                bridgeDirectory = args[index + 1];
        }

        Application.Run(new BrowserForm(smokeTest, statusPath, menuMode, bridgeDirectory));
    }
}
