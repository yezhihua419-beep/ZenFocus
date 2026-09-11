import io

path = r"D:\github\chanjing\src\ChanJing.App\StatsPage.xaml.cs"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

replacements = [
    # 今日定心卡片弹窗
    ('Title = "今日定心卡片",', 'Title = "Today\'s Focus Card",'),
    ('PrimaryButtonText = "保存图片",', 'PrimaryButtonText = "Save Image",'),
    ('CloseButtonText = "关闭",', 'CloseButtonText = "Close",'),
    # 已保存弹窗
    ('Title = "已保存",', 'Title = "Saved",'),
    ('CloseButtonText = "好",', 'CloseButtonText = "OK",'),
    # 保存失败弹窗
    ('Title = "保存失败",', 'Title = "Save Failed",'),
    # 升级到正式版弹窗
    ('Title = "升级到正式版",', 'Title = "Upgrade to Pro",'),
    ('PrimaryButtonText = "输入激活码",', 'PrimaryButtonText = "Enter License Key",'),
    ('CloseButtonText = "稍后再说",', 'CloseButtonText = "Later",'),
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
