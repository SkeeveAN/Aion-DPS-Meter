🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 **繁體中文** · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)

# Aion DPS Meter

**AION 2 免費 DPS 統計器** —— 即時顯示全隊傷害與治療，提供可點擊穿透的懸浮視窗、首領排行榜、寵物與採集地圖、活動時間表和世界首領計時器。適用於 Windows，開源，絕不讀取遊戲記憶體。

<p align="center">
  <a href="../../releases/latest"><b>⬇ 下載 Windows 版</b></a> · <a href="https://aiondps.com">網站</a> · <a href="https://aiondps.com/features">全部功能</a> · <a href="https://aiondps.com/instances">首領排行榜</a>
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/SkeeveAN/Aion-DPS-Meter?label=release" alt="release"></a>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/SkeeveAN/Aion-DPS-Meter/total?label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" alt="Windows">
</p>

<p align="center">
  <img src="Web-Frontend/images/features/meter@2x.webp" width="440" alt="統計器視窗：每位玩家的職業圖示、DPS、總傷害與佔比長條">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="320" alt="可點擊穿透的浮動層，以小型標籤顯示在遊戲畫面上方">
</p>

> 以下圖片中的名稱、玩家與數字皆為示範資料。

## 為什麼選擇它

- 即時顯示隊伍或團隊中每位玩家的 DPS、iDPS 和 HPS
- 點擊可穿透的懸浮視窗，不會攔截任何點擊
- 技能明細，含暴擊率和 DPS 曲線
- aiondps.com 上的首領排行榜與公開角色檔案（上傳為選用，預設關閉）
- 互動地圖、活動時間表、世界首領計時器和網路懸浮視窗
- 安全：被動擷取封包，不讀取記憶體，不注入程式碼
- 免費、開源（MIT），可自動更新

適用於 AION 2 全球版——即 NCSOFT 於 2026 年 10 月 5 日上線的所有地區。韓國和台灣的版本領先數個版本，因此那裡的技能辨識可能不完整。

