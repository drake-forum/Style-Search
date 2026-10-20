using System.Windows;

namespace AintoWindows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var launcher = new MainWindow();
        app.Run(launcher);
    }
}

