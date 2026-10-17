using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ATW11;

/// <summary>版本号、反馈邮箱、程序图标这些"关于信息"集中放这里。</summary>
public static class AppInfo
{
    public const string Version = "v1.0";

    public const string Email = "3807318397@qq.com";

    public const string DisplayName = "ATW11 · Aero to Windows 11";

    /// <summary>从 exe 资源里取出图标（app.ico），给控制中心 / 托盘共用。</summary>
    public static ImageSource? LoadIcon(int size = 32)
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}
