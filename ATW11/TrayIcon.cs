using System.Drawing;
using System.Windows.Forms;

namespace ATW11;

/// <summary>
/// 右下角托盘图标 + 右键菜单。用 WinForms 的 NotifyIcon（WPF 没有自带托盘），
/// 它和 WPF 同线程工作，消息由 WPF 的 Dispatcher 泵驱动，不需要额外的消息循环。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseItem;

    public TrayIcon(Action openCenter, Action togglePause, Action exit)
    {
        Icon icon;
        try
        {
            icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application;
        }
        catch
        {
            icon = SystemIcons.Application;
        }

        _pauseItem = new ToolStripMenuItem("暂停装饰");
        _pauseItem.Click += (_, _) => togglePause();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("打开控制中心", null, (_, _) => openCenter()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出 ATW11", null, (_, _) => exit()));

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "ATW11 · Win7 Aero 玻璃外壳",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => openCenter();
    }

    public void SetPaused(bool paused)
    {
        _pauseItem.Text = paused ? "恢复装饰" : "暂停装饰";
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.ShowBalloonTip(2500);
        }
        catch
        {
            // 忽略
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
