using System.Diagnostics;
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
    private bool _isLoading; // OnLoaded刷新期间不触发SelectionChanged保存，避免页面销毁时ComboBox重置覆盖用户设置

    /// <summary>以管理员身份重启本程序并执行指定屏蔽动作（apply/remove/cleanup）。
    /// 同时传递 --db-path 确保管理员进程读写同一个数据库。
    /// 启动成功后主动释放单实例Mutex并强制退出，避免管理员进程因获取不到锁而"已在运行"退出。</summary>
    private static void RelaunchElevated(string arg)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
            Arguments = $"{arg} --db-path=\"{AppServices.DbPath}\""
        };
        Process.Start(psi); // UAC取消会抛异常，由调用方catch；走到这里说明管理员进程已启动
        (App.Current as App)?.ReleaseSingleInstanceMutex();
        Environment.Exit(0); // 强制立即退出，不等待WinUI异步Exit完成
    }

    /// <summary>权限不足时询问是否提权重启执行。</summary>
    private async Task<bool> AskElevateAsync(string action, string label)
    {
        var dialog = new ContentDialog
        {
            Title = "Admin Rights Required",
            Content = $"「{label}」需要写入系统 hosts 文件，需要管理员权限。\n\n是否以管理员身份重启禅净并自动执行？",
            PrimaryButtonText = "Restart as Admin",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return false;
        try
        {
            RelaunchElevated(action);
            App.LogAction("提权重启", action);
            return true;
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.Elevate", ex);
            StatusText.Text = "未能以管理员身份启动：可能取消了授权。请右键「以管理员身份运行」本程序后重试。";
            return false;
        }
    }

    public ShieldPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>每次进入屏蔽页都刷新（跨页面同步场景/状态）。</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshAll();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        _isLoading = true;
        try
        {
            CategoryPanel.Items.Clear();
            var enabled = _blocklist.GetEnabledCategories();
            var activated = _blocklist.IsActivated();
            var blockMode = _blocklist.GetAppBlockMode();

            foreach (var category in BlocklistService.DefaultCategories.Keys)
            {
                var siteCount = BlocklistService.DefaultCategories[category].Length;
                var checkBox = new CheckBox
                {
                    Content = $"{category}（{siteCount}站）",
                    IsChecked = enabled.Contains(category),
                    Tag = category,
                    FontSize = 13,
                    MinWidth = 100,
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 8, 8),
                    CornerRadius = new CornerRadius(14)
                };
                checkBox.Checked += OnCategoryChecked;
                checkBox.Unchecked += OnCategoryChanged;
                CategoryPanel.Items.Add(checkBox);
            }

            // 顶部显示当前场景摘要（跨页面同步：首页选中场景后这里可见）
            // 顶部场景摘要：比较当前勾选分类与场景配置，不一致时提示"有未保存修改"
            var sceneTag = AppServices.CurrentSceneTag;
            if (!string.IsNullOrEmpty(sceneTag))
            {
                var sceneCfg = SceneManager.GetSceneConfig(AppServices.Db, sceneTag);
                var curCats = _blocklist.GetEnabledCategories().ToHashSet(StringComparer.Ordinal);
                var scnCats = sceneCfg.Categories.ToHashSet(StringComparer.Ordinal);
                var hasDiff = !curCats.SetEquals(scnCats);
                var diffHint = hasDiff ? $" · 当前勾选{curCats.Count}类（有未保存修改，点「保存配置」或开始专注时记录）" : "";
                SceneStateHint.Text = $"当前场景：{SceneManager.GetSceneName(sceneTag)} · {sceneCfg.Minutes}分钟 · 屏蔽{sceneCfg.Categories.Length}类{diffHint}";
                SceneStateHint.Visibility = Visibility.Visible;
            }
            else
            {
                SceneStateHint.Visibility = Visibility.Collapsed;
            }

            LimitHint.Visibility = activated ? Visibility.Collapsed : Visibility.Visible;
            RefreshCustomDomains();
            RefreshLimits();
            RefreshAllowStatus();
            if (ManualShieldSwitch is not null)
            {
                ManualShieldSwitch.IsOn = AppServices.Blocklist.IsManualShieldActive();
                ManualShieldHint.Visibility = ManualShieldSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
            RefreshApps();
            AppModeBox.SelectedIndex = blockMode == "kill" ? 1 : 0;
            FocusOnlyCommSwitch.IsOn = _blocklist.IsFocusOnlyCommunication();
            // 初始化ADHD缓冲期时长
            var cooldownMinutes = _blocklist.GetCooldownMinutes();
            var cooldownIndex = cooldownMinutes == 5 ? 0 : cooldownMinutes == 10 ? 1 : cooldownMinutes == 15 ? 2 : cooldownMinutes == 20 ? 3 : 4;
            CooldownMinutesBox.SelectedIndex = cooldownIndex;
            RefreshStatus();
            App.LogAction("进入屏蔽页", $"激活={activated} 已选分类={enabled.Count}/{BlocklistService.DefaultCategories.Count} 拦截方式={blockMode} UI设置={(blockMode == "kill" ? 1 : 0)}");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.RefreshAll", ex);
            StatusText.Text = $"页面加载异常：{ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ---------- 分类（免费版全开放，不限制数量） ----------

    private void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        try
        {
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
        var enabled = CategoryPanel.Items
            .OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag);
        var list = enabled.ToList();
        _blocklist.SetEnabledCategories(list);
        App.LogAction("勾选分类", string.Join("+", list));
        RefreshApps();
        LimitHint.Visibility = _blocklist.IsActivated() ? Visibility.Collapsed : Visibility.Visible;
        RefreshStatus();
    }

    /// <summary>当前自定义域名数（分类不限制，仅自定义域名限3个）。</summary>
    private int CountCustomDomains() =>
        _blocklist.GetCustomDomains().Count;

    private void ShowLimitHint()
    {
        LimitHint.Visibility = Visibility.Visible;
        StatusText.Text = $"免费版自定义域名最多 {BlocklistService.FreeTargetLimit} 个，激活后不限。分类屏蔽全开放。";
    }

    // ---------- 自定义域名 ----------

    private void AddDomain_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = DomainBox.Text;
            if (string.IsNullOrWhiteSpace(input)) return;

            if (!_blocklist.IsActivated() && CountCustomDomains() + 1 > BlocklistService.FreeTargetLimit)
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

    /// <summary>临时放行（菜单选择时长）：放行指定域名 5/15/30 分钟。</summary>
    private void AddAllowMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var minutes = int.TryParse((sender as MenuFlyoutItem)?.Tag?.ToString(), out var m) ? m : 5;
            var domain = AllowDomainBox.Text;
            if (string.IsNullOrWhiteSpace(domain)) return;

            _blocklist.AddTempAllow(domain, minutes);
            AllowDomainBox.Text = string.Empty;
            RefreshAllowStatus();
            if (ManualShieldSwitch is not null)
            {
                ManualShieldSwitch.IsOn = AppServices.Blocklist.IsManualShieldActive();
                ManualShieldHint.Visibility = ManualShieldSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
            RefreshStatus();
            App.LogAction("临时放行", $"{domain} {minutes}分钟");
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
            if (ManualShieldSwitch is not null)
            {
                ManualShieldSwitch.IsOn = AppServices.Blocklist.IsManualShieldActive();
                ManualShieldHint.Visibility = ManualShieldSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
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
            // 保存到场景配置。未选场景时自动选中「工作」场景，避免配置无归属导致选场景后被覆盖丢失
            var sceneTag = AppServices.CurrentSceneTag;
            if (string.IsNullOrEmpty(sceneTag))
            {
                sceneTag = "work";
                AppServices.CurrentSceneTag = sceneTag;
                AppServices.Notify(I18n.Get("Notify_NoSceneAuto", "No scene selected. Auto-selected Work scene and saved config."));
            }
            var cats = _blocklist.GetEnabledCategories().ToArray();
            // 用当前场景的愿望和时长，只更新屏蔽分类
            var existing = ChanJing.Core.Services.SceneManager.GetSceneConfig(AppServices.Db, sceneTag);
            ChanJing.Core.Services.SceneManager.SaveSceneConfig(AppServices.Db, sceneTag,
                new ChanJing.Core.Services.SceneManager.SceneConfig(existing.Wish, existing.Minutes, cats));
            App.LogAction("保存场景配置", $"{sceneTag} 屏蔽=[{string.Join("/", cats)}]");
            RefreshStatus(I18n.Get("Notify_ConfigSaved", "Config saved. Applies when focus starts."));
            App.LogAction("保存屏蔽配置", "成功");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.Apply", ex);
            StatusText.Text = $"保存失败：{ex.Message}";
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = new ContentDialog
            {
                Title = "Confirm Clear",
                Content = "This will clear all saved block categories and custom domains. This cannot be undone. Continue?",
                PrimaryButtonText = "Confirm Clear",
                CloseButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };
            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            _blocklist.ResetAll();
            RefreshAll();
            RefreshStatus("所有屏蔽配置已清除（场景配置保留，可在首页右键场景单独重置）。");
            App.LogAction("清除屏蔽配置", "成功");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.Remove", ex);
            StatusText.Text = $"清除失败：{ex.Message}";
        }
    }

    private async void CleanUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _blocklist.Remove(); // 幂等：无标记段时无操作
            RefreshStatus("已检查并清理禅净的 hosts 标记段。");
            App.LogAction("清理标记段", "成功");
        }
        catch (UnauthorizedAccessException)
        {
            App.LogAction("清理标记段", "需要管理员权限(UnauthorizedAccess)");
            if (await AskElevateAsync("--cleanup", "清理残留"))
            {
                App.Current.Exit();
            }
            else
            {
                StatusText.Text = "需要管理员权限清理残留：请以管理员身份运行后重试。";
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.CleanUp", ex);
            StatusText.Text = $"清理失败：{ex.Message}。";
        }
    }

    // ---------- 桌面应用拦截 ----------

    private void RefreshApps()
    {
        var customProcs = _blocklist.GetCustomApps()
            .Select(a => a.Process)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = _blocklist.GetActiveApps()
            .Select(a => new AppItem(a.Process, a.Category, BlocklistService.GetAppDisplayName(a.Process),
                customProcs.Contains(a.Process) ? Visibility.Visible : Visibility.Collapsed))
            .ToList();
        AppList.ItemsSource = items;
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var proc = AppBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(proc))
            {
                StatusText.Text = "请输入进程名，如 Douyin。";
                return;
            }
            var category = (AppCategoryBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "短视频";
            _blocklist.AddCustomApp(proc, category);
            AppBox.Text = string.Empty;
            RefreshApps();
            RefreshStatus();
            App.LogAction("添加桌面应用拦截", $"{proc}({category})");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.AddApp", ex);
            StatusText.Text = $"添加失败：{ex.Message}";
        }
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button { Tag: string proc })
            {
                _blocklist.RemoveCustomApp(proc);
                RefreshApps();
                RefreshStatus();
                App.LogAction("移除桌面应用拦截", proc);
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.RemoveApp", ex);
        }
    }

    private void FocusOnlyComm_Toggled(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isLoading) return;
            _blocklist.SetFocusOnlyCommunication(FocusOnlyCommSwitch.IsOn);
            App.LogAction("设置沟通工具仅专注中屏蔽", FocusOnlyCommSwitch.IsOn.ToString());
            RefreshStatus();
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.FocusOnlyComm", ex);
        }
    }
    private async void AppMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || !IsLoaded) return;
        try
        {
            var isKill = AppModeBox.SelectedIndex == 1;
            if (isKill)
            {
                // 切换到结束进程模式时弹确认（仅第一次，确认后存在数据库）
                var confirmed = AppServices.Db.GetSetting("kill_mode_confirmed") == "true";
                if (!confirmed)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Kill-Process Mode",
                        Content = "Kill-Process will force close blocked apps. Unsaved content may be lost.\n\nUse this mode?",
                        PrimaryButtonText = "Confirm Use",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = XamlRoot
                    };
                    var result = await dialog.ShowAsync();
                    if (result != ContentDialogResult.Primary)
                    {
                        _isLoading = true;
                        AppModeBox.SelectedIndex = 0;
                        _isLoading = false;
                        return;
                    }
                    AppServices.Db.SetSetting("kill_mode_confirmed", "true");
                }
            }
            var mode = isKill ? "kill" : "minimize";
            _blocklist.SetAppBlockMode(mode);
            App.LogAction("设置桌面应用拦截方式", mode);
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.AppMode", ex);
        }
    }

    /// <summary>ADHD缓冲期时长变更。免费版只能5/10/15，付费版可选20/30。</summary>
    private void CooldownMinutes_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || !IsLoaded) return;
        try
        {
            var item = CooldownMinutesBox.SelectedItem as ComboBoxItem;
            if (item == null || item.Tag == null) return;
            if (!int.TryParse(item.Tag.ToString(), out var minutes)) return;
            // 免费版限制：20/30需要付费
            if (!_blocklist.IsActivated() && minutes > 15)
            {
                CooldownMinutesBox.SelectedIndex = 1; // 回退到10分钟
                AppServices.Notify(I18n.Get("Notify_CooldownPaid", "20/30 min cooldown is paid. Free version: 5/10/15 min."));
                return;
            }
            _blocklist.SetCooldownMinutes(minutes);
            App.LogAction("设置ADHD缓冲期", $"{minutes}分钟");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.CooldownMinutes", ex);
        }
    }

    private sealed record AppItem(string Process, string Category, string Text, Visibility RemoveVis);

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
            : $"未配置（已选 {active.Count} 个域名，点击「保存配置」）";
    }

    private sealed record LimitItem(string Domain, string Text, object Tag);

    // ---------- 导入/导出配置 ----------

    private async void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var json = _blocklist.ExportConfig();
            var file = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFileAsync("chanjing-config.json", Windows.Storage.CreationCollisionOption.GenerateUniqueName);
            await Windows.Storage.FileIO.WriteTextAsync(file, json);
            StatusText.Text = $"配置已导出到：{file.Path}";
            App.LogAction("导出配置", file.Path);
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.ExportConfig", ex);
            StatusText.Text = $"导出失败：{ex.Message}";
        }
    }

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var json = await Windows.Storage.FileIO.ReadTextAsync(file);
            _blocklist.ImportConfig(json);
            RefreshAll();
            StatusText.Text = "配置已导入，点击「保存配置」，开始专注时自动生效。";
            App.LogAction("导入配置", file.Path);
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.ImportConfig", ex);
            StatusText.Text = $"导入失败：{ex.Message}";
        }
    }


    private void ManualShield_Toggled(object sender, RoutedEventArgs e)
    {
        try
        {
            var isOn = ManualShieldSwitch.IsOn;
            if (isOn)
            {
                AppServices.Blocklist.EnableManualShield();
                AppServices.Activity.ApplyShieldNow();
            }
            else
            {
                AppServices.Blocklist.DisableManualShield(AppServices.Engine.IsRunning);
            }
            ManualShieldHint.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { App.LogCrash("ManualShield_Toggled", ex); }
    }

    /// <summary>反馈建议：打开默认邮件客户端，收件人预填。</summary>
    private void Feedback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "v0.8.0";
            var os = Environment.OSVersion.VersionString;
            var subject = Uri.EscapeDataString($"禅净反馈 - v{version}");
            var body = Uri.EscapeDataString($"版本：{version}\n系统：{os}\n\n请描述您遇到的问题或建议：\n");
            var url = $"mailto:yezhihua_yzh@163.com?subject={subject}&body={body}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            App.LogAction("反馈建议", "打开邮件客户端");
        }
        catch (Exception ex) { App.LogCrash("Feedback_Click", ex); }
    }
}
