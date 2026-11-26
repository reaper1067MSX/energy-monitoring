using VoltTray.Hosting;

namespace VoltTray;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ApplicationLaunchOptions options = ApplicationOptionsParser.Parse(args);
        IApplicationModeStrategy strategy = new ApplicationModeStrategyFactory().Create(options);
        return strategy.Execute();
    }
}
