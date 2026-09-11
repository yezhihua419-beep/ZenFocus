#!/usr/bin/env python3
"""把 FrictionOverlay.xaml 中的硬编码中文替换为 x:Uid 绑定。"""
import io

path = r"D:\github\chanjing\src\ChanJing.App\FrictionOverlay.xaml"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

replacements = [
    ('Text="深呼吸，5 秒后再决定"', 'x:Uid="FrictionOverlay_Hint" Text=""'),
    ('Content="继续专注" Click="ContinueFocus_Click"', 'x:Uid="FrictionOverlay_Continue" Content="" Click="ContinueFocus_Click"'),
    ('Content="我就要分心"', 'x:Uid="FrictionOverlay_GiveIn" Content=""'),
    ('Text="临时放行 5 分钟" Click="GiveIn5Min_Click"', 'x:Uid="FrictionOverlay_Allow5" Text="" Click="GiveIn5Min_Click"'),
    ('Text="临时放行 15 分钟" Click="GiveIn15Min_Click"', 'x:Uid="FrictionOverlay_Allow15" Text="" Click="GiveIn15Min_Click"'),
    ('Text="临时放行 30 分钟" Click="GiveIn30Min_Click"', 'x:Uid="FrictionOverlay_Allow30" Text="" Click="GiveIn30Min_Click"'),
    ('Text="本次专注期间都放行" Click="GiveInSession_Click"', 'x:Uid="FrictionOverlay_AllowSession" Text="" Click="GiveInSession_Click"'),
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
