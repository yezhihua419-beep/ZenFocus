using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace ChanJing_App;

/// <summary>
/// 摩擦式拦截全屏遮罩：打开分心应用时弹出，5秒冷静期后才能选择继续专注或分心。
/// one sec 科学背书：6周减少57%的分心App打开次数。
/// </summary>
public sealed partial class FrictionOverlay : Window
{
    private readonly DispatcherTimer _timer;
    private int _remaining = 5;
    private readonly string _processName;
    private readonly string _category;

    public event EventHandler? ContinueFocus;
    public event EventHandler? GiveIn;

    public FrictionOverlay(string processName, string category)
    {
        InitializeComponent();
        _processName = processName;
        _category = category;

        // 全屏置顶
        Activate();
        try
        {
            var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (presenter is not null)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }
            AppWindow.Resize(new SizeInt32(
                (int)Microsoft.UI.Xaml.Application.Current.Resources["ScreenWidth"] as int? ?? 1920,
                (int)Microsoft.UI.Xaml.Application.Current.Resources["ScreenHeight"] as int? ?? 1080));
            AppWindow.Move(new PointInt32(0, 0));
        }
        catch { }

        // 5秒倒计时
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            if (_remaining <= 0)
            {
                _timer.Stop();
                CountdownText.Text = "";
                HintText.Text = $"「{_processName}」属于{_category}，真的要分心吗？";
                ButtonPanel.Visibility = Visibility.Visible;
            }
            else
            {
                CountdownText.Text = _remaining.ToString();
            }
        };
        _timer.Start();

        App.LogAction("摩擦拦截", $"{processName}({category}) 5秒冷静期开始");
    }

    private void ContinueFocus_Click(object sender, RoutedEventArgs e)
    {
        App.LogAction("摩擦拦截", $"{_processName} 用户选择继续专注");
        ContinueFocus?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void GiveIn_Click(object sender, RoutedEventArgs e)
    {
        App.LogAction("摩擦拦截", $"{_processName} 用户选择分心，临时放行5分钟");
        GiveIn?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
