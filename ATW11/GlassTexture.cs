using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ATW11;

/// <summary>
/// 生成"玻璃底板"：把桌面壁纸按 Windows 的填充规则铺成屏幕大小，再做一次高斯模糊。
///
/// 为什么要壁纸而不是屏幕截图？
/// 资源管理器现在压在壁纸上面，它背后真正的东西是看不到的；Win7 Aero 玻璃里透出的
/// 恰恰是**壁纸**。所以直接读壁纸文件、按屏幕坐标铺好、模糊掉，就是最接近真实的做法。
/// 取不到壁纸（纯色桌面/幻灯片）时才退回整屏截图。
/// </summary>
internal static class GlassTexture
{
    /// <summary>模糊结果按屏幕分辨率的这个比例生成，够糊又省内存。</summary>
    private const int DownScale = 2;

    public static BitmapSource? Build(int screenWidth, int screenHeight, double blurRadius)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return null;
        }

        ImageSource? source = LoadWallpaper() ?? NativeMethods.CaptureScreen();
        if (source is null)
        {
            return null;
        }

        double width = Math.Max(32, screenWidth / (double)DownScale);
        double height = Math.Max(32, screenHeight / (double)DownScale);

        var visual = new DrawingVisual();
        if (blurRadius >= 1)
        {
            // 底图按 1/DownScale 分辨率生成，所以模糊半径也要按比例缩
            visual.Effect = new BlurEffect
            {
                Radius = blurRadius / DownScale,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance,
            };
        }
using (DrawingContext dc = visual.RenderOpen())
        {
            // 按 Windows 的"填充(Fill)"规则：等比放大到盖满屏幕，居中裁切
            double scale = Math.Max(width / source.Width, height / source.Height);
            double w = source.Width * scale;
            double h = source.Height * scale;
            dc.DrawImage(source, new Rect((width - w) / 2, (height - h) / 2, w, h));
        }

        var target = new RenderTargetBitmap(
            (int)Math.Round(width), (int)Math.Round(height), 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static ImageSource? LoadWallpaper()
    {
        string? path = NativeMethods.GetWallpaperPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
