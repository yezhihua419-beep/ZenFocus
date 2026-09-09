using System.Security;
using ChanJing.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChanJing_App;

/// <summary>
/// 屏蔽管理页：分类勾选（免费版限 3 目标）、自定义域名、每日限额、临时放行、
/// 应用/撤销/清理残留。hosts 写入权限不足时给出友好引导。
/// </summary>
public sealed partial class ShieldPage : Page
{
    private readonly BlocklistService _blocklist = AppServices.Blocklist;
    private readonly DailyLimitService _limits = AppServices.DailyLimits;

    public ShieldPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CategoryPanel.Children.Clear();
            var enabled = _blocklist.GetEnabledCategories();
            var activated = _blocklist.IsActivated();

            foreach (var category in BlocklistService.DefaultCategories.Keys)
            {
                var checkBox = new CheckBox
                {
                    Content = category,
                    IsChecked = enabled.Contains(category),
                    Tag = category,
                    FontSize = 14
                };
                checkBox.Checked += OnCategoryChecked;
                checkBox.Unchecked += OnCategoryChanged;
                CategoryPanel.Children.Add(checkBox);
            }

            LimitHint.Visibility = activated ? Visibility.Collapsed : Visibility.Visible;
            RefreshCustomDomains();
            RefreshLimits();
            RefreshAllowStatus();
            RefreshStatus();
            App.LogAction("进入屏蔽页", $"激活={activated} 已选分类={enabled.Count}/{BlocklistService.DefaultCategories.Count}");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.OnLoaded", ex);
            StatusText.Text = $"页面加载异常：{ex.Message}";
        }
    }

    // ---------- 分类（免费版目标数限制） ----------

    private void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_blocklist.IsActivated() && CountTargets() > BlocklistService.FreeTargetLimit)
            {
                ((CheckBox)sender).IsChecked = false; // 触发 Unchecked 保存
                ShowLimitHint();
                return;
            }
            SaveCategories();
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.OnCategoryChecked", ex);
        }
    }

    private void OnCategoryChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveCategories();
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.OnCategoryChanged", ex);
        }
    }

    private void SaveCategories()
    {
        var enabled = CategoryPanel.Children
            .OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag);
        var list = enabled.ToList();
        _blocklist.SetEnabledCategories(list);
        App.LogAction("勾选分类", string.Join("+", list));
        LimitHint.Visibility = _blocklist.IsActivated() ? Visibility.Collapsed : Visibility.Visible;
        RefreshStatus();
    }

    /// <summary>当前屏蔽目标数（启用分类 + 自定义域名项）。</summary>
    private int CountTargets() =>
        CategoryPanel.Children.OfType<CheckBox>().Count(c => c.IsChecked == true) +
        _blocklist.GetCustomDomains().Count;

    private void ShowLimitHint()
    {
        LimitHint.Visibility = Visibility.Visible;
        StatusText.Text = $"免费版最多 {BlocklistService.FreeTargetLimit} 个屏蔽目标，激活后不限。";
    }

    // ---------- 自定义域名 ----------

    private void AddDomain_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = DomainBox.Text;
            if (string.IsNullOrWhiteSpace(input)) return;

            if (!_blocklist.IsActivated() && CountTargets() + 1 > BlocklistService.FreeTargetLimit)
            {
                ShowLimitHint();
                return;
            }

            var list = _blocklist.GetCustomDomains().ToList();
            list.Add(input);
            _blocklist.SetCustomDomains(list);

            DomainBox.Text = string.Empty;
            RefreshCustomDomains();
            RefreshStatus();
            App.LogAction("添加自定义域名", input);
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.AddDomain", ex);
            StatusText.Text = $"添加失败：{ex.Message}";
        }
    }

    private void RefreshCustomDomains()
    {
        DomainList.ItemsSource = _blocklist.GetCustomDomains().ToList();
    }

    // ---------- 每日限额 ----------

    private void AddLimit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var domain = LimitDomainBox.Text;
            if (string.IsNullOrWhiteSpace(domain)) return;

            _limits.SetLimit(domain, (int)LimitMinutesBox.Value);
            LimitDomainBox.Text = string.Empty;
            RefreshLimits();
            RefreshStatus();
            App.LogAction("添加限额", $"{domain}={(int)LimitMinutesBox.Value} 分钟/天");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.AddLimit", ex);
            StatusText.Text = $"添加限额失败：{ex.Message}";
        }
    }

    private void DeleteLimit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button { Tag: string domain })
            {
                _limits.RemoveLimit(domain);
                RefreshLimits();
                App.LogAction("删除限额", domain);
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.DeleteLimit", ex);
        }
    }

    private void RefreshLimits()
    {
        var items = _limits.GetLimits()
            .Select(kv => new LimitItem(kv.Key, $"{kv.Key} — 每日 {kv.Value} 分钟", kv.Key))
            .ToList();
        LimitList.ItemsSource = items;
    }

    // ---------- 临时放行 ----------

    private void AddAllow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var domain = AllowDomainBox.Text;
            if (string.IsNullOrWhiteSpace(domain)) return;

            _blocklist.AddTempAllow(domain, 10);
            AllowDomainBox.Text = string.Empty;
            RefreshAllowStatus();
            RefreshStatus();
            App.LogAction("临时放行", $"{domain} 10分钟");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.AddAllow", ex);
        }
    }

    private void ClearAllow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _blocklist.ClearTempAllows();
            RefreshAllowStatus();
            RefreshStatus();
            App.LogAction("清除全部放行");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.ClearAllow", ex);
        }
    }

    private void RefreshAllowStatus()
    {
        var allows = _blocklist.GetTempAllows();
        AllowStatus.Text = allows.Count == 0
            ? "当前无临时放行"
            : string.Join("，", allows.Select(a => $"{a.Domain}（至 {a.Expires:HH:mm}）"));
    }

    // ---------- 应用 / 撤销 / 清理 ----------

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _blocklist.Apply();
            RefreshStatus("屏蔽已应用。");
            App.LogAction("应用屏蔽", "成功");
        }
        catch (UnauthorizedAccessException)
        {
            StatusText.Text = "需要管理员权限：请右键「以管理员身份运行」本程序，再应用屏蔽。";
            App.LogAction("应用屏蔽", "需要管理员权限(UnauthorizedAccess)");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.Apply", ex);
            StatusText.Text = $"应用失败：{ex.Message}。请以管理员身份运行后重试。";
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _blocklist.Remove();
            RefreshStatus("屏蔽已撤销。");
            App.LogAction("撤销屏蔽", "成功");
        }
        catch (UnauthorizedAccessException)
        {
            StatusText.Text = "需要管理员权限：请右键「以管理员身份运行」本程序，再撤销屏蔽。";
            App.LogAction("撤销屏蔽", "需要管理员权限(UnauthorizedAccess)");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.Remove", ex);
            StatusText.Text = $"撤销失败：{ex.Message}。请以管理员身份运行后重试。";
        }
    }

    private void CleanUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _blocklist.Remove(); // 幂等：无标记段时无操作
            RefreshStatus("已检查并清理禅净的 hosts 标记段。");
            App.LogAction("清理标记段", "成功");
        }
        catch (UnauthorizedAccessException)
        {
            StatusText.Text = "需要管理员权限清理残留：请以管理员身份运行后重试。";
            App.LogAction("清理标记段", "需要管理员权限(UnauthorizedAccess)");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.CleanUp", ex);
            StatusText.Text = $"清理失败：{ex.Message}。";
        }
    }

    private void RefreshStatus(string? overrideText = null)
    {
        if (overrideText is not null)
        {
            StatusText.Text = overrideText;
            return;
        }
        var active = _blocklist.GetActiveDomains();
        StatusText.Text = _blocklist.IsApplied()
            ? $"屏蔽已生效（{active.Count} 个域名）"
            : $"未生效（已选 {active.Count} 个域名，点击「应用屏蔽」）";
    }

    private sealed record LimitItem(string Domain, string Text, object Tag);
}
