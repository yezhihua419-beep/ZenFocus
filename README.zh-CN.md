# 禅净 / ZenFocus

[English](README.md)

Windows 桌面专注工具。在这台电脑上屏蔽分心网站和应用，再看清时间去向。数据只留在本机。

**买断 $19 / ¥69**（激活码）。免费版已包含屏蔽 + 专注计时。

## 下载

[最新 Release](https://github.com/yezhihua419-beep/ZenFocus/releases/latest) — 文件名 `ZenFocus-v0.9.4-win-x64.zip`。

1. 解压后**保留整个文件夹**，不要只拷 `ChanJing.App.exe`。
2. 右键 `ChanJing.App.exe` → **以管理员身份运行**（写 hosts 需要）。
3. 未签名可能被 Windows 拦截：选**仍要运行**。

界面默认英文，设置里可切中文。

## 怎么买断

人民币 **¥69**，只付一次，不是订阅。英文 $19 收银台尚未开通。

1. 打开 [爱发电主页](https://afdian.com/a/zenfocus)，点 **「发电」**（不要选按月赞助）
2. 金额填 **69**，只付这一次
3. 付款后把邮箱发到 [yezhihua419@gmail.com](mailto:yezhihua419@gmail.com)（也可抄送 [yezhihua_yzh@163.com](mailto:yezhihua_yzh@163.com)）
4. 我们手发终身激活码（格式 `CJ-xxxx-xxxxxx`）

应用里中文升级按钮打开的是同一页。

## 能力边界（不是 bug）

- 没有管理员时，浏览器里的 YouTube 等标签关不掉，只出分心气泡。
- 商店 / PWA 窗口只有标题对得上名单才会最小化。

## 免费 vs 买断

免费版要能解决核心问题：挡住分心、跑完一次专注。买断解开额外能力。

| | 免费 | 买断（$19 / ¥69） |
|---|---|---|
| 6 类网站 + 桌面应用拦截 | 有 | 有 |
| 专注计时、正计时、呼吸、深度 / ADHD 15 分钟 | 有 | 有 |
| 每日限额、临时放行 5/15/30 分钟、破功冷却 | 有 | 有 |
| 统计（今日 / 7 天 / 热力 / 质量分） | 有 | 有 |
| 自定义域名 | 3 个 | 不限 |
| 自定义场景（时长 / 分类 / 愿望） | 1 个场景 | 全部 4 个 |
| 手机伴侣（局域网扫码） | — | 有 |
| 统计导出（CSV / JSON） | — | 有 |
| ADHD 缓冲 20 / 30 分钟 | — | 有（免费为 5 / 10 / 15） |

灰锁仍然可点：降透明度 + 锁 + 说明。不会把按钮禁用，也不会跳到空支付页。

## 功能

- **屏蔽：** 国内 / 国际两套目录（与界面语言无关）、自定义域名、每日限额、临时放行、手动屏蔽、桌面应用（最小化或结束进程）。商店 PWA 只按标题匹配。
- **专注：** 四个场景（工作 / 写作 / 学习 / 会议）、正计时、今日一愿、呼吸引导、暂停、破功冷却、深度模式、ADHD 短周期。
- **统计：** 本机分钟、连续天数、7 天 / 小时图、分享卡、分心来源。
- **系统：** 托盘、深色模式、快捷键 `Ctrl+Alt+F`（开始/结束）· `P`（暂停）· `R`（休息 3 分钟）· `S`（设置）。
- **伴侣（买断）：** 同一 Wi-Fi 扫码，远程开始 / 提前结束。

点场景只填愿望和时长。屏蔽页勾选不会被覆盖。

## 抗焦虑

正计时，不倒计时。出口够用（放行、暂离、冷却后可走）。话术讲进步，不审判。深度模式不计时，你自己结束。

## 隐私

- 只在本机：`%LOCALAPPDATA%\ChanJing\`（SQLite）。无账号、无云、无遥测、无截图。
- 前台窗口只采进程名 + 标题，用于匹配和统计。不记键盘。
- Release 不写调试 `actions.log`。若崩溃，把该目录下的 `crash.log` 发来。

## 从源码构建

Windows、.NET 8 SDK、x64。先结束正在跑的应用，否则 exe 锁文件构建会失败。

```powershell
taskkill /F /IM ChanJing.App.exe
dotnet test tests\ChanJing.Tests\ChanJing.Tests.csproj
dotnet build src\ChanJing.App\ChanJing.App.csproj -p:Platform=x64
```

自包含目录（仓库根目录需要本机 `license.secret`，见 `license.secret.example`）：

```powershell
powershell -File tools\pack-inner.ps1
```

产物在 `dist\ZenFocus-inner\`。不要不带 `-p:Platform=x64` 去编 `.sln`。

## 联系

激活 / 反馈：[yezhihua419@gmail.com](mailto:yezhihua419@gmail.com) · [yezhihua_yzh@163.com](mailto:yezhihua_yzh@163.com)
