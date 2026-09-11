import io

path = r"D:\github\chanjing\src\ChanJing.App\CompanionPage.cs"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

replacements = [
    ('<html lang=""zh-CN"">', '<html lang=""en"">'),
    ('<title>禅净 · 伴侣</title>', '<title>ZenFocus · Companion</title>'),
    ('<h1>禅 净</h1>', '<h1>Zen Focus</h1>'),
    ('<div class=""label"">专注分钟</div>', '<div class=""label"">Focus Min</div>'),
    ('<div class=""label"">专注次数</div>', '<div class=""label"">Sessions</div>'),
    ('<div class=""label"">分心次数</div>', '<div class=""label"">Distractions</div>'),
    ('<div class=""state-label"" id=""stateLabel"">当前空闲</div>', '<div class=""state-label"" id=""stateLabel"">Idle</div>'),
    ('<div class=""time-label"">已专注</div>', '<div class=""time-label"">Focused</div>'),
    ('开始专注</button>', 'Start Focus</button>'),
    ('结束专注</button>', 'End Focus</button>'),
    ('<h3>今日分心来源</h3>', '<h3>Today\'s Distractions</h3>'),
    ('暂无分心记录', 'No distractions yet'),
    ('<h3>同伴专注（可选 · 非社交）</h3>', '<h3>Peer Focus (optional · non-social)</h3>'),
    ('输入同伴的地址（如 http://192.168.1.5:8765 ），即可看到对方是否在专注。', 'Enter peer\'s address (e.g. http://192.168.1.5:8765) to see if they\'re focusing.'),
    ('保存</button>', 'Save</button>'),
    ('禅净 · 局域网伴侣页 · 手机与电脑需连接同一WiFi', 'ZenFocus · LAN Companion · Phone and PC must be on same WiFi'),
    ("h + '小时' + m + '分' : m + '分钟'", "h > 0 ? h + 'h ' + m + 'm' : m + 'min'"),
    ('连接失败，请检查WiFi', 'Connection failed. Check WiFi.'),
    ("d.count + '次'", "d.count + 'x'"),
    ("stateLabel.textContent = '专注中';", "stateLabel.textContent = 'Focusing';"),
    ("stateLabel.textContent = '当前空闲';", "stateLabel.textContent = 'Idle';"),
    ("showToast('专注已开始');", "showToast('Focus started');"),
    ("data.message || '启动失败'", "data.message || 'Failed to start'"),
    ("showToast('专注已结束');", "showToast('Focus ended');"),
    ("data.message || '停止失败'", "data.message || 'Failed to stop'"),
    ("showToast('请输入同伴地址');", "showToast('Enter peer address');"),
    ("showToast('同伴地址已保存');", "showToast('Peer address saved');"),
    ("el.textContent = '未设置同伴地址';", "el.textContent = 'No peer address set';"),
    ("data.isFocusing ? '同伴正在专注中' : '同伴当前空闲'", "data.isFocusing ? 'Peer is focusing' : 'Peer is idle'"),
    ("el.textContent = '无法连接同伴（检查网络或地址）';", "el.textContent = 'Cannot connect to peer (check network/address)';"),
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
