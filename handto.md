# 禅净 handto

## 最终目标
未打包 WinUI 3 下 i18n 可用：默认英文，可切中文，启动不崩；用户可见文案按语言出净；无管理员时界面写清后果。

## Checkpoint
- **v0.9.0**（2026-09-12）：i18n + 双名单 + 体验对齐。单测 150。下一步支付/内测，不在本文件扩 scope。

## 阶段与进展
- [x] 定位本质：误用 `Windows.Globalization.ApplicationLanguages`（需包身份）
- [x] 改用 `Microsoft.Windows.Globalization.ApplicationLanguages`，偏好存 `language.txt`
- [x] 启动级虚拟验证（默认 en-US / 显式 en-US / zh-CN，均启动成功、无 crash.log）
- [x] 语言切换：I18n 走 PRI Resources 子树 + 托盘 SwitchLanguage 重启
- [x] 虚拟验证：en 出 ZenFocus/Start Focus；zh 出禅定/开始专注；SMOKE_RESTART 写文件并重启后变中文
- [x] C# 用户可见文案（对话框/状态/气泡/统计）走 I18n；分类存库仍用中文 key，展示时翻译
- [x] 屏蔽/统计页按钮全部 I18n.SetContent，不再只靠 x:Uid
- [x] 无管理员：首页+屏蔽页常驻提示；开始专注写 hosts 失败时 InfoBar 通知
- [x] 场景预设愿望/应用显示名按语言出；存库仍用中文原文
- [x] 英文默认屏蔽名单走国际站/国际 App（TikTok/YouTube/Discord），中文仍用国内名单

- [x] 英文屏蔽审计：进程别名（DiscordPTB/WhatsAppDesktop/steamwebhelper 等）+ hosts 扩展域 + x.com 标题「 / X」
- [x] 英文目录单测：工作场景/沟通工具/浏览器标题命中与否
- [x] 测试隔离：英文进 Hosts 集合；`PreApplyPathOverride` 不碰用户 hosts.pre
- [x] 英文 Apply 后断言隔离 hosts 含 youtube.com / www.youtube.com
- [x] 未 Apply / EmergencyPass：`CanInterceptApps` 为 false，`ApplyShieldNow` 不拦截

- [x] 体验对齐：场景文案、放行用最近分心、暂离清 hosts、语言确认+收尾、保存不吃额度、回首页不拆缓冲、ADHD 不改 kill
- [x] 英文 Work 加社交；伴侣 JS 修复 + token；托盘单击打开
- [x] 分类以屏蔽页为准（点场景/托盘开始不覆盖勾选）；空名单才按场景默认补一次
- [x] ADHD/深度/当前场景写入 Settings，OnLaunched 恢复
- [x] 自定义域名可单条删除
- [x] 托盘/快捷键结束走破功，不刷连续天数；分享卡英文 Zen；小时图 tooltip i18n；齿轮写热键
- [x] 编码检测第 3 次未专注时切工作愿望/时长（不覆盖屏蔽分类）；首页订阅刷新
- [x] 启动时清崩溃残留系统 hosts（专注中/手动屏蔽不碰）；隔离 hosts 单测
- [x] 屏蔽名单与界面语言脱钩（catalog.txt）；屏蔽页可单独切国内/国际
- [x] 开始按钮旁写死无管理员：标签关不掉，点开始即 InfoBar
- [x] 商店壳（ApplicationFrameHost）按窗口标题最小化；禁止杀壳进程；浏览器标签仍不关

## 当前卡点
无。英文**不能保证**仅浏览器页在无管理员时被拦死（无 hosts 时网站只记分心气泡，不关浏览器）。商店 PWA 仅当窗口标题能对上名单才最小化。

## 下一步
支付/激活上线后再接升级页跳转。

## 禁止再踩的坑
- 启动路径禁止碰 `AppServices.Db`（静态构造会崩）
- 禁止再用 `Windows.Globalization.ApplicationLanguages`
- 不要靠删 manifest 语言包“默认英文”
- 有 x:Uid 的属性不要再写 Content="" / Text=""，本地空值会盖掉资源，按钮变成空圆点
- 场景按钮不要挂 x:Uid：资源系统会在 Loaded 后再盖一层空 Content；用 I18n.SetContent 写文案
- 屏蔽页曾只有 Title/Subtitle 两个 key，其余 x:Uid 全空；按钮必须有 resw + I18n.SetContent，不能只挂 x:Uid
- 分类存库 key 必须保持中文（短视频/社交…），展示用 I18n.CategoryName，勾选保存用 Tag 不要用 Content
- 桌面快捷方式在 `D:\HuaweiMoveData\Users\yezhi\Desktop\`，必须指向 `bin\x64\Debug\...\win-x64\ChanJing.App.exe`
- 构建前先杀 `ChanJing.App`，否则 dll 被锁
- 管理员进程普通 Stop-Process 杀不掉，用 `taskkill /F`
- 桌面 App 拦截是进程名精确/别名匹配；商店壳可按标题拦当前窗，禁止 `Kill(ApplicationFrameHost)`
- 浏览器（chrome/msedge）禁止按标题当桌面 App 杀/最小化整窗
- 网站拦截靠 hosts（需管理员）+ 标题分心提醒；无管理员时浏览器里的 YouTube 等不会被关掉
- `x.com` 主域只有一个字母，标题匹配必须靠别名（`twitter` / ` / x`），不能靠 MainDomain
- 屏蔽页保存不要 `SaveSceneConfig`，否则吃掉免费自定义场景额度
- 切语言必须先 Finish + 清 hosts，再重启
- ADHD 不得偷偷改 `app_block_mode=kill`
- 点场景不要 `SetEnabledCategories` 覆盖屏蔽页；`FocusController.Start` 同样
- UI 偏好用 Settings 恢复，禁止在 App 构造函数里 `FocusContext.Load`
- 托盘/Ctrl+Alt+F 结束必须 `StopEarly`，只有首页「圆满」才能 `Finish(true)`
- 编码第 3 次只改工作愿望/时长，禁止 `SetEnabledCategories`；专注中不要切
- 启动清残留只清系统 hosts 标记段；手动屏蔽或仍在专注时禁止 Remove
- 切界面语言禁止改屏蔽名单；名单只读 `catalog.txt` / 屏蔽页下拉
