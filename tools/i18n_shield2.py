import io

path = r"D:\github\chanjing\src\ChanJing.App\ShieldPage.xaml.cs"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

replacements = [
    # 管理员权限弹窗
    ('Title = "需要管理员权限",', 'Title = "Admin Rights Required",'),
    ('PrimaryButtonText = "以管理员身份重启",', 'PrimaryButtonText = "Restart as Admin",'),
    ('CloseButtonText = "取消",', 'CloseButtonText = "Cancel",'),
    # 确认清除弹窗
    ('Title = "确认清除",', 'Title = "Confirm Clear",'),
    ('Content = "将清除所有已保存的屏蔽分类和自定义域名，此操作不可撤销。确定继续吗？",', 'Content = "This will clear all saved block categories and custom domains. This cannot be undone. Continue?",'),
    ('PrimaryButtonText = "确定清除",', 'PrimaryButtonText = "Confirm Clear",'),
    # 结束进程模式确认弹窗
    ('Title = "结束进程模式",', 'Title = "Kill-Process Mode",'),
    ('Content = "「结束进程」会强制关闭被拦截的应用，未保存的内容可能丢失。\\n\\n确定使用此模式吗？",', 'Content = "Kill-Process will force close blocked apps. Unsaved content may be lost.\\n\\nUse this mode?"'),
    ('PrimaryButtonText = "确定使用",', 'PrimaryButtonText = "Confirm Use",'),
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
