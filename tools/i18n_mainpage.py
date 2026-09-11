#!/usr/bin/env python3
"""把 MainPage.xaml 中的硬编码中文替换为 x:Uid 绑定。"""
import io
import re

path = r"D:\github\chanjing\src\ChanJing.App\MainPage.xaml"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

# 替换规则：(匹配文本, 控件x:Name或位置, Uid前缀, 属性名)
# 对于有 x:Name 的控件，直接加 x:Uid
replacements = [
    # 头部标题
    ('Text="禅定" FontSize="24"', 'x:Uid="MainPage_Title" Text="" FontSize="24"'),
    # 设置按钮 tooltip
    ('ToolTipService.ToolTip="模式设置"', 'ToolTipService.ToolTip="" x:Uid="MainPage_SettingsButton"'),
    # 模式设置标题
    ('Text="模式设置" FontSize="14" FontWeight="SemiBold"', 'x:Uid="MainPage_SettingsTitle" Text="" FontSize="14" FontWeight="SemiBold"'),
    # 深度模式开关
    ('Header="深度模式（不计时，随心而定）" OffContent="关" OnContent="开"', 'x:Uid="MainPage_DeepModeSwitch" Header="" OffContent="" OnContent=""'),
    # ADHD模式开关
    ('Header="ADHD友好模式（15分钟短周期·视觉化时间）" OffContent="关" OnContent="开"', 'x:Uid="MainPage_AdhdModeSwitch" Header="" OffContent="" OnContent=""'),
    # 互斥提示
    ('Text="深度与ADHD互斥，只能开启一个。"', 'x:Uid="MainPage_ModeMutexHint" Text=""'),
    # 场景按钮
    ('Content="工作" Tag="work"', 'x:Uid="MainPage_SceneWork" Content="" Tag="work"'),
    ('Content="写作" Tag="write"', 'x:Uid="MainPage_SceneWrite" Content="" Tag="write"'),
    ('Content="学习" Tag="study"', 'x:Uid="MainPage_SceneStudy" Content="" Tag="study"'),
    ('Content="会议" Tag="meeting"', 'x:Uid="MainPage_SceneMeeting" Content="" Tag="meeting"'),
    # 场景提示
    ('Text="选择场景一键进入 · 右键可自定义"', 'x:Uid="MainPage_SceneConfigHint" Text=""'),
    # 引导浮层
    ('Text="欢迎使用禅净 · 三步上手"', 'x:Uid="MainPage_GuideTitle" Text=""'),
    ('Text="① 写下「今日一愿」，点开始定心（或直接点上方场景快捷进入）"', 'x:Uid="MainPage_GuideStep1" Text=""'),
    ('Text="② 到「屏蔽」页勾选想拦的网站/应用，开始专注时自动生效"', 'x:Uid="MainPage_GuideStep2" Text=""'),
    ('Text="③ 到「统计」页回看定心数据；关窗口只是最小化到托盘，不会退出"', 'x:Uid="MainPage_GuideStep3" Text=""'),
    ('Content="开始使用" Click="GuideClose_Click"', 'x:Uid="MainPage_GuideClose" Content="" Click="GuideClose_Click"'),
    # 空闲态
    ('Text="今日一愿" FontSize="12"', 'x:Uid="MainPage_WishLabel" Text="" FontSize="12"'),
    ('PlaceholderText="此刻，你最想完成的一件事…"', 'x:Uid="MainPage_WishBox" PlaceholderText=""'),
    ('Content="开始专注" Click="StartButton_Click"', 'x:Uid="MainPage_StartButton" Content="" Click="StartButton_Click"'),
    ('Text="默认定心 25 分钟 · 正计时 · 心无旁骛"', 'x:Uid="MainPage_SessionHint" Text=""'),
    # 呼吸态
    ('Text="吸气…"', 'x:Uid="MainPage_BreathIn" Text=""'),
    ('Text="跟随呼吸，心先静下来"', 'x:Uid="MainPage_BreathHint" Text=""'),
    ('Content="跳过" Click="SkipBreath_Click"', 'x:Uid="MainPage_SkipBreath" Content="" Click="SkipBreath_Click"'),
    # 专注态
    ('Text="心已定" FontSize="14"', 'x:Uid="MainPage_Focusing" Text="" FontSize="14"'),
    ('Content="圆满结束" Click="Complete_Click"', 'x:Uid="MainPage_Complete" Content="" Click="Complete_Click"'),
    ('Content="放下" Click="Break_Click"', 'x:Uid="MainPage_Break" Content="" Click="Break_Click"'),
    ('Content="暂停" Click="PauseToggle_Click"', 'x:Uid="MainPage_Pause" Content="" Click="PauseToggle_Click"'),
    ('Content="休息5分钟" Click="EmergencyPass_Click"', 'x:Uid="MainPage_Rest5" Content="" Click="EmergencyPass_Click"'),
    ('Content="临时放行当前网站"', 'x:Uid="MainPage_AllowCurrent" Content=""'),
    ('Text="放行 5 分钟" Tag="5"', 'x:Uid="MainPage_Allow5" Text="" Tag="5"'),
    ('Text="放行 15 分钟" Tag="15"', 'x:Uid="MainPage_Allow15" Text="" Tag="15"'),
    ('Text="放行 30 分钟" Tag="30"', 'x:Uid="MainPage_Allow30" Text="" Tag="30"'),
    # 反馈态
    ('Content="再来 15 分钟" Click="StartAgain_Click"', 'x:Uid="MainPage_FeedbackAgain" Content="" Click="StartAgain_Click"'),
    ('Content="先休息一下" Click="FeedbackRest_Click"', 'x:Uid="MainPage_FeedbackRest" Content="" Click="FeedbackRest_Click"'),
    # 缓冲期
    ('Text="🧘 缓冲期"', 'x:Uid="MainPage_CooldownTitle" Text=""'),
    ('Text="可以喝水、伸展、远眺\\n暂时不能刷抖音哦"', 'x:Uid="MainPage_CooldownHint" Text=""'),
    ('Content="再来 15 分钟" Click="CooldownAgain_Click"', 'x:Uid="MainPage_CooldownAgain" Content="" Click="CooldownAgain_Click"'),
    ('Content="提前解除屏蔽" Click="CooldownRelease_Click"', 'x:Uid="MainPage_CooldownRelease" Content="" Click="CooldownRelease_Click"'),
    # 软着陆
    ('Text="🌿 缓冲期结束"', 'x:Uid="MainPage_SoftLandingTitle" Text=""'),
    ('Content="再来 15 分钟" Click="SoftLandingAgain_Click"', 'x:Uid="MainPage_SoftLandingAgain" Content="" Click="SoftLandingAgain_Click"'),
    ('Content="我想自由使用" Click="SoftLandingRelease_Click"', 'x:Uid="MainPage_SoftLandingRelease" Content="" Click="SoftLandingRelease_Click"'),
    ('Text="（解除屏蔽，返回首页）"', 'x:Uid="MainPage_SoftLandingHint" Text=""'),
    # 底部统计
    ('Text="今日定心（次）"', 'x:Uid="MainPage_TodayCountLabel" Text=""'),
    ('Text="今日专注（分）"', 'x:Uid="MainPage_TodayMinutesLabel" Text=""'),
    ('Text="屏蔽规则"', 'x:Uid="MainPage_BlockStatusLabel" Text=""'),
    # 手机伴侣
    ('Text="手机扫码连接" FontSize="13"', 'x:Uid="MainPage_CompanionTitle" Text="" FontSize="13"'),
    ('Text="手机和电脑需在同一WiFi下，手机浏览器扫码，查看专注统计、远程开始/结束专注"', 'x:Uid="MainPage_CompanionDesc" Text=""'),
    ('Text="手机伴侣为付费功能 · ¥68 买断后扫码连接"', 'x:Uid="MainPage_CompanionUpgrade" Text=""'),
]

count = 0
for old, new in replacements:
    if old in content:
        content = content.replace(old, new, 1)
        count += 1
    else:
        print(f"WARN: not found: {old[:50]}")

with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
    f.write(content)

print(f"Done: {count}/{len(replacements)} replacements")
