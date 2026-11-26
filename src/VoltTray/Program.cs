using VoltTray.Hosting;

namespace VoltTray;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ApplicationOptions options = ApplicationOptionsParser.Parse(args);
        IApplicationModeStrategy strategy = ApplicationModeStrategyFactory.Create(options);
        return strategy.Execute(options);
    }
}
