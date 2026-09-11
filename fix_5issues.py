import io

# 修复1：ExitApp中清除系统hosts
p1 = r'D:\github\chanjing\src\ChanJing.App\MainWindow.xaml.cs'
with io.open(p1, 'r', encoding='utf-8-sig') as f:
    t = f.read()
old1 = '''    private void ExitApp()
    {
        _exiting = true;
        Close();
        Application.Current.Exit();
    }'''
new1 = '''    private void ExitApp()
    {
        _exiting = true;
        // 退出时清除系统hosts中的屏蔽条目（防止退出后网站仍被屏蔽）
        try { ChanJing.Core.Services.HostsBlocker.Remove(); }
        catch (UnauthorizedAccessException) { /* 普通权限写不了hosts，跳过 */ }
        catch (Exception ex) { App.LogCrash("ExitApp清理hosts", ex); }
        Close();
        Application.Current.Exit();
    }'''
if old1 in t:
    t = t.replace(old1, new1, 1)
    print('修复1成功：ExitApp清除系统hosts')
else:
    print('修复1失败')
with io.open(p1, 'w', encoding='utf-8-sig', newline='') as f:
    f.write(t)

# 修复2：BlocklistService加ResetAll方法，真正清除所有配置
p2 = r'D:\github\chanjing\src\ChanJing.Core\Services\BlocklistService.cs'
with io.open(p2, 'r', encoding='utf-8-sig') as f:
    t = f.read()
old2 = '''    public void Remove()
    {
        HostsBlocker.ClearPreApply();
    }'''
new2 = '''    public void Remove()
    {
        HostsBlocker.ClearPreApply();
    }

    /// <summary>重置所有屏蔽配置：清除启用分类/自定义域名/自定义应用/每日限额/临时放行/hosts.pre。场景配置不受影响（需单独重置场景）。</summary>
    public void ResetAll()
    {
        _db.SetSetting(SettingKeyEnabledCategories, "");
        _db.SetSetting(SettingKeyCustomDomains, "");
        _db.SetSetting(SettingKeyCustomApps, "");
        _db.SetSetting(SettingKeyDailyLimits, "");
        _db.SetSetting(SettingKeyTempAllows, "");
        _db.SetSetting(SettingKeyManualShield, "false");
        HostsBlocker.ClearPreApply();
        try { HostsBlocker.Remove(); }
        catch (UnauthorizedAccessException) { }
    }'''
if old2 in t:
    t = t.replace(old2, new2, 1)
    print('修复2成功：BlocklistService加ResetAll方法')
else:
    print('修复2失败')
with io.open(p2, 'w', encoding='utf-8-sig', newline='') as f:
    f.write(t)

# 修复3：ShieldPage的Remove_Click调用ResetAll
p3 = r'D:\github\chanjing\src\ChanJing.App\ShieldPage.xaml.cs'
with io.open(p3, 'r', encoding='utf-8-sig') as f:
    t = f.read()
old3 = '''            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            _blocklist.Remove();
            RefreshStatus("配置已清除。");
            App.LogAction("清除屏蔽配置", "成功");'''
new3 = '''            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            _blocklist.ResetAll();
            RefreshAll();
            RefreshStatus("所有屏蔽配置已清除（场景配置保留，可在首页右键场景单独重置）。");
            App.LogAction("清除屏蔽配置", "成功");'''
if old3 in t:
    t = t.replace(old3, new3, 1)
    print('修复3成功：Remove_Click调用ResetAll')
else:
    print('修复3失败')
with io.open(p3, 'w', encoding='utf-8-sig', newline='') as f:
    f.write(t)

# 修复4：实现FocusOnlyCommunication——在GetActiveDomains中排除沟通工具（非专注中时）
p4 = r'D:\github\chanjing\src\ChanJing.Core\Services\BlocklistService.cs'
with io.open(p4, 'r', encoding='utf-8-sig') as f:
    t = f.read()
# 先找到GetActiveDomains方法
import re
match = re.search(r'(public List<string> GetActiveDomains\(\)\s*\{[^}]+\})', t)
if match:
    print('找到GetActiveDomains:', match.group(0)[:100])
else:
    print('未找到GetActiveDomains，用Grep定位')
