🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 **한국어**

# Aion DPS Meter

**AION 2용 무료 DPS 미터** — 파티 전체의 피해량과 치유량을 실시간으로 표시하고, 클릭이 통과하는 오버레이, 보스 순위표, 펫·채집 지도, 이벤트 일정, 월드 보스 타이머를 제공합니다. Windows용, 오픈 소스이며 게임 메모리를 절대 읽지 않습니다.

<p align="center">
  <a href="../../releases/latest"><b>⬇ Windows용 다운로드</b></a> · <a href="https://aiondps.com">웹사이트</a> · <a href="https://aiondps.com/features">모든 기능</a> · <a href="https://aiondps.com/instances">보스 순위표</a>
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/SkeeveAN/Aion-DPS-Meter?label=release" alt="release"></a>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/SkeeveAN/Aion-DPS-Meter/total?label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" alt="Windows">
</p>

<p align="center">
  <img src="Web-Frontend/images/features/meter@2x.webp" width="440" alt="미터 창: 모든 플레이어의 직업 아이콘, DPS, 총 대미지, 비중 막대">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="320" alt="게임 화면 위에 작은 칩으로 표시되는 클릭 통과 오버레이">
</p>

> 아래 그림 속 이름, 플레이어, 수치는 데모 데이터입니다.

## 왜 이 미터인가

- 파티·레이드 모든 플레이어의 DPS, iDPS, HPS를 실시간 표시
- 클릭을 가로채지 않는 오버레이
- 치명타율과 DPS 곡선이 포함된 스킬 상세
- aiondps.com의 보스 순위표와 공개 캐릭터 프로필(업로드는 선택, 기본값은 꺼짐)
- 인터랙티브 지도, 이벤트 일정, 월드 보스 타이머, 네트워크 오버레이
- 안전: 패시브 패킷 캡처만 사용, 메모리 읽기·코드 주입 없음
- 무료, 오픈 소스(MIT), 자동 업데이트

AION 2 글로벌 버전과 호환됩니다. NCSOFT가 2026년 10월 5일에 출시한 모든 지역이 해당됩니다. 한국과 대만은 몇 버전 앞서 있어 스킬 인식이 불완전할 수 있습니다.

