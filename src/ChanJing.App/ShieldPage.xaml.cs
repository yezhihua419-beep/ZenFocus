using System.Diagnostics;
using System.Security;
using ChanJing.Core.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChanJing_App;

/// <summary>
/// 屏蔽管理页：分类勾选（免费版限 3 目标）、自定义域名、每日限额、临时放行、
/// 应用/撤销/清理残留。hosts 写入权限不足时给出友好引导。
/// </summary>
public sealed partial class ShieldPage : Page
{
    private readonly BlocklistService _blocklist = AppServices.Blocklist;
    private readonly DailyLimitService _limits = AppServices.DailyLimits;
    private bool _isLoading = true; // 初始化/刷新期间不触发下拉保存，避免构造时弹确认框

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
            Title = I18n.Get("Admin_ElevateTitle", "Administrator required"),
            Content = I18n.GetFormat("Admin_ElevateContent", label),
            PrimaryButtonText = I18n.Get("Admin_ElevatePrimary", "Restart as admin"),
            CloseButtonText = I18n.Get("Common_Cancel.Content", "Cancel"),
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
            StatusText.Text = I18n.Get("Admin_ElevateFailed", "Could not elevate. Right-click → Run as administrator.");
            return false;
        }
    }

    public ShieldPage()
    {
        InitializeComponent();
        ApplyLocalized();
        Loaded += OnLoaded;
    }

    /// <summary>屏蔽页 x:Uid 缺 key 会变成空按钮，文案一律走 PRI。</summary>
    private void ApplyLocalized()
    {
        I18n.SetContent(ManualShieldSwitch, "ShieldPage_ManualShield.Header", "Manual blocking");
        I18n.SetContent(ManualShieldSwitch, "ShieldPage_ManualShield.OnContent", "On");
        I18n.SetContent(ManualShieldSwitch, "ShieldPage_ManualShield.OffContent", "Off");
        I18n.SetContent(ModeMinimize, "ShieldPage_AppModeMinimize.Content", "Minimize");
        I18n.SetContent(ModeKill, "ShieldPage_AppModeKill.Content", "Kill process");
        I18n.SetContent(CatShort, "ShieldPage_AppCatShort.Content", "Short video");
        I18n.SetContent(CatVideo, "ShieldPage_AppCatVideo.Content", "Video");
        I18n.SetContent(CatSocial, "ShieldPage_AppCatSocial.Content", "Social");
        I18n.SetContent(CatNews, "ShieldPage_AppCatNews.Content", "News");
        I18n.SetContent(CatShop, "ShieldPage_AppCatShop.Content", "Shopping");
        I18n.SetContent(CatComm, "ShieldPage_AppCatComm.Content", "Messaging");
        I18n.SetContent(AddAppButton, "ShieldPage_AddApp.Content", "Add");
        I18n.SetContent(AppBox, "ShieldPage_AppBox.PlaceholderText", "Process name, e.g. Douyin");
        I18n.SetContent(CustomDomainExpander, "ShieldPage_CustomDomain.Header", "Custom domains");
        I18n.SetContent(AddDomainButton, "ShieldPage_AddDomain.Content", "Add");
        I18n.SetContent(DomainBox, "ShieldPage_DomainBox.PlaceholderText", "Domain, e.g. example.com");
        I18n.SetContent(DailyLimitExpander, "ShieldPage_DailyLimit.Header", "Daily limits");
        I18n.SetContent(AddLimitButton, "ShieldPage_AddLimit.Content", "Add");
        I18n.SetContent(LimitDomainBox, "ShieldPage_LimitDomainBox.PlaceholderText", "Domain, e.g. bilibili.com");
        I18n.SetContent(LimitMinutesBox, "ShieldPage_LimitMinutes.Header", "Minutes/day");
        I18n.SetContent(TempAllowExpander, "ShieldPage_TempAllow.Header", "Temporary allow");
        I18n.SetContent(AllowButton, "ShieldPage_AllowButton.Content", "Allow…");
        I18n.SetContent(ClearAllowButton, "ShieldPage_ClearAllow.Content", "Clear allows");
        I18n.SetContent(ElevateButton, "Admin_ElevatePrimary", "Restart as admin");
        I18n.SetContent(ApplyButton, "ShieldPage_Apply.Content", "Save config");
        I18n.SetContent(ClearButton, "ShieldPage_Clear.Content", "Clear");
        I18n.SetContent(DiagnoseButton, "Shield_Diagnose", "Diagnose");
        I18n.SetContent(FeedbackButton, "ShieldPage_Feedback.Content", "Feedback");
        I18n.SetContent(Allow5Item, "MainPage_Allow5.Text", "Allow 5 min");
        I18n.SetContent(Allow15Item, "MainPage_Allow15.Text", "Allow 15 min");
        I18n.SetContent(Allow30Item, "MainPage_Allow30.Text", "Allow 30 min");
        I18n.SetContent(AllowDomainBox, "Shield_AllowPlaceholder", "Domain, e.g. example.com");
        CatalogLabel.Text = I18n.Get("Shield_CatalogLabel", "Site list");
        I18n.SetContent(CatalogIntl, "Shield_CatalogIntl", "International (YouTube / TikTok)");
        I18n.SetContent(CatalogChina, "Shield_CatalogChina", "China (Douyin / Bilibili)");
        ApplyAdminHint();
        RefreshCatalogHint();
        RefreshDomainLock();
        RefreshCooldownLock();
        var appSample = string.Join("+", BlocklistService.DefaultAppCategories.SelectMany(kv => kv.Value).Take(6));
        var siteSample = string.Join("+", BlocklistService.DefaultCategories.SelectMany(kv => kv.Value).Take(4));
        App.LogAction("i18n-shield", $"apply={ApplyButton.Content} mode={ModeMinimize.Content} add={AddAppButton.Content} apps={appSample} sites={siteSample}");
    }

    private void ApplyAdminHint()
    {
        var show = !I18n.IsElevated();
        AdminHint.Text = I18n.Get("Admin_Hint", "Not administrator: YouTube/TikTok tabs will stay open (hosts skipped). Desktop apps can still be minimized. Right-click → Run as administrator.");
        AdminHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AdminPrivacy.Text = I18n.Get("Admin_Privacy", "Admin is only for hosts and desktop-app blocking. Nothing is uploaded. Data stays on this PC.");
        ElevateButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
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
                    Content = I18n.GetFormat("Shield_CatLabel", I18n.CategoryName(category), siteCount),
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
                var diffHint = hasDiff ? I18n.GetFormat("Scene_Unsaved", curCats.Count) : "";
                SceneStateHint.Text = I18n.GetFormat("Shield_SceneState", I18n.SceneName(sceneTag), sceneCfg.Minutes, sceneCfg.Categories.Length, diffHint);
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
            var catalog = BlocklistService.ReadCatalogLocale(App.GetLanguage());
            CatalogBox.SelectedItem = catalog == "zh-CN" ? CatalogChina : CatalogIntl;
            RefreshCatalogHint();
            AppModeBox.SelectedIndex = blockMode == "kill" ? 1 : 0;
            FocusOnlyCommSwitch.IsOn = _blocklist.IsFocusOnlyCommunication();
            // 初始化ADHD缓冲期时长；免费版若库里残留 20/30，显示并回落到 10
            var cooldownMinutes = _blocklist.GetCooldownMinutes();
            if (SceneManager.IsAdhdCooldownLocked(cooldownMinutes, activated))
            {
                _blocklist.SetCooldownMinutes(10);
                cooldownMinutes = 10;
            }
            var cooldownIndex = cooldownMinutes == 5 ? 0 : cooldownMinutes == 10 ? 1 : cooldownMinutes == 15 ? 2 : cooldownMinutes == 20 ? 3 : 4;
            CooldownMinutesBox.SelectedIndex = cooldownIndex;
            RefreshCooldownLock();
            RefreshStatus();
            App.LogAction("进入屏蔽页", $"激活={activated} 已选分类={enabled.Count}/{BlocklistService.DefaultCategories.Count} 拦截方式={blockMode} UI设置={(blockMode == "kill" ? 1 : 0)}");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.RefreshAll", ex);
            StatusText.Text = I18n.GetFormat("Shield_LoadFail", ex.Message);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void RefreshCatalogHint()
    {
        var zh = BlocklistService.UseZhCatalog;
        CatalogHint.Text = zh
            ? I18n.Get("Shield_CatalogHintZh", "China list: Douyin, Bilibili, Weibo. Switching UI language will not change this.")
            : I18n.Get("Shield_CatalogHintEn", "International list: YouTube, TikTok, Reddit. Switching UI language will not change this.");
    }

    /// <summary>切名单语言：勾选分类不变，域名/App 从抖音换成 TikTok（或反过来）。</summary>
    private async void Catalog_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        var next = (CatalogBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (next is not ("en-US" or "zh-CN")) return;
        var current = BlocklistService.ReadCatalogLocale(App.GetLanguage());
        if (next == current) return;
        try
        {
            var dialog = new ContentDialog
            {
                Title = I18n.Get("Shield_CatalogConfirmTitle", "Switch site list?"),
                Content = I18n.Get("Shield_CatalogConfirmContent", "Checked categories stay. Sites and apps switch between China and International lists. UI language is unchanged."),
                PrimaryButtonText = I18n.Get("Shield_CatalogConfirmPrimary", "Switch list"),
                CloseButtonText = I18n.Get("Common_Cancel.Content", "Cancel"),
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _isLoading = true;
                CatalogBox.SelectedItem = current == "zh-CN" ? CatalogChina : CatalogIntl;
                _isLoading = false;
                return;
            }
            BlocklistService.WriteCatalogLocale(next);
            _blocklist.Apply();
            _blocklist.RestoreSystemHostsIfNeeded(AppServices.Engine.IsRunning || _blocklist.IsManualShieldActive());
            RefreshAll();
            AppServices.Notify(I18n.Get(next == "zh-CN" ? "Notify_CatalogZh" : "Notify_CatalogEn",
                next == "zh-CN" ? "Switched to China site list." : "Switched to International site list."));
            App.LogAction("切换屏蔽名单", next);
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.Catalog", ex);
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
        StatusText.Text = I18n.GetFormat("Shield_FreeDomainLimit", BlocklistService.FreeTargetLimit);
    }

    /// <summary>自定义域名满 3 个：Add 灰锁+锁标，点击出升级框。</summary>
    private void RefreshDomainLock()
    {
        var locked = SceneManager.IsCustomDomainAddLocked(CountCustomDomains(), _blocklist.IsActivated());
        PaidLock.Prefix(AddDomainButton, I18n.Get("ShieldPage_AddDomain.Content", "Add"), locked);
        PaidLock.Apply(AddDomainButton, locked,
            I18n.Get("PaidLock_Domain", "Free plan: 3 custom domains · $19 lifetime"));
    }

    /// <summary>未激活：20/30 灰+锁标+hover，仍可选中以弹出升级框。</summary>
    private void RefreshCooldownLock()
    {
        var locked = !_blocklist.IsActivated();
        var tip = I18n.Get("PaidLock_Cooldown", "ADHD cooldown 20/30 min · $19 lifetime");
        PaidLock.Prefix(Cooldown20, "20", locked);
        PaidLock.Prefix(Cooldown30, "30", locked);
        PaidLock.Apply(Cooldown20, locked, tip);
        PaidLock.Apply(Cooldown30, locked, tip);
    }

    private async System.Threading.Tasks.Task ShowCooldownUpgradeHint()
    {
        var dialog = new ContentDialog
        {
            Title = I18n.Get("Upgrade_Title", "Upgrade to Pro"),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = I18n.Get("PaidLock_Cooldown", "ADHD cooldown 20/30 min · $19 lifetime"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = I18n.Get("Notify_CooldownPaid", "20/30 min cooldown is paid. Free version: 5/10/15 min."), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = I18n.Get("Price_Buyout", "$19 lifetime, forever."), Foreground = (Brush)Application.Current.Resources["BrushAccent"] }
                }
            },
            PrimaryButtonText = I18n.Get("SceneUpgrade_Primary", "Learn more"),
            CloseButtonText = I18n.Get("Common_Cancel.Content", "Cancel"),
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            App.LogAction("ADHD缓冲升级提示", "用户点击了解升级");
    }

    private async System.Threading.Tasks.Task ShowDomainUpgradeHint()
    {
        var dialog = new ContentDialog
        {
            Title = I18n.Get("Upgrade_Title", "Upgrade to Pro"),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = I18n.Get("PaidLock_Domain", "Free plan: 3 custom domains · $19 lifetime"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = I18n.GetFormat("Shield_FreeDomainLimit", BlocklistService.FreeTargetLimit), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = I18n.Get("Price_Buyout", "$19 lifetime, forever."), Foreground = (Brush)Application.Current.Resources["BrushAccent"] }
                }
            },
            PrimaryButtonText = I18n.Get("SceneUpgrade_Primary", "Learn more"),
            CloseButtonText = I18n.Get("Common_Cancel.Content", "Cancel"),
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            App.LogAction("域名升级提示", "用户点击了解升级");
    }

    // ---------- 自定义域名 ----------

    private void AddDomain_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SceneManager.IsCustomDomainAddLocked(CountCustomDomains(), _blocklist.IsActivated()))
            {
                ShowLimitHint();
                _ = ShowDomainUpgradeHint();
                return;
            }

            var input = DomainBox.Text;
            if (string.IsNullOrWhiteSpace(input)) return;

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
            StatusText.Text = I18n.GetFormat("Shield_AddFail", ex.Message);
        }
    }

    private void RefreshCustomDomains()
    {
        var remove = I18n.Get("ShieldPage_RemoveApp.Content", "Remove");
        DomainList.ItemsSource = _blocklist.GetCustomDomains()
            .Select(d => new DomainItem(d, remove))
            .ToList();
        RefreshDomainLock();
    }

    private void RemoveDomain_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button { Tag: string domain })
            {
                _blocklist.RemoveCustomDomain(domain);
                RefreshCustomDomains();
                RefreshStatus();
                App.LogAction("移除自定义域名", domain);
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.RemoveDomain", ex);
        }
    }

    private sealed record DomainItem(string Domain, string RemoveLabel);

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
            StatusText.Text = I18n.GetFormat("Shield_AddLimitFail", ex.Message);
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
            .Select(kv => new LimitItem(kv.Key, kv.Value, I18n.Get("ShieldPage_DeleteLimit.Content", "Delete")))
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
            ? I18n.Get("Shield_NoAllow", "No temporary allows")
            : string.Join(" · ", allows.Select(a => I18n.GetFormat("Shield_AllowUntil", a.Domain, a.Expires)));
    }

    // ---------- 应用 / 撤销 / 清理 ----------

    private async void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _blocklist.Apply();
            await AskElevateAsync("--apply-shield", I18n.Get("Admin_ElevateHosts", "Write website block to hosts"));
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.ElevateClick", ex);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _blocklist.Apply();
            // 只写 hosts.pre，不 SaveSceneConfig：避免把免费「自定义场景」额度吃掉
            RefreshStatus();
            if (!I18n.IsElevated())
                AppServices.Notify(I18n.Get("Notify_SaveNeedAdmin", "Saved. Desktop apps will block. Websites need administrator."));
            App.LogAction("保存屏蔽配置", "成功");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.Apply", ex);
            StatusText.Text = I18n.GetFormat("Shield_SaveFail", ex.Message);
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = new ContentDialog
            {
                Title = I18n.Get("Shield_ClearTitle", "Clear all rules?"),
                Content = I18n.Get("Shield_ClearContent", "This clears saved categories and custom domains. Cannot undo."),
                PrimaryButtonText = I18n.Get("Shield_ClearPrimary", "Clear"),
                CloseButtonText = I18n.Get("Common_Cancel.Content", "Cancel"),
                XamlRoot = this.Content.XamlRoot
            };
            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            _blocklist.ResetAll();
            RefreshAll();
            RefreshStatus(I18n.Get("Shield_Cleared", "All block rules cleared."));
            App.LogAction("清除屏蔽配置", "成功");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.Remove", ex);
            StatusText.Text = I18n.GetFormat("Shield_ClearFail", ex.Message);
        }
    }

    private async void CleanUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _blocklist.Remove(); // 幂等：无标记段时无操作
            RefreshStatus(I18n.Get("Shield_Cleaned", "Checked and cleaned ZenFocus hosts markers."));
            App.LogAction("清理标记段", "成功");
        }
        catch (UnauthorizedAccessException)
        {
            App.LogAction("清理标记段", "需要管理员权限(UnauthorizedAccess)");
            if (await AskElevateAsync("--cleanup", I18n.Get("Admin_CleanupLabel", "Clean leftovers")))
            {
                App.Current.Exit();
            }
            else
            {
                StatusText.Text = I18n.Get("Admin_CleanupNeed", "Cleaning leftovers needs administrator.");
            }
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.CleanUp", ex);
            StatusText.Text = I18n.GetFormat("Shield_CleanFail", ex.Message);
        }
    }

    // ---------- 桌面应用拦截 ----------

    private void RefreshApps()
    {
        var customProcs = _blocklist.GetCustomApps()
            .Select(a => a.Process)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = _blocklist.GetActiveApps()
            .Select(a => new AppItem(a.Process, a.Category, $"{I18n.AppName(a.Process)} · {I18n.CategoryName(a.Category)}",
                customProcs.Contains(a.Process) ? Visibility.Visible : Visibility.Collapsed,
                I18n.Get("ShieldPage_RemoveApp.Content", "Remove")))
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
                StatusText.Text = I18n.Get("Shield_NeedProc", "Enter a process name, e.g. Douyin.");
                return;
            }
            var category = (AppCategoryBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "短视频";
            _blocklist.AddCustomApp(proc, category);
            AppBox.Text = string.Empty;
            RefreshApps();
            RefreshStatus();
            App.LogAction("添加桌面应用拦截", $"{proc}({category})");
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.AddApp", ex);
            StatusText.Text = I18n.GetFormat("Shield_AddFail", ex.Message);
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
                        Title = I18n.Get("Shield_KillTitle", "Kill-process mode"),
                        Content = I18n.Get("Shield_KillContent", "This will force-close blocked apps. Unsaved work may be lost."),
                        PrimaryButtonText = I18n.Get("Shield_KillPrimary", "Use it"),
                        CloseButtonText = I18n.Get("Common_Cancel.Content", "Cancel"),
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
            if (SceneManager.IsAdhdCooldownLocked(minutes, _blocklist.IsActivated()))
            {
                _isLoading = true;
                CooldownMinutesBox.SelectedIndex = 1; // 回退到10分钟，不禁用选项
                _isLoading = false;
                _ = ShowCooldownUpgradeHint();
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

    private sealed record AppItem(string Process, string Category, string Text, Visibility RemoveVis, string RemoveLabel);

    private void RefreshStatus(string? overrideText = null)
    {
        if (overrideText is not null)
        {
            StatusText.Text = overrideText;
            return;
        }
        var active = _blocklist.GetActiveDomains();
        if (HostsBlocker.IsApplied())
            StatusText.Text = I18n.GetFormat("Shield_HostsActive", active.Count);
        else if (_blocklist.IsApplied())
            StatusText.Text = I18n.IsElevated()
                ? I18n.GetFormat("Shield_SavedPendingFocus", active.Count)
                : I18n.GetFormat("Shield_SavedAppsOnly", active.Count);
        else
            StatusText.Text = I18n.GetFormat("Shield_NotApplied", active.Count);
    }

    private sealed record LimitItem(string Domain, int Minutes, string DeleteLabel);

    // ---------- 导入/导出配置（次要：诊断框里，免费不灰锁） ----------

    private async System.Threading.Tasks.Task ExportConfigAsync()
    {
        try
        {
            var json = _blocklist.ExportConfig();
            var file = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFileAsync("chanjing-config.json", Windows.Storage.CreationCollisionOption.GenerateUniqueName);
            await Windows.Storage.FileIO.WriteTextAsync(file, json);
            StatusText.Text = I18n.GetFormat("Shield_Exported", file.Path);
            App.LogAction("导出配置", file.Path);
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.ExportConfig", ex);
            StatusText.Text = I18n.GetFormat("Shield_ExportFail", ex.Message);
        }
    }

    private async System.Threading.Tasks.Task ImportConfigAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            if (App.MainWindow is not null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var json = await Windows.Storage.FileIO.ReadTextAsync(file);
            _blocklist.ImportConfig(json);
            RefreshAll();
            StatusText.Text = I18n.Get("Shield_Imported", "Config imported. Tap Save config.");
            App.LogAction("导入配置", file.Path);
        }
        catch (Exception ex)
        {
            App.LogCrash("ShieldPage.ImportConfig", ex);
            StatusText.Text = I18n.GetFormat("Shield_ImportFail", ex.Message);
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

    /// <summary>一键诊断：只读，说清网站/桌面/浏览器各拦到哪一层。</summary>
    private async void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var r = FocusDiagnose.Run(_blocklist, AppServices.Engine.IsRunning, I18n.IsElevated());
            var yes = I18n.Get("Diag_Yes", "Yes");
            var no = I18n.Get("Diag_No", "No");
            var catalog = r.Catalog.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? I18n.Get("Shield_CatalogChina", "China")
                : I18n.Get("Shield_CatalogIntl", "International");
            var website = r.WebsiteLayer == "hosts"
                ? I18n.Get("Diag_WebsiteHosts", "Websites: hosts can block new visits.")
                : I18n.Get("Diag_WebsiteNeedAdmin", "Websites: need administrator. Browser tabs stay open.");
            var browser = r.BrowserLayer == "hosts-new-nav"
                ? I18n.Get("Diag_BrowserHosts", "Browser: new visits fail. Already-open tabs may stay.")
                : I18n.Get("Diag_BrowserBubble", "Browser: distraction bubble only. Tabs are not closed.");
            var lines = new List<string>
            {
                I18n.GetFormat("Diag_Elevated", r.Elevated ? yes : no),
                I18n.GetFormat("Diag_HostsWrite", r.HostsWritable ? yes : no),
                I18n.GetFormat("Diag_Marker", r.OrphanMarker ? yes : no),
                I18n.GetFormat("Diag_Pre", r.PreApplyPresent ? yes : no),
                I18n.GetFormat("Diag_Catalog", catalog),
                website,
                I18n.Get("Diag_App", "Desktop apps: process name. Store apps: window title (never kill the store host)."),
                browser,
                I18n.GetFormat("Diag_Log", App.CrashLogPath)
            };
            var exportBtn = new Button { Content = I18n.Get("ShieldPage_Export.Content", "Export"), Padding = new Thickness(16, 8, 16, 8) };
            var importBtn = new Button { Content = I18n.Get("ShieldPage_Import.Content", "Import"), Padding = new Thickness(16, 8, 16, 8) };
            var dialog = new ContentDialog
            {
                Title = I18n.Get("Diag_Title", "Diagnose"),
                CloseButtonText = I18n.Get("Common_Close.Content", "OK"),
                XamlRoot = XamlRoot,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = string.Join("\n", lines), TextWrapping = TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = I18n.Get("Diag_ConfigBackup", "Blocklist backup (categories / custom sites / apps, not stats):"),
                            FontSize = 12,
                            Foreground = (Brush)Application.Current.Resources["BrushTextSecondary"],
                            TextWrapping = TextWrapping.Wrap
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children = { exportBtn, importBtn }
                        }
                    }
                }
            };
            exportBtn.Click += async (_, _) => await ExportConfigAsync();
            importBtn.Click += async (_, _) =>
            {
                dialog.Hide();
                await ImportConfigAsync();
            };
            await dialog.ShowAsync();
            App.LogAction("一键诊断", $"{r.WebsiteLayer}/{r.BrowserLayer} elevated={r.Elevated}");
        }
        catch (Exception ex) { App.LogCrash("ShieldPage.Diagnose", ex); }
    }

    /// <summary>反馈建议：打开默认邮件客户端，收件人预填。</summary>
    private void Feedback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "v0.8.0";
            var os = Environment.OSVersion.VersionString;
            var subject = Uri.EscapeDataString(I18n.GetFormat("FeedbackMail_Subject", version));
            var body = Uri.EscapeDataString(I18n.GetFormat("FeedbackMail_Body", version, os));
            var url = $"mailto:yezhihua_yzh@163.com?subject={subject}&body={body}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            App.LogAction("反馈建议", "打开邮件客户端");
        }
        catch (Exception ex) { App.LogCrash("Feedback_Click", ex); }
    }
}
