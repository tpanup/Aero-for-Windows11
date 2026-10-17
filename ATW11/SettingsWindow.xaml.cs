using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ATW11;

/// <summary>
/// 控制中心。所有改动直接写进 AppSettings 并立刻生效（覆盖层下一帧就刷新）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _changed;
    private readonly Action _exit;
    private readonly List<Ellipse> _swatches = new();

    // 必须在 InitializeComponent 之前就是 true：滑杆初值会被强制夹到 Minimum，
    // 那个 ValueChanged 会误把设置改成最小值。
    private bool _loading = true;

    public SettingsWindow(AppSettings settings, Action changed, Action exit)
    {
        _settings = settings;
        _changed = changed;
        _exit = exit;

        InitializeComponent();

        AppIconImage.Source = AppInfo.LoadIcon();
        FooterText.Text = $"{AppInfo.Version}　·　反馈：{AppInfo.Email}";

        BuildSwatches();
        LoadFromSettings();
    }

    /// <summary>把 AppSettings 的当前值刷到界面上（不触发回写）。</summary>
    public void LoadFromSettings()
    {
        _loading = true;

        OpacitySlider.Value = _settings.GlassOpacity;
        BlurSlider.Value = _settings.BlurRadius;
        AllWindowsCheck.IsChecked = _settings.DecorateAllWindows;
        HintCheck.IsChecked = _settings.ShowHint;
        PauseButton.Content = _settings.Paused ? "恢复装饰" : "暂停装饰";

        OpacityValue.Text = $"{_settings.GlassOpacity * 100:F0}%";
        BlurValue.Text = $"{_settings.BlurRadius:F0}";
        ColorValue.Text = AppColors.Presets[_settings.ColorPreset].Name;

        UpdateSwatchSelection();

        _loading = false;
    }

    public void UpdateStatus(string text)
    {
        if (!string.Equals(StatusText.Text, text, StringComparison.Ordinal))
        {
            StatusText.Text = text;
        }
    }

    private void BuildSwatches()
    {
        for (int i = 0; i < AppColors.Presets.Length; i++)
        {
            int index = i;
            (Color bar, Color glass) = AppColors.Get(i);

            var dot = new Ellipse
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 9, 0),
                Cursor = Cursors.Hand,
                Fill = new LinearGradientBrush(bar, glass, 90),
                StrokeThickness = 2,
                Stroke = Brushes.Transparent,
            };

            dot.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _settings.ColorPreset = index;
                ColorValue.Text = AppColors.Presets[index].Name;
                UpdateSwatchSelection();
                Apply();
            };

            _swatches.Add(dot);
            SwatchPanel.Children.Add(dot);
        }
    }

    private void UpdateSwatchSelection()
    {
        for (int i = 0; i < _swatches.Count; i++)
        {
            bool selected = i == _settings.ColorPreset;
            _swatches[i].Stroke = selected ? Brushes.White : Brushes.Transparent;
            _swatches[i].StrokeThickness = selected ? 2.5 : 2;
            _swatches[i].Opacity = selected ? 1.0 : 0.82;
        }
    }

    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        _changed();
    }

    // ── 事件 ──

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // 拖动过程中松开鼠标会抛，忽略
            }
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        _settings.GlassOpacity = Math.Round(e.NewValue, 2);
        OpacityValue.Text = $"{_settings.GlassOpacity * 100:F0}%";
        Apply();
    }

    private void BlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        _settings.BlurRadius = Math.Round(e.NewValue);
        BlurValue.Text = $"{_settings.BlurRadius:F0}";
        Apply();
    }

    private void AllWindowsCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.DecorateAllWindows = AllWindowsCheck.IsChecked == true;
        Apply();
    }

    private void HintCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowHint = HintCheck.IsChecked == true;
        Apply();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _settings.Paused = !_settings.Paused;
        PauseButton.Content = _settings.Paused ? "恢复装饰" : "暂停装饰";
        Apply();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        _settings.GlassOpacity = defaults.GlassOpacity;
        _settings.BlurRadius = defaults.BlurRadius;
        _settings.ColorPreset = defaults.ColorPreset;
        _settings.DecorateAllWindows = defaults.DecorateAllWindows;
        _settings.ShowHint = defaults.ShowHint;

        LoadFromSettings();
        Apply();
    }

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Hide();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _exit();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }
}
