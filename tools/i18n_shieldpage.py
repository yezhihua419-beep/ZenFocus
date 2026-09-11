#!/usr/bin/env python3
"""把 ShieldPage.xaml 中的硬编码中文替换为 x:Uid 绑定。"""
import io

path = r"D:\github\chanjing\src\ChanJing.App\ShieldPage.xaml"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

replacements = [
    # 标题
    ('Text="屏蔽" FontSize="26"', 'x:Uid="ShieldPage_Title" Text="" FontSize="26"'),
    ('Text="勾选分类、添加域名，点击底部「保存为场景配置」，开始专注或开启立即屏蔽后生效"', 'x:Uid="ShieldPage_Subtitle" Text=""'),
    # 立即屏蔽
    ('Header="立即屏蔽（不计时）" OffContent="关" OnContent="开"', 'x:Uid="ShieldPage_ManualShield" Header="" OffContent="" OnContent=""'),
    ('Text="开启后立即屏蔽已保存的网站和应用，无需开始专注；关闭后恢复"', 'x:Uid="ShieldPage_ManualShieldHint" Text=""'),
    # 网站分类
    ('Text="网站分类" FontSize="15"', 'x:Uid="ShieldPage_CategoryTitle" Text="" FontSize="15"'),
    ('Text="免费版最多启用 3 个屏蔽目标（分类+自定义域名合计），升级后无限制。"', 'x:Uid="ShieldPage_LimitHint" Text=""'),
    # 桌面应用拦截
    ('Text="桌面应用拦截" FontSize="15"', 'x:Uid="ShieldPage_AppBlockTitle" Text="" FontSize="15"'),
    ('Text="拦截方式"', 'x:Uid="ShieldPage_AppModeLabel" Text=""'),
    ('Content="自动最小化" Tag="minimize"', 'x:Uid="ShieldPage_AppModeMinimize" Content="" Tag="minimize"'),
    ('Content="结束进程" Tag="kill"', 'x:Uid="ShieldPage_AppModeKill" Content="" Tag="kill"'),
    ('Text="自动最小化：温和拦截，可手动切回（专注中 2 秒冷却）。结束进程：强制关闭应用，未保存内容可能丢失。"', 'x:Uid="ShieldPage_AppModeHint" Text=""'),
    ('Text="沟通工具仅专注中屏蔽"', 'x:Uid="ShieldPage_FocusOnlyCommLabel" Text=""'),
    ('Text="开启后，微信/钉钉/QQ等沟通工具仅在专注模式下被拦截，非专注时间可正常使用。"', 'x:Uid="ShieldPage_FocusOnlyCommHint" Text=""'),
    # ADHD缓冲期
    ('Text="ADHD缓冲期（分钟）"', 'x:Uid="ShieldPage_CooldownLabel" Text=""'),
    ('Content="20（付费）" Tag="20"', 'x:Uid="ShieldPage_Cooldown20" Content="" Tag="20"'),
    ('Content="30（付费）" Tag="30"', 'x:Uid="ShieldPage_Cooldown30" Content="" Tag="30"'),
    ('Text="ADHD模式下专注结束后屏蔽保持的时长，防止一结束就刷手机。免费版可选5/10/15分钟，付费版可选20/30分钟。"', 'x:Uid="ShieldPage_CooldownHint" Text=""'),
    # 添加应用
    ('PlaceholderText="进程名如 Douyin"', 'x:Uid="ShieldPage_AppBox" PlaceholderText=""'),
    ('Content="短视频" IsSelected="True"', 'x:Uid="ShieldPage_AppCatShort" Content="" IsSelected="True"'),
    ('Content="视频娱乐"', 'x:Uid="ShieldPage_AppCatVideo" Content=""'),
    ('Content="社交"', 'x:Uid="ShieldPage_AppCatSocial" Content=""'),
    ('Content="资讯"', 'x:Uid="ShieldPage_AppCatNews" Content=""'),
    ('Content="购物"', 'x:Uid="ShieldPage_AppCatShop" Content=""'),
    ('Content="沟通工具"', 'x:Uid="ShieldPage_AppCatComm" Content=""'),
    ('Content="添加拦截" Click="AddApp_Click"', 'x:Uid="ShieldPage_AddApp" Content="" Click="AddApp_Click"'),
    ('Content="移除" Tag="{Binding Process}"', 'x:Uid="ShieldPage_RemoveApp" Content="" Tag="{Binding Process}"'),
    ('Text="仅「已勾选分类」内的桌面 App 会被拦截；预设：抖音、快手、B站、虎牙、斗鱼、爱奇艺、优酷。可用任务管理器查看某应用的进程名（如抖音是 Douyin）。"', 'x:Uid="ShieldPage_AppListHint" Text=""'),
    # 自定义域名
    ('Header="自定义域名"', 'x:Uid="ShieldPage_CustomDomain" Header=""'),
    ('Text="添加分类名单之外的单站域名（不含 http://）。"', 'x:Uid="ShieldPage_CustomDomainHint" Text=""'),
    ('PlaceholderText="如 example.com"', 'x:Uid="ShieldPage_DomainBox" PlaceholderText=""'),
    ('Content="添加" Click="AddDomain_Click"', 'x:Uid="ShieldPage_AddDomain" Content="" Click="AddDomain_Click"'),
    # 每日限额
    ('Header="每日限额"', 'x:Uid="ShieldPage_DailyLimit" Header=""'),
    ('Text="设定某域名每日最多使用分钟数，超限后自动最小化浏览器窗口（按域名每日去重提醒）。"', 'x:Uid="ShieldPage_DailyLimitHint" Text=""'),
    ('PlaceholderText="域名如 bilibili.com" Width="200"', 'x:Uid="ShieldPage_LimitDomainBox" PlaceholderText="" Width="200"'),
    ('Header="分钟/天"', 'x:Uid="ShieldPage_LimitMinutes" Header=""'),
    ('Content="添加限额" Click="AddLimit_Click"', 'x:Uid="ShieldPage_AddLimit" Content="" Click="AddLimit_Click"'),
    ('Text=" 分钟/天"', 'x:Uid="ShieldPage_LimitUnit" Text=""'),
    ('Content="删除" Tag="{Binding Domain}"', 'x:Uid="ShieldPage_DeleteLimit" Content="" Tag="{Binding Domain}"'),
    # 临时放行
    ('Header="临时放行"', 'x:Uid="ShieldPage_TempAllow" Header=""'),
    ('Text="临时放行某域名 5/15/30 分钟，到期自动恢复屏蔽。适合查资料时短暂放行。"', 'x:Uid="ShieldPage_TempAllowHint" Text=""'),
    ('Content="放行" Padding="16,8"', 'x:Uid="ShieldPage_AllowButton" Content="" Padding="16,8"'),
    ('Content="清除全部放行" Click="ClearAllow_Click"', 'x:Uid="ShieldPage_ClearAllow" Content="" Click="ClearAllow_Click"'),
    # 底部操作栏
    ('Content="保存为场景配置" Click="Apply_Click"', 'x:Uid="ShieldPage_Apply" Content="" Click="Apply_Click"'),
    ('Content="清除配置" Click="Remove_Click"', 'x:Uid="ShieldPage_Clear" Content="" Click="Remove_Click"'),
    ('Content="导出配置" Click="ExportConfig_Click"', 'x:Uid="ShieldPage_Export" Content="" Click="ExportConfig_Click"'),
    ('Content="导入配置" Click="ImportConfig_Click"', 'x:Uid="ShieldPage_Import" Content="" Click="ImportConfig_Click"'),
    ('Content="反馈建议" Click="Feedback_Click"', 'x:Uid="ShieldPage_Feedback" Content="" Click="Feedback_Click"'),
]

count = 0
for old, new in replacements:
    if old in content:
        content = content.replace(old, new, 1)
        count += 1
    else:
        print(f"WARN: not found: {old[:60]}")

with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
    f.write(content)

print(f"Done: {count}/{len(replacements)} replacements")
