using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ATW11;

/// <summary>
/// 一个原生资源管理器窗口的"Aero 玻璃外壳"。
///
/// 版式（单位 DIP，容器坐标）：
///   ┌──────────────────────────────┐  ← 外沿圆角 10
///   │  ┌────────────────────────┐  │  ← 顶部 8px 玻璃包边
///   │  │  图标  标题    ─ □ ✕   │  │  ← 标题栏：压在原生标签栏上面，把它盖掉
///   │  ├────────────────────────┤  │
///   │  │                        │  │  ← 中间是"洞"，完全透明，露出下面活的窗口内容
///   │  │      真实窗口内容       │  │
///   │  └────────────────────────┘  │
///   └──────────────────────────────┘  ← 左右/下各 8px 玻璃包边
///
/// 洞的形状带圆角，正好吻合 Win11 窗口自身的圆角，所以不会露出桌面的一丝缝。
/// </summary>
public partial class AeroFrame : UserControl
{
    /// <summary>玻璃包边厚度（DIP）。最大化时为 0。</summary>
    public const double RimWidth = 8;

    /// <summary>盖住原生标题栏的高度（DIP）。Win11 的资源管理器标签栏约 35 DIP，取 38 兜住。</summary>
    public const double BarHeight = 38;

    private const double OuterRadius = 10;
    private const double InnerRadius = 8;

    private readonly ImageBrush _textureBrush = new ImageBrush
    {
        Stretch = Stretch.Fill,
        TileMode = TileMode.None,
        AlignmentX = AlignmentX.Left,
        AlignmentY = AlignmentY.Top,
        ViewboxUnits = BrushMappingMode.Absolute,
        ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
    };

    // 玻璃染色由代码生成，才能跟着"配色 / 透明度"实时变
    private readonly LinearGradientBrush _glassTint = new() { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
    private readonly LinearGradientBrush _barTint = new() { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };

    private WindowMetrics _metrics;
    private bool _hasShape;
    private double _appliedScale = -1;
    private bool _maxGlyphIsRestore;
    private double _rim = RimWidth;
    private double _bar = BarHeight;

    // 遮挡裁剪缓存：只有"覆盖矩形集合换了一份"或者本壳动了，才重建 Clip
    private IReadOnlyList<Rect>? _lastCoverSource;
    private double _lastClipLeft = double.NaN;
    private double _lastClipTop = double.NaN;

    public IntPtr Target { get; }

    public AeroFrame(IntPtr target, AppSettings settings)
    {
        Target = target;
        InitializeComponent();

        TexturePath.Fill = _textureBrush;
        TintPath.Fill = _glassTint;
        BarTintPath.Fill = _barTint;

        ApplyAppearance(settings);
        UpdateMaxGlyph(false);
    }

    /// <summary>配色、透明度变化时调用（实时生效）。</summary>
    public void ApplyAppearance(AppSettings settings)
    {
        (Color bar, Color glass) = AppColors.Get(settings.ColorPreset);

        SetStops(_glassTint,
            (0.00, AppColors.WithAlpha(AppColors.Lighten(glass, 0.45), 0.90)),
            (0.07, AppColors.WithAlpha(AppColors.Lighten(glass, 0.18), 0.86)),
            (0.40, AppColors.WithAlpha(glass, 0.86)),
            (1.00, AppColors.WithAlpha(AppColors.Darken(glass, 0.12), 0.86)));

        SetStops(_barTint,
            (0.00, AppColors.WithAlpha(AppColors.Lighten(bar, 0.24), 0.95)),
            (0.35, AppColors.WithAlpha(bar, 0.95)),
            (1.00, AppColors.WithAlpha(AppColors.Darken(bar, 0.08), 0.95)));

        Opacity = Math.Clamp(settings.GlassOpacity, 0.3, 1.0);
    }

    /// <summary>
    /// 按"压在它上面的窗口"裁剪玻璃壳 —— 两个窗口重叠时不再穿模的关键：
    /// 后面那个窗口，凡是会被前面窗口盖住的部分，直接从裁剪区里减掉。
    /// </summary>
    public void SetCoverRects(IReadOnlyList<Rect> physicalRects, double dpiScale)
    {
        if (!_hasShape || dpiScale <= 0)
        {
            return;
        }

        double left = Canvas.GetLeft(this);
        double top = Canvas.GetTop(this);
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return;
        }

        // 覆盖集合是同一个对象、且本壳没动 → 不用重建
        if (ReferenceEquals(_lastCoverSource, physicalRects)
            && Math.Abs(left - _lastClipLeft) < 0.01
            && Math.Abs(top - _lastClipTop) < 0.01)
        {
            return;
        }

        _lastCoverSource = physicalRects;
        _lastClipLeft = left;
        _lastClipTop = top;

        if (physicalRects.Count == 0)
        {
            Clip = null;
            return;
        }

        var cover = new GeometryGroup { FillRule = FillRule.Nonzero };

        for (int i = 0; i < physicalRects.Count; i++)
        {
            Rect r = physicalRects[i];
            double w = r.Width / dpiScale;
            double h = r.Height / dpiScale;
            if (w <= 0.5 || h <= 0.5)
            {
                continue;
            }

            double x = r.X / dpiScale - left;
            double y = r.Y / dpiScale - top;
            cover.Children.Add(new RectangleGeometry(new Rect(x, y, w, h)));
        }

        if (cover.Children.Count == 0)
        {
            Clip = null;
            return;
        }

        // 自己范围（略微外扩避免边缘抗锯齿被切）减去覆盖矩形
        var own = new RectangleGeometry(new Rect(-2, -2, Width + 4, Height + 4));
        Clip = new CombinedGeometry(GeometryCombineMode.Exclude, own, cover);
}

