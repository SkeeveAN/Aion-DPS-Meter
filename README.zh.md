🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 **中文** · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)

# Aion DPS Meter

**AION 2 免费 DPS 统计器** —— 实时显示全队伤害与治疗，提供可点击穿透的悬浮窗、首领排行榜、宠物与采集地图、活动时间表和世界首领计时器。适用于 Windows，开源，绝不读取游戏内存。

<p align="center">
  <a href="../../releases/latest"><b>⬇ 下载 Windows 版</b></a> · <a href="https://aiondps.com">网站</a> · <a href="https://aiondps.com/features">全部功能</a> · <a href="https://aiondps.com/instances">首领排行榜</a>
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/SkeeveAN/Aion-DPS-Meter?label=release" alt="release"></a>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/SkeeveAN/Aion-DPS-Meter/total?label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" alt="Windows">
</p>

<p align="center">
  <img src="Web-Frontend/images/features/meter@2x.webp" width="440" alt="统计窗口：每位玩家都有职业图标、DPS、总伤害和占比条">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="320" alt="游戏画面上方以小标签呈现的可穿透点击悬浮窗">
</p>

> 下方图片中的名称、玩家和数字均为演示数据。

## 为什么选择它

- 实时显示队伍或团队中每位玩家的 DPS、iDPS 和 HPS
- 点击可穿透的悬浮窗，不会拦截任何点击
- 技能明细，含暴击率和 DPS 曲线
- aiondps.com 上的首领排行榜与公开角色档案（上传为可选，默认关闭）
- 互动地图、活动时间表、世界首领计时器和网络悬浮窗
- 安全：被动抓包，不读取内存，不注入代码
- 免费、开源（MIT），可自动更新

适用于 AION 2 全球版——即 NCSOFT 于 2026 年 10 月 5 日开服的所有地区。韩国和台湾的版本领先数个版本，因此那里的技能识别可能不完整。

