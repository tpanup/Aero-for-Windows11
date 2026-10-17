using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace ATW11;

/// <summary>
/// 全部可调参数。存在 %APPDATA%\ATW11\settings.json，重启后自动恢复。
/// </summary>
public sealed class AppSettings
{
    /// <summary>玻璃整体透明度：1.0 = 完全不透，0.5 = 很透（会隐约看到原生标签栏，这是透明玻璃的代价）。</summary>
    public double GlassOpacity { get; set; } = 0.96;

    /// <summary>壁纸高斯模糊半径（0~80）。改这个会重新生成玻璃底图。</summary>
    public double BlurRadius { get; set; } = 30;

    /// <summary>配色预设下标（见 AppColors.Presets）。</summary>
    public int ColorPreset { get; set; }

    /// <summary>是否给所有资源管理器窗口都套壳（关掉则只给当前前台窗口套）。</summary>
    public bool DecorateAllWindows { get; set; } = true;

    /// <summary>右下角提示条。</summary>
    public bool ShowHint { get; set; } = true;

    /// <summary>暂停：暂时不画任何玻璃壳。</summary>
    public bool Paused { get; set; }

    // ── 持久化 ──

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string Directory
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATW11");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch
        {
            // 配置坏了就用默认值，绝不影响主流程
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Normalize();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // 忽略：存不下也不影响使用
        }
    }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            GlassOpacity = GlassOpacity,
            BlurRadius = BlurRadius,
            ColorPreset = ColorPreset,
            DecorateAllWindows = DecorateAllWindows,
            ShowHint = ShowHint,
            Paused = Paused,
        };
    }

    private void Normalize()
    {
        GlassOpacity = Math.Clamp(GlassOpacity, 0.45, 1.0);
        BlurRadius = Math.Clamp(BlurRadius, 0, 80);
        ColorPreset = Math.Clamp(ColorPreset, 0, AppColors.Presets.Length - 1);
    }
}

/// <summary>Win7 Aero 的几套配色（标题栏色 / 边框玻璃色）。</summary>
public static class AppColors
{
    public static readonly (string Name, Color Bar, Color Glass)[] Presets =
    {
        ("天蓝", Color.FromRgb(0x92, 0xB0, 0xD2), Color.FromRgb(0xA1, 0xBE, 0xDE)),
        ("青", Color.FromRgb(0x7E, 0xB2, 0xBA), Color.FromRgb(0x8F, 0xC2, 0xC9)),
        ("绿", Color.FromRgb(0x84, 0xAF, 0x8A), Color.FromRgb(0x96, 0xC0, 0x9C)),
        ("紫", Color.FromRgb(0x9B, 0x92, 0xC2), Color.FromRgb(0xAC, 0xA3, 0xD0)),
        ("银灰", Color.FromRgb(0xA6, 0xB0, 0xBE), Color.FromRgb(0xB6, 0xC0, 0xCE)),
        ("琥珀", Color.FromRgb(0xC2, 0xA8, 0x82), Color.FromRgb(0xD0, 0xB8, 0x96)),
    };

    public static (Color Bar, Color Glass) Get(int index)
    {
        index = Math.Clamp(index, 0, Presets.Length - 1);
        return (Presets[index].Bar, Presets[index].Glass);
    }

    public static Color Lighten(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));
    }

    public static Color Darken(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(color.R * (1 - amount)),
            (byte)(color.G * (1 - amount)),
            (byte)(color.B * (1 - amount)));
    }

    public static Color WithAlpha(Color color, double alpha)
    {
        return Color.FromArgb((byte)(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);
    }
}
