# 禅净 handto

## 最终目标
未打包 WinUI 3：默认英文可切中文，启动不崩；付费入口灰锁可点出说明；直装可发给人。收款链接未定时不跳空页。

## Checkpoint
- **v0.9.0**（2026-09-12）：`b47163b` i18n + 双名单 + 体验对齐。
- **v0.9.1**（2026-09-12）：`f536a6e` 诊断/信任（crash.log、管理员隐私、首启引导、一键诊断）。
- **v0.9.2**（2026-09-12）：`8fd1457` 直装脚本 + md 对齐（灰锁在 `d9a97d3`）。
- **v0.9.3**（2026-09-14）：https://github.com/yezhihua419-beep/ZenFocus/releases/tag/v0.9.3 （zip 约 100MB，不含 `license.secret`）
- **v0.9.4**（2026-09-14）：中文升级跳爱发电；https://github.com/yezhihua419-beep/ZenFocus/releases/tag/v0.9.4

## 阶段与进展
- [x] 未打包 i18n（WASDK `ApplicationLanguages` + `language.txt`）；分类 key 中文、展示翻译
- [x] 国内/国际双名单与界面语言脱钩（`catalog.txt`）
- [x] 无管理员写清后果；hosts 失败 InfoBar；商店壳只按标题最小化
- [x] 点场景 / `FocusController.Start` 不覆盖屏蔽页勾选
- [x] 托盘/Ctrl+Alt+F 结束 = `StopEarly`；只有首页圆满 `Finish(true)`
- [x] 免费灰锁：场景第 2 个 / 域名第 4 个 / 伴侣 / 统计导出 / ADHD 20/30
- [x] 配置导入导出：诊断框里，免费；导入裁到 3 域名
- [x] `tools/pack-inner.ps1` → `dist/ZenFocus-inner/` + `READ_ME.txt`（目录 gitignore）
- [x] W3 真人内测跳过；商业路径：双收银台 + 手发 HMAC 码；发现靠内容不靠商店
- [x] 激活密钥移出仓库（`license.secret` gitignore）；测试用 `LicenseKey.Issue` 现算，不提交能用的样例码
- [x] README / 直装说明 / 升级与反馈写上 yezhihua419@gmail.com 与 yezhihua_yzh@163.com
- [x] 公开仓库 https://github.com/yezhihua419-beep/ZenFocus ；Release 挂直装 zip
- [x] 对外 README 英文；中文见 `README.zh-CN.md`。进度/坑只写本文件，不挂仓库首页
- [x] 中文升级跳转爱发电 `https://afdian.com/a/zenfocus`（自定义 69，不是按月方案）
- [x] GitHub README 中英「怎么买断」与爱发电主页介绍对齐

## 当前卡点
- 中文升级已跳 https://afdian.com/a/zenfocus ；英文仍无收银台（大陆 PayPal/Gumroad 提不出来）。
- 英文**不能保证**仅浏览器页在无管理员时被拦死。商店 PWA 仅当标题对得上才最小化。

## 下一步（要你出面）
1. 爱发电主页确认能「自定义发电 69」；有人付了用 `python tools/gen_license.py` 回码。
2. 发现：AlternativeTo / Reddit（你的号）。微软商店以后再说。
3. 英文 $19 仍无可用下款通道，先别接空页。

我这边可接着做：有人付款后的发码流程（`python tools/gen_license.py`）。

## DPI 回归（本机过一遍）
- 缩放：100% / 125% / 150%
- 看：首页四场景+开始、屏蔽底栏、诊断框导入导出、统计导出灰锁、齿轮
- 期望：不裁字、不叠、底栏不滚出窗

## 禁止再踩的坑
- 启动路径禁止碰 `AppServices.Db`（静态构造会崩）
- 禁止再用 `Windows.Globalization.ApplicationLanguages`
- 不要靠删 manifest 语言包“默认英文”
- 有 x:Uid 的属性不要再写 Content="" / Text=""
- 场景按钮不要挂 x:Uid；用 `I18n.SetContent`
- 分类存库 key 必须中文；勾选保存用 Tag
- 桌面快捷方式必须指向 `bin\x64\Debug\...\win-x64\ChanJing.App.exe`
- 构建前先 `taskkill /F /IM ChanJing.App.exe`
- 商店壳禁止 `Kill(ApplicationFrameHost)`；chrome/msedge 禁止按标题当桌面 App
- 无管理员时浏览器 YouTube 关不掉（只气泡）
- `x.com` 标题靠别名，不能靠 MainDomain
- 屏蔽页保存不要 `SaveSceneConfig`
- 切语言必须先 Finish + 清 hosts，再重启；切语言不改屏蔽名单
- ADHD 不得改 `app_block_mode=kill`
- 点场景不要 `SetEnabledCategories`；`FocusController.Start` 同样
- 托盘结束必须 `StopEarly`
- 诊断只读，禁止为探测改系统 hosts
- 灰锁禁止 `IsEnabled=false`；有收款链接再跳，禁止跳空页
- 配置导入导出不是付费钩子；免费导入裁到 3 域名
- 已自定义场景仍可右键编辑
- ADHD 20/30 选中回退到 10 并出升级框
- 定价表不要写未做的「周报」
- 微软商店不作第一渠道（hosts / 杀进程 / 审核周期）
- 不要用 Polar 在线 License 替换离线 HMAC
- 大陆银行卡走不通 Lemon/Polar 银行入账，先试 PayPal
- 激活密钥禁止进 GitHub；Release 必须有本机 `license.secret`。公开仓库当渠道，不指望防破解
- 仓库首页 README 对外英文；中文只放 `README.zh-CN.md`；不要把 handto/changelog 贴回 README
