using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ATW11;

/// <summary>
/// 应用入口 / 控制器：持有设置、覆盖层、控制中心、托盘图标。
/// 关掉覆盖层（Esc）就等于退出整个程序；关掉控制中心只是隐藏。
/// 同一时间只允许一个实例：再次启动会唤醒已有实例的控制中心。
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\ATW11.SingleInstance";
    private const string ShowCenterEventName = @"Local\ATW11.ShowCenter";

    private AppSettings _settings = new();
    private MainWindow? _overlay;
    private SettingsWindow? _center;
    private TrayIcon? _tray;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showCenterEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── 单实例：已经有了就把控制中心叫出来，然后自己退出 ──
        _singleInstance = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                EventWaitHandle.OpenExisting(ShowCenterEventName).Set();
            }
            catch
            {
                // 打不开就算了（对方可能正在退出）
            }

            Shutdown();
            return;
        }

        _showCenterEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowCenterEventName);

        var listener = new Thread(() =>
        {
            while (_showCenterEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(new Action(ShowCenter));
            }
        })
        {
            IsBackground = true,
            Name = "ATW11.ShowCenterListener",
        };
        listener.Start();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 多个窗口 + 托盘：必须显式控制退出
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        bool firstRun = !System.IO.File.Exists(AppSettings.FilePath);
        _settings = AppSettings.Load();

        _overlay = new MainWindow(_settings);
        _overlay.StatusChanged += text => _center?.UpdateStatus(text);
        _overlay.Closed += (_, _) => ExitApp();
        _overlay.Show();

        _center = new SettingsWindow(_settings, OnSettingsChanged, ExitApp);

        _tray = new TrayIcon(ShowCenter, TogglePause, ExitApp);
        _tray.SetPaused(_settings.Paused);

        if (firstRun)
        {
            _tray.ShowBalloon("ATW11 已启动", "在托盘图标上点右键：打开控制中心 / 暂停装饰 / 退出。");
        }

        // 带 --center 启动时直接把控制中心亮出来（也方便做快捷方式）
        if (e.Args.Any(a => string.Equals(a, "--center", StringComparison.OrdinalIgnoreCase)))
        {
            ShowCenter();
        }    }

    /// <summary>托盘 / 双击图标：打开控制中心。</summary>
    private void ShowCenter()
    {
        if (_center is null)
        {
            return;
        }

        _center.LoadFromSettings();
        _center.UpdateStatus(_overlay?.Status ?? string.Empty);
        _center.Show();
        _center.Activate();
        _center.Focus();
    }

    /// <summary>托盘菜单：暂停 / 恢复装饰。</summary>
    private void TogglePause()
    {
        _settings.Paused = !_settings.Paused;
        _settings.Save();

        _tray?.SetPaused(_settings.Paused);
        _overlay?.ApplySettings();
        _center?.LoadFromSettings();
    }

    /// <summary>控制中心里改了任何设置。</summary>
    private void OnSettingsChanged()
    {
        _settings.Save();

        _tray?.SetPaused(_settings.Paused);
        _overlay?.ApplySettings();
    }

    private void ExitApp()
    {
        if (_tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }

        if (_center is not null)
        {
            SettingsWindow center = _center;
            _center = null;
            center.Close();
        }

        if (_overlay is not null)
        {
            MainWindow overlay = _overlay;
            _overlay = null;
            overlay.Close();
        }

        _showCenterEvent?.Dispose();
        _showCenterEvent = null;

        try
        {
            _singleInstance?.ReleaseMutex();
        }
        catch
        {
            // 忽略
        }

        _singleInstance?.Dispose();
        _singleInstance = null;

        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        MessageBox.Show(
            "ATW11 遇到一个未处理的错误：\n\n" + e.Exception.Message +
            "\n\n程序会继续运行；如果界面错乱，请从托盘退出再启动。",
            "ATW11 · Aero 玻璃外壳",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
