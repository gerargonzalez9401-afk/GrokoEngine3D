using System;

namespace GrokoEngine.Hub;

internal static class Program
{
    // STA es necesario para FolderBrowserDialog (System.Windows.Forms).
    [STAThread]
    private static void Main()
    {
        using var hub = new HubApp();
        hub.Run();
    }
}