這是一款適用於 **Aion 2** 的傷害／治療統計器與工具集。它透過 [Npcap](https://npcap.com) 驅動程式讀取您電腦上遊戲本身的網路流量——完全被動：它絕不會送出任何封包，也不會碰觸遊戲處理程序或其記憶體。除非您自行上傳，否則關於您遊玩的任何資料都不會離開您的電腦（參見[上傳](#上傳)）；它送出的另一件事是更新檢查，會向 GitHub 詢問是否有較新的版本，且可以關閉；參見[更新](#更新)。


## 目錄

[安裝](#安裝) · [功能介紹](#功能介紹) · [使用方式](#使用方式) · [快速鍵](#快速鍵) ·
[上傳](#上傳) · [更新](#更新) · [語言](#語言) · [從原始碼建置](#從原始碼建置)

## 安裝

1. 安裝 [Npcap](https://npcap.com) 驅動程式（統計器需要它才能看到遊戲的流量；它不包含在安裝程式中）。
2. 從[最新版本](../../releases/latest)下載 `AionDpsMeter-win-Setup.exe` 並執行。
   它會安裝到您的使用者設定檔中並啟動統計器——不需要系統管理員權限，也不需要 .NET。
3. 啟動統計器，然後以您的角色登入。統計器會從遊戲本身讀取您的角色、裝備、技能與
   Daevanion 面板；您所在的伺服器會被自動偵測。

## 功能介紹

### 所有人的即時傷害與治療

您隊伍或團隊中的每位玩家都會顯示名稱、總傷害、DPS 與佔比長條；陣營與職業以圖示顯示。可依職業、隊伍或目標篩選。**小怪/首領**篩選可在整體 DPS 與真正的逐目標 **iDPS** 之間切換；首領會依遊戲本身的資料辨識並以名稱顯示。**PVE/PVP** 切換開關將兩者分開，而對首領的團滅會有獨立的一筆紀錄（骷髏圖示代表該次嘗試後首領回到了滿血）。

### 五種檢視，一鍵切換

**傷害**、**治療**、**承受傷害**，以及兩種 **All in One** 檢視。使用快速鍵（預設 **Ctrl+Alt+M**）或點擊浮動層中的模式標籤即可切換。*All in One* 會並排顯示承受傷害、治療與總計，*All in One Compact* 則保留熟悉的 DPS 與總計欄位，並把治療與承受傷害放在名稱下方的小型第二行。

<p align="center">
  <img src="Web-Frontend/images/features/mode-all@2x.webp" width="460" alt="All in One 檢視：並排顯示承受傷害、治療與總傷害">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="380" alt="可點擊穿透的浮動層，以小型標籤顯示在遊戲畫面上方">
</p>

### 可點擊穿透的浮動層

**隱藏介面**會把視窗變成小型標籤（或精簡面板），顯示在 Aion 2 上方，且不會攔截任何一次點擊。不透明度、傷害佔比長條、首領的生命值，以及每位玩家的承受傷害都是可選項目。浮動層只會在遊戲位於最前方時繪製。可隨時以快速鍵從任何地方切換。

選用（**設定 → 介面 → 開啟遊戲選單時隱藏**，預設關閉）：只要遊戲的某個選單（地圖、背包、軍團、商店、Esc 等）處於開啟狀態，計量器及其浮動層就會讓開，關閉選單後再恢復顯示。它依據滑鼠游標運作：在遊玩時隱藏游標的操作模式下，選單開啟時遊戲會顯示游標；按住顯示游標鍵（預設 Alt）可讓計量器保持顯示，方便你點擊它，而 **Ctrl+Alt+J** 會在目前開啟的選單中顯示浮動層。在經典操作模式下游標一律顯示，因此無法區分選單；計量器會在戰鬥中察覺這一點，並直接保持顯示。

### 每位玩家的技能明細

**雙擊**一位玩家：每個技能的命中次數、爆擊率、總計、最小值、最大值與平均值，並依目標拆分，另附整場戰鬥的 DPS 曲線。爆擊率直接取自伺服器的命中資料，因此十分精確。

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="單一玩家的技能明細與傷害隨時間變化的曲線">
</p>

### 技能循環、增益效果與硬直

同一視窗還有兩個分頁。**技能循環**會把玩家使用過的每個技能，依使用的時刻繪製在時間軸上：輔助技能在上方的軌道，傷害技能在下方，緊接在另一個技能之後穿插的技能則低一行，因此一連串的穿插看起來像一道階梯。背景中是玩家的 DPS（最近五秒的平均值）以及怪物陷入硬直的時刻。拖曳可在戰鬥中移動，**Ctrl + 滾輪**可縮放，將滑鼠停在圖示上可查看該次使用（何時、共幾次中的第幾次、距上一次的時間、當時的 DPS）。下方是**起手**（依序排列的最初幾個技能）和*技能循環概覽*：使用次數、每分鐘、間隔（兩次使用之間的時間）、首次以及傷害占比。**增益效果**列出玩家身上的每個效果及其持續時間、其中來自玩家自身的部分、施加的次數以及施加者；兩名玩家同時維持的效果只計一次。

戰鬥會把這些內容保存在本機歷史紀錄中，因此可以重新檢視舊的首領；用舊版本錄製的戰鬥不會顯示技能循環。

<p align="center">
  <img src="Web-Frontend/images/features/rotation@2x.webp" width="640" alt="玩家視窗的技能循環分頁：時間軸上的技能，背景為 DPS，以及起手和技能循環概覽">
</p>

### 您的整個角色一目了然

**角色**視窗（人形圖示）會顯示遊戲傳送給您的資料：裝備及其道具等級與強化（包含 +15 以上的增幅階段）、神器（Arcana）、種族知識、所有技能及其等級，以及完整的 Daevanion 面板。它會保留您上一次的登入資料，因此不會是空的。將滑鼠移到裝備上可查看詳細資訊（石頭、隨機屬性、神石）。

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="角色視窗：裝備、道具等級與強化">
</p>

### 含寵物與採集的互動地圖

地圖浮動層會顯示您的位置、您想要的寵物的出現點，以及您開啟的採集點——草藥、礦石、木材、寶石等等——無需切換視窗去看網站。只會列出您自己的陣營可以使用的項目，而且在遊戲關閉時，最後已知的位置仍會留在地圖上。寵物設定會列出維爾特隆（Verteron）、阿爾特加德（Altgard）與深淵的每個地區及其寵物，以及您自己的寵物等級，另有一個**稀有**分頁，列出出現點很少的寵物。邊緣箭頭會指向最近的寵物或採集物。

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="互動地圖：寵物出現點與寵物清單">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="採集設定：開啟要顯示在地圖上的項目">
</p>

### 含提醒的活動時間表

浮動層會列出接下來的活動——戰場（Battlefield）、深淵的攻城戰（神器攻城戰）、舒格節（Shugo Festival）、貝里特拉空襲（Beritra Air Raid）、時空裂縫（Spacetime Rift）等等。可為每個活動選擇音效與音量，並在活動開始前幾分鐘收到提醒。一個開關即可開啟或關閉所有提醒。

<p align="center">
  <img src="Web-Frontend/images/features/timetable@2x.webp" width="380" alt="時間表設定：每個活動可設定音效與音量">
</p>

### 浮動層上的世界首領

開啟某個野外首領後，即可在浮動層上看到它的計時，並在它出現的那一刻聽到它的音效。時間直接來自遊戲；伺服器會傳送您所在地圖的時間，其他地圖則在您去過之後才會顯示。音效與音量依首領設定，並依地區分組。

<p align="center">
  <img src="Web-Frontend/images/features/worldboss@2x.webp" width="380" alt="世界首領設定：依地圖列出每個野外首領的重生時間，可開關並設定音效與音量">
</p>

### 網路浮動層

Ping、抖動、流量、封包遺失，以及您已上線多久，皆由您電腦本來就會收到的流量量測而得。隨著連線品質變差，數值會由綠轉紅。可選擇想要的行、將其鎖定在原位，並以快速鍵開啟或關閉。

<p align="center">
  <img src="Web-Frontend/images/features/network-overlay@2x.webp" width="340" alt="網路浮動層：Ping、抖動、流量與封包遺失">
</p>

### 戰鬥浮動層

一個透明框架，最多包含三個元素，每個元素都可以單獨擺放與調整大小：

- **Boss 施法條：** Boss 一開始施放技能，你就能看到技能名稱、隨剩餘時間縮短的進度條，以及目標是誰（你顯示為紅色）。擊中過多名玩家的技能會標記為**範圍**；**致命**表示你目前受到的該技能最高一擊不低於你目前的生命值。計量器會學習每個技能（是否為範圍技能、傷害何時生效），並在多次工作階段之間記住。
- **目標的目標：** 如果你用 Tab 選取的目標本身正瞄準某人，你會看到是誰：Boss 顯示圖片與名稱，玩家顯示職業徽章與名稱。
- **治療者目標**（選用）：所選目標的生命條，以及其身上的減益（或增益）。

在**設定 → 戰鬥浮動層**中解鎖浮動層以進行配置：拖曳元素主體可移動，拖曳邊緣可調整大小，拖曳角落可縮放；整個框架右下角的握把可同時縮放所有元素。鎖定後它可被點擊穿透，且僅在遊戲位於前景時顯示。**Ctrl+Alt+C** 可開關它，*所有浮動層 開 / 關*快速鍵同樣如此。

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="戰鬥浮動層：兩個帶時間條的 Boss 施法、目標的目標，以及顯示生命與減益的治療者目標">
</p>

### 公告與 Twitch 掉落

公告按鈕（喇叭圖示）會在 aiondps.com 開啟 NCSoft 的官方公告——維護、中斷、新伺服器、伺服器轉移、補償——最新的在最前面，每種類型都有彩色標籤與篩選功能。當 AION 2 的 Twitch 掉落活動進行時，Twitch 按鈕會開啟一個頁面，內含獎勵、需要觀看的時數、倒數計時，以及目前已啟用掉落的直播。當有比您上次造訪更新的內容時，會顯示*新*標記。

<p align="center">
  <img src="Web-Frontend/images/features/announcements@2x.webp" width="430" alt="AION 2 官方公告與類型篩選">
  <img src="Web-Frontend/images/features/twitchdrops@2x.webp" width="430" alt="Twitch 掉落活動的獎勵與即時直播">
</p>

### 戰鬥歷史

戰鬥會記錄在本機，讓您可以再次查看先前的首領戰（可在**設定 → 介面**中關閉）。統計器會在脫離戰鬥一段可設定的時間後重設。

## 使用方式

統計器一執行就會開始記錄。**暫停會直接捨棄**而不是延後處理：暫停期間的事件會被永久略過，因此繼續時絕不會重播您沒參與的戰鬥。

- **複製**會把一行、可直接貼到聊天的排名放進剪貼簿（`Name 1.234.567 (890), …`）；**全部複製**
  則提供 Discord markdown 表格。
- 外觀在**設定 → 介面**中設定：佈景主題（深色／淺色）、字型大小、語言、浮動層外觀與不透明度、隨 Windows 自動啟動。

<p align="center">
  <img src="Web-Frontend/images/features/settings@2x.webp" width="340" alt="設定：介面、浮動層與行為">
  <img src="Web-Frontend/images/features/hotkeys@2x.webp" width="340" alt="設定：快速鍵">
</p>

## 快速鍵

全系統有效，遊戲取得焦點時也一樣。在**設定 → 快速鍵**中點擊欄位，按下您想要的組合（必須包含 Ctrl、Alt、Shift 或 Win）；**×** 可移除它。它們取代了聊天指令，因為聊天指令無法運作——Aion 2 的聊天無法從網路串流中讀取。

| 預設 | 動作 |
|---|---|
| **Ctrl+Alt+H** | 浮動層（隱藏視窗，顯示標籤） |
| **Ctrl+Alt+P** | 暫停 / 繼續 |
| **Ctrl+Alt+D** | 複製傷害排名 |
| **Ctrl+Alt+Shift+X** | 清除戰鬥記錄 |
| **Ctrl+Alt+U** | 上傳首領傷害 |
| **Ctrl+Alt+M** | 下一個模式（五種檢視） |
| **Ctrl+Alt+T** | 時間表浮動層 |
| **Ctrl+Alt+K** | 互動地圖 開 / 關 |
| **Ctrl+Alt+E** / **Ctrl+Alt+G** | 寵物／採集物的邊緣箭頭 |
| **Ctrl+Alt+B** | 世界首領浮動層 |
| **Ctrl+Alt+N** | 網路浮動層 |
| **Ctrl+Alt+C** | 戰鬥浮動層 |
| **Ctrl+Alt+J** | 在此選單中顯示浮動層 |
| **Ctrl+Alt+O** | 快速鍵浮動層（可移動的快速鍵清單） |
| **Ctrl+Alt+A** | 所有浮動層 開 / 關 |

## 上傳

- **首領戰鬥**會在您點擊上傳時上傳（工具列按鈕、戰鬥記錄選單或 **Ctrl+Alt+U**）：包含首領、參戰玩家、傷害、治療、承受傷害與技能。只接受遊戲有公告且目錄中已收錄的首領。自動上傳可用，且**預設關閉**。
- **您自己的角色檔案**（名稱、職業、等級、裝備、技能、Daevanion、軍團、伺服器、陣營）可在登入後幾秒自動上傳，讓您能在網站上被找到。這**預設關閉**；請在**設定**中開啟。
- 未上傳時，任何資料都不會離開您的電腦。

## 更新

統計器會自行更新。它會在啟動時以及執行期間每五分鐘向 GitHub 詢問是否有較新的版本，在背景下載，並於下次啟動時換上——不需要安裝程式，也不會跳出 UAC 提示。更新就緒時，狀態列會出現一行綠色文字（透明浮動層上則會出現徽章）；點擊它即可選擇立即重新啟動。**應用程式 → 檢查更新**可隨時手動執行同樣的動作。舊版本在新版本發布後仍可繼續上傳 72 小時，之後就必須更新。

檢查只會讀取一個 URL，除了請求本身外不會送出任何內容：

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

可在**設定 → 更新**中關閉；即使關閉，選單項目仍可使用。

## 語言

英文、德文、法文、西班牙文、俄文、中文（簡體與繁體）、葡萄牙文（巴西）、日文與韓文——在**設定 → 介面 → 語言**中設定。本 README 另有以下語言版本：
[Deutsch](README.de.md)、[Español](README.es.md)、[Français](README.fr.md)、[Русский](README.ru.md)、
[中文](README.zh.md)、[繁體中文](README.zh-TW.md)、[Português (Brasil)](README.pt.md)、
[日本語](README.ja.md) 與 [한국어](README.ko.md)。

## 從原始碼建置

```
cd Client
dotnet build
dotnet run -- selftest                              # self-checks (protocol, capture, decoding, ...)
dotnet run -- aion2-record <out.jsonl>              # record the game's traffic ("stop" ends it)
dotnet run -- aion2-replay <file.jsonl>             # replay a recording through the real decoder
dotnet run -- aion2-upload-dryrun <file.jsonl>      # build the uploads of a recording, send nothing
```

僅支援 Windows（WPF）。`Tools/aion2-dat` 會讀取遊戲的文字表（八種語言的名稱）；請參閱其 README。

## 關於伺服器規則的說明

統計器僅被動觀察遊戲本身的網路流量。即便如此，發行商對第三方工具仍有自己的規定——使用前值得先查看遊戲的條款。

## 免責聲明與授權

Aion 2 及所有相關名稱、文字與美術作品均為 NCSOFT 的財產。本專案與 NCSOFT 無任何隸屬、背書或關聯；遊戲內容仍屬其所有者。原始碼依 [MIT License](LICENSE) 釋出——該授權僅涵蓋程式碼，不含任何遊戲內容。
