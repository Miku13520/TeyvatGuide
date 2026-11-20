# 提瓦特随行

Windows 桌面上的原神攻略跟跑助手。把 B 站或本地攻略视频悬浮在游戏上方，用快捷键控制播放、进度和分集。

## 开始跟跑

1. 双击发布目录里的 `TeyvatGuide.exe`。便携包请先完整解压。在 **攻略播放** 页点封面上的 **启动原神** 即可打开游戏。
2. 在 **攻略播放** 粘贴 B 站分享文字、视频页地址、b23.tv 短链或 BV 号，按回车。也可打开本地 MP4、WebM 等视频。助手分别记住每集和每个本地文件的播放进度。再打开时优先从上次位置开始，首次打开则使用链接里的时间。
3. 拖动视频窗顶栏，把它移到合适的位置。控制台可调节视频窗宽度和不透明度，**复位位置** 可将视频窗移回主屏。
4. 在原神的**无边框全屏**或窗口模式下，按 **Ctrl + 0** 锁定视频窗，让鼠标穿透到游戏并收起 B 站顶栏、进度条和暂停遮罩。再次按下可解锁并恢复播放器控件。
5. 点视频窗右上角 **×** 或控制台右下角 **关闭视频**，会停止声音并收起视频窗。锁定时也可以从控制台关闭。要结束整个程序，点左下角 **退出应用**。

封面会优先使用已保存的游戏路径，也能从米哈游启动器或 HoYoPlay 自动定位原神。首次未识别时，会提示选择国服的 `YuanShen.exe` 或国际服的 `GenshinImpact.exe`。游戏移动后，可在 **设置 → 游戏位置** 更换路径。游戏已运行时点击启动会尝试切回游戏窗口。

### 导入合集与选集

在 B 站打开攻略，点 **分享 → 复制链接**。回到 **攻略播放** 或 **合集选集**，整段粘贴后点 **导入合集**。多 P 视频按 P 号列出分集，视频合集可从其中一集导入，UP 主空间中的公开合集和系列也可直接粘贴详情页地址。空间目录按投稿列出。

例如将这份纳塔 5.2 攻略导入后会看到 20 集。双击第 2 行 **浮羽之湾** 即可播放 9 分 45 秒的第 2 P，也可以选中一行按回车，或用 **上一集 / 下一集** 切换。程序记住最近一次导入的目录和选中集。[纳塔 5.2 攻略](https://www.bilibili.com/video/BV1P6SNYREo8/)

### 默认快捷键

| 操作 | 默认组合键 |
| --- | --- |
| 播放 / 暂停 | Ctrl + 空格 |
| 快退、快进 | Ctrl + ←、Ctrl + → |
| 降低、提高倍速 | Ctrl + ↓、Ctrl + ↑ |
| 上一集、下一集 | Ctrl + Page Up、Ctrl + Page Down |
| 锁定 / 解锁视频窗 | Ctrl + 主键盘数字 `0` |

在游戏中按切集键，会从正在播放的分集切到相邻一集并开始播放。直接打开 B 站视频时，首次切集会自动读取其公开目录。到合集首尾时会停在当前集。

在 **设置 → 快捷键** 点击键位后，按下想用的组合，松开即保存。按 Esc 取消录制。快进和快退默认每次 5 秒，在 **设置 → 跳转步长** 可修改，输入后按回车或移开焦点保存。倍速范围为 0.5× 到 2×。

原神位于前台时，助手同时使用 Windows 热键、原始输入和已绑定键的按下状态。持续按住一个键只触发一次。

### 运行条件

- 目标平台为 64 位 Windows 10 / 11，需要 .NET Framework 4.8 系列和 Edge WebView2 Evergreen 运行时。[微软运行时下载](https://developer.microsoft.com/microsoft-edge/webview2/#download-section)
- 桌面置顶适用于由 Windows 合成的无边框全屏和窗口。使用独占全屏出现遮挡时，请切换到无边框全屏。
- B 站的清晰度、地区和外链播放许可由官方播放器决定。遇到站内提示可解锁视频窗查看。失效的分享短链可换成视频页地址。
- 窗口布局、快捷键、观看记录和合集目录保存在 `%LOCALAPPDATA%\TeyvatGuide\settings.json`。程序用独立的 WebView2 配置播放攻略。

## 实施规划

| 阶段 | 实现内容 | 验收方式 |
| --- | --- | --- |
| 播放与悬浮 | 接入 B 站官方外链播放器，保留分享链接的分 P 和起播时间。视频窗支持拖动、缩放、透明度和鼠标穿透。 | 在无边框全屏窗口上播放，调整布局后锁定，点击可落到下层窗口。 |
| 游戏内控制 | 同时支持 Windows 全局热键和原神前台原始输入，为播放、跳转、倍速、切集和锁定分发一次操作。 | 在禁用普通应用热键的前台测试窗口发送主键盘按键。 |

侧栏提供攻略播放、合集选集和设置三个入口。视频接入使用 B 站官方外链播放器。[B 站播放器](https://player.bilibili.com/)

<details>
<summary>首页插画与生成提示词</summary>

首页插画使用内置 imagegen 生成，源文件位于 `assets/travel-banner.jpg`，发布时嵌入可执行文件。生成提示词如下。

为原神攻略桌面工具绘制 3:1 横幅插画。日系奇幻绘本，暖杏色晨光照亮风车山城和蜿蜒山路，右侧一位原创的短发旅行绘图师披着青蓝短斗篷，手捧摊开的地图，侧脸望向远山。脸、地图和风车都落在画面中间的水平带，适合裁成超宽横幅。左侧三分之一保留安静的暗色山影供界面叠字。细腻赛璐璐线稿，通透水彩色块，无文字、标志或水印。

</details>

## 构建与验证

在项目根目录运行以下命令。构建脚本使用 Windows 的 C# 编译器，首次从 NuGet 下载固定版本 WebView2 SDK 并校验 SHA-256。发布目录为 `dist`。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

检查程序使用项目里的独立测试配置。桌面检查会短暂显示无边框测试窗口，在线检查会播放一段公开的 B 站攻略。游戏启动检查会操作测试窗口自己的文件选择器，并启动一个独立的测试程序。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Checks
.\.checks\bin\GuideChecks.exe --core
.\.checks\bin\GuideChecks.exe --game
.\.checks\bin\GuideChecks.exe --game-keys
.\.checks\bin\GuideChecks.exe --settings
.\.checks\bin\GuideChecks.exe --desktop
.\.checks\bin\GuideChecks.exe --bili
.\.checks\bin\GuideChecks.exe --collections
```

联网检查用公开的 B 站攻略验证播放、锁定后的纯视频画面、合集切集与续播。[风神瞳攻略](https://www.bilibili.com/video/BV18i4y1m7LY/?p=2&t=23) · [纳塔攻略](https://www.bilibili.com/video/BV1P6SNYREo8/?p=2)

播放器在 `src/GuidePlayer.cs` 和 `src/media.js`，视频悬浮窗在 `src/Overlays.cs`，快捷键注册在 `src/Shortcuts.cs`。