适用于 **Aion 2** 的伤害/治疗统计工具和工具箱。它通过 [Npcap](https://npcap.com) 驱动在您的电脑上读取游戏自身的网络流量——纯被动：
从不发送数据包，也不触碰游戏进程及其内存。除非您主动上传（见[上传](#上传)），否则您的游戏数据不会离开电脑；
另外会发送的只有更新检查，即询问 GitHub 是否有新版本，可以关闭；见[更新](#更新)。


## 目录

[安装](#安装) · [功能介绍](#功能介绍) · [使用方法](#使用方法) · [快捷键](#快捷键) ·
[上传](#上传) · [更新](#更新) · [语言](#语言) · [从源码构建](#从源码构建)

## 安装

1. 安装 [Npcap](https://npcap.com) 驱动（工具需要它才能看到游戏流量；它不包含在安装程序中）。
2. 从[最新发布](../../releases/latest)下载 `AionDpsMeter-win-Setup.exe` 并运行。
   它安装到您的用户目录并启动工具——无需管理员权限，也无需 .NET。
3. 启动工具，然后用您的角色登录。工具会从游戏本身读取角色、装备、技能和
   Daevanion 面板；所在服务器会被自动识别。

## 功能介绍

### 所有人的实时伤害与治疗

您队伍或团队中的每位玩家都会显示名称、总伤害、DPS 和占比条；阵营和职业以图标显示。可按职业、队伍或目标过滤。**Mob/Boss** 过滤器在整体 DPS 与按目标计算的真实 **iDPS** 之间切换；
首领由游戏自身数据识别并按名称显示。**PVE/PVP** 开关将两者分开，首领战团灭会有单独的一条记录（骷髅图标表示该次尝试之后首领恢复了满血）。

### 五种视图，一键切换

**伤害**、**治疗**、**承受伤害**和两种**综合**视图。用快捷键切换
（默认 **Ctrl+Alt+M**），或点击悬浮窗中的模式标签。*综合*并排显示承受伤害、
治疗和总计，*All in One Compact* 保留熟悉的 DPS 和总计列，并把
治疗和承受伤害放在名称下方的小号第二行。

<p align="center">
  <img src="Web-Frontend/images/features/mode-all@2x.webp" width="460" alt="综合视图：承受伤害、治疗和总伤害并排显示">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="380" alt="游戏画面上方以小标签呈现的可穿透点击悬浮窗">
</p>

### 可穿透点击的悬浮窗

**隐藏界面**（Hide UI）把窗口变成小标签（或紧凑面板），悬浮在 Aion 2 上方，
不会吞掉任何一次点击。不透明度、伤害占比条、首领血量和每位玩家的承受伤害均可作为选项。悬浮窗只在游戏位于前台时绘制。可随时随地用快捷键切换。

### 每位玩家的技能明细

**双击**玩家：显示每个技能的命中数、暴击率、总计、最小值、最大值和平均值，
按目标拆分，并附有整场战斗的 DPS 曲线。暴击率直接取自服务器的命中数据，因此是精确的。

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="单个玩家的技能明细及伤害随时间变化曲线">
</p>

### 技能循环、增益效果与硬直

同一窗口还有两个标签页。**技能循环**会把玩家使用过的每个技能，按使用的时刻绘制在时间轴上：辅助技能在上方的轨道，伤害技能在下方，紧接在另一个技能之后穿插的技能则低一行，因此一连串的穿插看起来像一道阶梯。背景中是玩家的 DPS（最近五秒的平均值）以及怪物陷入硬直的时刻。拖动可在战斗中移动，**Ctrl + 滚轮**可缩放，将鼠标悬停在图标上可查看该次使用（何时、共几次中的第几次、距上一次的时间、当时的 DPS）。下方是**起手**（按顺序排列的最初几个技能）和*技能循环概览*：使用次数、每分钟、间隔（两次使用之间的时间）、首次以及伤害占比。**增益效果**列出玩家身上的每个效果及其持续时间、其中来自玩家自身的部分、施加的次数以及施加者；两名玩家同时维持的效果只计一次。

战斗会把这些内容保存在本地历史记录中，因此可以重新查看旧的首领；用旧版本录制的战斗不显示技能循环。

<p align="center">
  <img src="Web-Frontend/images/features/rotation@2x.webp" width="640" alt="玩家窗口的技能循环标签页：时间轴上的技能，背景为 DPS，以及起手和技能循环概览">
</p>

### 一眼看清您的整个角色

**角色**窗口（人形图标）显示游戏发送的关于您自己的数据：含物品等级与
强化的装备（包括 +15 以上的增幅阶段）、奥术、种族知识、含等级的所有技能，
以及完整的 Daevanion 面板。它会保留您上次登录的数据，因此从不为空。将鼠标悬停在装备上可查看
详情（魔石、随机属性、神石）。

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="角色窗口：装备、物品等级和强化">
</p>

### 带有宠物和采集点的互动地图

地图悬浮窗显示您的位置、您想找的宠物的刷新点，以及您开启的采集点——草药、矿石、木材、宝石等——
无需切换到网站。只列出您自己阵营可用的内容，游戏关闭期间地图上仍保留最后已知的位置。宠物
设置列出维特隆、阿尔特加德和深渊的每个区域及其宠物和您自己的宠物等级，另有
**稀有**标签页，列出刷新点很少的宠物。边缘箭头指向最近的宠物或采集物。

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="带有宠物刷新点和宠物列表的互动地图">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="采集设置：开启要在地图上显示的内容">
</p>

### 带提醒的活动时间表

悬浮窗列出接下来的活动——战场、深渊攻城战（神器攻城战）、舒古节、
贝里特拉空袭、时空裂缝等。可为每个活动选择声音和音量，并在开始前
几分钟收到提醒。一个开关即可开启或关闭所有提醒。

<p align="center">
  <img src="Web-Frontend/images/features/timetable@2x.webp" width="380" alt="时间表设置：每个活动可设置声音和音量">
</p>

### 悬浮窗上的世界首领

开启某个野外首领，即可在悬浮窗上看到它的计时器，并在它刷新的瞬间听到提示音。时间
直接来自游戏；服务器会发送您所在地图的时间，其他地图的时间则在您去过之后
显示。声音和音量按首领设置，并按区域分组。

<p align="center">
  <img src="Web-Frontend/images/features/worldboss@2x.webp" width="380" alt="世界首领设置：按地图列出每个野外首领的刷新时间，可开关并设置声音和音量">
</p>

### 网络悬浮窗

延迟、抖动、流量、丢包以及您已在线的时长，均根据您的电脑
已经收到的流量测得。随着连接变差，数值会由绿变红。可选择您想要的行，
锁定位置，并用快捷键开启或关闭。

<p align="center">
  <img src="Web-Frontend/images/features/network-overlay@2x.webp" width="340" alt="网络悬浮窗：延迟、抖动、流量和丢包">
</p>

### 战斗悬浮窗

一个透明框架，最多包含三个元素，每个元素都可以单独摆放和调整大小：

- **Boss 施法条：** Boss 一开始施放技能，你就能看到技能名称、随剩余时间缩短的进度条，以及目标是谁（你显示为红色）。击中过多名玩家的技能会标记为**范围**；**致命**表示你目前受到的该技能最高一击不低于你当前的生命值。计量器会学习每个技能（是否为范围技能、伤害何时生效），并在多次会话之间记住。
- **目标的目标：** 如果你用 Tab 选中的目标本身正瞄准某人，你会看到是谁：Boss 显示图片和名称，玩家显示职业徽章和名称。
- **治疗者目标**（可选）：所选目标的生命条，以及其身上的减益（或增益）。

在**设置 → 战斗悬浮窗**中解锁悬浮窗以进行布置：拖动元素主体可移动，拖动边缘可调整大小，拖动角落可缩放；整个框架右下角的手柄可同时缩放所有元素。锁定后它可被点击穿透，并且仅在游戏位于前台时显示。**Ctrl+Alt+C** 可开关它，*所有悬浮窗 开 / 关*热键同样如此。

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="战斗悬浮窗：两个带时间条的 Boss 施法、目标的目标，以及显示生命与减益的治疗者目标">
</p>

### 公告与 Twitch 掉落

喇叭按钮会在 aiondps.com 上打开 NCSoft 官方公告——维护、故障、新
服务器、服务器转移、补偿——最新的在前，每种类型有彩色标签和过滤器。当
AION 2 Twitch 掉落活动进行时，Twitch 按钮会打开一个页面，显示奖励、需要观看的
时长、倒计时，以及当前已开启掉落的直播。当有
比您上次访问更新的内容时，会显示*新*标记。

<p align="center">
  <img src="Web-Frontend/images/features/announcements@2x.webp" width="430" alt="带类型过滤器的 AION 2 官方公告">
  <img src="Web-Frontend/images/features/twitchdrops@2x.webp" width="430" alt="带奖励和直播的 Twitch 掉落活动">
</p>

### 战斗历史

战斗会在本地记录，方便您再次查看之前的首领战（可在
**设置 → 界面**中关闭）。脱离战斗一段可配置的时间后，统计会重置。

## 使用方法

工具一运行就开始记录。**暂停是丢弃而非延后**：暂停期间的
事件会永久丢失，恢复时绝不会补录您没参与的战斗。

- **Copy** 把一行可直接发到聊天的排名放入剪贴板（`名称 1.234.567 (890), …`）；**Copy All**
  给出适用于 Discord 的 Markdown 表格。
- 外观在**设置 → 界面**中设置：主题（深色/浅色）、字体大小、语言、悬浮窗样式
  和不透明度、随 Windows 启动。

<p align="center">
  <img src="Web-Frontend/images/features/settings@2x.webp" width="340" alt="设置：界面、悬浮窗和行为">
  <img src="Web-Frontend/images/features/hotkeys@2x.webp" width="340" alt="设置：快捷键">
</p>

## 快捷键

在任何位置都有效，包括游戏处于焦点时。在**设置 → 快捷键**中点击一个输入框，按下
您想要的组合键（需要包含 Ctrl、Alt、Shift 或 Win）；点击 **×** 可删除。它们取代了聊天命令，
而聊天命令无法工作，因为无法从网络流量中读取 Aion 2 的聊天。

| 默认 | 操作 |
|---|---|
| **Ctrl+Alt+H** | 悬浮窗（隐藏窗口，显示标签） |
| **Ctrl+Alt+P** | 暂停 / 继续 |
| **Ctrl+Alt+D** | 复制伤害排名 |
| **Ctrl+Alt+Shift+X** | 清空本次记录 |
| **Ctrl+Alt+U** | 上传首领伤害 |
| **Ctrl+Alt+M** | 下一个模式（五种视图） |
| **Ctrl+Alt+T** | 时间表悬浮窗 |
| **Ctrl+Alt+K** | 互动地图 开 / 关 |
| **Ctrl+Alt+E** / **Ctrl+Alt+G** | 宠物 / 采集物的边缘箭头 |
| **Ctrl+Alt+B** | 世界首领悬浮窗 |
| **Ctrl+Alt+N** | 网络悬浮窗 |
| **Ctrl+Alt+C** | 战斗悬浮窗 |
| **Ctrl+Alt+O** | 快捷键浮层（可移动的快捷键列表） |
| **Ctrl+Alt+A** | 所有悬浮窗 开 / 关 |

## 上传

- **首领战斗**在您点击上传（工具栏按钮、会话菜单或 **Ctrl+Alt+U**）时上传：
  首领、参战玩家、伤害、治疗、承受伤害和技能。仅接受游戏宣布且目录已知的首领。自动上传可用，但**默认关闭**。
- **您自己的角色资料**（名称、职业、等级、装备、技能、Daevanion、军团、服务器、阵营）可
  在登录后几秒自动上传，以便在网站上被找到。默认**关闭**；可在**设置**中开启。
- 不上传时，任何数据都不会离开您的电脑。

## 更新

工具会自行更新。它在启动时以及运行期间每五分钟询问 GitHub 是否有新版本，
在后台下载，并在下次启动时替换——无需安装程序，也没有 UAC
提示。更新就绪时，状态栏会出现一行绿色文字（透明悬浮窗上会出现一个徽标）；
点击它即可选择立即重启。**应用 → 检查更新**可按需执行同样操作。
旧版本在新版本发布后仍可继续上传 72 小时，之后必须更新。

检查只读取一个 URL，除请求本身外不发送任何内容：

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

可在**设置 → 更新**中关闭；菜单项在关闭后仍可使用。

## 语言

英语、德语、法语、西班牙语、俄语、中文（简体和繁体）、葡萄牙语（巴西）、
日语和韩语——在**设置 → 界面 → 语言**中设置。本 README 另有以下语言版本：
[Deutsch](README.de.md)、[Español](README.es.md)、[Français](README.fr.md)、[Русский](README.ru.md)、
[中文](README.zh.md)、[繁體中文](README.zh-TW.md)、[Português (Brasil)](README.pt.md)、
[日本語](README.ja.md) 和 [한국어](README.ko.md)。

## 从源码构建

```
cd Client
dotnet build
dotnet run -- selftest                              # 自检（协议、抓包、解码……）
dotnet run -- aion2-record <out.jsonl>              # 录制游戏流量（输入 "stop" 结束）
dotnet run -- aion2-replay <file.jsonl>             # 用真实解码器回放录制
dotnet run -- aion2-upload-dryrun <file.jsonl>      # 构建录制的上传内容，不发送任何东西
```

仅限 Windows（WPF）。`Tools/aion2-dat` 读取游戏的文本表（八种语言的名称）；见其 README。

## 关于服务器规则的说明

本工具只被动观察游戏自身的网络流量。尽管如此，运营方会对第三方工具制定自己的规则——使用前请先查看游戏的条款。

## 免责声明与许可证

Aion 2 及所有相关名称、文本和美术资源均为 NCSOFT 的财产。本项目与 NCSOFT 无关联，也未获得 NCSOFT 的支持或认可；游戏内容归其所有者所有。源代码以 [MIT 许可证](LICENSE) 发布——该许可证仅适用于代码，不适用于游戏内容。
