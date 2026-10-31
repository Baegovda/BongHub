# BongHub 인수인계 문서 (HANDOFF)

- 작성: 2026-10-31 (토) 16:05~16:15 (KST, UTC+9), 이전 계정의 Grok Bot(이름 "BongHub")
- 대상: 다른 계정에서 이 프로젝트를 이어받는 봇
- 원칙: 아래 내용은 2026-10-31 15:58~16:10 KST 사이에 PC 파일과 코드, GitHub를 직접 확인하고 쓴 것이다. 확인하지 못한 내용은 **(추정)** 으로 표시했다.
- 비밀값(토큰, 비밀번호)은 이 문서에 넣지 않았다.

---

## 1. 한눈에 요약과 현재 상태

| 항목 | 값 |
|---|---|
| 앱 | **BongHub**. 부제는 "석봉이의 유틸 창고". Windows 트레이 유틸리티(C# WinForms, .NET Framework 4.x) |
| 예전 이름 | MonitorMode → BongMonitor → (BongUtil 검토) → BongHub |
| 배포 버전 | **3.3.1**. `BongHub.exe`의 FileVersion은 3.3.1.0이고, 16:00:40 KST에 빌드·배포됐다(executor가 배포). 소스 `BongHub.cs`도 3.3.1이다(15:57 KST) |
| 실행 중인 배포본 | `C:\Users\Revaptor_FX\BongHub\BongHub.exe`. PID 102548, 16:00:40 KST 시작, 16:02 KST 확인 시 작업 집합 약 55MB |
| 직전 버전 | 3.3.0(15:36 KST 배포). 백업은 `backup\BongHub.3.3.0.20261031_160036.*` |
| 진행 중인 작업 | **3.3.1 "모니터 프리셋 ↔ 작업 영역(창 배치) 연계"**. 파일 기준으로는 배포까지 끝났다. 다만 executor의 최종 보고(회귀 테스트 전체 결과, 미리보기, 사용자 보고)는 이 문서를 쓸 때까지 **확인하지 못했다**. 그래서 상태를 "배포됨, 최종 보고 대기"로 적는다. `_dev\`에 개발본과 테스트 결과가 있다(`_dev\extrastest.txt`에서 작업 영역 연계 항목은 모두 PASS, `_dev\tab_bench.txt`는 16:00 KST에 생성) |
| 클라우드 동기화 | 비공개 레포 `Baegovda/BongHub`. 앱 기본값은 꺼짐이다. PC에 `%LOCALAPPDATA%\BongHub\cloud\` 폴더가 없으므로 사용자는 아직 동기화를 켜지 않은 상태다 |
| 자동 시작 | `Startup\BongHub.lnk`가 `C:\Users\Revaptor_FX\BongHub\BongHub.exe`를 가리킨다 |

**이어받으면 먼저 할 일:** 11장 체크리스트로 실행 버전이 3.3.1인지, 테스트 프로세스가 남아 있지 않은지 확인한다. 그다음 사용자에게 3.3.1 결과(프리셋마다 작업 영역 연결, 전환이 끝날 때 한 번 배치)를 아직 보고하지 않았다면 보고한다.

---

## 2. 사용자 프로필, 소통 규칙, 금지 사항

- 사용자: **Exodus**. 본명 호칭은 "석봉이"(부제 "석봉이의 유틸 창고"). GitHub 계정은 **Baegovda**. 시간대는 Asia/Seoul이다.
- 언어: **한국어**. 짧은 반말을 쓰고 오타가 많다(예: "해줭", "하렴", "제대로이해햇으"). 봇은 존댓말 해요체로 답하고, 짧고 결론부터 말한다.
- 메시지는 iPhone과 데스크톱 두 곳에서 온다. 폰에서 보낼 때는 PC 앞에 없을 수 있다.
- 사용자가 자주 하는 말: "세련되게", "구식이야", "촌스러움", "최적화 쫙", "완벽하게 똑같이", "끝까지 끝내놔 결과만 알려줘".

**규칙과 금지 사항(반드시 지킬 것):**
1. **AutoHotkey를 제안하지 않는다.** 예전 AHK 방식을 없애는 것이 이 프로젝트의 출발점이었다.
2. **테스트 중에 사용자 화면에 창을 띄우지 않는다.** 숨은 데스크톱(`testdesk.ps1`, CreateDesktop "BongHubTest")이나 화면 밖 좌표(-30000,-30000)만 쓴다. 사용자는 "계속 실행됐다가 탭 쭈우욱 훑고 꺼지는 이유는 뭐야", "창 계속 뜨네"라고 두 번 항의했다.
3. **실제 모니터 전환, 밝기 변경, 바탕화면 아이콘 복원, 마우스 후킹을 테스트에서 실행하지 않는다.** 모의(Mock) 테스트만 한다. 실제 동작은 사용자가 직접 확인한다.
4. 위험한 기능(전역 후킹, 창 끌기, 탐색기 연동, 레지스트리)은 **기본값을 꺼짐**으로 둔다.
5. 파일을 바꾸기 전에 `backup\`에 백업한다(5장).
6. 표기는 **BongHub**, 부제는 "석봉이의 유틸 창고"를 쓴다. 탭 이름은 "모니터 프리셋"과 "카톡 광고 제거"를 쓴다("프리셋"이나 "카카오톡"만 쓰지 않는다).
7. 설정은 **바뀌는 즉시 적용·저장**한다. "적용 버튼"은 금지다(사용자 표현으로 "구식").
8. UI는 **그라데이션 다크 테마**로, 블록 아이콘 컨셉에 맞추고 커스텀 타이틀바를 쓴다. 기본 Windows 타이틀바나 스크롤바는 쓰지 않는다.
9. 아이콘은 **블록 하나로 고정**한다. 선택 옵션은 없다(허브 아이콘은 삭제했고 백업만 남아 있다).
10. 기능이 흡수된 옛 독립 유틸은 삭제한다. BongAd 레포는 삭제가 끝났다. BongAd **봇 삭제는 사용자가 직접** 한다(사이드바에서 우클릭 → Delete).
11. 메일이나 메시지는 사용자 확인 없이 보내지 않는다. 레포 생성·삭제처럼 외부에 영향을 주는 작업은 사용자가 명시적으로 요청했을 때만 한다.
12. 빌드할 때마다 **버전을 올리고** 정보 탭과 CHANGELOG에 남긴다. 이전 버전과 비교해 빠진 기능이 없는지 회귀 테스트를 한다(사용자 지시 "버전 체크도 하면서 하렴").
13. 보고는 정직하게 한다. 실제로 테스트하지 못한 것은 "직접 확인해 주세요"라고 명시한다. "완벽하다"는 말은 피한다.
14. 사용량이 부족해서 다른 계정으로 인계하는 중이다. 작업 단위를 작게 쪼개고 중간 결과를 파일로 남긴다.

---

## 3. 프로젝트 구조

### 3.1 PC 경로 (machineId `d3fb99ad-25ca-45c1-81af-41e5ca09104a`, 호스트 Revaptor-FX, 사용자 Revaptor_FX)

| 경로 | 내용 |
|---|---|
| `C:\Users\Revaptor_FX\BongHub\BongHub.cs` | 단일 소스 파일. 3.3.0 기준 7,327줄·589KB, 3.3.1은 약 597KB |
| `...\BongHub\BongHub.exe` | 배포 실행 파일(3.3.1, 약 810KB). MultiMonitorTool, BongAd, 아이콘을 리소스로 포함한다 |
| `...\BongHub\build.ps1` | 빌드 스크립트(5장) |
| `...\BongHub\CHANGELOG.md` | 변경 이력(3.0.0~3.3.1) |
| `...\BongHub\res\` | `MultiMonitorTool.exe`(NirSoft), `work_layout.cfg`(작업 모드의 MMT 레이아웃), `BongAd.exe`(15KB, C로 작성), `BongAd_README.md`, `icon_blocks.ico` |
| `...\BongHub\backup\` | 이전 버전들. 형식은 `BongHub.<ver>.<yyyyMMdd_HHmmss>.cs/.exe(.bak)`, `build.<ver>...ps1`, `CHANGELOG.<ver>...md`. 그 밖에 `BongAd.lnk.bak`, `icon_hub.*.ico.bak`, `MonitorMode*.bak`, `BongMonitor.v2name/v3dark.*.bak`, `BongHub.v4kakao/v4pre*.bak`이 있다 |
| `...\BongHub\_preview\` | 탭별 미리보기 PNG(배율별 `_100/_125/_150`), `tab_bench_*.txt` |
| `...\BongHub\_dev\` | **진행 중인 3.3.1 작업 폴더**. 개발용 cs/exe, `dev.ps1`(개발 빌드와 테스트 함수 T), `devb.ps1`, `offshot.ps1`(화면 밖 캡처 검증), `testdesk.ps1`(숨은 데스크톱 실행), 테스트 결과 txt/png, `base320\`(3.2.0 기준본), `bh33.cs.gz` |
| `...\BongHub\_dev33\`, `v33dev.tgz` | 3.3.0 작업용 임시 폴더와 압축본(박스와 주고받는 용도로 추정) |
| `...\BongHub\settingstest_error.txt` | 02:48 KST에 생긴 옛 오류 기록(CopyFromScreen의 CopyPixelOperation 예외). 지금은 해결된 이전 테스트 방식의 흔적이다 |
| `%LOCALAPPDATA%\BongHub\` | 런타임 데이터(3.3장) |
| `%LOCALAPPDATA%\MonitorMode\` | 옛 데이터 폴더. 설정을 불러올 때 대체 경로로 쓴다(`Core.LegacyDir`) |
| `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\BongHub.lnk` | 자동 시작 바로가기. 인수는 없고, 아이콘은 `BongHub.exe,0`이다 |
| `C:\Users\Revaptor_FX\MonitorPresets\` | **옛 AHK 방식 폴더가 남아 있다**(hotkeys.ahk, 작업.bat, 자리비움.bat, work.ps1, away.ps1, audio_playing.ps1, MultiMonitorTool.exe 등). 정리를 제안만 했고 아직 지우지 않았다 |
| 바탕화면 `모니터 - 작업.lnk`, `모니터 - 자리비움.lnk` | 아직 옛 `MonitorPresets\*.bat`를 가리킨다. 바꾸거나 지워야 한다(사용자 확인 필요) |
| `C:\Users\Revaptor_FX\BongAd\` | **옛 독립 BongAd 폴더가 남아 있다**. 프로세스와 시작 바로가기는 제거했다. 폴더 삭제는 제안만 했다 |

시작프로그램 폴더의 다른 항목: `ForgeLensWindowWatch.lnk`(다른 봇 ForgeLens가 만든 것이니 건드리지 않는다), `Notion.lnk`.

### 3.2 소스 구조 (BongHub.cs, 주요 클래스 순서)
`Native`(Win32 P/Invoke) · `Hotkey` · `WindowRule` · `Preset` · `GeneralCfg` · `Config` · `DeskCfg/Desk*`(바탕화면 아이콘) · `Remote/CloudCfg/CloudState/CloudStamp/Cloud`(동기화) · `TestEnv`(화면 밖 테스트) · `AppInfo`(버전) · `SlimBar/SlimScroll`(얇은 스크롤바) · `ImageViewer/ViewCanvas` · `OverlayForm/Overlay`(전환 스피너) · `MonInfo/Core/Monitors`(모니터 전환) · `MonBright/RealBright/PresetExtras`(밝기, 아이콘 배치, 작업 영역 연계) · `WinInfo/WindowRules/Watcher`(창 규칙) · `UI/Dark*/FlatBtn/GradSlider`(테마 컨트롤) · `KakaoCfg/Kakao` · `NotesCfg/Notes/NoteForm/RichBox/PalettePopup/SwatchGrid` · `ChromeForm/TitleBar`(커스텀 타이틀바) · `SettingsForm/SidePanel/BrandHeader/RuleDialog/CaptureDialog` · `Startup` · `PtCfg`와 그 하위(`ZonesCfg/GrabCfg/WorkspacesCfg/PaletteCfg/PasteCfg/PeekCfg/ResizerCfg`) · `Zones/ZoneOverlay/GrabMove/Workspaces/Palette/AdvPaste/Peek/Wic/Resizer` · `PtCard` · `TrayApp` · `Program`(Main과 테스트 모드) · `MockBright/ThrowBright`(테스트용).

- 진입점: `Program.Main`. 인수가 없으면 트레이를 띄운다(뮤텍스 `Local\BongHub_Tray`, 단일 인스턴스). 인수가 있으면 테스트 모드나 CLI로 동작한다(6장).
- CLI 전환: `BongHub.exe <프리셋 이름>`(예전 이름 `work`/`away`도 된다). 뮤텍스 `Local\BongHub_Routine`을 쓴다. `BongHub.exe workspace <이름>`은 작업 영역을 실행한다. `BongHub.exe resize <파일...>`은 Image Resizer를 연다. `BongHub.exe version`은 exe 폴더에 version.txt를 쓴다.
- 안전장치: `...test`나 `...bench`로 끝나는 모르는 인수는 exit 9를 돌려준다. 실제 전환으로 넘어가지 않는다.
- 설정 탭 키(`PageKeys`): presets, windows, notes, kakao, desktop, general, about, powertoys. 각각 모니터 프리셋, 창 위치, 스티커 메모, 카톡 광고 제거, 바탕화면 아이콘, 일반, 정보, PowerToys다. 기본 순서는 presets, windows, notes, kakao, desktop, powertoys, general, about이고, `General.TabOrder`에 저장된다.

### 3.3 런타임 데이터 (`%LOCALAPPDATA%\BongHub\` = `C:\Users\Revaptor_FX\AppData\Local\BongHub\`)

| 파일/폴더 | 내용 |
|---|---|
| `config.json` | 전체 설정(아래 키 표) |
| `state.csv`, `monitors_ui.csv` | MultiMonitorTool의 모니터 상태 덤프 |
| `MultiMonitorTool.exe`, `work_layout.cfg` | 임베드 리소스를 실행할 때 푼 사본 |
| `helpers\BongAd.exe` | 카톡 광고 제거 도우미를 푼 사본 |
| `icons\icon_blocks.ico` | 아이콘 사본 |
| `notes\<id>.json` + `<id>.rtf` | 스티커 메모 메타와 본문(RTF, 이미지 포함). 지금 2개가 있다 |
| `desktop\layouts.json` | 바탕화면 아이콘 배치 목록 |
| `cloud\` | 동기화를 켜면 생긴다. `cloud\repo\`(클론)와 `cloud\state.json`이 들어간다(코드 기준, 지금은 없다) |

**config.json 키(코드의 기본값 기준):**
- `Version`(=2)
- `Presets[]`(Preset):
  - 기본 동작 키: `Name`, `Hotkey`, `Enable[]`, `Disable[]`(모니터 Short ID, 예: SAM747A, SAM73D3, GSM76F5, SAM100B, DRS2555), `Primary`, `Layout`(MMT cfg)
  - 대기 시간과 재시도: `Retries`=1, `EnableDelay`=1500, `StepDelay`=2000, `LayoutDelay`=3000, `RulesDelay`=3000(ms)
  - 화면 처리: `RefreshTaskbar`=true, `Overlay`=true, `OverlayMessage`
  - 연계 항목: `Rules[]`(프리셋 전용 창 규칙), `Brightness[]`(MonBright), `DeskLayout`(연결된 아이콘 배치 id, ""이면 없음). 3.3.1부터 `Workspace`(연결된 작업 영역 id, ""이면 없음) 키가 추가된다(`_dev\BongHub.cs`에서 확인)
  - 기본 프리셋: "작업"(Ctrl+Alt+1, 주 모니터 SAM73D3), "자리비움"(Ctrl+Alt+0, 주 모니터 GSM76F5)
- `General`: `IdleEnabled`=true, `IdleMinutes`(코드 기본값 30, 실제 값 15), `IdlePreset`="자리비움", `SkipIfAudio`=true, `AudioThreshold`=0.001, `OverlayEnabled`=true, `OverlayOpacity`=82, `TabOrder[]`. 그 밖의 키는 코드에서 확인할 것
- `Kakao`: `AdBlock`(코드 기본값 false, 실제 값 true), `Compact`=true, `Dark`=false, `AutoApply`=true
- `GlobalRules[]`(WindowRule): `Enabled`, `Name`, `Process`, `Title`, `TitleRegex`, `Class`, `Monitor`, `Action`(pos/monitor/max/min/restore/none/zone), `X`, `Y`, `W`, `H`, `Pin`, `Keep`, `Zone`
- `Notes`: `DefaultColor`="g:blocks"(그라데이션), `Gradient`=true, `ColorVer`, `FontSize`=11, `Opacity`=100, `Hotkey`="Ctrl+Alt+N", `OpenOnStartup`=true
- `Desk`: `AutoSave`=true, `AutoSaveMinutes`=10, `SaveOnExit`, `SaveBeforeSwitch`, `RestoreOnChange`=true, `RestoreOnStartup`=false, `HotkeySave`, `HotkeyRestore`
- `Cloud`: `Enabled`=false, `Repo`="https://github.com/Baegovda/BongHub.git", `Branch`="main", `PullMinutes`=2
- `Pt`: `Zones`, `Grab`, `Work`, `Palette`, `Paste`, `Peek`, `Resizer`(4.7장)

주의: 실제 `config.json`은 02:x 버전 형식으로 03:18 KST에 마지막으로 저장됐다. 그래서 Desk, Cloud, Pt 키가 아직 파일에 없고 코드 기본값으로 동작한다. 설정창에서 무언가를 바꾸면 그때 모든 키가 써진다.

### 3.4 클라우드 동기화 레이아웃 (레포 `Baegovda/BongHub`, main)
- 앱이 관리하는 경로: `README.md`(없으면 자동 생성), `data/meta.json`(스키마 버전), `data/config.json` + `data/config.stamp.json`, `data/notes/<id>.json|.rtf` + `data/notes/_deleted.json`(삭제 기록), `data/desktop/layouts.json`, `data/devices/<기기명>.json`
- 충돌 처리: 파일별로 수정 시각을 비교하고 1초보다 새로운 쪽이 이긴다. 삭제는 `_deleted.json`에 기록해서 전파한다. 원격 스키마가 더 새로우면 보호한다(CloudVersionException)
- 인증: PC의 GitHub CLI(`C:\Program Files\GitHub CLI\gh.exe`)를 쓴다. 토큰은 Windows 자격 증명 관리자(keyring)에 있다. 설정 → 일반의 "GitHub 연결" 버튼은 `gh auth login --web` 후 `gh auth setup-git`을 실행한다. "연결 확인" 버튼도 있다
- 변경 즉시 push, `PullMinutes`(2분)마다 pull(코드 기준. 실제 동작은 사용자가 켠 뒤 확인 필요)
- 사람이 관리하는 경로: `legacy/BongAd/`(삭제된 BongAd 레포의 소스 사본), `docs/HANDOFF.md`(이 문서). **`data/`는 사람이 직접 건드리지 않는다.**

---

## 4. 기능별 상세

### 4.1 모니터 프리셋 (탭 "모니터 프리셋")
- 동작: MultiMonitorTool을 `/enable`, `/disable`, `/SetPrimary`, `/LoadConfig`, `/SaveConfig`, `/scomma` 순서로 실행해 모니터를 켜고 끄고 주 모니터를 바꾼다. 필요하면 탐색기와 DisplayFusion을 재시작해 작업 표시줄을 새로 고친다. 이어서 창 규칙을 적용한다.
- 단축키: 작업 `Ctrl+Alt+1`, 자리비움 `Ctrl+Alt+0`. 프리셋마다 바꿀 수 있다.
- 자동 자리비움: `GetLastInputInfo`로 입력이 IdleMinutes 동안 없으면 IdlePreset으로 전환한다. WASAPI 피크값이 AudioThreshold를 넘으면(소리 재생 중) 건너뛴다.
- 프리셋 추가, 삭제, 편집이 된다. 모니터별 켜기/끄기, 주 모니터, 대기 시간, 오버레이 문구를 정할 수 있다.
- 3.2.0부터 **프리셋별 밝기**: 전체 또는 모니터별 0~100%이고 "변경 안 함"도 있다. DDC/CI → VCP 0x10 → WMI(노트북) 순서로 시도한다. 모니터가 켜질 때까지 재시도하고, 실패해도 전환은 계속하면서 상태를 표시한다. DRS2555는 DDC/CI를 지원하지 않는 것으로 보인다(모의 테스트 문구 기준, 추정).
- 3.2.0부터 **프리셋별 바탕화면 아이콘 배치 연계**: 화면이 안정된 뒤(StableMs 1500, 최대 8000ms) 한 번만 복원한다. 자동 복원과 겹치지 않게 막고, 프로세스 사이 스탬프를 쓴다.
- 3.3.1(진행 중) **프리셋별 작업 영역 연계**: 편집 화면에 "창 배치(작업 영역)" 드롭다운이 생긴다. 밝기 → 아이콘 배치 복원 다음, 오버레이가 닫히기 전에 정확히 한 번 실행한다. 뮤텍스와 10초 스탬프로 중복을 막는다.
- **실제 전환은 한 번도 봇이 실행해 보지 않았다.** 사용자가 직접 확인해야 한다.

### 4.2 전환 오버레이(스피너)
- 주 모니터에 큰 스피너와 "전환 중" 문구를 띄운다. 별도 UI 스레드에서 돌고 DPI를 인식한다. 불투명도는 `OverlayOpacity`로 정한다.
- 3.0.0에서 새로 디자인했다. 3.2.0에서 가운데 아이콘 떨림을 고쳤다(매 프레임 크기를 바꿔 소수점 좌표에 그리던 것을 고정 크기와 정수 좌표로 바꿈). 160프레임 측정에서 중심 이동이 1.2px에서 0px로 줄었다.

### 4.3 창 위치 규칙 (탭 "창 위치", DisplayFusion 방식)
- 프로세스, 제목(정규식 가능), 클래스로 창을 찾는다. 동작은 위치·크기 지정, 모니터로만 이동, 최대화, 최소화, 원래 크기로 복원, 변경 없음, FancyZones 영역에 맞춤 중에서 고른다. 항상 위(Pin)와 위치 유지(Keep, SetWinEventHook) 옵션이 있다.
- 전역 규칙과 프리셋별 규칙이 있다. 창을 잡아서 규칙을 만드는 CaptureDialog가 있다.
- 3.1.0에서 유휴 CPU를 줄였다. 시스템 전체의 위치 변경 이벤트를 받던 것을 최상위 창만 받게 바꿨고, 점검 주기를 5초에서 15초로 늘렸다.

### 4.4 카톡 광고 제거 (탭 "카톡 광고 제거", 옛 BongAd 2.4)
- 내장한 `BongAd.exe`를 `%LOCALAPPDATA%\BongHub\helpers\`에 풀어서 실행한다. 플래그는 `/once`, `/restore`, `/dark`, `/nocompact`이고, 단일 인스턴스로 새 실행이 기존 것을 대체한다. 레이아웃 상수는 125% 배율에 맞춰져 있다.
- 옵션: 광고 숨김(AdBlock), 컴팩트(Compact), 다크 오버레이(Dark, 끌 수 있음), 자동 적용(AutoApply). 바꾸면 즉시 적용된다.
- 옛 시작 바로가기 `BongAd.lnk`(/dark)는 제거했다. 사본은 `backup\BongAd.lnk.bak`에 있다. 코드에 옛 lnk를 정리하는 부분이 있다.
- 원본 소스: 레포 BongHub의 `legacy/BongAd/`(BongAd.c, build.bat 등). 원래 BongAd 레포는 사용자가 승인한 뒤 삭제했다.

### 4.5 스티커 메모 (탭 "스티커 메모")
- 새 메모는 `Ctrl+Alt+N`이나 트레이 메뉴로 만든다. 기본은 그라데이션("g:blocks")이고 팔레트 팝업(SwatchGrid)으로 색을 고른다. 항상 위, 불투명도, 서식 도구, 자동 저장이 되고, 시작할 때 열어둔 메모를 복원한다.
- 타이틀바와 본문은 하나의 그라데이션 면으로 이어진다(본문은 같은 계열의 단색이다. 투명 본문은 스크롤할 때 글자가 안 보여서 되돌렸다). 스크롤바는 얇은 오버레이 방식이다.
- 이미지: 붙여넣기가 된다. 클릭하면 핸들이 나오고 비율을 유지한 채 리사이즈한다(Shift를 누르면 자유 비율). 크기 툴팁이 뜬다. 우클릭 메뉴는 이미지 복사, 25/50/75/100%, 폭 맞춤, 원본, 삭제다. Delete 키로 지운다. 더블클릭하면 원본 뷰어가 열리고 휠로 확대, 드래그로 이동, 맞춤/100% 전환이 된다.
- 메모 크기가 8MB(`WarnBytes`)를 넘으면 경고한다. 붙여넣은 이미지는 긴 변 1600px(`MaxEdge`)로 줄여 저장한다(코드 상수 기준, 추정). Windows 스티커 메모의 데이터는 건드리지 않는다.

### 4.6 바탕화면 아이콘 (탭 "바탕화면 아이콘", DesktopOK 방식)
- 아이콘 배치를 저장, 복원, 이름 변경, 삭제한다. 해상도마다 키가 있다. 자동 저장은 10분마다 하고, 종료할 때와 전환 전에도 저장한다. 해상도가 바뀌면 자동 복원한다. 시작할 때 복원은 기본 꺼짐이다. 저장과 복원 단축키를 정할 수 있다.
- 데이터는 `desktop\layouts.json`에 있다. 실제 복원은 모의 테스트만 했다.

### 4.7 PowerToys 탭 (Microsoft PowerToys는 MIT 라이선스이고, 정보 탭에 고지문이 있다)
카드 그리드에 아이콘, 설명, 즉시 토글, 상태 배지가 있다. 카드를 누르면 상세 화면이 열린다. 검색과 단축키 충돌 표시가 된다.

| 기능 | 기본값 | 단축키 / 사용법 | 비고 |
|---|---|---|---|
| FancyZones | 꺼짐 | Shift+드래그로 스냅, Ctrl로 여러 영역 걸치기, Win+←/→ 이동(기본 꺼짐) | 영역 편집기, 모니터별 레이아웃, 제외 앱 목록. 가상 데스크톱별 레이아웃은 공개 API가 없어서 **넣지 못했다** |
| 잡아서 이동 | 꺼짐(`Grab.Enabled`=false, `Modifier`="Alt", `Resize`=true, `SkipFullscreen`=true) | 보조 키(Alt/Win/Ctrl+Alt)+왼쪽 드래그로 이동, +오른쪽 드래그로 크기 조정 | 후킹 콜백은 바로 반환하고, 창 이동은 전용 스레드에서 처리한다 |
| 작업 영역 | 켜짐(`Work.Enabled`=true) | 작업 영역별 단축키, `BongHub.exe workspace <이름>`, 바탕화면 바로가기 "<이름> (작업 영역).lnk" | 이미 열린 창을 옮기거나(MoveExisting) 없는 앱을 실행한다(LaunchMissing) |
| 명령 팔레트 | 꺼짐 | `Ctrl+Alt+Space` | 앱(시작 메뉴와 스토어), 계산기, 단위 변환, 세계 시각, 창 전환 `<`, 파일 검색 `?`, 명령 실행 `>`, 웹 검색 `??`, 터미널 프로필, 시스템 명령(확인 후), 설정 페이지. 타사 확장은 지원하지 않는다 |
| 고급 붙여넣기 | 꺼짐 | `Ctrl+Alt+V`, 서식 없이 `Ctrl+Alt+Shift+V`, JSON/MD 단축키는 비어 있음 | 사용자 지정 동작(정규식, 템플릿, 대소문자, 정렬, 중복 제거, URL, Base64), 파일로 붙여넣기. AI, OCR, 오디오/비디오 변환은 **빠졌다** |
| 미리보기(Peek) | 꺼짐 | `Ctrl+Shift+Space`(탐색기에서 파일 선택) | 이미지, 코드 강조, 마크다운/HTML(스크립트 차단), 동영상, ZIP, 폴더, PDF/Office(설치된 미리보기 처리기) |
| Image Resizer | 켜짐, 우클릭 메뉴는 꺼짐 | 끌어 놓기, `BongHub.exe resize`, 선택형 탐색기 우클릭(HKCU) | WIC 엔진 병렬 처리, 단위 px/%/cm/in, 파일 이름 형식 %1~%6, 메타데이터 옵션 |

### 4.8 일반 / 정보 / UI 공통
- 일반 탭: 자동 시작, 자동 자리비움, 오버레이, 클라우드 동기화(켜기, 연결 확인, GitHub 연결), 탭 순서 등.
- 정보 탭: 버전, 구성 요소 버전, 변경 이력, MIT 고지.
- 테마: 그라데이션 다크. 강조색은 처음에 #5B8CFF였고 3.x의 `UI.Accent`는 RGB(124,140,255)=#7C8CFF, 사이드바 #131224, 페이지 #0F0F1B다. FancyZones 영역 기본색은 #5A6CFF, 강조색은 #6EC6FF다. 커스텀 타이틀바는 설정창, 확인창, 입력창에 모두 쓴다. 탭은 드래그로 순서를 바꿀 수 있고 저장된다. 사이드바 브랜드 헤더에 "석봉이의 유틸 창고"가 있다.
- 트레이 메뉴(확인한 항목): 모니터 프리셋(하위 메뉴) · 새 스티커 메모 · 스티커 메모 모두 보이기/숨기기 · 바탕화면 아이콘(지금 배치 저장, 현재 화면 배치로 복원, 배치 관리…) · 카톡 광고 제거(광고 숨김, 컴팩트 레이아웃, 다크 오버레이, 원상복구) · 설정, 종료 등(나머지는 `TrayApp` 참고).

### 4.9 단축키 총정리(기본값)

| 단축키 | 기능 |
|---|---|
| Ctrl+Alt+1 | 작업 프리셋 |
| Ctrl+Alt+0 | 자리비움 프리셋 |
| Ctrl+Alt+N | 새 스티커 메모 |
| Ctrl+Alt+Space | 명령 팔레트(기능을 켰을 때) |
| Ctrl+Alt+V / Ctrl+Alt+Shift+V | 고급 붙여넣기 / 서식 없이 붙여넣기(기능을 켰을 때) |
| Ctrl+Shift+Space | 미리보기(기능을 켰을 때) |
| Shift+드래그, Ctrl+드래그, Win+←/→ | FancyZones |
| Alt+왼쪽 드래그 / Alt+오른쪽 드래그 | 잡아서 이동: 이동 / 크기 조정(켰을 때, `Grab.Modifier` 기본값 Alt) |

단축키는 `RegisterHotKey`(MOD_NOREPEAT)로 등록한다. 등록에 실패하면 알린다. 단축키 변경은 **설정창을 닫을 때** 적용된다(3.2.0 보고 기준).

---

## 5. 빌드, 배포, 롤백

### 5.1 build.ps1 동작
1. `BongHub.cs`에서 `const string Version = "x.y.z"`(AppInfo.Version)를 읽는다.
2. `[assembly: AssemblyFileVersion("x.y.z.0")]`과 맞지 않으면 실패한다. 버전을 올릴 때는 **AppInfo.Version과 AssemblyVersion, AssemblyFileVersion, AssemblyInformationalVersion 네 곳을 함께** 바꾼다.
3. `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /codepage:65001 /optimize+`로 빌드한다. 리소스는 MultiMonitorTool.exe, work_layout.cfg, BongAd.exe, icon_blocks.ico이고 win32icon은 icon_blocks.ico다. 참조는 WinForms, Drawing, Web.Extensions, Core, Security, Data, Xml, Management, IO.Compression(.FileSystem), WPF PresentationCore, WindowsBase, Xaml이다.
4. 출력은 `BongHub\BongHub.exe`다. 그래서 **실행 중인 배포본을 먼저 종료해야** 덮어쓸 수 있다.

개발 빌드는 `_dev\dev.ps1`로 한다. 같은 csc 옵션으로 `_dev\BongHub.exe`를 만들고, 테스트 함수 `T <모드> <초>`를 제공한다.

### 5.2 배포 절차(PC PowerShell)
```powershell
cd C:\Users\Revaptor_FX\BongHub
$v = (Get-Item BongHub.exe).VersionInfo.FileVersion -replace '\.0$',''
$ts = Get-Date -Format yyyyMMdd_HHmmss
Copy-Item BongHub.cs   "backup\BongHub.$v.$ts.cs"
Copy-Item BongHub.exe  "backup\BongHub.$v.$ts.exe"
Copy-Item build.ps1    "backup\build.$v.$ts.ps1"
Copy-Item CHANGELOG.md "backup\CHANGELOG.$v.$ts.md"
# 새 소스와 CHANGELOG 반영(예: _dev에서 복사)
Copy-Item _dev\BongHub.cs BongHub.cs; Copy-Item _dev\CHANGELOG.md CHANGELOG.md
Get-Process BongHub -EA 0 | ? Path -eq "$PWD\BongHub.exe" | Stop-Process
powershell -ExecutionPolicy Bypass -File .\build.ps1
Start-Process .\BongHub.exe
```
- 배포 뒤에는 시작 바로가기가 그대로 BongHub.exe를 가리키는지, 실행 중인 버전이 맞는지(`(Get-Item BongHub.exe).VersionInfo.FileVersion`), 1분 뒤 유휴 CPU와 메모리를 확인해서 보고한다.
- 3.2.0 백업은 `.bak` 확장자 없이 저장돼 있다(`backup\BongHub.3.2.0.20261031_153633.cs/.exe`). 이름 규칙이 섞여 있으니 둘 다 인식할 것.

### 5.3 롤백
```powershell
cd C:\Users\Revaptor_FX\BongHub
Get-Process BongHub -EA 0 | ? Path -eq "$PWD\BongHub.exe" | Stop-Process
Copy-Item backup\BongHub.3.2.0.20261031_153633.exe BongHub.exe -Force   # 예: 3.2.0으로
Copy-Item backup\BongHub.3.2.0.20261031_153633.cs  BongHub.cs  -Force
Start-Process .\BongHub.exe
```
config.json은 하위 호환으로 이전된다. 다만 새 키가 들어간 설정을 옛 버전이 저장하면 새 키가 사라질 수 있다(추정).

### 5.4 시작프로그램
`Startup` 클래스가 `Startup\BongHub.lnk`를 관리한다(`LinkName`="BongHub.lnk"). 옛 바로가기 이름 `OldLinkName`="모니터 단축키.lnk"는 정리 대상이다. 일반 탭의 자동 시작 토글과 연결돼 있다.

---

## 6. 테스트

### 6.1 숨은 실행 원칙
- **숨은 데스크톱:** `_dev\testdesk.ps1`가 `CreateDesktop("BongHubTest")`로 보이지 않는 Windows 데스크톱을 만들고 그 안에서 테스트 모드를 실행한다. 사용자 화면에는 아무것도 뜨지 않는다.
- **화면 밖 좌표:** `TestEnv.Off=true`이면 창을 (-30000,-30000) 3840×2160 영역에 놓는다. 이때 창은 활성화되지 않고 작업 표시줄에도 안 나온다(WS_EX_NOACTIVATE|TOOLWINDOW). 캡처는 PrintWindow로 한다. `offshot.ps1`은 100ms마다 테스트 프로세스의 창이 실제 모니터와 겹치는지 검사해서 이를 증명한다.
- TestEnv.Off가 자동으로 켜지는 모드: settingstest, tabbench, notestest, imagetest, pttest, jittertest, perfbench, paritytest.
- 결과는 exe 폴더의 txt/png로 남는다. 예외가 나면 `crash_<모드>.txt`가 생기고 exit 3이다.

### 6.2 모드 목록(Program.Main의 switch에서 확인)

| 모드 | 의미 |
|---|---|
| `configtest` | 설정 불러오기, 저장, 이전(MonitorMode → BongHub, 옛 스키마) |
| `windowtest` | 창 규칙 매칭과 적용 로직 |
| `kakaotest` | 카톡 광고 제거 도우미 시작, 교체, 종료 |
| `notestest` | 스티커 메모 저장, 복원, 색, 이미지(화면 밖) |
| `notesbench <n>` | 메모 성능(기본 45) |
| `imagetest` | 메모 이미지 리사이즈, 복사, 뷰어 |
| `settingstest [인수]` | 설정창 모든 탭 렌더링과 미리보기 캡처(화면 밖) |
| `tabbench [인수]` | 탭 전환 시간 측정 |
| `cloudtest` | 임시 폴더의 가짜 기기 PC-A, PC-B로 동기화(충돌, 삭제 전파) 검증. 실제 레포에는 쓰지 않는다(코드 기준) |
| `icontest` | 아이콘 리소스와 바로가기 아이콘 |
| `pttest` | PowerToys 7개 기능의 로직과 켜기/끄기 시 후킹 해제 |
| `paritytest` | PowerToys 기능 동등성 점검(TestEnv 목록에만 있고 switch에는 case가 없다. 3.3.0에서 통합된 것으로 추정) |
| `perfbench` | 시작 시간, 스냅 계산, 팔레트, Peek, Resizer 성능. 결과는 `_dev\perfbench_3.3.0.txt` |
| `jittertest` | 오버레이 아이콘 떨림(프레임마다 중심 좌표) |
| `extrastest` | 프리셋 밝기, 아이콘 배치, 작업 영역 연계(MockBright, ThrowBright 사용) |
| `overlaytest` | 전환 오버레이 표시. **주 모니터에 실제로 뜰 수 있으니** 반드시 숨은 데스크톱에서만 실행한다 |
| `watchbench <초>` | 창 감시 CPU 측정(기본 40) |
| `version` | version.txt 쓰기 |

### 6.3 절대 실행하면 안 되는 것
- `BongHub.exe 작업`, `BongHub.exe 자리비움`, `work`, `away` 등 **프리셋 이름 인수**. 실제 모니터가 전환된다.
- MultiMonitorTool `/enable`, `/disable`, `/SetPrimary`, `/LoadConfig`의 직접 실행
- 실제 밝기 변경(RealBright), 실제 바탕화면 아이콘 복원, `workspace <이름>`(실제 창을 옮기고 실행한다)
- 사용자 데스크톱에서 설정창이나 테스트 창 띄우기, 탐색기 또는 DisplayFusion 재시작
- 실제 레포 `data/`에 테스트 데이터 쓰기

---

## 7. 환경과 도구

- **PC**: Windows, machineId `d3fb99ad-25ca-45c1-81af-41e5ca09104a`(Revaptor-FX). 사용자가 데스크톱에서 보낼 때는 다른 machineId(`ae865bf3-9d44-4d5a-a352-b2d3c1cdce08`, 기기 종류 미확인)가 붙는 경우도 있었다. 작업 대상 PC는 d3fb99ad...다.
  - PC의 gh: `C:\Program Files\GitHub CLI\gh.exe`, 계정 Baegovda(keyring), 스코프 delete_repo, gist, read:org, repo, workflow.
  - 이 밖에 DisplayFusion, Everything, MSI Afterburner, PureRef, Notion 등이 설치돼 있다. Sizer는 사용자 요청으로 제거했다.
- **박스(봇 리눅스 머신)**: PC와 **파일시스템이 다르다**. 파일은 CopyToBox, CopyFromBox로 옮긴다. 박스의 gh는 Baegovda로 로그인돼 있고 토큰은 `/home/box/.config/gh/hosts.yml`에 있다. 스코프는 delete_repo, gist, read:org, repo다. `gh auth setup-git`이 설정돼 있다. **새 계정의 박스에는 이 로그인이 없다.** 필요하면 `gh auth login --web`(기기 코드 방식)으로 사용자 승인을 받는다.
- GitHub 연결 도구(cursor-github)로는 레포 생성, 삭제, 공개 범위 변경을 할 수 없다. 그래서 gh CLI의 기기 인증을 썼다.
- **에이전트 역할 분담**: 대화 담당 봇(BongHub)이 요구를 받아 executor 서브에이전트에게 자기완결 브리프로 일을 맡겼다. executor가 PC에서 빌드, 테스트, 배포를 했다. 팀 봇으로 ForgeLens, BongExtend, BongGIF 등이 있고, BongAd 봇은 삭제 대상이다(사용자가 직접 삭제).
- **executor 사용 시 주의**:
  1. 진행 중에 추가 지시(steer)가 유실된 적이 있다. 지시는 그것만 읽어도 이해되는 완결된 브리프로 보내고, 반영됐는지 결과에서 확인한다.
  2. 작업이 길면 중간에 끊긴 적이 있다(3.3.0 패리티 작업). 단계별로 결과를 파일로 남기게 한다.
  3. 같은 BongHub 폴더를 두 작업이 동시에 수정하지 않게 한다. 개발은 `_dev`에서 하고 배포만 루트에서 한다.
  4. 테스트 창 노출 금지와 실제 전환 금지 규칙을 브리프마다 다시 적는다.

---

## 8. 이력

### 8.1 타임라인(KST, 2026-10-30~31)
1. 30일 밤: AHK, bat, PS 방식을 단일 C# 트레이 앱 MonitorMode로 바꿨다. AutoHotkey를 제거했다.
2. 전환 스피너 오버레이(v1overlay)를 넣고, Windows 시작 시 자동 실행을 설정하고, 메모리와 CPU를 보고했다.
3. 상세 설정 UI를 만들었다. 프리셋 추가와 삭제, DisplayFusion식 창 위치 규칙이 들어갔다. "직관적인 UI가 관건"이라는 요구를 반영했다.
4. Sizer와 관련 파일을 모두 제거했다.
5. 31일 01시대: 세련된 다크 모드(v3dark), 테스트 파일 정리, 이름을 BongMonitor로 바꿨다(v2name). 제목과 화면 표기("멀티모니터")가 다른 문제를 고쳤다.
6. BongAd 흡수(v4kakao, 02:31): BongAd 봇에게 인수인계를 받았다. 이름을 BongUtil로 검토하다가 Bong 이름 20개를 추천했고 BongHub로 정했다. 봇 이름도 BongHub로 바꿨다.
7. 스티커 메모, "모니터 프리셋"으로 이름 변경, 아이콘 디자인(허브, 블록 등)과 그라데이션, 카톡 다크 끄기, "카톡 광고 제거"로 표기 변경(v4pre, 02:52).
8. 3.0.0(03:41): 블록 아이콘 고정, 탭 전환 최적화, DesktopOK식 바탕화면 아이콘, 즉시 적용, 그라데이션 UI 전면 개편, 새 스피너, 커스텀 타이틀바, 탭 드래그, 클라우드 동기화. 사용자가 기기 인증으로 비공개 레포 Baegovda/BongHub를 만들었다. "석봉이의 유틸 창고" 문구가 들어갔다.
9. BongAd 소스를 `legacy/BongAd`에 사본으로 저장한 뒤 BongAd 레포를 삭제했다(사용자 승인, 두 번째 기기 인증).
10. 3.1.0(04:11): CPU 7.66%를 0%로 줄였다. 메모 타이틀바와 본문을 하나로 잇고, 얇은 스크롤바, 이미지 리사이즈, 복사, 뷰어, 버전 관리를 넣었다. 테스트 창이 보이던 문제를 숨은 데스크톱으로 해결했다.
11. 3.2.0(04:34): PowerToys 탭 7개 기능, 스피너 떨림 수정, 프리셋별 밝기, 아이콘 배치 연계.
12. 3.3.0(15:36): PowerToys 기능 맞춤과 최적화. 중간에 한 번 끊겨서 다시 진행했다.
13. 3.3.1(16:00 배포, 최종 보고 대기): 프리셋과 작업 영역 연계. 15:58~16:00 KST에 `_dev`에서 테스트한 뒤 루트에 배포했다.
14. 16시경: 이 인수인계 문서를 작성했다.

### 8.2 해결한 문제와 실패 사례
- settingstest에서 CopyFromScreen 예외가 났다(`settingstest_error.txt`). 화면 캡처를 PrintWindow로 바꿔 해결했다.
- 테스트 창이 사용자 화면에 노출됐다(두 번 항의). 숨은 데스크톱과 offshot 검증으로 해결했다.
- 배포 직후 유휴 CPU가 7.66%였다. 창 감시를 줄여 0%로 만들었다.
- 3.3.0 작업 중 executor가 끊겼다. 상태를 점검한 뒤 다시 시작했다.
- 메모 본문을 투명하게 하는 시도는 스크롤할 때 글자가 안 보여서 되돌렸다.
- 스티커 메모의 "불투명도" 라벨이 슬라이더보다 살짝 아래에 있던 문제는 3.0.0 개편 이후 해결 여부를 확인하지 않았다.

### 8.3 성능 전/후

| 항목 | 전 | 후 |
|---|---|---|
| 탭 전환 평균 | 약 330~340ms | 29ms(3.0.0) → 18~19ms(3.1.0) → 13.6ms(3.2.0) |
| 유휴 CPU | 7.66%(3.0.0) | 0%(3.1.0 이후) |
| 유휴 메모리 | 42MB(메모 3개, 2.x) → 55MB(3.1) → 54MB(3.2) | **74MB(3.3.0, 늘어남)**. 실행 중인 프로세스의 작업 집합은 약 77MB |
| CLI 시작 | 42ms | 61ms(3.3.0, 원인 미확정). perfbench의 startup_to_bench는 41ms |
| 스냅 계산 | 163µs | 0.29µs |
| Peek 6000×4000 JPG | 131ms | 28ms |
| Resizer 12MP 16장 | 1.98s | 0.18s |
| 마우스 후킹(이동) | 108ns | 10ns |
| 오버레이 아이콘 떨림 | 1.2px | 0px |

### 8.4 사용자와 합의한 결정, 질문과 답 (누락 방지용)
- 이름: Bong을 붙인 이름 20개를 추천했고 사용자가 "BongHub로 가자"고 정했다. 봇 이름도 BongHub로 바꿨다.
- 아이콘: 처음에 허브 노드와 모듈 블록 두 가지를 고를 수 있게 했다. 이후 "블록으로 확정, 나머지 지워, 옵션에서도 선택 못 하게"로 바뀌었다.
- 사용자가 "PowerToys 오픈소스야?"라고 물었다. 답: MIT 라이선스이고, 코드를 가져다 쓰면 고지문을 남긴다(정보 탭에 넣었다).
- "Windows 스티커 메모 오픈소스야?"에 대한 답: 아니다. BongHub의 메모는 처음부터 직접 만들었다.
- "원래 레포는 어디 가?"에 대한 답: 기존 레포는 그대로 있다. BongHub 레포는 새로 추가한 것이다. 이후 BongAd 레포만 사용자 지시로 삭제했다.
- "버전 체크"의 뜻: 빌드마다 버전을 올리고 이전 버전과 회귀 비교를 한다는 뜻으로 해석했고 사용자가 확인했다("제대로 이해했으"). 자동 업데이트 기능은 아니다.
- 탭 이름: "카카오톡" → "카톡 광고 제거", "프리셋" → "모니터 프리셋".
- 메인 화면 부제: "모니터 · 창 · 메모 · 카톡" → "석봉이의 유틸 창고".
- PowerToys의 7개 기능(고급 붙여넣기, 명령 팔레트, 미리보기, 잡아서 이동, 작업 영역, FancyZones, Image Resizer)은 모두 "PowerToys" 탭 하나에 카드로 모았다. 요구 수준은 "기존과 기능은 완전히 똑같되 훨씬 가볍고 빠르게"다.
- 프리셋 연계는 전환이 끝날 무렵 한 번 실행한다. 순서: 화면 안정 → 밝기 → 아이콘 배치 → 작업 영역(3.3.1).
- Sizer는 무슨 앱인지 확인한 뒤 사용자 요청으로 관련 파일까지 모두 제거했다.

---

## 9. 미해결 이슈, 한계, 다음 할 일

**사용자가 직접 확인해야 할 것(봇이 실제로 실행하지 않음):**
- [ ] `Ctrl+Alt+1` / `Ctrl+Alt+0` 실제 모니터 전환(한 번도 확인하지 않음)
- [ ] 자동 자리비움(입력 없음 15분, 소리 재생 중이면 건너뜀)
- [ ] 프리셋별 실제 밝기 변경과 아이콘 배치 복원
- [ ] 3.3.1 작업 영역 연계(배포 후)
- [ ] 카톡 광고 제거와 다크 오버레이의 실제 동작
- [ ] FancyZones 드래그 스냅, 잡아서 이동, 탐색기 Peek, 작업 영역 캡처와 배치, 명령 팔레트와 고급 붙여넣기 단축키, 시스템 명령
- [ ] 메모 이미지 핸들 드래그의 실제 사용감
- [ ] 클라우드 동기화를 켜고 연결 확인(아직 켜지 않음)

**알려진 한계:**
- 메모리 74MB로 증가, CLI 시작 61ms로 지연(원인 미확정).
- 가상 데스크톱별 FancyZones 레이아웃은 넣지 못했다(공개 API 없음).
- 고급 붙여넣기의 AI, OCR, 오디오/비디오 변환은 빠졌다. 명령 팔레트의 타사 확장은 지원하지 않는다.
- exe 파일 자체의 아이콘은 실행 중에 바꿀 수 없다(지금은 블록으로 고정돼 있어서 상관없다).
- 단축키 변경은 설정창을 닫을 때 반영된다.

**다음 할 일(우선순위 순):**
1. 3.3.1이 배포됐는지와 회귀 테스트 결과(`_dev\*.txt`)를 확인하고 사용자에게 보고한다. 실패한 항목이 있으면 고친 뒤 3.3.2로 배포한다
2. 사용자의 실사용 피드백 반영(기능별 실사용 점검 목록 제안)
3. 메모리와 CLI 시작 시간 최적화(사용자가 원하면)
4. 정리 제안(사용자 확인 후): `MonitorPresets\`, 바탕화면 `모니터 - *.lnk` 두 개(BongHub CLI로 바꾸거나 삭제), `C:\Users\Revaptor_FX\BongAd\`, `_dev33\`, `v33dev.tgz`, `settingstest_error.txt`
5. BongAd 봇 삭제는 사용자가 직접 한다(삭제했는지 확인)

---

## 10. 보고 스타일과 자주 하는 요청

- 첫 문장에 결론을 쓴다: "BongHub 3.x.x를 배포하고 실행해뒀어요."
- 굵은 머리말 + 짧은 글머리표로 쓴다. 수치는 전/후로 보여준다. 마지막에 "직접 써보고 확인해주셔야 하는 것" 목록을 둔다. 미리보기 경로(`_preview\`)와 CHANGELOG 경로를 알려준다.
- 요청이 이어서 올 때: "네, ~할게요. 진행 중인 빌드에 같이 넣을게요." 한 줄로 받는다. 그다음 executor에 전달하고 "전달했어요"라고 짧게 알린다.
- 예시:
  > BongHub 3.2.0을 배포하고 실행해뒀어요. … **직접 써보고 확인해주셔야 하는 것:** …
- 자주 하는 요청 패턴: 다른 유명 유틸 기능을 "그대로/똑같이" 넣기(DisplayFusion, DesktopOK, PowerToys, Windows 스티커 메모), UI를 더 세련되게, 즉시 적용, 최적화, 이름과 문구 변경, 정리와 삭제, "오픈소스야?" 같은 라이선스 질문, 수면 전에 "끝까지 끝내놔 결과만 알려줘".

---

## 11. 이어받는 봇의 첫 10분 체크리스트

1. 이 문서, `CHANGELOG.md`, `_dev\CHANGELOG.md`를 읽는다.
2. PC 상태를 확인한다(PowerShell, machineId d3fb99ad-...):
```powershell
cd C:\Users\Revaptor_FX\BongHub
Get-Process BongHub -EA 0 | select Id,Path,StartTime,@{n='MB';e={[int]($_.WS/1MB)}}
(Get-Item BongHub.exe).VersionInfo.FileVersion; (Get-Item _dev\BongHub.exe -EA 0).VersionInfo.FileVersion
Select-String -Path BongHub.cs,_dev\BongHub.cs -Pattern 'const string Version = "[^"]+"'
gci _dev | sort LastWriteTime -desc | select -First 15 Name,LastWriteTime
gci backup | sort LastWriteTime -desc | select -First 6 Name
gc _dev\extrastest.txt
$w=New-Object -ComObject WScript.Shell; $w.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\BongHub.lnk").TargetPath
gci $env:LOCALAPPDATA\BongHub; Test-Path $env:LOCALAPPDATA\BongHub\cloud
& 'C:\Program Files\GitHub CLI\gh.exe' auth status
```
3. `_dev\BongHub.exe` 테스트 프로세스가 남아 있으면 종료한다(경로가 `_dev`인 것만).
4. 박스에서 `gh auth status`를 확인한다. 로그인이 없으면 기기 인증으로 사용자 승인을 받는다(레포를 읽고 쓸 때만 필요하다).
5. 레포를 확인한다: `gh api repos/Baegovda/BongHub --jq .private` → true. `data/`가 있으면 동기화가 켜진 것이다.
6. 사용자에게 "인계받았어요. 지금 3.3.x 상태이고 ○○이 남아 있어요"라고 짧게 보고한다.

---

## 12. 용어집

| 용어 | 뜻 |
|---|---|
| 모니터 프리셋 | 켤/끌 모니터, 주 모니터, 레이아웃, 밝기, 아이콘 배치, 작업 영역을 묶은 설정. 기본은 "작업"과 "자리비움" |
| 작업 모드 / 자리비움 | 작업: 모든 모니터(5대) 켜기, 주 모니터 SAM73D3. 자리비움: 주 모니터 GSM76F5 중심으로 일부만 켜기(세부는 config 참고) |
| MMT | NirSoft MultiMonitorTool |
| 오버레이 / 스피너 | 전환 중에 주 모니터에 뜨는 안내 화면 |
| 창 위치 규칙 | DisplayFusion식 앱 창 배치와 고정 규칙 |
| BongAd | 옛 카톡 광고 제거 C 유틸(2.4). 지금은 BongHub에 내장된 도우미다 |
| 작업 영역(Workspaces) | 여러 앱 창의 위치를 저장했다가 한 번에 실행하고 배치하는 기능 |
| 아이콘 배치 | 바탕화면 아이콘 위치를 저장한 것(DesktopOK식) |
| TestEnv.Off | 화면 밖 테스트 모드 플래그 |
| 숨은 데스크톱 | `_dev\testdesk.ps1`, 사용법 `-Modes <모드...> -Sec 180`. CreateDesktop "BongHubTest". 사용자에게 보이지 않는 Win32 데스크톱 |
| executor | 실제 PC 작업을 맡는 서브에이전트 |
| 패리티 | PowerToys와 기능을 똑같이 맞추는 작업 |

---

## 검증 기록

| 라운드 | 확인 방법 | 수정·보강 내용 |
|---|---|---|
| 1 | 코드에서 GrabCfg, UI.Accent, Startup.LinkName, TrayApp 메뉴, `_dev` Preset 키를 grep으로 확인 | 잡아서 이동 기본값을 "추정"에서 확정값(꺼짐, Alt)으로 바꿨다. 강조색 #5B8CFF는 옛 값이라 현재 #7C8CFF로 고쳤다. 3.3.1 프리셋 키 `Workspace`를 확인했다. 트레이 메뉴를 실제 항목으로 바꿨다. OldLinkName을 추가했다 |
| 2 | 부모 대화 기록(t0u~t67u)과 요약을 다시 훑었다 | 이름 결정, 아이콘 결정 변화, 오픈소스 질문 2건, "원래 레포" 질문, 버전 체크 해석, 탭과 문구 변경, Sizer 처리 등 누락된 결정과 질문을 8.4장에 보강했다 |
| 3 | PC에서 버전과 프로세스를 다시 확인했다 | 쓰는 도중에 executor가 **3.3.1을 배포했다**(16:00:40 KST, PID 102548, 55MB). 1장의 상태, 직전 버전 백업명(`backup\BongHub.3.3.0.20261031_160036.*`), 타임라인, 다음 할 일을 고쳤다. "진행 중"을 "배포됨, 최종 보고 대기"로 구체화했다 |
| 4 | 문서에 나온 경로 25개를 PC에서 Test-Path로 확인(모두 True). `case "paritytest"`가 없는 것을 확인. WarnBytes와 MaxEdge 확인 | 메모 경고 기준을 8MB로 구체화했다. testdesk.ps1 사용법을 추가했다. paritytest는 switch에 없다는 점을 유지했다 |
| 5 | 비밀값을 검사했다(gho_, 비밀번호 문자열 없음). 표와 코드 블록 문법, 시각(KST) 표기를 통일했다. 레포에 올린 뒤 내용을 대조했다 | 작성 시각을 정정했다. 레포 업로드 후 SHA와 줄 수를 대조했다(보고 참조) |

**확인하지 못한 항목:** executor의 3.3.1 최종 보고 내용, 3.3.1의 메모리와 CPU 안정값, 스티커 메모 "불투명도" 라벨 정렬 문제가 해결됐는지, 카톡, 전환, 드래그 기능의 실제 동작(정책상 실행 금지), `General`의 세부 키 전체 목록, 클라우드 pull과 push의 실제 동작(아직 켜지 않음).