    /// <summary>标题栏区域（覆盖层坐标 DIP），用来做"按住顶栏拖窗口"。</summary>
    public Rect GetBarRect()    {
        if (!_hasShape || Visibility != Visibility.Visible)
        {
            return Rect.Empty;
        }

        double left = Canvas.GetLeft(this);
        double top = Canvas.GetTop(this);
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return Rect.Empty;
        }

        return new Rect(left + _rim, top + _rim, Math.Max(1, Width - _rim * 2), Math.Max(1, _bar));
    }

    private static void SetStops(LinearGradientBrush brush, params (double Offset, Color Color)[] stops)
    {
        brush.GradientStops.Clear();
        foreach ((double offset, Color color) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }
    }

    /// <summary>
    /// 按窗口最新的几何信息更新外壳。位置/尺寸没变时只更新玻璃底图，不做几何重建。
    /// </summary>
    public void Apply(in WindowMetrics metrics, double dpiScale, BitmapSource? texture, double textureScale)
    {
        if (dpiScale <= 0)
        {
            dpiScale = 1;
        }

        if (textureScale <= 0)
        {
            textureScale = 1;
        }

        bool shapeChanged = !_hasShape
                            || Math.Abs(dpiScale - _appliedScale) > 1e-6
                            || !metrics.SameShape(_metrics);

        if (shapeChanged)
        {
            _metrics = metrics;
            _hasShape = true;
            _appliedScale = dpiScale;
            Rebuild(metrics, dpiScale);
        }

        if (texture is not null && !ReferenceEquals(_textureBrush.ImageSource, texture))
        {
            _textureBrush.ImageSource = texture;
        }

        // 模糊壁纸是"屏幕空间"的贴图：窗口移到哪儿，就从那张大图上取对应的一块
        Rect viewbox = new Rect(
            Canvas.GetLeft(this) * dpiScale * textureScale,
            Canvas.GetTop(this) * dpiScale * textureScale,
            Width * dpiScale * textureScale,
            Height * dpiScale * textureScale);

        if (_textureBrush.Viewbox != viewbox)
        {
            _textureBrush.Viewbox = viewbox;
        }
    }

    public void SetTitle(string title)
    {
        if (!string.Equals(TitleText.Text, title, StringComparison.Ordinal))
        {
            TitleText.Text = title;
            TitleText.ToolTip = title;
        }
    }

    public void SetIcon(ImageSource? icon)
    {
        if (icon is null || ReferenceEquals(AppIcon.Source, icon))
        {
            return;
        }

        AppIcon.Source = icon;
    }

    /// <summary>
    /// 三个按钮在"覆盖层坐标"里的矩形。点击判定走 Win32 消息（见 MainWindow.WndProc），
    /// 因为 WS_EX_NOACTIVATE 的窗口里 WPF 的点击路径不可靠；但悬停高光仍然由 WPF 的触发器负责。
    /// </summary>
    public (Rect Min, Rect Max, Rect Close) GetButtonRects()
    {
        return (GetElementRect(MinButton), GetElementRect(MaxButton), GetElementRect(CloseButton));
    }

    private Rect GetElementRect(FrameworkElement element)
    {
        if (Visibility != Visibility.Visible || element.ActualWidth <= 0)
        {
            return Rect.Empty;
        }

        Point origin;
        try
        {
            origin = element.TransformToAncestor(this).Transform(new Point(0, 0));
        }
        catch
        {
            return Rect.Empty;
        }

        double left = Canvas.GetLeft(this);
        double top = Canvas.GetTop(this);
        if (double.IsNaN(left))
        {
            left = 0;
        }

        if (double.IsNaN(top))
        {
            top = 0;
        }

        var rect = new Rect(origin.X + left, origin.Y + top, element.ActualWidth, element.ActualHeight);
        rect.Inflate(1.5, 1.5);
        return rect;
    }

    // ════════════════════════ 几何 ════════════════════════

    private void Rebuild(in WindowMetrics metrics, double dpiScale)
    {
        double windowWidth = Math.Max(1, metrics.Frame.Width) / dpiScale;
        double windowHeight = Math.Max(1, metrics.Frame.Height) / dpiScale;

        // 最大化时 Win11 窗口贴边，不能再往外扩，否则会画到屏幕外
        double rim = metrics.IsMaximized ? 0 : RimWidth;
        double bar = Math.Min(BarHeight, Math.Max(20, windowHeight - 12));

        _rim = rim;
        _bar = bar;

        double frameWidth = windowWidth + rim * 2;
        double frameHeight = windowHeight + rim * 2;

        Width = frameWidth;
        Height = frameHeight;
        Canvas.SetLeft(this, metrics.Frame.X / dpiScale - rim);
        Canvas.SetTop(this, metrics.Frame.Y / dpiScale - rim);

        double outerRadius = rim > 0 ? OuterRadius : 0;
        double innerRadius = rim > 0 ? InnerRadius : 0;

        Geometry outer = RoundedRect(0, 0, frameWidth, frameHeight,
            outerRadius, outerRadius, outerRadius, outerRadius);

        // 洞：左右各让 rim，顶部让 rim + bar；底部两个角跟随窗口圆角
        var hole = new Rect(rim, rim + bar, windowWidth, Math.Max(1, windowHeight - bar));
        Geometry holeGeometry = RoundedRect(hole.X, hole.Y, hole.Width, hole.Height,
            0, 0, innerRadius, innerRadius);

        var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outer, holeGeometry);
        try
        {
            ring.Freeze();
        }
        catch
        {
            // 冻结失败不影响显示
        }

        TexturePath.Data = ring;
        TintPath.Data = ring;

        // 标题栏加深层 & 顶部反光：都只铺在"标题栏"这块矩形里，
        // 这样渐变按标题栏高度计算，反光才是 Win7 那种薄薄一层，而不是把整条刷白。
        Geometry barGeometry = RoundedRect(0, 0, frameWidth, rim + bar,
            outerRadius, outerRadius, 0, 0);
        BarTintPath.Data = barGeometry;
        GlossPath.Data = barGeometry;

        OuterEdge.Data = outer;
        InnerEdge.Data = holeGeometry;

        // 高光内侧那道暗线：比洞再往里缩 1.2 DIP
        InnerShadow.Data = RoundedRect(hole.X + 1.2, hole.Y + 1.2,
            Math.Max(1, hole.Width - 2.4), Math.Max(1, hole.Height - 2.4),
            0, 0, Math.Max(0, innerRadius - 1.2), Math.Max(0, innerRadius - 1.2));

        Bar.Height = bar;
        Bar.Margin = new Thickness(rim + 9, rim, rim + 3, 0);

        UpdateMaxGlyph(metrics.IsMaximized);
    }

    private void UpdateMaxGlyph(bool maximized)
    {
        if (_maxGlyphIsRestore == maximized)
        {
            return;
        }

        _maxGlyphIsRestore = maximized;
        MaxGlyph.Data = Geometry.Parse(maximized ? "M0,3 H6 V9 H0 Z M3,0 H9 V6" : "M0,0 H9 V9 H0 Z");
    }

    /// <summary>四角可分别设半径的圆角矩形（WPF 自带的 RectangleGeometry 只能四角同半径）。</summary>
    private static Geometry RoundedRect(double x, double y, double w, double h,
        double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        if (w <= 0 || h <= 0)
        {
            return Geometry.Empty;
        }

        double limit = Math.Min(w, h) / 2;
        topLeft = Math.Min(topLeft, limit);
        topRight = Math.Min(topRight, limit);
        bottomRight = Math.Min(bottomRight, limit);
        bottomLeft = Math.Min(bottomLeft, limit);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x + topLeft, y), true, true);
            ctx.LineTo(new Point(x + w - topRight, y), true, false);

            if (topRight > 0)
            {
                ctx.ArcTo(new Point(x + w, y + topRight), new Size(topRight, topRight), 0,
                    false, SweepDirection.Clockwise, true, false);
            }

            ctx.LineTo(new Point(x + w, y + h - bottomRight), true, false);

            if (bottomRight > 0)
            {
                ctx.ArcTo(new Point(x + w - bottomRight, y + h), new Size(bottomRight, bottomRight), 0,
                    false, SweepDirection.Clockwise, true, false);
            }

            ctx.LineTo(new Point(x + bottomLeft, y + h), true, false);

            if (bottomLeft > 0)
            {
                ctx.ArcTo(new Point(x, y + h - bottomLeft), new Size(bottomLeft, bottomLeft), 0,
                    false, SweepDirection.Clockwise, true, false);
            }

            ctx.LineTo(new Point(x, y + topLeft), true, false);

            if (topLeft > 0)
            {
                ctx.ArcTo(new Point(x + topLeft, y), new Size(topLeft, topLeft), 0,
                    false, SweepDirection.Clockwise, true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }
}
