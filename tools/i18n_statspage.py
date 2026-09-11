#!/usr/bin/env python3
"""把 StatsPage.xaml 中的硬编码中文替换为 x:Uid 绑定。"""
import io

path = r"D:\github\chanjing\src\ChanJing.App\StatsPage.xaml"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

replacements = [
    # 标题
    ('Text="统计" FontSize="26"', 'x:Uid="StatsPage_Title" Text="" FontSize="26"'),
    ('Content="导出数据" Click="ExportData_Click"', 'x:Uid="StatsPage_Export" Content="" Click="ExportData_Click"'),
    # 洞察
    ('Text="今日已专注 0 分钟 · 每一次定心都是进步"', 'x:Uid="StatsPage_InsightDefault" Text=""'),
    # 核心数字
    ('Text="今日定心（次）"', 'x:Uid="StatsPage_TodayCountLabel" Text=""'),
    ('Text="今日专注（分）"', 'x:Uid="StatsPage_TodayMinutesLabel" Text=""'),
    ('Text="连续定心（天）"', 'x:Uid="StatsPage_StreakLabel" Text=""'),
    ('Text="起身活动（次）"', 'x:Uid="StatsPage_DistractionLabel" Text=""'),
    ('Text="专注质量（分）"', 'x:Uid="StatsPage_QualityLabel" Text=""'),
    ('Content="生成今日卡片" Click="ShareCard_Click"', 'x:Uid="StatsPage_ShareCard" Content="" Click="ShareCard_Click"'),
    # 付费预览
    ('Text="升级解锁更多洞察" FontSize="14"', 'x:Uid="StatsPage_UpgradeTitle" Text="" FontSize="14"'),
    ('Text="付费版：最专注时段分析 · 分心模式识别 · 跨设备同步屏蔽 · 无限自定义场景 · 手机伴侣 · 数据导出"', 'x:Uid="StatsPage_UpgradeDesc" Text=""'),
    ('Content="立即升级 ¥68 终身买断" Click="Upgrade_Click"', 'x:Uid="StatsPage_UpgradeButton" Content="" Click="Upgrade_Click"'),
    # 高效时段
    ('Text="最高效时段（近30天）"', 'x:Uid="StatsPage_PeakHourTitle" Text=""'),
    # 连续纪录
    ('Text="连续纪录" FontSize="13"', 'x:Uid="StatsPage_StreakHistoryTitle" Text="" FontSize="13"'),
    # 空状态
    ('Text="还没有专注记录。回到「禅定」页点击开始专注，完成后这里会显示近 7 天、24 小时分布、本月热力和今日使用分布。"', 'x:Uid="StatsPage_EmptyHint" Text=""'),
    # 详细图表
    ('Text="查看详细图表" FontSize="13"', 'x:Uid="StatsPage_ChartExpander" Text="" FontSize="13"'),
    ('Text="近 7 天专注（分钟）"', 'x:Uid="StatsPage_WeekTitle" Text=""'),
    ('Text="今日 24 小时专注分布"', 'x:Uid="StatsPage_HourTitle" Text=""'),
    ('Text="本月专注热力"', 'x:Uid="StatsPage_MonthTitle" Text=""'),
    ('Text="分心来源 TOP3（近7天）"', 'x:Uid="StatsPage_DistractionSourcesTitle" Text=""'),
    ('Text="今日使用分布"', 'x:Uid="StatsPage_UsageTitle" Text=""'),
    # 分享卡片
    ('Text="定" FontSize="96"', 'x:Uid="StatsPage_ShareZenChar" Text="" FontSize="96"'),
    ('Text="禅净 · 今日定心" FontSize="14"', 'x:Uid="StatsPage_ShareTitle" Text="" FontSize="14"'),
    ('Text="次定心" FontSize="12"', 'x:Uid="StatsPage_ShareCountUnit" Text="" FontSize="12"'),
    ('Text="分钟定心" FontSize="12"', 'x:Uid="StatsPage_ShareMinutesUnit" Text="" FontSize="12"'),
    ('Text="连续定心天数" FontSize="12"', 'x:Uid="StatsPage_ShareStreakUnit" Text="" FontSize="12"'),
    ('Text="定心质量分" FontSize="12"', 'x:Uid="StatsPage_ShareQualityUnit" Text="" FontSize="12"'),
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
