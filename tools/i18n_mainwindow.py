#!/usr/bin/env python3
"""把 MainWindow.xaml 中的硬编码中文替换为 x:Uid 绑定。"""
import io

path = r"D:\github\chanjing\src\ChanJing.App\MainWindow.xaml"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

replacements = [
    ('Title="禅净"\n    mc:Ignorable="d"', 'x:Uid="MainWindow" Title=""\n    mc:Ignorable="d"'),
    ('Title="禅净" Grid.Row="0"', 'x:Uid="MainWindow_TitleBar" Title="" Grid.Row="0"'),
    ('Content="禅定" Tag="home"', 'x:Uid="MainWindow_NavHome" Content="" Tag="home"'),
    ('Content="屏蔽" Tag="shield"', 'x:Uid="MainWindow_NavShield" Content="" Tag="shield"'),
    ('Content="统计" Tag="stats"', 'x:Uid="MainWindow_NavStats" Content="" Tag="stats"'),
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
