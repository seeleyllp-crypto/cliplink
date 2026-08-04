namespace ClipLinkYouTubeBrowser;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool smokeTest = args.Any(arg => arg.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));
        string? statusPath = null;
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals("--status-file", StringComparison.OrdinalIgnoreCase))
                statusPath = args[index + 1];
        }

        Application.Run(new BrowserForm(smokeTest, statusPath));
    }
}