**Aion 2**용 대미지/치유 미터 및 도구 모음입니다. [Npcap](https://npcap.com) 드라이버를 통해 내 PC에서 게임 자체의
네트워크 트래픽을 읽습니다. 완전히 수동 방식이라 패킷을 단 하나도 보내지 않으며 게임 프로세스나 메모리에도
접근하지 않습니다. 직접 업로드하지 않는 한 플레이 정보는 PC 밖으로 나가지 않습니다([업로드](#업로드) 참조).
그 밖에 전송하는 것은 업데이트 확인뿐이며, GitHub에 새 릴리스가 있는지 묻는 이 요청은 끌 수 있습니다.
[업데이트](#업데이트)를 참조하세요.


## 목차

[설치](#설치) · [기능 소개](#기능-소개) · [사용법](#사용법) · [단축키](#단축키) ·
[업로드](#업로드) · [업데이트](#업데이트) · [언어](#언어) · [소스에서 빌드](#소스에서-빌드)

## 설치

1. [Npcap](https://npcap.com) 드라이버를 설치합니다(미터가 게임 트래픽을 보려면 필요하며, 설치 프로그램에는
   포함되어 있지 않습니다).
2. [최신 릴리스](../../releases/latest)에서 `AionDpsMeter-win-Setup.exe`를 내려받아 실행합니다.
   사용자 프로필에 설치되고 미터가 시작됩니다. 관리자 권한도, .NET도 필요 없습니다.
3. 미터를 시작한 다음 캐릭터로 로그인합니다. 미터는 캐릭터, 장비, 스킬, 데바니온 보드를 게임에서 직접
   읽어 오며, 플레이 중인 서버는 자동으로 감지됩니다.

## 기능 소개

### 모두를 위한 실시간 대미지 및 치유

파티나 레이드의 모든 플레이어를 이름, 총 대미지, DPS, 비중 막대와 함께 보여 주며, 진영과 직업은
아이콘으로 표시됩니다. 직업, 그룹, 대상별로 필터링할 수 있습니다. **몹/보스** 필터는 전체 DPS와 대상별
실제 **iDPS**를 전환하며, 보스는 게임 자체 데이터로 인식해 이름으로 표시합니다. **PVE/PVP** 전환으로
두 가지를 구분하고, 보스전에서 전멸하면 별도의 항목이 생깁니다(해골 표시는 시도 후 보스의 생명력이
다시 가득 찼음을 뜻합니다).

### 클릭 한 번으로 바꾸는 다섯 가지 보기

**대미지**, **치유**, **받은 대미지**, 그리고 두 가지 **올인원** 보기입니다. 단축키(기본값
**Ctrl+Alt+M**)나 오버레이의 모드 표시를 클릭해 전환합니다. *올인원*은 받은 대미지, 치유, 합계를 나란히
보여 주고, *올인원 간략*은 익숙한 DPS와 합계 열을 유지하면서 치유와 받은 대미지를 이름 아래의 작은 둘째
줄에 표시합니다.

<p align="center">
  <img src="Web-Frontend/images/features/mode-all@2x.webp" width="460" alt="올인원 보기: 받은 대미지, 치유, 총 대미지를 나란히 표시">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="380" alt="게임 화면 위에 작은 칩으로 표시되는 클릭 통과 오버레이">
</p>

### 클릭이 통과하는 오버레이

**UI 숨기기**는 창을 작은 칩(또는 간략 패널)으로 바꿔 Aion 2 위에 띄우며, 클릭을 전혀 가로채지 않습니다.
투명도, 대미지 비중 막대, 보스의 생명력, 플레이어별 받은 대미지는 옵션으로 설정할 수 있습니다. 오버레이는
게임이 맨 앞에 있을 때만 표시됩니다. 단축키로 어디서든 켜고 끌 수 있습니다.

### 모든 플레이어의 스킬 상세 분석

플레이어를 **더블 클릭**하면 모든 스킬의 적중 수, 치명타율, 합계, 최소, 최대, 평균이 대상별로 나뉘어
표시되고, 전투 전체의 DPS 곡선도 볼 수 있습니다. 치명타율은 서버의 적중 데이터에서 그대로 읽으므로
정확합니다.

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="시간에 따른 대미지 곡선이 있는 한 플레이어의 스킬 상세 분석">
</p>

### 내 캐릭터를 한눈에

**캐릭터** 창(사람 아이콘)은 게임이 내 캐릭터에 대해 보내 주는 정보를 보여 줍니다. 아이템 레벨과 강화
수치(+15 이상의 증폭 단계 포함)가 표시된 장비, 아르카나, 종족 지식, 레벨이 표시된 모든 스킬, 전체
데바니온 보드가 포함됩니다. 마지막 로그인 정보를 보관하므로 비어 있는 일이 없습니다. 장비에 마우스를
올리면 세부 정보(스톤, 굴린 능력치, 신석)를 볼 수 있습니다.

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="장비, 아이템 레벨, 강화 수치가 표시된 캐릭터 창">
</p>

### 펫과 채집을 위한 인터랙티브 지도

지도 오버레이는 내 위치, 찾고 있는 펫의 출현 지점, 켜 둔 채집 지점(약초, 광석, 목재, 보석 등)을
웹사이트로 알트탭하지 않고도 보여 줍니다. 내 진영이 사용할 수 있는 것만 표시되며, 게임을 닫은 동안에도
마지막으로 알려진 위치가 지도에 남습니다. 펫 설정에는 베르테론, 알트가르드, 어비스의 모든 지역과 해당
펫, 내 펫 레벨이 나열되며, 출현 지점이 적은 펫을 위한 **희귀** 탭도 있습니다. 가장자리 화살표가 가장
가까운 펫이나 채집물을 가리킵니다.

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="펫 출현 지점과 펫 목록이 있는 인터랙티브 지도">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="채집 설정: 지도에 표시할 항목 켜기">
</p>

### 알림이 있는 이벤트 시간표

오버레이에 다음 이벤트가 나열됩니다. 전장, 어비스 공성전(아티팩트 공성전), 슈고 축제, 베리트라 공습,
시공의 균열 등이 있습니다. 이벤트마다 소리와 음량을 고르면 시작 몇 분 전에 알려 줍니다. 스위치 하나로
모든 알림을 켜거나 끌 수 있습니다.

<p align="center">
  <img src="Web-Frontend/images/features/timetable@2x.webp" width="380" alt="이벤트마다 소리와 음량을 정하는 시간표 설정">
</p>

### 오버레이에서 보는 월드 보스

필드 보스를 켜 두면 오버레이에서 타이머를 보고, 보스가 나타나는 순간 소리를 들을 수 있습니다. 시간은
게임에서 직접 가져옵니다. 서버는 내가 있는 지도의 시간을 보내 주며, 다른 지도의 시간은 한 번 가 본 뒤에
표시됩니다. 소리와 음량은 지역별로 묶인 보스마다 따로 설정합니다.

<p align="center">
  <img src="Web-Frontend/images/features/worldboss@2x.webp" width="380" alt="월드 보스 설정: 지도별 필드 보스의 재등장 시간, 스위치, 소리와 음량">
</p>

### 네트워크 오버레이

핑, 지터, 트래픽, 패킷 손실, 접속 시간을 PC가 이미 받고 있는 트래픽으로 측정합니다. 연결 상태가
나빠질수록 값이 초록색에서 빨간색으로 바뀝니다. 표시할 줄을 고르고, 위치를 고정하고, 단축키로 켜고 끌 수
있습니다.

<p align="center">
  <img src="Web-Frontend/images/features/network-overlay@2x.webp" width="340" alt="네트워크 오버레이: 핑, 지터, 트래픽, 패킷 손실">
</p>

### 전투 오버레이

최대 세 가지 요소가 있는 하나의 투명한 프레임이며, 각 요소는 따로 배치하고 크기를 조절할 수 있습니다:

- **보스 시전 바:** 보스가 스킬을 시작하는 순간 스킬 이름, 남은 시간에 따라 줄어드는 바, 그리고 누구를 겨냥하는지(나는 빨간색)가 표시됩니다. 여러 플레이어를 맞힌 스킬은 **범위**로 표시되며, **치명적**은 지금까지 그 스킬에서 받은 가장 큰 피해가 현재 체력 이상이라는 뜻입니다. 미터는 각 스킬(범위 여부, 피해가 들어오는 시점)을 학습하고 세션 사이에도 기억합니다.
- **대상의 대상:** Tab으로 선택한 대상이 다른 누군가를 겨냥하고 있다면 그 대상이 표시됩니다. 보스는 그림과 이름, 플레이어는 클래스 엠블럼과 이름으로 나옵니다.
- **힐러 대상**(선택 사항): 선택한 대상의 체력 바와 그 대상에 걸린 디버프(또는 버프).

**설정 → 전투 오버레이**에서 오버레이 잠금을 해제해 배치하세요. 요소의 본체를 드래그하면 이동하고, 가장자리를 당기면 크기가 바뀌며, 모서리를 당기면 배율이 조절됩니다. 전체 프레임 오른쪽 아래의 손잡이는 모든 요소의 배율을 한꺼번에 조절합니다. 잠겨 있으면 클릭이 통과되며 게임이 맨 앞에 있을 때만 표시됩니다. **Ctrl+Alt+C**로 켜고 끌 수 있고, *모든 오버레이 켜기 / 끄기* 단축키로도 가능합니다.

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="전투 오버레이: 시간 바가 있는 보스 시전 두 개, 대상의 대상, 체력과 디버프가 있는 힐러 대상">
</p>

### 공지사항과 트위치 드롭

확성기 버튼을 누르면 aiondps.com에서 NCSoft 공식 공지사항이 열립니다. 점검, 장애, 신규 서버, 서버 이전,
보상 등이 최신순으로 종류별 색상 라벨 및 필터와 함께 표시됩니다. AION 2 트위치 드롭 캠페인이 진행 중이면
트위치 버튼으로 보상, 시청해야 하는 시간, 카운트다운, 지금 드롭이 활성화된 방송이 있는 페이지가
열립니다. 마지막 방문 이후 새 내용이 있으면 *새 글* 표시가 나타납니다.

<p align="center">
  <img src="Web-Frontend/images/features/announcements@2x.webp" width="430" alt="종류별 필터가 있는 AION 2 공식 공지사항">
  <img src="Web-Frontend/images/features/twitchdrops@2x.webp" width="430" alt="보상과 실시간 방송이 있는 트위치 드롭 캠페인">
</p>

### 전투 기록

전투는 로컬에 기록되므로 이전 보스전을 다시 볼 수 있습니다(**설정 → 인터페이스**에서 끌 수 있습니다).
미터는 설정한 비전투 시간이 지나면 초기화됩니다.

## 사용법

미터가 실행되는 즉시 기록이 시작됩니다. **일시정지는 보류가 아니라 폐기입니다.** 일시정지 중의 이벤트는
완전히 건너뛰므로, 재개해도 참여하지 않은 전투가 재생되는 일은 없습니다.

- **복사**는 채팅에 바로 붙여 넣을 수 있는 한 줄 순위(`Name 1.234.567 (890), …`)를 클립보드에 넣고,
  **전체 복사**는 Discord 마크다운 표를 만들어 줍니다.
- 외형은 **설정 → 인터페이스**에서 정합니다. 테마(다크/라이트), 글꼴 크기, 언어, 오버레이 모양과
  투명도, Windows 시작 시 실행이 있습니다.

<p align="center">
  <img src="Web-Frontend/images/features/settings@2x.webp" width="340" alt="설정: 인터페이스, 오버레이, 동작">
  <img src="Web-Frontend/images/features/hotkeys@2x.webp" width="340" alt="설정: 단축키">
</p>

## 단축키

시스템 전체에서 작동하며 게임에 포커스가 있을 때도 동작합니다. **설정 → 단축키**에서 항목을 클릭하고
원하는 조합을 누르세요(Ctrl, Alt, Shift, Win 중 하나가 필요합니다). **×**로 삭제합니다. 단축키는
채팅 명령어를 대신하는데, Aion 2의 채팅은 네트워크 스트림에서 읽을 수 없어 채팅 명령어는 작동할 수
없기 때문입니다.

| 기본값 | 동작 |
|---|---|
| **Ctrl+Alt+H** | 오버레이(창을 숨기고 칩 표시) |
| **Ctrl+Alt+P** | 일시정지 / 재개 |
| **Ctrl+Alt+D** | 대미지 순위 복사 |
| **Ctrl+Alt+Shift+X** | 세션 지우기 |
| **Ctrl+Alt+U** | 보스 대미지 업로드 |
| **Ctrl+Alt+M** | 다음 모드(다섯 가지 보기) |
| **Ctrl+Alt+T** | 시간표 오버레이 |
| **Ctrl+Alt+K** | 인터랙티브 지도 켜기 / 끄기 |
| **Ctrl+Alt+E** / **Ctrl+Alt+G** | 펫 / 채집물 가장자리 화살표 |
| **Ctrl+Alt+B** | 월드 보스 오버레이 |
| **Ctrl+Alt+N** | 네트워크 오버레이 |
| **Ctrl+Alt+C** | 전투 오버레이 |
| **Ctrl+Alt+O** | 단축키 오버레이(이동 가능한 단축키 목록) |
| **Ctrl+Alt+A** | 모든 오버레이 켜기 / 끄기 |

## 업로드

- **보스전**은 업로드를 클릭할 때(도구 모음 버튼, **세션** 메뉴 또는 **Ctrl+Alt+U**) 업로드됩니다.
  보스, 참여한 플레이어, 대미지, 치유, 받은 대미지, 스킬이 포함됩니다. 게임이 알린 보스 중 카탈로그에
  있는 보스만 접수됩니다. 자동 업로드도 사용할 수 있으며 **기본값은 꺼짐**입니다.
- **내 캐릭터 프로필**(이름, 직업, 레벨, 장비, 스킬, 데바니온, 레기온, 서버, 진영)은 로그인 몇 초 후에
  자동으로 업로드할 수 있어, 웹사이트에서 나를 찾을 수 있습니다. 이 기능은 **기본값이 꺼짐**이며
  **설정**에서 켤 수 있습니다.
- 업로드하지 않으면 아무것도 PC 밖으로 나가지 않습니다.

## 업데이트

미터는 스스로 업데이트됩니다. 시작할 때와 실행 중 5분마다 GitHub에 새 릴리스가 있는지 확인하고,
백그라운드에서 내려받아 다음 시작 때 교체합니다. 설치 프로그램도, UAC 확인 창도 없습니다. 업데이트가
준비되면 상태 줄에 초록색 줄이 나타나며(투명 오버레이에는 배지가 표시됩니다), 이를 클릭하면 바로 재시작할
수 있습니다. **앱 → 업데이트 확인**으로 필요할 때 직접 실행할 수도 있습니다. 이전 버전은 후속 버전이
나온 뒤 72시간 동안 계속 업로드할 수 있으며, 그 후에는 업데이트해야 합니다.

이 확인은 URL 하나만 읽으며 요청 자체 외에는 아무것도 보내지 않습니다.

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

**설정 → 업데이트**에서 끌 수 있으며, 꺼져 있어도 메뉴 항목은 계속 작동합니다.

## 언어

영어, 독일어, 프랑스어, 스페인어, 러시아어, 중국어(간체 및 번체), 포르투갈어(브라질), 일본어, 한국어를
지원하며 **설정 → 인터페이스 → 언어**에서 설정합니다. 이 README는 다음 언어로도 제공됩니다:
[Deutsch](README.de.md), [Español](README.es.md), [Français](README.fr.md), [Русский](README.ru.md),
[中文](README.zh.md), [繁體中文](README.zh-TW.md), [Português (Brasil)](README.pt.md),
[日本語](README.ja.md), [한국어](README.ko.md).

## 소스에서 빌드

```
cd Client
dotnet build
dotnet run -- selftest                              # self-checks (protocol, capture, decoding, ...)
dotnet run -- aion2-record <out.jsonl>              # record the game's traffic ("stop" ends it)
dotnet run -- aion2-replay <file.jsonl>             # replay a recording through the real decoder
dotnet run -- aion2-upload-dryrun <file.jsonl>      # build the uploads of a recording, send nothing
```

Windows 전용(WPF)입니다. `Tools/aion2-dat`는 게임의 텍스트 테이블(8개 언어의 이름)을 읽습니다. 해당 README를 참조하세요.

## 서버 규정에 관한 참고

미터는 게임 자체의 네트워크 트래픽을 수동적으로 관찰할 뿐입니다. 그럼에도 퍼블리셔는 서드파티 도구에 대해
자체 규정을 두고 있으므로, 사용 전에 게임 이용 약관을 확인해 보시기 바랍니다.

## 면책 조항 및 라이선스

Aion 2 및 관련된 모든 이름, 텍스트, 아트워크는 NCSOFT의 자산입니다. 이 프로젝트는 NCSOFT와 제휴, 보증 또는 연관 관계가 없으며, 게임 콘텐츠의 권리는 소유자에게 있습니다. 소스 코드는 [MIT License](LICENSE)로 공개됩니다. 라이선스는 코드에만 적용되며 게임 콘텐츠에는 적용되지 않습니다.
