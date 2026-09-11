import io

path = r"D:\github\chanjing\src\ChanJing.App\MainPage.xaml.cs"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

replacements = [
    # 场景自定义额度弹窗
    ('PrimaryButtonText = "了解升级",', 'PrimaryButtonText = "Learn More",'),
    ('CloseButtonText = "取消",', 'CloseButtonText = "Cancel",'),
    # 反馈界面
    ('FeedbackAgainButton.Content = "再来 15 分钟";', 'FeedbackAgainButton.Content = "Another 15 min";'),
    # 提前解除屏蔽弹窗
    ('Title = "提前解除屏蔽",', 'Title = "Disable Blocking Early",'),
    ('PrimaryButtonText = "确定解除",', 'PrimaryButtonText = "Confirm Disable",'),
    ('CloseButtonText = "再等等",', 'CloseButtonText = "Wait",'),
    # 暂停按钮
    ('PauseButton.Content = "暂停";', 'PauseButton.Content = "Pause";'),
    ('PauseButton.Content = "继续";', 'PauseButton.Content = "Resume";'),
    # ADHD暂停确认弹窗
    ('Title = "暂停专注",', 'Title = "Pause Focus",'),
    ('Content = "确定要暂停吗？ADHD模式下暂停后容易一去不回，建议直接结束或继续。",', 'Content = "Sure to pause? In ADHD mode, pausing often leads to not coming back. Consider ending or continuing instead.",'),
    ('PrimaryButtonText = "还是暂停",', 'PrimaryButtonText = "Pause Anyway",'),
    ('CloseButtonText = "继续专注",', 'CloseButtonText = "Keep Focusing",'),
    # 没有可放行的网站弹窗
    ('Title = "没有可放行的网站",', 'Title = "No Site to Allow",'),
    ('Content = "当前前台没有正在被屏蔽的网站。先打开那个网站（如 bilibili.com），再点此按钮。",', 'Content = "No blocked site in foreground. Open the site first (e.g. bilibili.com), then tap this button.",'),
    ('CloseButtonText = "知道了",', 'CloseButtonText = "Got it",'),
    # 临时放行弹窗
    ('PrimaryButtonText = "确认放行",', 'PrimaryButtonText = "Confirm Allow",'),
    # 结束专注弹窗
    ('Title = "发生了什么？",', 'Title = "What happened?",'),
    ('PrimaryButtonText = "再定心一会儿",', 'PrimaryButtonText = "Focus More",'),
    ('endButton.Content = "结束";', 'endButton.Content = "End";'),
    # ADHD首次引导
    ('Title = "ADHD友好模式 · 1/3",', 'Title = "ADHD-Friendly Mode · 1/3",'),
    ('Content = "专为注意力容易分散的你设计：\\n\\n【15分钟短周期】\\n降低心理门槛，坐不住也能开始。\\n\\n【结束进程强屏蔽】\\n抖音/B站等分心App会被直接结束（不是最小化），防止手贱点回去。\\n\\n【正反馈鼓励】\\n结束后只夸你完成了多少，不批评你分心了几次。",', 'Content = "Designed for easily distracted minds:\\n\\n[15-min cycles]\\nLow barrier to start, even if you can\'t sit still.\\n\\n[Kill-process blocking]\\nDistraction apps like TikTok/Bilibili are killed (not minimized), preventing impulsive return.\\n\\n[Positive feedback]\\nOnly praise what you completed, never criticize distractions."'),
    ('Title = "ADHD友好模式 · 2/3",', 'Title = "ADHD-Friendly Mode · 2/3",'),
    ('Content = "【缓冲期】\\n专注结束后屏蔽保持10分钟，防止「一结束就刷手机」的条件反射。\\n\\n【软着陆】\\n缓冲期到了不会自动解除屏蔽，你需要主动选择「再来15分钟」或「自由使用」。\\n\\n【暂停有摩擦】\\n暂停时会弹确认，防止「暂停一下就再也不回来了」。",', 'Content = "[Cooldown]\\nBlocking stays on for 10 min after focus ends, preventing the reflex of grabbing phone immediately.\\n\\n[Soft landing]\\nBlocking won\'t auto-disable after cooldown. You actively choose Another 15 min or Free Use.\\n\\n[Pause friction]\\nPause shows confirmation, preventing pausing and never returning."'),
    ('Title = "ADHD友好模式 · 3/3",', 'Title = "ADHD-Friendly Mode · 3/3",'),
    ('Content = "【注意事项】\\n• 结束进程可能丢失未保存内容，请确保重要文件已保存\\n• 屏蔽分类跟随当前场景（工作/写作/学习/会议），可在屏蔽页修改\\n• 可随时在屏蔽页把拦截方式改回「最小化」\\n• ADHD模式与深度模式互斥，不能同时开启\\n\\n准备好了吗？",', 'Content = "[Notes]\\n• Kill-process may lose unsaved work, ensure important files are saved\\n• Block categories follow current scene (Work/Write/Study/Meeting), editable in Block page\\n• Can switch blocking mode back to Minimize in Block page anytime\\n• ADHD and Deep modes are mutually exclusive\\n\\nReady?"'),
    ('PrimaryButtonText = "开始使用",', 'PrimaryButtonText = "Get Started",'),
    ('CloseButtonText = "先关掉",', 'CloseButtonText = "Close",'),
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
