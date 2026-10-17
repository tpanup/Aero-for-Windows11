using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ATW11;

/// <summary>
/// 全屏透明覆盖层：给资源管理器窗口"描"一圈 Win7 Aero 玻璃外壳。
///
/// 几条铁律：
///   · 覆盖层自己几乎全透明，只有玻璃壳和提示条是画出来的 → 鼠标其余位置全部穿透；
///   · 玻璃壳的顶栏是我们自己的"标题栏"：按住它拖窗口（把消息转给真窗口的原生拖拽）；
///   · 三个按钮的点击判定走 Win32 消息，用光标的真实物理位置（跨 DPI 时消息坐标会被改写）。
/// </summary>
public partial class MainWindow : Window
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_CAPTURECHANGED = 0x0215;

    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;

    private readonly AppSettings _settings;
    private readonly Dictionary<IntPtr, AeroFrame> _frames = new();
    private readonly Dictionary<IntPtr, ImageSource?> _icons = new();
    private readonly Dictionary<IntPtr, bool> _occluded = new();

    // 每个壳"被谁压着"的矩形列表（物理像素，按 Z 序）—— 用来裁剪玻璃，防穿模
    private readonly Dictionary<IntPtr, List<Rect>> _coverRects = new();

    private static readonly List<Rect> EmptyCover = new();

    /// <summary>可点击区域（DIP）：三个按钮 + 右下角提示条。</summary>
    private readonly List<(Rect Rect, HitAction Action, IntPtr Target)> _hits = new();

    private readonly DispatcherTimer _timer = new();

    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _activeTarget = IntPtr.Zero;
    private HwndSource? _source;

    private BitmapSource? _texture;
    private double _textureScale = 0.5;
    private double _textureBlur = -1;
    private double _dpiScale = 1.0;

    private bool _hotkeyRegistered = true;
    private bool _hintInteractive;
    private int _tick;
    private int _lastFrameCount = -1;
    private int _occlusionTick = -100;
    private string _lastStatus = string.Empty;

    private bool _dragging;
    private IntPtr _dragTarget = IntPtr.Zero;
    private Point _dragStartCursor;
    private Rect _dragStartRect;

    private IntPtr _lastBarTarget = IntPtr.Zero;
    private long _lastBarTick;
    private Point _lastBarPoint;

    private enum HitAction
    {
        None,
        Minimize,
        Maximize,
        Close,
        ExitApp,
    }

    /// <summary>当前状态文字（控制中心刚打开时用来补显示）。</summary>
    public string Status => _lastStatus;

    /// <summary>状态文字变化时通知控制中心。</summary>
    public event Action<string>? StatusChanged;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
    }

    // ════════════════════════ 窗口初始化 ════════════════════════

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        _dpiScale = NativeMethods.GetDpiScale(_hwnd);

        Left = 0;
        Top = 0;
        Width = NativeMethods.ScreenWidth / _dpiScale;
        Height = NativeMethods.ScreenHeight / _dpiScale;

        NativeMethods.SetupOverlayWindow(_hwnd, _dpiScale);

        // 全局 Esc：覆盖层不抢焦点，键盘到不了我们，只能靠热键收 WM_HOTKEY
        NativeMethods.RegisterEscapeHotkey(_hwnd);
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        RebuildTexture();

        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += OnTick;
        _timer.Start();

        SyncFrames();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _tick++;

        if (_tick == 1 || _tick % 30 == 0)
        {
            SyncFrames();
        }

        UpdateFrames();
    }

    // ════════════════════════ 设置 ════════════════════════

    /// <summary>设置变更后由控制中心/托盘调用：立刻重新贴图并刷新外壳。</summary>
    public void ApplySettings()
    {
        if (Math.Abs(_settings.BlurRadius - _textureBlur) > 0.5)
        {
            RebuildTexture();
        }

        foreach (AeroFrame frame in _frames.Values)
        {
            frame.ApplyAppearance(_settings);
        }

        if (!_settings.ShowHint)
        {
            _hintInteractive = false;
            HintChip.BeginAnimation(OpacityProperty, null);
            HintChip.Opacity = 0;
        }

        UpdateFrames();
    }

    private void RebuildTexture()
    {
        _texture = GlassTexture.Build(NativeMethods.ScreenWidth, NativeMethods.ScreenHeight, _settings.BlurRadius);
        _textureBlur = _settings.BlurRadius;

        if (_texture is not null && NativeMethods.ScreenWidth > 0)
        {
            _textureScale = _texture.PixelWidth / (double)NativeMethods.ScreenWidth;
        }
    }

    // ════════════════════════ 窗口列表同步 ════════════════════════

    private void SyncFrames()
    {
        List<IntPtr> targets = NativeMethods.FindExplorerWindows();

        foreach (IntPtr hwnd in _frames.Keys.ToList())
        {
            if (!targets.Contains(hwnd) || !NativeMethods.IsWindowAlive(hwnd))
            {
                RemoveFrame(hwnd);
            }
        }

        foreach (IntPtr hwnd in targets)
        {
            if (!_frames.ContainsKey(hwnd))
            {
                AddFrame(hwnd);
            }
        }

        if (_frames.Count != _lastFrameCount)
        {
            _lastFrameCount = _frames.Count;
            UpdateHint();
        }
    }

    private void AddFrame(IntPtr hwnd)
    {
        var frame = new AeroFrame(hwnd, _settings) { Visibility = Visibility.Collapsed };

        _frames[hwnd] = frame;
        _icons[hwnd] = NativeMethods.GetWindowIcon(hwnd);
        FrameLayer.Children.Add(frame);
    }

    private void RemoveFrame(IntPtr hwnd)
    {
        if (!_frames.TryGetValue(hwnd, out AeroFrame? frame))
        {
            return;
        }

        FrameLayer.Children.Remove(frame);
        _frames.Remove(hwnd);
        _icons.Remove(hwnd);
        _occluded.Remove(hwnd);

        if (_activeTarget == hwnd)
        {
            _activeTarget = IntPtr.Zero;
        }
    }

    // ════════════════════════ 每帧更新外壳 ════════════════════════

    private void UpdateFrames()
    {
        _hits.Clear();

        if (_settings.Paused)
        {
            foreach (AeroFrame f in _frames.Values)
            {
                f.Visibility = Visibility.Collapsed;
            }

            PublishStatus(0);
            return;
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (_frames.ContainsKey(foreground))
        {
            _activeTarget = foreground;
        }

        // 遮挡判断有点贵，250ms 一次就够
        if (_tick - _occlusionTick >= 15)
        {
            _occlusionTick = _tick;
            RefreshOcclusion();
        }

        int shown = 0;

        foreach (KeyValuePair<IntPtr, AeroFrame> pair in _frames)
        {
            AeroFrame frame = pair.Value;
            WindowMetrics metrics = default;

            bool show = !NativeMethods.IsMinimized(pair.Key)
                        && NativeMethods.TryGetMetrics(pair.Key, out metrics)
                        && IsOnScreen(metrics);

            if (show && !_settings.DecorateAllWindows)
            {
                // 只装饰前台窗口
                show = pair.Key == _activeTarget;
            }

            if (show && _settings.DecorateAllWindows)
            {
                show = !_occluded.TryGetValue(pair.Key, out bool occluded) || !occluded;
            }

            if (!show)
            {
                if (frame.Visibility != Visibility.Collapsed)
                {
                    frame.Visibility = Visibility.Collapsed;
                }

                continue;
            }

            if (frame.Visibility != Visibility.Visible)
            {
                frame.Visibility = Visibility.Visible;
            }

            if (_tick % 6 == 0 || frame.Width <= 0)
            {
                frame.SetTitle(NativeMethods.GetTitle(pair.Key));
                frame.SetIcon(_icons.TryGetValue(pair.Key, out ImageSource? icon) ? icon : null);
            }

            frame.Apply(metrics, _dpiScale, _texture, _textureScale);

            // 重叠的窗口互相遮挡：把压在它上面的窗口区域从玻璃里裁掉
            frame.SetCoverRects(
                _coverRects.TryGetValue(pair.Key, out List<Rect>? cover) ? cover : EmptyCover,
                _dpiScale);

            shown++;

            AddHits(frame, pair.Key);
        }

        if (_settings.ShowHint && _hintInteractive && HintChip.Opacity > 0.05 && HintChip.ActualWidth > 0)
        {
            Point origin = HintChip.TransformToAncestor(this).Transform(new Point(0, 0));
            _hits.Add((new Rect(origin, new Size(HintChip.ActualWidth, HintChip.ActualHeight)),
                HitAction.ExitApp, IntPtr.Zero));
        }

        PublishStatus(shown);
    }

    private bool IsOnScreen(in WindowMetrics metrics)
    {
        return metrics.Frame.Right > 0 && metrics.Frame.Left < NativeMethods.ScreenWidth
            && metrics.Frame.Bottom > 0 && metrics.Frame.Top < NativeMethods.ScreenHeight;
    }

    private void AddHits(AeroFrame frame, IntPtr target)
    {
        (Rect min, Rect max, Rect close) = frame.GetButtonRects();

        AddHit(min, HitAction.Minimize, target);
        AddHit(max, HitAction.Maximize, target);
        AddHit(close, HitAction.Close, target);
    }

    private void AddHit(Rect rect, HitAction action, IntPtr target)
    {
        // 被别的窗口压住的按钮不响应，否则会点到"看不见的按钮"
        if (rect.IsEmpty || IsCoveredByOtherWindow(target, rect))
        {
            return;
        }

        _hits.Add((rect, action, target));
    }

    /// <summary>
    /// 刷新遮挡信息：
    ///   · 每个壳记录"压在它上面的窗口矩形"，用来裁剪玻璃（重叠时互相遮挡，不穿模）；
    ///   · 顶栏中心点被完全盖住的，干脆不画（省资源，也避免浮在别人窗口上）。
    /// </summary>
    private void RefreshOcclusion()
    {
        List<IntPtr> zOrder = NativeMethods.TopLevelWindows();
        var occluders = new List<Rect>();

        // 每轮都重建，保证 AeroFrame 能感知"覆盖集合变了"
        _coverRects.Clear();

        foreach (IntPtr h in zOrder)
        {
            if (h == _hwnd || NativeMethods.IsOwnProcessWindow(h))
            {
                continue;
            }

            if (!NativeMethods.IsVisible(h) || NativeMethods.IsCloakedWindow(h))
            {
                continue;
            }

            // 用"看得见的边框"而不是窗口矩形：后者含 8px 不可见缩放边，会把玻璃裁过头
            if (!NativeMethods.TryGetVisibleBounds(h, out Rect bounds))
            {
                continue;
            }

            if (_frames.ContainsKey(h))
            {
                // 此刻 occluders 里正好是"排在它上面的所有可见窗口"
                _coverRects[h] = new List<Rect>(occluders);
                _occluded[h] = IsPointCovered(occluders, BarProbePoint(h));
            }

            occluders.Add(bounds);
        }

        foreach (IntPtr h in _frames.Keys)
        {
            if (!_occluded.ContainsKey(h))
            {
                _occluded[h] = false;
            }

            if (!_coverRects.ContainsKey(h))
            {
                _coverRects[h] = new List<Rect>();
            }
        }
    }

    /// <summary>某个按钮是不是被别的窗口盖住了（盖住就别响应点击）。</summary>
    private bool IsCoveredByOtherWindow(IntPtr target, Rect dipRect)
    {
        if (!_coverRects.TryGetValue(target, out List<Rect>? cover) || cover.Count == 0)
        {
            return false;
        }

        var center = new Point(
            (dipRect.X + dipRect.Width / 2) * _dpiScale,
            (dipRect.Y + dipRect.Height / 2) * _dpiScale);

        for (int i = 0; i < cover.Count; i++)
        {
            if (cover[i].Contains(center))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointCovered(List<Rect> rects, Point p)
    {
        for (int i = 0; i < rects.Count; i++)
        {
            if (rects[i].Contains(p))
            {
                return true;
            }
        }

        return false;
    }

    private Point BarProbePoint(IntPtr hwnd)
    {
        if (!NativeMethods.TryGetMetrics(hwnd, out WindowMetrics m))
        {
            return new Point(-1000, -1000);
        }

        double rimDip = m.IsMaximized ? 0 : AeroFrame.RimWidth;
        return new Point(
            m.Frame.X + m.Frame.Width / 2.0,
            m.Frame.Y + (rimDip + AeroFrame.BarHeight / 2.0) * _dpiScale);
    }

    private void PublishStatus(int shown)
    {
        string text = _settings.Paused
            ? "已暂停（托盘 → 恢复装饰）"
            : shown == 0
                ? "已装饰 0 个窗口（打开文件夹窗口，或把它切到最前面）"
                : $"已装饰 {shown} 个窗口";

        if (text != _lastStatus)
        {
            _lastStatus = text;
            StatusChanged?.Invoke(text);
        }
    }

    // ════════════════════════ 命中 / 点击 / 拖拽 ════════════════════════

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
            {
                long packed = lParam.ToInt64();
                int screenX = (short)(packed & 0xFFFF);
                int screenY = (short)((packed >> 16) & 0xFFFF);

                Point dip = PointFromScreen(new Point(screenX, screenY));

                // 按钮 + 顶栏都算"我们吃鼠标"：顶栏要用来拖窗口
                bool over = FindHit(dip).Action != HitAction.None || IsOverBar(dip);

                handled = true;
                return new IntPtr(over ? HTCLIENT : HTTRANSPARENT);
            }

            case WM_LBUTTONDOWN:
            {
                if (!NativeMethods.TryGetCursorPosition(out Point cursor))
                {
                    break;
                }

                var dip = new Point(cursor.X / _dpiScale, cursor.Y / _dpiScale);

                // 按钮上的按下要等 UP 再执行
                if (FindHit(dip).Action != HitAction.None)
                {
                    break;
                }

                if (!TryFindBar(dip, out IntPtr target))
                {
                    break;
                }

                handled = true;

                // 顶栏双击 = 最大化 / 还原
                long now = Environment.TickCount64;
                bool isDouble = target == _lastBarTarget
                                && now - _lastBarTick < 420
                                && Math.Abs(dip.X - _lastBarPoint.X) < 8
                                && Math.Abs(dip.Y - _lastBarPoint.Y) < 8;

                _lastBarTarget = target;
                _lastBarTick = isDouble ? 0 : now;
                _lastBarPoint = dip;

                if (isDouble)
                {
                    EndDrag();
                    NativeMethods.ToggleMaximize(target);
                }
                else
                {
                    BeginDrag(target, cursor);
                }

                break;
            }

            case WM_LBUTTONUP:
            {
                bool wasDragging = _dragging;
                EndDrag();

                if (!NativeMethods.TryGetCursorPosition(out Point cursor))
                {
                    break;
                }

                var dip = new Point(cursor.X / _dpiScale, cursor.Y / _dpiScale);
                (HitAction action, IntPtr target) = FindHit(dip);

                if (action != HitAction.None)
                {
                    handled = true;

                    // 刚拖完的那一下不要顺手触发按钮
                    if (!wasDragging)
                    {
                        ExecuteHit(action, target);
                    }
                }

                break;
            }

            case WM_MOUSEMOVE:
                UpdateDrag();
                break;

            case WM_CAPTURECHANGED:
                EndDrag();
                break;

            case WM_HOTKEY:
                if (wParam.ToInt32() == NativeMethods.HOTKEY_ID_ESC)
                {
                    handled = true;
                    Close();
                }

                break;
        }

        return IntPtr.Zero;
    }

    // ── 顶栏拖拽（自己做，比转交原生更可控；Aero Snap 不参与，但绝对跟手） ──

    private void BeginDrag(IntPtr target, Point cursor)
    {
        if (!NativeMethods.IsWindowAlive(target) || !NativeMethods.TryGetWindowBounds(target, out Rect bounds))
        {
            return;
        }

        _dragging = true;
        _dragTarget = target;
        _dragStartCursor = cursor;
        _dragStartRect = bounds;

        // 抓住鼠标，这样指针移出窗口也能继续收到 WM_MOUSEMOVE
        NativeMethods.CaptureMouse(_hwnd);
    }

    private void UpdateDrag()
    {
        if (!_dragging)
        {
            return;
        }

        if (!NativeMethods.IsWindowAlive(_dragTarget)
            || !NativeMethods.TryGetCursorPosition(out Point cursor))
        {
            EndDrag();
            return;
        }

        double dx = cursor.X - _dragStartCursor.X;
        double dy = cursor.Y - _dragStartCursor.Y;

        NativeMethods.MoveWindowTo(_dragTarget,
            (int)Math.Round(_dragStartRect.X + dx),
            (int)Math.Round(_dragStartRect.Y + dy));
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _dragTarget = IntPtr.Zero;
        NativeMethods.ReleaseMouseCapture();
    }
    private (HitAction Action, IntPtr Target) FindHit(Point dip)
    {
        for (int i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Rect.Contains(dip))
            {
                return (_hits[i].Action, _hits[i].Target);
            }
        }

        return (HitAction.None, IntPtr.Zero);
    }

    private bool IsOverBar(Point dip)
    {
        foreach (AeroFrame frame in _frames.Values)
        {
            if (frame.Visibility == Visibility.Visible && frame.GetBarRect().Contains(dip))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindBar(Point dip, out IntPtr target)
    {
        foreach (KeyValuePair<IntPtr, AeroFrame> pair in _frames)
        {
            if (pair.Value.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (pair.Value.GetBarRect().Contains(dip))
            {
                target = pair.Key;
                return true;
            }
        }

        target = IntPtr.Zero;
        return false;
    }

    private void ExecuteHit(HitAction action, IntPtr target)
    {
        switch (action)
        {
            case HitAction.Minimize:
                NativeMethods.MinimizeWindow(target);
                break;

            case HitAction.Maximize:
                NativeMethods.ToggleMaximize(target);
                break;

            case HitAction.Close:
                NativeMethods.CloseWindow(target);
                break;

            case HitAction.ExitApp:
                Close();
                break;
        }
    }

    // ════════════════════════ 提示条 ════════════════════════

    private void UpdateHint()
    {
        if (!_settings.ShowHint)
        {
            HintChip.Opacity = 0;
            _hintInteractive = false;
            return;
        }

        string text = _frames.Count > 0
            ? "ATW11 · 按住蓝色顶栏可拖动窗口　·　Esc 退出（也可点这里）"
            : "ATW11 · 打开任意文件夹窗口即会自动套壳　·　Esc 退出（也可点这里）";

        if (HintText.Text == text && HintChip.Opacity > 0.2)
        {
            return;
        }

        HintText.Text = text;
        HintChip.Opacity = 0.95;
        _hintInteractive = true;
        HintChip.IsHitTestVisible = true;

        HintChip.BeginAnimation(OpacityProperty, null);
        HintChip.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0.95,
            To = 0.42,
            BeginTime = TimeSpan.FromSeconds(8),
            Duration = TimeSpan.FromMilliseconds(900),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        });
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    // ════════════════════════ 收尾 ════════════════════════

    private void Window_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTick;

        if (_hotkeyRegistered && _hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterEscapeHotkey(_hwnd);
            _hotkeyRegistered = false;
        }

        _source?.RemoveHook(WndProc);
        _source = null;

        foreach (AeroFrame frame in _frames.Values)
        {
            FrameLayer.Children.Remove(frame);
        }

        _frames.Clear();
        _icons.Clear();
        _occluded.Clear();
        _hits.Clear();

        _texture = null;
        _activeTarget = IntPtr.Zero;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
