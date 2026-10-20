using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace AintoWindows;

internal static class AppAssets
{
    public static BitmapImage? LoadLogo()
    {
        try
        {
            using var stream = OpenAsset("StyleSearch.logo.png", "logo.png");
            if (stream is null) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    public static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            using var stream = OpenAsset("StyleSearch.logo.ico", "logo.ico");
            if (stream is not null)
            {
                using var icon = new System.Drawing.Icon(stream);
                return (System.Drawing.Icon)icon.Clone();
            }
        }
        catch { }

        return System.Drawing.SystemIcons.Application;
    }

    private static Stream? OpenAsset(string resourceName, string fileName)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is not null) return stream;

        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }
}
