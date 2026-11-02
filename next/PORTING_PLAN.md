# BongHub Next 이식 계획

> 기준일: 2026-11-02 KST. 소스 기준은 `cursor/next-writer-lock-c7a7`(PR #4)의 트레이 **3.4.8** (`app/BongHub.cs`)과 Next **0.2.0** (`next/BongHub.Next`)이에요. 이 문서는 계획만 담아요. `app/`와 `next/` 소스는 이 작업에서 바꾸지 않아요.

## 결론

**3.4.x는 지금 기능에서 얼려요.** 버그 수정과 데이터 형식 호환만 하고, 새 기능은 Next에만 넣어요. **3.4.x 은퇴는 Next가 아래 목록을 모두 ‘있음’으로 채운 뒤**예요. 그 전에 배포 폴더의 exe를 Next로 바꾸거나, 시작프로그램 바로가기를 Next로 돌리거나, `Local\BongHub_Tray`를 Next가 차지하지 않아요.

**UI는 WPF를 유지해요.** WinUI 3(Windows App SDK 안정 2.6.4)으로 갈아타지 않아요. 메모의 저장 형식이 Win32 RichEdit RTF(`\pngblip`/`\jpegblip`)이고, 전역 후킹·DDC/CI·탐색기 목록은 Win32예요. WinUI 3은 그 표면을 다시 짜야 하고, 비패키지 배포는 Windows App SDK 런타임이 따로 필요해요. 지원 창도 짧아요(2.0 계열 서비스 종료 2027-04-29). 지금 셸이 이미 WPF예요.

**대상 프레임워크는 `net10.0-windows`예요.** .NET 10은 LTS이고, 2025-11-11에 나왔어요. 2026-11-02 기준 최신 안정 패치는 **런타임 10.0.14 / SDK 10.0.403**(2026-10-25)이고, 지원 종료는 **2028-11-14**예요. .NET 8(8.0.33)과 .NET 9(9.0.22)는 둘 다 **2026-11-10**에 지원이 끝나요. 9는 STS라 목표로 두지 않아요. 주인 PC의 기존 빌드는 SDK 8.0.426이에요. 0단계에 .NET 10 Desktop Runtime과 SDK를 설치해요.

**Native AOT와 트림은 쓰지 않아요.** WPF는 C++/CLI와 리플렉션 때문에 SDK가 트림을 막고, Native AOT도 되지 않아요(.NET 8/9/10 동일). 시작은 **ReadyToRun + 프레임워크 종속 게시**로 줄이고, 메모리는 **WinForms 트레이를 빼고, 꺼진 기능 어셈블리를 지연 로드**해서 줄여요.

**메모리 목표(Release, 유휴 20초, 중앙값 3회):**

| 상태 | 작업 집합 |
|---|---|
| 트레이만, 설정 창 없음, 메모 창 0 | **≤ 120MB** (도전 100MB) |
| 설정 창만 | **≤ 150MB** |
| 메모 7개(지금 `data/notes` 규모)를 연 뒤 유휴 | **≤ 180MB**, 이후 10분 증가 **≤ 10MB** |
| PowerToys가 모두 꺼진 상태와 켠 상태의 유휴 차이 | 후킹이 꺼져 있으면 **≤ 15MB** |

지금 숫자 227–239MB는 스모크(설정 창 + 메모 창, `next/test-logs/next_smoketest.txt`)예요. 트레이만의 유휴가 아니에요. 최적화 전에 그 둘을 따로 재요. 3.4.7 트레이 약 103MB, CLI 시작 약 61ms는 비교 기준이에요. 61ms는 창을 만들지 않는 .NET Framework 진입점이에요. WPF 목표로 쓰지 않아요. 트레이 아이콘까지 **≤ 500ms**, 설정 첫 프레임 **≤ 700ms**(런타임이 이미 설치된 Release ReadyToRun).

**데이터 규칙:** `%LOCALAPPDATA%\BongHub`의 `config.json`, `notes\<id>.json`+`.rtf`, `desktop\layouts.json`은 3.4.x가 끝까지 읽고 써야 해요. 클라우드 스키마는 **1**로 유지해요. Next 전용 값은 `%LOCALAPPDATA%\BongHub.Next\settings.json`에만 둬요. 저장권은 계속 `Local\BongHub_NotesWriter`예요.

---

## 1. 기능 목록

상태 뜻:

- **있음** — Next에서 같은 사용자 동작이 돼요.
- **일부** — 화면이나 데이터는 있으나 3.4.8보다 짧아요.
- **없음** — Next에 동작이 없어요.

줄 번호는 PR #4 팁의 `app/BongHub.cs`(8541줄)와 `next/BongHub.Next`예요.

### 1.1 요약

| 영역 | 있음 | 일부 | 없음 |
|---|---|---|---|
| 셸·트레이·단일 실행 | 트레이, 설정 창, 단일 인스턴스, 다크 메뉴 | 트레이 클릭(3.4.8은 왼쪽 더블클릭), 탭 구성 | 탭 순서 드래그, 8개 설정 탭 |
| 스티커 메모 | 열기·만들기·RTF/JSON 저장·색·핀·확대·불투명도·시작 시 열기·읽기 전용 | 서식, 글자 크기, 목록 | 이미지, 삭제, 전역 단축키, 모두 보이기/숨기기 |
| 모니터 프리셋 | — | 이름·단축키 표시 | 전환, 밝기, 레이아웃, 오버레이, 연계 |
| 창 위치·바탕화면·카톡 | — | 더 보기 안내 문구 | 동작 전부 |
| PowerToys 7종 | — | — | 전부 |
| 일반(시작·자리비움·잠금·클라우드) | — | 시작프로그램 스위치는 보이고 꺼져 있음 | 동작 전부 |
| 클라우드 `data/` | 메모 파일을 같은 형식으로 씀 | — | git 동기화 자체 |

### 1.2 트레이와 셸

| 기능 | 3.4.8 | Next |
|---|---|---|
| 트레이 아이콘, 블록 아이콘 | `TrayApp` `app/BongHub.cs:7126`, `Program.InitIcon` `:7508` | **있음.** `TrayService` — H.NotifyIcon.Wpf 2.4.1 (0.3.0). WinForms `NotifyIcon`는 0단계에서 뺐어요 |
| 왼쪽 더블클릭 → 설정 | `:7138` | **일부.** 왼쪽 클릭 한 번 (`TrayService.cs:20`) |
| 메뉴: 프리셋 실행, 체크 표시, 단축키 표시 | `:7213` | **없음.** 메뉴는 열기·새 메모·종료뿐 |
| 새 스티커 메모 / 모두 보이기 / 모두 숨기기 | `:7226` | **일부.** 새 메모만 (`TrayService.cs:16`, `NoteManager.NewNote`) |
| 바탕화면 아이콘 하위 메뉴(저장·복원·최근 10개·배치 관리) | `:7232` | **없음** |
| 카톡 광고 제거 토글(광고/컴팩트/다크/원상복구) | `:7249`, `KakaoToggle` `:7269` | **없음.** 더 보기는 문구만 (`MainWindow.xaml.cs:276`) |
| 창 규칙 지금 적용 | `:7257` | **없음** |
| 종료 | `:7264` | **있음.** `App.RequestShutdown` |
| 풍선 알림(전환 실패, 단축키 실패, 아이콘 복원) | `Balloon` `:7295` | **없음** |
| 트레이 툴팁에 클라우드 상태 | `:7142` | **없음** |
| 이미 실행 중이면 기존 창을 띄우고 종료(코드 1) | `:7611` | **있음.** `Local\BongHub.Next.SingleInstance` + Show 이벤트 (`App.xaml.cs:12`, `:35`) |
| Next가 저장 중이면 안내 후 코드 4 | `:7604` | 해당 없음(Next가 잠금의 다른 쪽). 트레이 뮤텍스가 있으면 Next는 읽기 전용 (`AppEnv.cs:59`) |
| 설정 창 커스텀 타이틀바, 스냅·그림자 유지 | `ChromeForm` `:3732`, `TitleBar` `:3812` | **일부.** WPF `WindowChrome` + Mica (`MainWindow.xaml`, `Fx.cs` `WindowFx`). 그라데이션 다크는 유지. 닫기 = 숨기기 (`MainWindow.xaml.cs:47`) |
| 사이드바 8탭, 끌어서 순서 변경 → `General.TabOrder` | `PageKeys` `:3888`, `HookNavDrag` `:3903` | **일부.** 5탭 고정: 모니터 프리셋, 스티커 메모, 일반, 더 보기, 정보 (`MainWindow.xaml.cs:13`). 순서 저장 없음 |
| 변경 즉시 저장, 적용 버튼 없음 | `Changed`/`Flush` `:4038` | **일부.** Notes 키만 즉시 패치 (`ConfigStore.SetNotesValue` `:48`). 다른 설정은 편집 UI가 없음 |
| 첫 페이지를 먼저 띄우고 나머지 탭은 쉬엄쉬엄 | `:3963`, `Prebuild` `:4006` | **일부.** 페이지는 이동할 때 그림. 무거운 탭이 아직 없음 |
| 창 크기 조절 중 1:1 유지(3.3.3 이후) | `ResizeGlide` `:3260` | **없음**(WPF 레이아웃이 실시간). 이식할 때 픽셀마다 전체 트리를 다시 재지 않는 쪽을 유지 |
| 얇은 오버레이 스크롤바 | `SlimScroll` `:1007` | **없음.** 기본 WPF 스크롤바 |
| 다크 확인/입력 창 | `DarkBox` `:2526`, `DarkPrompt` `:2507` | **없음** |
| 정보: 버전, 단축키, CLI 사용법, 구성요소, 변경 이력 | `BuildAbout` `:4824` | **일부.** 버전·뮤텍스·데이터 공유 (`MainWindow.xaml.cs:286`). 이력·구성요소·단축키 목록 없음 |
| 블록 아이콘 고정 | `BrandHeader` `:5211`, `icon_blocks.ico` | **일부.** 정보 탭의 네 칸 아이콘 (`UniformGrid4`). 트레이는 exe 아이콘 |
| 관리자 권한으로 다시 시작 (`restart <pid>`) | `:5032`, `Main` `:7545` | **없음** |
| DPI 인식 | `Native.SetDpiAware` `:83` | **있음.** PerMonitorV2 매니페스트 (`BongHub.Next.csproj:20`) |
| 화면 밖 테스트 배치 (−30000,−30000), 포커스 안 가져감 | `TestEnv` `:568` | **있음.** `AppEnv.Off`, `--off` (`AppEnv.cs:19`) |

### 1.3 모니터 프리셋

전환 본체는 `Core.Run` (`:1355`). Next 프리셋 페이지는 이름과 단축키만 읽고, 문구로 “적용은 3.4.8”이라고 해요 (`MainWindow.xaml.cs:169`). 아래는 전부 **없음**.

| 기능 | 위치 |
|---|---|
| 프리셋 추가·복제·이름·삭제·순서 | `BuildPresets` `:4066`, `AddPreset` `:4224` |
| 단축키 입력(`HotkeyBox`) | `:2084`, `:4106` |
| 모니터 지도: 켜기/끄기/유지, 우클릭 주 모니터 | `MonitorMap` `:2114`, `:4113` |
| MMT로 enable → LoadConfig → SetPrimary → disable, 재시도, 상태 확인 | `Core.Switch` `:1364`, `Verify` `:1383` |
| 레이아웃 cfg 저장·선택·사용 안 함 | `:4131`, `SaveLayout` `:1433` |
| 지연(켜기/단계/레이아웃/창 규칙), 재시도 1–10 | `Preset` `:181`, UI `:4143` |
| 전환 후 작업 표시줄 새로고침(DisplayFusion·탐색기 재시작) | `Refresh` `:1459`, 체크 `:4150` |
| 프리셋별 오버레이 on/off, 문구, 미리보기 | `:4156`, `Overlay` `:1304` |
| 밝기: 전체 또는 모니터별 0–100, 변경 안 함. DDC/CI(VCP 0x10) 다음 WMI. 실패해도 전환 계속 | `MonBright` `:1482`, `RealBright` `:1487`, `PresetExtras` `:1540` |
| 아이콘 배치 연계, 삭제된 배치 안내 | `Preset.DeskLayout` `:173`, UI `:4178` |
| 작업 영역 연계, 전환 끝(아이콘 복원 직후) 한 번 실행, 10초 스탬프+뮤텍스로 중복 방지 | `Preset.Workspace` `:174`, `:4182` |
| 지금 적용 / 창 규칙만 적용 | `:4189`. 테스트에서 실행 금지 |
| 자리비움 자동 전환: N분, 프리셋 선택, 소리 재생 중이면 건너뛰기 | `CheckIdle` `:7321`, `GeneralCfg` `:194`, `AudioPlaying` `:1467` |
| 전역 오버레이 사용, 배경 어둡기 30–95% (코드 기본 82, 동기화 설정은 70) | `GeneralCfg.OverlayOpacity` `:202`, UI `:4772` |
| CLI로 프리셋 이름, `work`/`away` | `FindCli` `:309`, `Main` `:7586` |
| 전환 중 재진입 거절, `Local\BongHub_Routine` | `Core.Busy` `:1337`, `:7591` |
| 옛 `%LOCALAPPDATA%\MonitorMode` 설정 복사 | `Config.Migrate` `:271` |

### 1.4 전환 오버레이

`OverlayForm` `:1160`, `Overlay` `:1304`. **없음.**

- 주 모니터를 덮는 로딩 카드, 문구, 상태 줄, 페이드.
- 아이콘은 정수 크기로 한 번만 그리고 중심은 정수로 고정(3.2.0 떨림 수정).
- 주 모니터 좌표가 약 0.5초 안정된 뒤에만 다시 배치 (`Place` `:1200`).
- 90초가 지나면 스스로 닫힘 (`:1187`).
- 미리보기는 2.5초 (`PreviewOverlay` `:4352`).

### 1.5 창 위치

**없음.** 데이터는 `config.json`의 `GlobalRules`와 프리셋 `Rules`에 있고, Next는 그 키를 지우지 않아요(패치가 다른 키를 유지).

| 기능 | 위치 |
|---|---|
| 조건: 프로세스, 제목, 제목 정규식, 클래스 | `WindowRule` `:135` |
| 동작: 위치·크기, 모니터만, 최대화, 최소화, 복원, 변경 없음, FancyZones 영역 | `Codes` `:148` |
| 항상 위, 위치 유지(Keep) | `:145` |
| 전역 / 프리셋 범위, 목록에서 켜기, 편집·복제·삭제·순서 | `BuildWindows` `:4373` |
| 규칙 대화상자 | `RuleDialog` `:5226` |
| 현재 창에서 캡처 | `CaptureDialog` `:5304`, `CaptureRules` `:4457` |
| 위에서부터 첫 일치만 적용 | `WindowRules` `:1657`, 안내 `:4405` |
| 전환 후 `RulesDelay` ms 뒤 적용 | `Trigger` `:7308` |
| Keep 규칙은 창 이벤트 + 15초 점검. 3.1.0에서 위치 변경 이벤트 제거, 주기 15초 | `Watcher` `:1845` |
| 트레이·명령 팔레트에서 즉시 적용 | `:7257`, `:7423` |

### 1.6 스티커 메모

저장 형식은 양쪽이 같아요. `notes\<12자 id>.json` + `.rtf`. 날짜는 `\/Date(ms)\/` (`NoteMeta` `:2650`, Next `MsDateConverter` `NoteStore.cs:25`).

| 기능 | 3.4.8 | Next |
|---|---|---|
| 메모 로드·저장(tmp 후 교체), 손상 파일은 건너뛰거나 안전 열기 | `Notes` `:2818` | **있음.** `NoteStore.cs`. 클라우드 `_deleted.json`은 건너뜀 (`:60`) |
| 새 메모, 기본색을 피한 무작위 색 | `Notes.New` `:2857`, `NoteColors.Random` `:2685` | **있음.** `NoteStore.Create`, `NoteColors.Random` |
| 시작 시 `Open==true`인 메모를 하나씩 | `OpenStartup` `:2886` | **있음.** `NoteManager.OpenStartup` (한 틱에 하나) |
| 위치·크기·Zoom·TopMost·Open·Text·Color·Modified | `NoteMeta` `:2650` | **있음.** 같은 필드 순서 (`NoteStore.cs:10`) |
| 그라데이션 테마, 글자 대비 유지 | `NoteColors` `:2661` | **있음.** `NoteColors` in `NoteStore.cs` |
| 단색 7색 + 그라데이션 키 + `c:RRGGBB:RRGGBB` | `Valid` `:2693` | **있음.** 팔레트 키는 같아요. 맞춤 색 고르기 UI는 **없음** |
| 불투명도 40–100%, 창 전체 | `ApplyOpacity` `:3597`, 설정 `:4506` | **있음.** 슬라이더 (`MainWindow.xaml.cs:200`), `StickyNoteWindow.ApplyOpacity` |
| 글자 크기 7–48pt | `:4504` | **일부.** 작게 10 / 보통 11 / 크게 13 (`MainWindow.xaml.cs:195`) |
| 새 메모 단축키 설정·전역 등록 (기본 Ctrl+Alt+N) | `:4510`, 등록 id 900 `:7287` | **일부.** 설정 화면에 문자열만 보여요. 등록은 없음 |
| 시작 시 열려 있던 메모 | `OpenOnStartup` `:2648` | **있음** |
| 목록에서 열기·닫기·삭제·색 일괄·모두 보이기/숨기기·텍스트 복사 | `BuildNotes` `:4463` | **일부.** 목록에서 열기, 창에서 최근 목록 (`StickyNoteWindow.xaml.cs:261`). 삭제·일괄 색·모두 보이기/숨기기·텍스트 복사 없음 |
| 핀(항상 위) | `:3486`, `:3672` | **있음.** `SetTopMost` |
| 닫기(목록에 남음), 삭제(Ctrl+D, 빈 메모는 바로) | `:3484`, `AskDelete` `:3669` | **일부.** 닫기만 (`CloseNote`). 삭제 없음 |
| 굵게·기울임·밑줄·취소선·글머리 | `:3496`, 키 `:3692` | **일부.** 같은 버튼 (`StickyNoteWindow.xaml`). Ctrl+T·Ctrl+Shift+L·Ctrl+N/D/W·Ctrl++/− 는 3.4.8 전용 처리. WPF 기본 Ctrl+B/I/U/Z는 동작 |
| 텍스트·제목1(24)·제목2(20)·제목3(16)·제목4(13), 작게/보통/크게 | `ShowFontSizes` `:3612` | **없음** |
| 색 팔레트, 호버 미리보기(벗어나면 복귀, 클릭해야 저장) | `PalettePopup` `:2767`, `:3673` | **일부.** 클릭하면 저장 (`BuildSwatches`). 호버 미리보기 없음 |
| Ctrl+휠 확대, 메타에 Zoom | `:3527` | **있음.** 0.6–3.0 (`StickyNoteWindow.xaml.cs:49`). 3.4.8은 RichEdit ZoomFactor(약 0.5–5) |
| 이미지 붙여넣기·드롭·파일 선택, PNG/JPEG로 줄여 RTF `\pict` | `InsertImage` `:3636`, `ImgExt` `:3624` (webp 포함) | **없음.** 이미 있는 그림은 화면 크기로만 디코딩하고, 저장 때 원래 pict 바이트를 다시 넣어요. 개수가 줄면 기존 RTF를 유지 (`FlushText`, `ImageGuardHits`) |
| 이미지 클릭 선택, 핸들 크기(비율 고정, Shift 자유), 이동, 삽입선 | `RichBox` `:2954`, `MoveImage` `:3110` | **없음** |
| 우클릭: 복사, 25/50/75/100%, 폭 맞춤, 원본, 삭제 | `:3097` | **없음** |
| Ctrl+C가 선택 이미지를 비트맵으로 | `CopySelectedImage` `:3080`, `:3705` | **없음** |
| 더블클릭 뷰어: 휠 확대, 드래그, 맞춤/100%, 닫기, 제목줄 없음 | `ImageViewer` `:1053` | **없음** |
| 링크 클릭으로 열기 | `:3528` | **없음** |
| 입력 후 1.8초 뒤 저장, 끄는 동안 타이머를 재시작하지 않음 | `:3537`, `:3716` | **일부.** 텍스트·메타 타이머는 있음. 이동 중 저장 합치기는 3.4.7의 Win32 `WM_ENTERSIZEMOVE` 경로 |
| 첫 드래그 지연 제거(WS_EX_NOACTIVATE를 표시 직후 해제) | `AllowActivation` `:3550` | **일부.** `ShowActivated=false`. 3.4.7과 같은 스타일 해제는 없음 |
| 모달 창이 떠도 메모를 다시 활성화 | `:3549` | **없음** |
| 화면 밖으로 나가지 않게 클램프, 해상도 변경 대응 | `Clamp` `:2905` | **일부.** 처음 배치(`PlacePhysical`). 이후 모니터 변경 클램프는 없음 |
| 8MB 넘으면 경고 | `:3641` | **없음** |
| 클라우드가 메모를 갈아끼우면 열린 창을 다시 읽기 | `ReloadFromDisk` `:2891` | **없음**(클라우드 단계) |
| 읽기 전용(트레이가 저장권) | 3.4.8에는 없는 Next 동작 | **있음.** `AppEnv.DenyWrite`, 편집기 `IsReadOnly` |
| 메모 안 단축키 안내 문구 | `:4517` | **없음** |

### 1.7 카톡 광고 제거

**없음.** 도우미는 내장 `BongAd.exe` (`Kakao` `:2569`).

| 기능 | 위치 |
|---|---|
| 광고 숨김, 컴팩트(`/nocompact`가 아니면 켬), 다크(`/dark`), 시작 시 자동 적용 | `KakaoCfg` `:2562`, UI `:4552` |
| 도우미를 `%LOCALAPPDATA%\BongHub\helpers\BongAd.exe`로 풀고, 바뀌면 재시작 | `Extract` `:2579` |
| 원상복구는 `/restore` 후 프로세스 종료 | `Stop` `:2606` |
| 상태: 도우미 실행, 카톡 감지 | `Status` `:2621` |
| 첫 실행 때 옛 `Startup\BongAd.lnk`와 돌고 있는 BongAd를 이어받음 | `DetectLegacy` `:2625` |
| 트레이에서 즉시 토글 | `:7249` |
| 종료 때 도우미 정지 | `:7468` |

### 1.8 바탕화면 아이콘

**없음.** 파일은 `desktop\layouts.json` (`Desk` `:357`).

| 기능 | 위치 |
|---|---|
| 화면 구성 키(해상도@좌표, 주 모니터 표시)와 설명 | `ScreenKey` `:401` |
| 탐색기 SysListView32를 다른 프로세스 메모리로 읽어 이름·좌표 저장/복원 | `ReadIcons` `:450`, `Apply` `:476` |
| 이름 저장, 자동 저장(구성마다 하나, 같으면 생략), 덮어쓰기, 이름 변경, 삭제 | `:515` |
| 자동 정렬이 켜져 있으면 복원 실패로 안내 | `Restore` `:549` |
| 자동 정렬·눈금·아이콘 표시 토글, 모든 창 최소화/되돌리기 | `:498` |
| 해상도 변경·프리셋 전환 후 복원, 전환 직전 저장, N분마다, 종료 시, 시작 시 | `DeskCfg` `:338`, `TrayApp` `:7155` |
| 단축키 저장/복원 (id 901/902), 기본은 비어 있음 | `:7288` |
| 트레이 최근 10개 | `:7240` |
| 다른 화면 구성 배치 보기 | `:4624` |

### 1.9 일반

| 기능 | 3.4.8 | Next |
|---|---|---|
| Windows 시작 시 실행 (`Startup\BongHub.lnk`, 옛 `모니터 단축키.lnk` 이전) | `Startup` `:5362` | **일부.** 스위치가 보이고 비활성 (`MainWindow.xaml.cs:266`). 바로가기를 쓰지 않음. 은퇴 전에는 Next가 이 lnk의 대상을 바꾸면 안 돼요 |
| 자리비움 | `:4720` | **없음** |
| 잠금 배경만 숨김(인증 유지). HKLM 정책 `NoLockScreen`, 백업 `SOFTWARE\BongHub\LockScreenBackup`, 다른 값이 있으면 덮어쓰지 않음 | `LockScreenPolicy` `:588`, UI `:4741` | **없음** |
| 절전 복귀 인증(AC/DC) 끄기·복원. 전원 계획이 바뀌거나 남이 바꾸면 중단 | `SleepResumeAuth` `:634`, UI `:4757` | **없음** |
| 무암호 로컬 계정 부팅 자동 로그인. 빈 `DefaultPassword`만. 기존 자동 로그인이 있으면 중단 | `BlankAutoLogon` `:671` | **없음** |
| 암호 있는 계정은 Sysinternals Autologon 문서만 연다. 앱은 암호를 받지 않음 | `:4764` | **없음** |
| 설정 폴더 열기 | `:4796` | **없음**(메모 페이지에 경로 표시는 있음) |
| 애니메이션 줄이기 | 없음 | **있음.** Next 전용 `settings.json` (`NextSettings`) |

잠금·절전·자동 로그인은 별도 프로세스를 `runas`로 띄워요(`lockscreen`, `sleepauth`, `blankautologon`). 테스트에서 이 인자를 실제 PC에 실행하지 않아요.

### 1.10 PowerToys 스타일 기능

코드 기본값은 위험한 것(Zones, Grab, Palette, Paste, Peek)이 **꺼져** 있고, 사용자 `data/config.json`은 여러 항목이 켜져 있어요. 문서·코드 기본과 PC 설정을 구분해요. Next는 **없음**. 카드 UI는 `BuildPowerToys` `:4865`, 키 `PtKeys` `:4851`.

**FancyZones** (`ZonesCfg` `:5446`, `Zones` `:5475`)

- 사용 중에만 WinEvent 후킹. 끄면 후킹 해제.
- Shift+드래그, Shift 없이 항상 표시(Shift는 반전), Ctrl로 여러 영역, Win+←/→(기본 꺼짐, 다음 모니터까지), 끌어낼 때 원래 크기, 모든 모니터, 불투명도·영역 색·강조 색·번호, 제외 프로세스.
- 레이아웃: 세로 열, 가로 행, 격자, 가운데 강조, 직접 비율, 캔버스(클릭 나누기, Shift+클릭 가로, 우클릭 두 번 합치기).
- 창 규칙 동작 `zone`이 이 사각형을 써요.

**잡아서 이동** (`GrabCfg` `:5458`, `GrabMove` `:5797`)

- 기본 꺼짐. 보조키(Alt/Win/Ctrl+Alt)+왼쪽 드래그 이동, +오른쪽 드래그 크기.
- 후킹 콜백은 최신 좌표만 기록하고 바로 반환해요. 전용 스레드가 그 좌표를 약 60fps로 적용해요 (`:5833`, `:5852`). 3.4.0 이력의 8ms 간격은 이후 이 페이스로 바뀌었어요.
- 최대화 창은 실제 드래그 때 복원, 최소·최대 크기, 반대편 가장자리 고정.
- 전체화면은 기본 제외. 관리자 권한 창은 BongHub도 관리자일 때만.
- 보안 데스크톱·잠금 화면은 대상이 아님 (`:5031`).

**작업 영역** (`WorkspacesCfg` `:5461`, `Workspaces` `:5953`)

- 열린 앱·위치·모니터·상태 저장, 단축키, 없는 앱 실행, 있는 앱은 옮기기만.
- 바탕화면 바로 가기, `BongHub.exe workspace <이름>` (`WorkspaceCli` `:7538`). 테스트에서 실행 금지.
- 프리셋 `Workspace` id와 연결 (사용자 작업 프리셋은 `6d0f354b`).

**명령 팔레트** (`PaletteCfg` `:5462`, `Palette` `:6055`, `PaletteForm` `:6265`)

- 기본 키 Win+Alt+Space. 코드 기본은 꺼짐. 창은 숨겨 재사용.
- 켜면 시작 8초 뒤 앱 색인 (`:7348`), 10분마다 갱신. 시작 메뉴 lnk + 스토어 AppsFolder.
- 검색: 단어 첫 글자. 접두사: 계산, `??` 웹, `?` 파일(Windows Search), `>` 명령, `<` 창 전환.
- 계산: + − * / % ^, sqrt sin cos tan abs ln log exp round floor ceil, hex.
- 단위: 길이·무게·부피·데이터·시간·속도·넓이·온도·평. 시간/세계 시각. Windows 터미널 프로필. 시스템 명령은 확인 후(잠금, 절전, 휴지통 비우기 등). Windows 설정 URI.
- BongHub 명령: 프리셋, 새 메모, 설정 탭, 작업 영역, 아이콘 배치, Image Resizer, 창 규칙 (`PaletteCommands` `:7413`).

**고급 붙여넣기** (`PasteCfg` `:5464`, `AdvPaste` `:6332`, `PasteMenu` `:6436`)

- 메뉴 Win+Shift+V(id 911). 선점되면 Ctrl+Alt+V → Ctrl+Alt+Shift+V → Win+Ctrl+Shift+V 순으로 바꿔 설정에 저장 (`:7358`).
- 서식 없이 Ctrl+Win+Alt+V (912). JSON (914), 마크다운 (915)은 키가 비어 있을 수 있음.
- 동작: 일반 텍스트, 마크다운, JSON, 공백 정리, 이미지→PNG, 사용자 지정(regex, template, upper/lower/title, sort, dedupe, urlencode/decode, base64/unbase64).
- .txt/.html/.png/.jpg로 붙여넣기.

**미리보기** (`PeekCfg` `:5465`, `Peek` `:6515`, `PeekForm` `:6789`, `WebPImage` `:6739`)

- 기본 키 Space. 탐색기·바탕화면이고 입력 칸이 아닐 때만, 누르는 동안만 (`PeekSpace` `:6478`). Space가 아니면 일반 핫키 913.
- 여러 파일 ←/→, 코드 구문 강조, 마크다운·HTML(스크립트 차단), GIF, WebP(libwebp 1.6.0, 애니메이션은 첫 프레임), HEIC 썸네일, 동영상/음악, ZIP, 폴더, PDF/Office(설치된 미리보기), 포커스 잃으면 닫기, 직접 파일 선택.
- WebP는 별도 작업. Space를 떼면 창을 바로 닫고, 늦게 도착한 이미지는 버려요.
- 파일 목록 조회는 STA 스레드 (`PeekNow` `:7395`).

**Image Resizer** (`ResizerCfg` `:5468`, `Wic` `:6938`, `Resizer` `:6982`)

- 기본 켜짐. 탐색기 우클릭은 기본 꺼짐(HKCU `Software\Classes\SystemFileAssociations\image\shell\BongHubResize`).
- 끌어서 놓기, 프리셋(작게/중간/크게/휴대폰), 맞춤/채우기/늘이기, 줄이기만, JPEG 품질, %·cm·in, 파일 이름 `%1`–`%6`, 방향 무시, 메타데이터 유지/제거, 수정 날짜, 대체 형식 png.
- WIC, JPEG 축소 디코딩, 병렬(최대 6).
- CLI `resize`는 창을 실제로 띄워요 (`:7575`). 자동 테스트에서 실행하지 않아요.

단축키 충돌 표시, 예약 키(Ctrl+C, Win+L 등), 다른 프로그램 선점 탐지: `PtConflicts` `:4918`, `HotkeyTaken` `:7426`.

### 1.11 단축키

등록은 `RegisterHotKey` + `MOD_NOREPEAT` (`RegisterAll` `:7277`). 설정 저장 즉시 다시 등록해요. 창을 닫을 때까지 기다리지 않아요. 실패하면 `%LOCALAPPDATA%\BongHub\hotkeys_status.txt`와 풍선.

| 단축키 | 하는 일 | id | 코드 기본 | Next |
|---|---|---|---|---|
| Ctrl+Alt+1 | 프리셋 작업 | 100부터 | 예 (`:221`) | 표시만 |
| Ctrl+Alt+0 | 프리셋 자리비움 | 이어서 | 예 (`:223`) | 표시만 |
| (설정값) Ctrl+Alt+5 | 프리셋 게임 | 이어서 | 코드 기본 없음. `data/config.json`에 있음 | 표시만 |
| Ctrl+Alt+N | 새 메모 | 900 | 예 | 표시만 |
| (비어 있음) | 아이콘 저장 / 복원 | 901 / 902 | 빈 문자열이면 등록 안 함 | 없음 |
| Win+Alt+Space | 명령 팔레트 | 910 | 기능이 켜져 있을 때 | 없음 |
| Win+Shift+V | 고급 붙여넣기 | 911 | 켜져 있을 때. 선점 시 대체 키를 저장 | 없음 |
| Ctrl+Win+Alt+V | 서식 없이 | 912 | 같음 | 없음 |
| (설정) | JSON / 마크다운 붙여넣기 | 914 / 915 | 기본 빈 문자열 | 없음 |
| Space | 미리보기, 누르는 동안, 탐색기 비입력 | 후킹. Space가 아니면 913 | 켜져 있을 때 | 없음 |
| 작업 영역마다 | 실행 | 920–979 | 기능이 켜져 있을 때 | 없음 |
| Win+←/→ | 영역 이동 | 키보드 후킹 | 기본 꺼짐 | 없음 |
| Alt+드래그 등 | 잡아서 이동 | 마우스 후킹 | 기본 꺼짐 | 없음 |
| 메모 안 Ctrl+B/I/U/T, Ctrl+Shift+L, Ctrl+Z/Y, Ctrl++/−, Ctrl+N/D/W, Ctrl+V 이미지 | `NoteForm.ProcessCmdKey` `:3692` | — | — | 일부 (위 1.6) |

3.4.0이 이전한 옛 기본값(Ctrl+Alt+Space, Ctrl+Alt+V, Ctrl+Shift+Space)은 `ShortcutVersion`이 0일 때만 바꿔요 (`PtCfg.Normalize` `:5431`). 사용자가 고친 키는 유지해요.

### 1.12 백그라운드 동작

| 동작 | 주기·조건 | 위치 | Next |
|---|---|---|---|
| 메모 저장권 뮤텍스, 트레이 뮤텍스보다 먼저 | 프로세스 수명. abandoned면 다음이 인수 | `NotesWriterLock` `:7475` | **있음.** 테스트·임시 폴더는 잡지 않음 (`AppEnv.cs:59`) |
| 단일 인스턴스 | `Local\BongHub_Tray` | `:7611` | **있음.** 다른 이름 `Local\BongHub.Next.SingleInstance`. 은퇴 전에는 이 이름을 트레이 뮤텍스로 바꾸지 않아요 |
| 자리비움 감시 | 30초. 소리 감지는 별도 스레드 약 2초 | `:7139`, `:7321` | **없음** |
| 아이콘 자동 저장 | 60초 틱, 설정 분, 해상도 변경 후 120초는 쉼 | `:7171` | **없음** |
| 아이콘 자동 복원 | 표시 변경·전환 후 3–4초, 바쁨이면 2초 뒤 | `:7156`, `WM_DISPLAYCHANGE` `:7300` | **없음** |
| 클라우드 끌기 | 변경 1.5초 뒤, `PullMinutes`(기본 2)마다 | `Cloud.Apply` `:741` | **없음** |
| 창 위치 유지 | Keep 규칙이 있을 때만 후킹 + 15초 | `Watcher` `:1854` | **없음** |
| FancyZones / 잡아서 이동 / Space 미리보기 | 해당 기능이 켜져 있을 때만 | `:5571`, `:5797`, `:6492` | **없음** |
| 팔레트 색인 | 팔레트가 켜져 있으면 8초 뒤, 낮은 우선순위 | `:7348` | **없음** |
| 카톡 도우미 | `AdBlock`일 때 프로세스 | `:7202` | **없음** |
| 시작 8초 뒤 아이콘 복원 | `RestoreOnStartup` | `:7200` | **없음** |
| 메모 저장 타이머 | 1.8초, 이동 중에는 멈춤 | `:3537` | **일부** |
| 종료 시 메모 flush, 클라우드 flush, 아이콘 저장, 카톡 정지, 후킹 해제 | `:7462` | **일부.** 메모 flush와 트레이 해제 (`App.xaml.cs:81`) |

### 1.13 설정 키

`config.json`은 `JavaScriptSerializer`로 읽고, 저장은 들여쓰기 있는 JSON이에요 (`Config.ToJson` `:266`, `Pretty` `:316`). `Version` 기본 2 (`:206`). Next가 Notes 키를 패치할 때는 그 값의 JSON 토큰만 갈아끼워요 (`NotesConfigPatch`). 다른 키와 들여쓰기는 바이트 그대로고, 값이 같으면 파일을 다시 쓰지 않아요. 키를 지우지만 않으면 3.4.8 `Load`는 통과해요. 새 필수 키를 공유 파일에 넣지 않아요.

| 키 | 코드 기본 | 하는 일 | Next가 읽고 씀 |
|---|---|---|---|
| `Version` | 2 | 설정 형식 | 파일이 없을 때만 2를 만듦. 올리기 금지 |
| `Presets[].Name, Hotkey` | 작업 Ctrl+Alt+1, 자리비움 Ctrl+Alt+0 | 표시·등록 | **읽기만** |
| `Presets[].Enable, Disable, Primary, Layout` | 샘플 모니터 id, `work_layout.cfg` | MMT | 유지(패치 시). 편집 없음 |
| `Retries, EnableDelay, StepDelay, LayoutDelay, RulesDelay` | 1 또는 샘플 4 / 1500 / 2000 / 3000 / 3000 | 전환 타이밍 | 유지 |
| `RefreshTaskbar, Overlay, OverlayMessage` | true, true, "" | 전환 후처리·안내 | 유지 |
| `Rules[]` | 빈 목록 | 창 규칙. 필드는 `WindowRule` `:135` | 유지 |
| `Brightness[]` (`Monitor`, `Value` −1=유지) | 빈 목록 | 밝기 | 유지 |
| `DeskLayout, Workspace` | "" | 연계 id | 유지 |
| `General.TabOrder` | 빈 목록이면 UI 기본 순서 | 탭 순서 | 유지. Next는 안 읽음 |
| `General.IdleEnabled, IdleMinutes, IdlePreset` | true, 30, 자리비움 | 자리비움 | 유지 |
| `General.SkipIfAudio, AudioThreshold` | true, 0.001 | 소리 중 건너뛰기 | 유지 |
| `General.OverlayEnabled, OverlayOpacity` | true, 82 (범위 20–100) | 전역 오버레이 | 유지 |
| `Kakao.AdBlock, Compact, Dark, AutoApply` | false, true, false, true | 카톡 | 유지 |
| `GlobalRules` | 빈 목록 | 항상 적용 규칙 | 유지 |
| `Notes.DefaultColor` | `g:blocks` (`ColorVer<2`면 yellow를 이 값으로) | 무작위 제외 색 | **읽기.** 쓰기 UI 없음 |
| `Notes.Gradient` | true | 그라데이션 | **읽고 씀** |
| `Notes.ColorVer` | 0 → 정규화 시 2 | 색 마이그레이션 | 유지. Next가 올리면 안 됨 |
| `Notes.FontSize` | 11 (7–48) | 본문 | **읽고 씀.** UI는 10/11/13만 |
| `Notes.Opacity` | 100 (정규화 30–100, UI 40–100) | 창 불투명도 | **읽고 씀** |
| `Notes.Hotkey` | Ctrl+Alt+N | 전역 키 | **읽기** |
| `Notes.OpenOnStartup` | true | 시작 시 열기 | **읽고 씀** |
| `Desk.AutoSave, AutoSaveMinutes` | true, 10 (1–240) | 주기 저장 | 유지 |
| `Desk.SaveOnExit, SaveBeforeSwitch, RestoreOnChange, RestoreOnStartup` | true, true, true, false | 자동 복원 | 유지 |
| `Desk.HotkeySave, HotkeyRestore` | "" | 단축키 | 유지 |
| `Cloud.Enabled, Repo, Branch, PullMinutes` | false, `https://github.com/Baegovda/BongHub.git`, main, 2 | 동기화 | 유지. Next는 동기화를 시작하지 않음 |
| `Pt.ShortcutVersion` | 0 → 1로 올리며 옛 기본키만 이전 | 단축키 마이그레이션 | 유지. Next가 올리면 안 됨 |
| `Pt.Zones.*` | `Enabled` false. 나머지 `:5446` | FancyZones | 유지 |
| `Pt.Grab.*` | Enabled false, Modifier Alt, Resize true, SkipFullscreen true | 잡아서 이동 | 유지 |
| `Pt.Work.Enabled, LaunchMissing, MoveExisting, Items[]` | true, true, true, 빈 목록 | 작업 영역. `WsApp` `:5459` | 유지 |
| `Pt.Palette.*` | Enabled false, Hotkey Win+Alt+Space, 하위 검색 기본 true | 팔레트 | 유지 |
| `Pt.Paste.*` | Enabled false, 키는 1.11, `Custom[]` | 붙여넣기 | 유지 |
| `Pt.Peek.*` | Enabled false, Space, CloseOnFocusLoss false, RenderHtml true | 미리보기 | 유지 |
| `Pt.Resizer.*` | Enabled true, ContextMenu false, 프리셋 4개, Fit fit, ShrinkOnly true, Quality 90, NameFormat `%1 (%2)`, Fallback png | 리사이저 | 유지 |

Next 전용(공유 파일이 아님): `ReduceMotion` (`ConfigStore.cs` `NextSettings`).

메모 JSON 필드 순서: `Id, Color, X, Y, W, H, TopMost, Open, Zoom, Text, Modified`. 3.4.8은 public 필드 선언 순서예요. Next 클래스도 그 순서예요. 필드를 중간에 끼우지 않아요.

바탕화면 배치: `Id, Name, Key, Desc, Auto, Saved, Items[{Name,X,Y}]` (`DeskLayout` `:349`).

### 1.14 외부 도구

| 도구 | 쓰는 곳 | 라이선스·위치 | Next |
|---|---|---|---|
| NirSoft MultiMonitorTool.exe | 리소스로 포함하고 `%LOCALAPPDATA%\BongHub\`에 풀어서 `/enable` `/disable` `/LoadConfig` `/SaveConfig` `/SetPrimary` `/scomma` | `Core.Extract` `:1340`. 정보 탭에 버전 표시 `:4843` | **없음** |
| work_layout.cfg | 작업 프리셋 기본 레이아웃 | 같은 Extract | **없음** |
| DisplayFusion | 작업 표시줄 새로고침 때 프로세스 종료 후 재실행. 없으면 `C:\Program Files\DisplayFusion\DisplayFusion.exe` | `DetectDf` `:1451` | **없음.** 테스트에서 프로세스를 죽이지 않아요 |
| BongAd.exe (2.4, MIT) | 카톡 광고. 리소스 `BongAd.exe`, 15360 bytes | `Kakao.Extract` `:2579`, `legacy/BongAd/` | **없음** |
| dwebp.exe, webpmux.exe (libwebp 1.6.0) | WebP 미리보기. 애니메이션은 첫 프레임 | `WebPImage` `:6739`. 설명은 `app/res/WebP-README.md` | **없음** |
| git.exe | 클론·페치·커밋·푸시. `C:\Program Files\Git\cmd\git.exe` 우선 | `Cloud.Git` `:761` | **없음** |
| gh.exe | `auth login` + `auth setup-git`. 토큰은 자격 증명 관리자. 앱은 토큰을 저장하지 않음 | `Cloud.Connect` `:914` | **없음** |
| WIC | Image Resizer | `Wic` `:6938` | **없음** |
| dxva2.dll | 모니터 밝기 | `RealBright` `:1489` | **없음** |
| powrprof.dll | 절전 복귀 인증, 팔레트의 절전 | `SleepResumeAuth` `:638` | **없음** |
| Shell.Application | 아이콘 최소화, 스토어 앱 목록 | `Desk.MinimizeAll` `:505`, `Palette.BuildApps` `:6082` | **없음** |

### 1.15 클라우드 동기화

`Cloud` `:725`. Next **없음**. 로컬 작업 복사본은 `%LOCALAPPDATA%\BongHub\cloud\repo`이고, 커밋되는 트리는 저장소의 `data/`예요.

- 스키마 `Cloud.Schema = 1`. 원격 `meta.json`의 schema가 더 크면 덮어쓰지 않고 “업데이트 필요”. 더 작으면 schema와 `minApp`을 올려 씀 (`Merge` `:845`).
- `config.json`은 파일 전체, `config.stamp.json`의 시각이 최신이 이김.
- 메모는 id마다 json+rtf, 수정 시각이 최신이 이김. 삭제는 `notes/_deleted.json` 톰스톤(60일 뒤 정리).
- 아이콘 배치는 id마다 `Saved`가 최신이 이김.
- `data/devices/<기기>.json`은 기기 이름, 앱 버전, 스키마만. 비밀 없음.
- 커밋 메시지 `sync: <기기> yyyy-MM-dd HH:mm:ss`. 사용자 git 설정은 건드리지 않고 저장소 로컬 `user.name`만 `BongHub`.
- 푸시가 거절되면 최대 3번 다시 페치.
- 인증 실패·오프라인은 상태로 남고, 보내기 실패는 30초 뒤 재시도.
- 종료 시 대기 중인 변경은 최대 20초 flush (`FlushOnExit` `:758`).
- 설정 창을 연 채로 원격 설정이 오면, 창을 닫을 때 다시 읽어요 (`cloudCfgPending` `:7131`).

테스트는 가짜 폴더의 `cloudtest`만 해요. `Baegovda/BongHub`에 push하지 않아요. `data/`는 사람이 고치지 않아요.

### 1.16 CLI와 테스트 모드

`Program.Main` `:7544`. 알 수 없는 `*test`/`*bench`는 **9**를 반환하고 모니터 전환으로 떨어지지 않아요 (`:7585`). 예외는 `crash_<모드>.txt`, 종료 **3**.

| 인자 | 하는 일 | 자동 테스트에서 |
|---|---|---|
| (없음) | 트레이 | 사용자 PC에서만. 봇이 배포 exe를 띄우지 않음 |
| `restart <pid>` | 부모 종료를 기다린 뒤 트레이 | 관리자 재시작 버튼이 호출. 테스트에서 호출 금지 |
| `<프리셋>`, `work`, `away` | 실제 모니터 전환 | **금지** |
| `workspace <이름>` | 작업 영역 실행·창 이동 | **금지** |
| `resize` [파일…] | 리사이저 창, 저장 시 실제 `config.json`을 다시 씀 | **금지** |
| `version` | exe 옆에 `version.txt` | 허용(창·모니터 없음) |
| `settingstest` [접미] | 설정 UI를 화면 밖에 두고 스크린샷·회귀 | 허용. 사용자 바탕화면에 띄우지 않음 |
| `extrastest` | 프리셋 부가(밝기 목, 아이콘 연계, 작업 영역 한 번) | 허용. 2026-11-02 **26/0 PASS** |
| `configtest` | 설정 라운드트립 | 허용 |
| `windowtest` | 창 규칙 | 허용(실제 사용자 창을 옮기는 경로가 있으면 그 호출은 금지) |
| `kakaotest` | BongAd 인자·수명. 카톡 창을 숨기는 실사용은 사용자 확인 | 기존 테스트가 임시 경로를 쓰면 허용 |
| `notestest`, `notesbench [n]` | 메모 | 허용. 임시 폴더 |
| `imagetest` | 이미지 RTF·뷰어 | 허용. 화면 밖 |
| `icontest` | 아이콘 배치 로직 | 허용은 기존 테스트가 탐색기 메모리에 쓰지 않을 때만. 실제 바탕화면 이동 금지 |
| `cloudtest` | 가짜 기기 동기화 | 허용. 원격 push 금지 |
| `pttest` | PowerToys 회귀. 실제 후킹으로 다른 창을 옮기지 않는 경로 | 허용 |
| `perfbench`, `jittertest`, `tabbench`, `uiperf`, `inputtest`, `interactiontest`, `watchbench` | 성능·입력 | 허용(화면 밖). 숫자 목표는 사용자 PC에서 |
| `overlaytest` | 오버레이. **숨은 데스크톱에서만** | `testdesk.ps1` 없이 사용자 화면에 금지 |
| `lockscreenpolicytest` | HKCU 임시 키로 정책 로직 | 허용 |
| `lockscreen enable\|restore`, `sleepauth disable\|restore`, `blankautologon enable\|restore` | 실제 HKLM·전원·Winlogon | **금지** |
| `blankcheck` | 현재 계정이 로컬인지 종료 코드만 | 허용(상태를 바꾸지 않음) |
| `paritytest` | `TestEnv.Off`만 켜고 case 없음 → 9 | 그대로 두기(실수 전환 방지) |

Next 테스트: `--smoketest`, `--notetest`. 0.2.0 기록은 스모크 43, 노트 23이에요. 0.3.0 스모크는 그 43개에 트레이 단언 13개(다크 메뉴, 메뉴로 열기, 왼쪽 클릭, 종료, 풍선, 셸 아이콘을 만들지 않음)를 더해 **56**이에요. 0.4.0 스모크는 거기에 소프트웨어 렌더 2개(설정 창, 메모 창)를 더해 **58**이에요. 0.4.0 노트는 그 23개에 표시 비트맵 크기·원본 pict 바이트 유지 2개와 선형 확대 1개를 더해 **26**이에요. `--data`, `BONGHUB_NEXT_DATA`, `--out`, `--off`, `--tray`, `--quit`, `--idle-hidden`(화면 밖 설정 창을 만든 뒤 숨김. 메모리 기준선용). 테스트와 `--off` 측정은 임시 폴더라 `Local\BongHub_NotesWriter`를 잡지 않아요. `--off`는 Next 단일 실행 뮤텍스를 잡지 않아, 이미 켜진 Next 창을 끌어올리지 않아요.

---

## 2. 프레임워크와 라이브러리

조사 시점 2026-11-02. 안정 버전만 적어요. 프리뷰·beta는 앱에 넣지 않아요.

| 선택 | 버전 | 라이선스 | 권장 | 메모리·시작에 미치는 영향 |
|---|---|---|---|---|
| .NET 10 Desktop | 런타임 **10.0.14**, SDK **10.0.403** (2026-10-25). LTS ~ 2028-11-14 | 마이크로소프트 .NET 라이선스 | **목표 TFM `net10.0-windows`** | 8과 같은 WPF 스택이에요. 버전을 올린다고 227MB가 103MB가 되지 않아요. 지원이 9일 뒤 끝나는 8에 새 코드를 쌓지 않는 것이 이유예요. .NET 9는 같은 날 지원이 끝나고 STS예요 |
| .NET 8에 남기 | 8.0.33, 지원 종료 2026-11-10 | 같음 | 하지 않음 | 보안 패치가 곧 끝나요. 아래 H.NotifyIcon 2.4.1도 net10을 겨냥해요 |
| WPF | SDK에 포함 (UseWPF) | 같음 | **유지** | 스모크 227MB의 대부분이에요. 창을 만들기 전에 트레이만 재면 더 낮게 나와요. 그 숫자를 0단계 기준으로 삼아요 |
| WinUI 3 | Windows App SDK **2.6.4** (2026-10-27). 1.8은 2026-09-24에 서비스 종료. 2.0 계열 종료 2027-04-29 | 마이크로소프트 | **채택하지 않음** | 비패키지는 런타임 설치 또는 자체 포함이에요. 자체 포함은 디스크가 크고, RichEdit RTF와 전역 후킹을 다시 짜야 해요. 메모리 이득이 측정된 적 없어요 |
| WinForms | UseWindowsForms, 트레이만 | 같음 | **트레이를 빼면 참조 제거** | 지금 Next는 아이콘 하나 때문에 WPF와 WinForms를 같이 올려요. 같이 있으면 작업 집합이 늘어나요. 0단계에서 뺀 뒤 다시 재요 |
| CommunityToolkit.Mvvm | **8.4.2** (2026-03-25) | MIT | **새 화면부터 사용** | 소스 생성기라 런타임 리플렉션이 거의 없어요. 작업 집합 영향은 1MB 안쪽이에요. 0단계에서 기존 코드 전체를 ViewModel로 옮기지 않아요 |
| H.NotifyIcon.Wpf | **2.4.1** (2025-12-01). 2.5는 아직 dev/beta | MIT | **0단계에 트레이 교체** | net10-windows와 net462만 대상이에요. WinForms `NotifyIcon` 없이 아이콘·메뉴·풍선을 그려요. WinForms 제거가 목표예요. 풍선과 다크 메뉴를 스모크로 확인해요 |
| WPF-UI (lepoco) | **4.3.0** (2026-05-04). 4.0–4.2 일부는 치명 버그로 표시됨 | MIT | **셸로 쓰지 않음** | Fluent 테마·NavigationView가 컨트롤 트리를 키워요. 지금 셸은 그라데이션 다크·블록 아이콘·즉시 저장이고, Mica는 `WindowFx`가 이미 해요. 컨트롤 팩을 올리면 227MB를 줄이는 방향이 아니에요 |
| Microsoft.Windows.CsWin32 | **0.3.367** (2026-10-22) | MIT | **새 P/Invoke부터** | 생성 코드만 들어가고 런타임 DLL은 없어요. 빌드가 Win32 메타데이터 패키지를 받아요. `GetAsyncKeyState` 부호 확장처럼 손수 선언한 실수를 막아요. 3.4.8의 DllImport를 한 번에 바꾸지 않아요 |
| System.Text.Json 소스 생성기 | SDK 포함. 추가 패키지 없음 | 같음 | **읽기 모델에만** | 리플렉션 워밍업이 줄어요. 시작이 수십 ms 줄 수 있고, 100MB를 줄이지는 않아요. 공유 `config.json` 전체를 생성기 DTO로 다시 쓰면 키 누락·날짜 형식·필드 순서가 깨져요. 기록은 지금처럼 `JsonNode` 패치와 메모 전용 컨텍스트예요 |
| Microsoft.Extensions.Hosting | **10.0.14** (메타패키지, 런타임과 같은 밴드) | MIT | **프로세스 호스트로 사용** | DI와 `IHostedService`로 클라우드·자리비움·후킹을 ‘켜져 있을 때만’ 시작해요. Generic Host 자체는 수 MB예요. ASP.NET 호스트는 넣지 않아요. WinExe로 유지해요 |
| Microsoft.Extensions.Logging | **10.0.14** | MIT | **기본 로그** | 파일 한 개, 롤링, 메모 본문·토큰 금지. `%LOCALAPPDATA%\BongHub.Next\` (동기화 안 됨) |
| Serilog | **4.4.0** (2026-07-10) + Sinks.File **7.0.0** + Extensions.Hosting **10.0.0** | Apache-2.0 | **기본은 아님** | 구조화 로그가 필요할 때 위 버전으로 바꿔요. 동기화 실패 한 줄을 위해 싱크 구성을 늘리면 시작 경로가 늘어요. 4.4.1-dev는 쓰지 않아요 |
| Octokit | **14.0.0** (2026-01-08) | MIT | **쓰지 않음** | REST로 커밋을 만들면 지금 git 병합(mtime, 톰스톤, `data/` 히스토리)을 다시 구현해요. 인증도 gh 자격 증명과 갈라져요. 동기화는 **git.exe + gh.exe**를 유지해요 |
| libwebp 1.6.0 (dwebp/webpmux) | 이미 `app/res` | libwebp 라이선스(`app/res/WebP-LICENSE.txt`) | **미리보기 단계까지 유지** | 프로세스만 잠깐 띄워요. 상주 메모리가 아니에요. 관리되는 디코더로 바꾸려면 라이선스와 ‘애니메이션은 첫 프레임’ 동작을 맞춘 뒤에 비교해요 |
| MultiMonitorTool, BongAd | 기존 바이너리 | 지금 고지 그대로 | **그대로 포함** | Next 리소스로 옮기고, 푸는 위치는 계속 `%LOCALAPPDATA%\BongHub`예요. 두 벌로 풀지 않아요 |

게시 설정(0단계에서 csproj에 반영할 값):

- `PublishReadyToRun=true`, `RuntimeIdentifier=win-x64`, `SelfContained=false`.
- `PublishSingleFile`은 끄기. 첫 실행이 임시 폴더에 풀리고, 메모리 이득이 없어요.
- `PublishTrimmed`와 `PublishAot`는 끄기.
- `TieredPGO`는 이미 true. 유지.
- `SatelliteResourceLanguages=ko` 유지.
- `ConcurrentGarbageCollection`은 0.4.0부터 **true**예요. 0.3.0 기준선(3.3)에서 켜는 쪽이 설정 첫 프레임을 700ms 아래로 내렸고, 트레이 작업 집합은 늘지 않았어요.
- 디스크의 .NET 10 Desktop Runtime이 없으면 프레임워크 종속 exe는 시작하지 않아요. 설치가 어려운 PC용 자체 포함은 나중 선택이고, 메모리 목표의 기본 경로는 아니에요.

어셈블리는 켜진 기능만 로드해요. 3.4.8은 한 파일이지만 꺼진 기능은 후킹을 만들지 않아요. .NET은 같은 어셈블리를 참조하면 그 코드가 같이 올라와요.

- `BongHub.Next` — 트레이, 설정 셸, 메모, 설정 패치.
- `BongHub.Next.Monitors` — MMT, 밝기, 오버레이. 프리셋을 실행할 때.
- `BongHub.Next.Desktop` — 아이콘 배치. 그 화면을 열거나 자동 저장이 켜져 있을 때.
- `BongHub.Next.PowerToys` — 카드 하나라도 켜져 있을 때, 기능별로 다시 나눌 수 있음.
- `BongHub.Next.Cloud` — `Cloud.Enabled`이고 이 프로세스가 저장권일 때.

참조는 인터페이스 뒤로 두고, `Assembly.Load`는 그 기능의 첫 사용에서만 해요.

---

## 3. 메모리와 시작

### 3.1 왜 227MB인가

스모크 로그는 설정 첫 프레임과 메모 창을 만든 뒤 **작업 집합 227MB, 프라이빗 184MB**예요. 프로세스 가동 시간은 그때 약 12초인데, 그 12초는 테스트가 돌기 전의 대기예요. “시작 12초”로 읽지 않아요. 첫 프레임 1292ms는 테스트 시작 기준이에요.

227MB를 한 번에 103MB로 맞추는 단일 스위치는 없어요. 순서는 이래요.

1. `--tray`이고 `OpenOnStartup`이 꺼진 임시 설정으로 **창을 만들기 전** 유휴를 재요. 이 값이 기준선이에요.
2. WinForms를 뺀 뒤 같은 측정을 반복해요.
3. 설정 창을 숨긴 뒤에도 트리를 들고 있는지 봐요. 지금은 닫기가 숨기기라 한 번 연 창은 남아요 (`MainWindow.xaml.cs:47`). 유휴 목표를 숨긴 창이 있는 상태로 잡을지, 트레이만으로 잡을지 0단계 표에 둘 다 적어요. 목표 표의 “트레이만”은 **MainWindow를 아직 만들지 않은 상태**예요.
4. 메모 창은 RTF를 연 개수만큼 문서 메모리를 잡아요. 시작 시 한 틱에 하나씩 여는 구조는 유지하고, 보이지 않는 메모의 문서를 닫는 실험은 1단계 뒤로 미뤄요. 형식 호환보다 메모리 실험이 앞서지 않아요.
5. 꺼진 PowerToys 어셈블리를 로드하지 않아요.
6. 클라우드 git 프로세스는 저장할 때만 띄워요. 상주시키지 않아요.

### 3.2 재는 방법

자동 측정은 임시 데이터만 써요. 프리셋 CLI, MMT, 실제 모니터, 실제 바탕화면, 배포 중인 3.4.7 프로세스는 건드리지 않아요.

```powershell
dotnet publish next\BongHub.Next\BongHub.Next.csproj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true
$pub = "next\BongHub.Next\bin\Release\net10.0-windows\win-x64\publish"
$data = Join-Path $env:TEMP ("bhnext_mem_" + $PID)
New-Item -ItemType Directory -Force $data | Out-Null
# 메모 0개, OpenOnStartup false 인 config.json 을 $data 에 복사 (data/ 원본은 수정하지 않음)
$p = Start-Process "$pub\BongHub.Next.exe" -ArgumentList "--tray","--off","--data",$data -PassThru
Start-Sleep -Seconds 20
$p.Refresh()
"ws_mb={0} private_mb={1}" -f [int]($p.WorkingSet64/1MB), [int]($p.PrivateMemorySize64/1MB)
Stop-Process -Id $p.Id
```

같은 방법으로 설정 창(`--off`만, `--tray` 없음)과, 메모 7개 복사본을 연 경우를 각 3회 재고 중앙값을 `next/test-logs/`에 남겨요. 로그 파일은 결과 txt만 커밋하고 `bin/`은 커밋하지 않아요.

이 절차는 `next/measure-baseline.ps1`에 있어요. 시나리오는 네 가지예요. 계획의 3종(트레이만, 설정 창, 메모 7개)에, 설정 창을 만든 뒤 숨긴 상태(`--tray --off --idle-hidden`)를 더해요. 목표 표의 “트레이만”은 MainWindow를 아직 만들지 않은 상태이고, 숨긴 창 행은 그 트리를 그대로 들고 있는 상태예요. 스크립트는 준비 줄(`next_startup.txt`)이 생긴 뒤 20초를 쉬고 재요. `data/`와 `%LOCALAPPDATA%\BongHub`는 읽지도 쓰지도 않아요. 메모 7개는 동기화된 `data/notes` 개수와 무관하게, 스크립트가 임시 폴더에 고정으로 만들어요. 본문이 있는 RTF 7개이고 그 중 하나는 1×1 PNG예요. 스크립트는 동시 GC를 끈 경우와 켠 경우를 각 3회 재요. csproj 기본값은 0.4.0부터 true이고, 스크립트는 끝나면 runtimeconfig도 true로 돌려놔요. 판정은 `gc_concurrent=true` 중앙값이에요. 창을 끄는 동안의 멈춤은 이 스크립트가 재지 않아요. 실모니터에서 드래그하지 않아요.

시작은 기존 `next_startup.txt`와 같이 프로세스 시작 시각부터 첫 `ContentRendered`까지예요 (`first_frame_ms`). 트레이 시점은 아이콘이 보인 직후 `tray_visible_ms`로 같은 파일에 남겨요. `--out`이 있을 때만 써요. 트레이만이면 `main_window_created=0`이고 `first_frame_ms`는 없어요.

부가로 `dotnet-counters monitor --process-id <pid> System.Runtime`을 쓸 수 있어요. 없어도 목표 판정은 WorkingSet64와 PrivateMemorySize64로 해요.

목표를 넘기면 그 단계의 기능 이식은 계속하고, 숫자는 기록해요. 기능을 빼서 숫자를 맞추지는 않아요. 은퇴 조건은 “목표 달성 또는, 초과분을 원인과 함께 기록하고 사용자가 그 상태로 매일 쓰기로 함”이에요.

### 3.3 0.4.0 — 기준선을 보고 줄인 것

**상태: `38e6b9b`는 주인 PC에서 쟀고 설정·메모가 목표를 넘었어요. 그 뒤를 소프트웨어 렌더로 줄였어요. 소프트웨어 렌더의 중앙값은 아직 비어 있음.** 그라데이션, 글꼴, 메모 색, 3.4.x 파일 형식, `Local\BongHub_NotesWriter` 순서는 그대로예요. 바탕화면이 설정 틴트 뒤로 비치는 효과만 소프트웨어 경로에서 빠져요. 이유는 아래 측정 다음 문단이에요.

0.3.0을 주인 PC에서 잰 뒤의 판단:

| 항목 | false (기본이었음) | true | 목표 |
|---|---|---|---|
| 트레이만 작업 집합 / 아이콘 | 57MB / 341ms | 56MB / 293ms | ≤ 120MB, ≤ 500ms. 이미 통과 |
| 설정 첫 프레임 중앙값 | 1648ms (1648, 1522, 3795) | 684ms | ≤ 700ms. true만 통과 |
| 설정 작업 집합 | 192MB (private 162) | (첫 프레임만 기록) | ≤ 150MB. 초과 |
| 메모 7개 작업 집합 | 276MB (private 250) | 264MB | ≤ 180MB. 초과 |

그래서 0.4.0은 동시 GC를 기본으로 켜요. 남은 작업 집합은 아래에서 줄여요.

`38e6b9b`를 같은 스크립트(`gc_concurrent=true`, 3회 중앙값)로 잰 값: 트레이 60MB·아이콘 약 290ms, 설정 첫 프레임 662ms, 설정 작업 집합 195MB(private 162MB), 메모 7개 240MB(private 212MB). 스모크 56, 노트 25, 원본 pict 바이트는 유지됐어요. 관리 힙은 5–6MB예요. 트레이(60MB)와 설정 창(195MB)의 차이는 페이지 내용이 아니에요. 기준선 설정은 프리셋 두 줄짜리 빈 창이고, 그 창 하나만으로 private가 162MB예요. 메모 7개의 추가분은 약 45MB(private 약 50MB)라, 화면 크기로 줄인 그림과 창 버퍼 정도예요. 목표를 넘는 큰 덩어리는 첫 하드웨어 창이 프로세스에 올리는 D3D9On12 장치(디스크립터 힙, Intel 드라이버가 프로세스 private로 잡는 할당. [dotnet/wpf#7704](https://github.com/dotnet/wpf/issues/7704))예요.

그 장치를 만들지 않으려고 프로세스를 소프트웨어 렌더로 고정해요. 시계를 떼고 그림 확대를 Linear로 바꾸는 것은 창 크기 중간 표면과 HighQuality 캐시를 지우기 위한 것이고, 장치 자체를 없애지는 않아요. 유휴 전에 작업 집합을 한 번 내리는 호출은 이미 해제한 페이지만 정리해요. private가 162·212에 머물면 그 호출만으로는 통과가 아니에요. 판정은 작업 집합과 private를 같은 천장으로 봐요.

`fb8d5f2` Windows 결과: 게시 성공, 스모크 58, 노트 26. `gc_concurrent=true` 중앙값은 트레이 작업 집합/private 8/18MB·아이콘 294ms, 설정 23/65MB, 메모 7개 22/95MB. 메모리 목표는 통과. 설정 첫 프레임만 706ms(652, 706, 2386)로 700ms를 넘김. GPU일 때는 651–678ms. 소프트웨어에서 `main_ctor_done`→`main_hwnd`가 190–250ms. 이어서 설정 창의 첫 페인트에서 루트 불투명도 애니메이션(중간 표면 한 장)과 방사 빛 번짐을 빼요. 빛 번짐은 시각을 기록한 뒤에 켭니다. 다시 열 때의 페이드와 메모 창 페이드는 유지해요. 목표는 첫 프레임 중앙값 600ms 안, 메모리는 위 숫자에서 늘지 않는 것.

- **설정 창.** 트레이만(57MB)과 설정 창(192MB)의 차이는 페이지 내용이 아니에요. 기준선 설정은 프리셋 두 줄짜리 빈 창이에요. 창을 띄운 뒤 `Opacity` 애니메이션이 `FillBehavior=HoldEnd`로 남으면 WPF가 창 크기 중간 표면을 유휴 때까지 잡고, 큰 타원에 `Opacity`를 주면 그 타원도 따로 표면이 돼요. 0.4.0은 애니메이션이 끝나면 시계를 떼고, 빛 번짐의 알파는 브러시 색에 넣어요. 색은 0.22·0.14를 곱한 것과 같게요. Mica와 반투명 틴트(`#D0`)는 유지해요.
- **메모 7개.** `data/notes`의 그림은 두 파일이에요. `11fb464aae97.rtf`에 JPEG 6장(디코딩하면 약 26MB, 가장 큰 장 1472×1537), `5652dbbb0999.rtf`에 PNG 2장(약 2.4MB). 화면에서는 twip 목표 크기라 가로 약 260~550 DIP예요. WPF RTF 로더는 파일 픽셀 그대로 디코딩해서 FlowDocument가 그 비트맵을 잡고 있어요. 0.4.0은 로드용 RTF의 pict만 1×1 PNG로 바꾼 뒤, WIC `DecodePixelWidth`로 시스템 DPI·확대·목표 크기만큼만 디코딩해요. 긴 변은 1280px을 넘지 않고, 원본보다 키우지는 않아요. 창이 그려지기 전에 `Image.Source`를 바꿔서 큰 비트맵이 렌더 스레드에 올라가지 않게 해요.
- **저장.** 0.2.0 노트 테스트는 이미지 메모를 고치면 WPF가 RTF를 다시 쓰고 pict 개수는 유지했어요(guard 0). 축소 비트맵을 그대로 저장하면 3.4.x 파일이 작아져요. 그래서 저장 문자열의 pict 묶음을 파일에 있던 묶음으로 바꿔 넣어요. 개수가 안 맞으면 예전 guard처럼 `.rtf`는 두고 본문만 메타에 적어요.
- **유휴 전에 한 번 압축.** 시작 메모를 다 연 뒤(없으면 첫 프레임을 기록한 뒤) LOH를 한 번 압축해요. 우선순위는 `ApplicationIdle`이라 `first_frame_ms` / `tray_visible_ms` 안에는 들어가지 않아요. 측정 스크립트는 그 다음에 20초를 쉬어요.

합격(Windows, 화면 밖, 임시 폴더): 스모크 58, 노트 26, `measure-baseline.ps1`의 `gc_concurrent=true` 중앙값이 트레이 작업 집합·private ≤ 120MB, 설정 둘 다 ≤ 150MB, 메모 7개 둘 다 ≤ 180MB, 트레이 아이콘 ≤ 500ms, 설정 첫 프레임 중앙값 ≤ 700ms. 스타트업에 `render_mode=software`가 있고, 창이 있는 시나리오는 `hwnd_render_mode=SoftwareOnly`예요. 프리셋 CLI와 실제 모니터는 쓰지 않아요. 설정 창을 실제 바탕화면에서 한 번 봐서, 어두운 그라데이션이고 거의 검정이나 하양이 아닌지도 봐 주세요.

---

## 4. 이식 순서

각 단계는 그 상태만으로 주인 PC의 숨은 데스크톱 또는 화면 밖 좌표에서 시험할 수 있어야 해요. 3.4.8 exe는 그 단계의 배포 트레이로 남아 있어요. Next 버전은 단계마다 올리고 CHANGELOG에 해당하는 메모를 `next/` 쪽에 남겨요. 3.4.x CHANGELOG는 유지보수 수정이 있을 때만 올려요.

### 데이터 호환 (모든 단계)

1. 공유 파일의 필드 이름·메모 필드 순서·`\/Date(ms)\/`·RTF `\pict`를 유지해요.
2. 공유 `config.json`에 Next만 아는 필수 키를 넣지 않아요. `ReduceMotion`처럼 Next 파일에 둬요.
3. `Version`은 2, `Cloud.Schema`는 1, `Notes.ColorVer`와 `Pt.ShortcutVersion`은 3.4.8 정규화가 한 번 더 돌아도 사용자 키가 바뀌지 않게 둬요.
4. 저장권은 `Local\BongHub_NotesWriter` 한 개예요. 잡는 순서는 `docs/HANDOFF.md` 3.2와 같아요. 프리셋 CLI, `resize`, `workspace`는 이 잠금을 보지 않아요. Next가 그 셋의 역할을 맡기 전에는 Next도 그 인자로 모니터나 창을 움직이지 않아요.
5. 저장권자만 클라우드를 돌려요. 두 프로세스가 같이 push하지 않아요.
6. `Startup\BongHub.lnk`의 대상은 은퇴 커밋 전까지 3.4.x exe예요.
7. Next는 `Local\BongHub_Tray`를 만들지 않아요. 만들면 3.4.8이 “이미 실행 중”(코드 1)으로 끝나요.
8. 설정 패치는 모르는 키를 삭제하지 않아요. Notes 한 키는 그 값 토큰만 바꾸고, 바깥 바이트는 그대로 둬요.
9. 새 config를 옛 exe가 저장하면 옛 exe가 모르는 키가 사라질 수 있어요(`docs/HANDOFF.md` 배포 절). 그래서 공유 파일에 새 키를 넣지 않아요.

### 0단계 — 기준선과 .NET 10

**상태: 0.3.0 기준선과 `38e6b9b` 재측정은 주인 PC에서 채움 (2026-11-02). 소프트웨어 렌더는 코드에 반영됨. 그 중앙값을 다시 재는 일은 남아 있어요.** `app/`은 그대로예요. 0.3.0에서 `ConcurrentGarbageCollection=false`일 때 트레이만 57MB(private 15MB, 아이콘 341ms), 설정 창 192MB(private 162MB, 첫 프레임 중앙값 1648ms, 회차 1648/1522/3795), 메모 7개 276MB(private 250MB)였어요. 동시 GC를 켜면 설정 첫 프레임 684ms, 메모 7개 264MB, 트레이 56MB / 293ms였어요. `38e6b9b`는 트레이 60MB·약 290ms, 설정 첫 프레임 662ms, 설정 195/162MB, 메모 7개 240/212MB였어요. 그 표로 기본값을 true로 정했고, 설정·메모가 목표를 넘는 부분은 3.3에서 소프트웨어 렌더로 줄여요.

| 항목 | 코드 | Windows에서 채울 것 |
|---|---|---|
| TFM `net10.0-windows`, SDK 10.0.403(또는 같은 10 기능 대역의 더 높은 SDK) | `global.json`, csproj | `dotnet publish` 종료 코드 0 |
| H.NotifyIcon.Wpf 2.4.1, `UseWindowsForms` 제거 | `TrayService` | 스모크 58 PASS (0.2.0의 43 + 트레이 13 + 소프트웨어 렌더 2) |
| ReadyToRun, 프레임워크 종속, 단일 파일·트림·AOT 끔 | csproj, `Properties/PublishProfiles/ReadyToRun.pubxml` | publish 폴더에 `coreclr.dll` 없음, dll에 ReadyToRun |
| 트레이만 / 설정 창 / 숨긴 설정 창 / 메모 7개, GC 동시 켜짐·꺼짐 각 3회 중앙값 | `next/measure-baseline.ps1` | `next/test-logs/baseline_memory.txt` |
| `ConcurrentGarbageCollection` | **true** (0.4.0, 3.3의 기준선으로 결정) | `gc_concurrent=true` 중앙값이 목표 안인지 |
| 3.4.8 `build.ps1` 경고 0 | `app/` 변경 없음 | 오류 0, 경고 0. exe를 배포 폴더에 복사하지 않음 |
| 노트 23 (0.4.0은 26), 읽기 전용 저장 차단, extrastest 26/0, settingstest | 0.4.0 노트는 표시 크기·pict 바이트·선형 확대 3개가 더해짐 | 각 종료 코드 0 |

채택하지 않음 (0단계 범위 밖): CommunityToolkit.Mvvm(기존 화면을 ViewModel로 옮기지 않음), CsWin32(새 P/Invoke가 없음), Microsoft.Extensions.Hosting·Logging(켜진 기능 서비스는 뒤 단계), Serilog, Octokit, WPF-UI, WinUI 3.

선적 가능: 지금과 같은 메모·설정 프로토타입. 기능 추가는 없어요.

- TFM `net10.0-windows`. SDK 10.0.403으로 `dotnet publish` PASS.
- H.NotifyIcon.Wpf 2.4.1. `UseWindowsForms` 제거. 다크 메뉴·왼쪽 클릭·종료·풍선이 스모크에서 통과. 스모크는 셸 트레이 아이콘을 만들지 않아요.
- ReadyToRun, 프레임워크 종속.
- 측정 3종과 숨긴 설정 창을 `next/test-logs/baseline_memory.txt`에 기록.
- 3.4.8 `build.ps1`은 동작 변경 없이 경고 0인지만 확인(HANDOFF에 남은 확인). 소스를 기능 쪽으로 고치지 않아요.
- 합격: 스모크 58(기존 43은 유지, 트레이 단언 13, 소프트웨어 렌더 2), 노트 26(0.4.0), 읽기 전용 저장 차단, extrastest 26/0, settingstest PASS.

### 1단계 — 메모를 3.4.9와 같게

**상태: 코드 반영됨 (0.5.0). Windows 스모크 65·노트 89·메모리 재측정은 아직 비어 있음.** 모니터 전환은 계속 3.4.9예요. `app/`은 그대로예요.

- 전역 Ctrl+Alt+N (설정 키 존중, 충돌 시 풍선, 저장 즉시 재등록). `--off`·테스트는 `RegisterHotKey`를 호출하지 않고 메시지 처리만 흉내 내요.
- 삭제, 모두 보이기/숨기기, 텍스트 복사, 맞춤 색, 글자 7–48, 제목 크기, 호버 색 미리보기, 링크, 드롭.
- 이미지: 디스크의 `\pngblip`/`\jpegblip`을 유지해요. WPF `RichTextBox`가 pict를 떨어뜨리면 그 파일 바이트를 그대로 두고 로그만 남겨요. msftedit는 쓰지 않아요. 크기 조절은 `\picwgoal`/`\pichgoal`만 바꿔요.
- 뷰어, 복사, 25–100%, 폭 맞춤, 이동, 첫 드래그 지연, 모달 중 활성화, 화면 클램프(`--off`에서는 좌표 테스트를 위해 클램프하지 않음).
- 트레이 메뉴의 메모 항목(새 메모, 모두 보이기, 모두 숨기기).
- 합격: 스모크 **65**, 노트 **89**(7개 복사본에 그림이 있을 때). 3.4.9 `notestest`/`imagetest`는 자기 임시 폴더만 쓰므로 경로 인자를 넣지 않아요(`app/` 동결). Next 노트 테스트가 같은 형식(JSON 필드 순서, `\/Date(ms)\/`, blip 바이트, `\picwgoal`)을 단언해요. 잠금 순서는 HANDOFF 3.2 그대로예요. 메모리 기준선은 0.4.0 숫자(트레이 8/18MB, 설정 20/64MB·593ms, 메모 7개 22/94MB, 소프트웨어 렌더)를 넘기면 안 돼요.

### 2단계 — 설정 셸 (위험 기능 제외)

**상태: 코드 반영됨 (0.6.0).** 모니터 전환·카톡 실행·아이콘 이동·후킹은 하지 않아요.

- 8탭과 `TabOrder`. 표기 “모니터 프리셋”, “카톡 광고 제거”.
- 일반의 자리비움·오버레이 투명도·폴더 열기. 시작프로그램 스위치는 **상태를 보여 주기만** 하고 lnk를 쓰지 않아요.
- 정보 탭에 버전, 구성요소, 단축키, 변경 이력.
- 즉시 저장. 적용 버튼 없음.
- 합격: 스모크 **83**, 노트 **89**. 화면 밖에서 탭을 돌고, 공유 config의 Pt·Presets·Desk 키가 바이트 수준으로 남아요. `--smoketest`는 `--data`를 무시하고, 그 복사본이 있으면 임시 폴더에서만 `General.TabOrder` 바이트 차이를 봐요. 설정 메모리는 20/64MB를 넘기면 안 돼요.

### 3단계 — 바탕화면 아이콘

- `layouts.json` 라운드트립, 화면 키, 자동 저장 정책.
- 실제 탐색기 메모리 쓰기는 사용자 확인 항목으로 남기고, 자동 테스트는 가짜 목록으로 `Apply`의 이름 매칭·중복 이름·자동 정렬 거부만 해요.
- 합격: `icontest`와 같은 픽스처. 사용자 바탕화면 좌표는 불변.

### 4단계 — 창 위치

- 규칙 CRUD, 첫 일치, Keep 감시(규칙이 있을 때만).
- 자동 테스트는 테스트가 만든 창만 옮겨요. 사용자 창과 실제 모니터는 그대로예요.
- 합격: `windowtest`, `watchbench`에 해당하는 화면 밖 실행.

### 5단계 — PowerToys

기본 꺼짐은 코드 기본을 유지해요. 사용자 파일이 켜져 있어도, 테스트 프로세스는 후킹을 설치하지 않아요(`TestEnv.Off` / `--off`에서 `SetWinEventHook`과 저수준 후킹 금지).

순서: 붙여넣기(후킹 없음) → 리사이저(WIC, HKCU 메뉴는 테스트에서 레지스트리를 쓰지 않음. `Resizer.SetMenu`가 이미 `TestEnv.Off`에서 return) → 팔레트(계산·단위는 순수 함수로 테스트, 앱 실행은 목) → 작업 영역(실행 대신 배치 계산) → 미리보기(WebP는 샘플 파일, 탐색기 선택 COM은 목) → Zones 기하 → Grab은 마지막. Grab의 실사용은 사용자 확인.

합격: `pttest`가 커버하는 순수 로직이 Next에서 같은 결과를 내요. 실제 창 이동 케이스는 “직접 확인해 주세요”.

### 6단계 — 모니터 프리셋

- `IBrightBackend` 목을 그대로 옮겨요. `extrastest` 26개 시나리오를 Next에서 재현해요.
- MMT 호출은 인터페이스 뒤예요. 테스트 대역은 명령을 기록만 해요.
- 오버레이 시험은 `_dev\testdesk.ps1`의 숨은 데스크톱 `BongHubTest`에서만.
- UI의 “지금 적용”은 이 단계의 수동 확인이에요. 자동 테스트가 누르지 않아요.
- DisplayFusion·탐색기 종료 코드 경로는 테스트에서 호출 금지.
- 합격: extrastest 동등, overlaytest는 숨은 데스크톱에서만 PASS 기록.

### 7단계 — 카톡

- 같은 `BongAd.exe` 바이트를 풀어요. 인자 `/nocompact`, `/dark`, `/restore`를 유지해요.
- 자동 테스트는 도우미 수명과 인자만. 카톡 창은 숨기지 않아요.
- 합격: `kakaotest` 동등. 실사용은 사용자 확인.

### 8단계 — 클라우드

- 저장권자만 `Cloud.Apply`에 해당하는 서비스를 시작해요.
- git.exe + gh.exe. Octokit 없음. 스키마 1, 톰스톤, mtime.
- 테스트는 로컬 bare 저장소. `github.com/Baegovda/BongHub` push 금지.
- 합격: `cloudtest` 동등(설정 전체, 메모별, 배치별, 삭제, 더 높은 schema면 중단).

### 9단계 — 은퇴

체크리스트의 사용자 기능이 모두 **있음**이고, 0–8 테스트가 임시 폴더에서 PASS이며, 메모리 표를 기록한 뒤예요.

- 시작프로그램 lnk 대상을 Next exe로.
- 배포 폴더를 Next로 바꾸기 전에 `backup\`에 3.4.x exe·cs를 복사(HANDOFF 규칙).
- 3.4.x exe는 바로가기에서 빼되, 한 버전 동안 폴더에 남겨 롤백할 수 있게 해요.
- 롤백 시 공유 config를 옛 exe가 읽어요. 그래서 9단계 직전까지 새 키를 넣지 않은 상태가 유지돼야 해요.
- 소스의 `app/` 삭제는 사용자가 매일 Next만 쓴 뒤를 따로 요청할 때 해요. 이 계획의 완료 조건에 소스 삭제는 없어요.

### 위험

| 위험 | 대응 |
|---|---|
| WPF가 RTF 그림을 버림 | 1단계 가드. 파일을 덮어쓰지 않음. msftedit는 쓰지 않음 |
| 3.4.7(잠금 없음)과 Next가 겹침 | 배포 트레이를 3.4.8로 올린 뒤에 Next를 매일 쓰게 해요. HANDOFF에 남은 “Next 먼저 → 코드 4” 확인을 0단계 수동 항목으로 두어요 |
| 양쪽이 클라우드 push | 저장권자만. 8단계 전에 Next는 git을 실행하지 않음 |
| 시작프로그램이 Next를 가리켜 3.4.8이 안 켜짐 | 9단계 전 lnk 변경 금지. 테스트가 Startup 폴더를 쓰지 않음 |
| 테스트가 모니터·밝기·아이콘·후킹을 침 | 5절 금지 목록. `--off`에서 후킹 API를 호출하면 테스트 실패로 처리 |
| 잠금 화면·절전·자동 로그인 테스트가 실제 정책을 바꿈 | `*test` 격리 키만. `enable`/`disable`/`restore` 실인자 금지 |
| WinUI·WPF-UI·자체 포함·트림으로 메모리가 더 늘어남 | 2절에서 채택하지 않음 |
| CsWin32 메타데이터 프리뷰 패키지가 빌드를 흔듦 | 생성기 버전 고정. 런타임 출력에 그 패키지가 없어야 함 |
| 공유 JSON을 통째로 다시 쓰면 클라우드가 매 저장마다 커밋 | Notes 한 키는 값 토큰만 교체. 다른 키를 통째로 다시 쓰지 않음 |
| Grab/Zones가 관리자 창·전체화면에 오동작 | 코드 기본 꺼짐. 실사용은 사용자 확인. 테스트는 후킹 미설치 |

---

## 5. 단계별 시험

공통 규칙 (`docs/HANDOFF.md` 8장과 같음):

- 사용자 화면에 테스트 창을 띄우지 않아요. 화면 밖 (−30000,−30000) 또는 `_dev\testdesk.ps1`의 숨은 데스크톱.
- 프리셋 CLI, `workspace`, `resize`, MMT enable/disable, 실제 밝기, 실제 아이콘 복원, 실제 `data/`에 테스트 쓰기를 하지 않아요.
- 배포 중인 3.4.7 트레이를 끄지 않아요. 잠금 순서 확인은 사용자가 그 트레이를 끈 세션에서만 “직접 확인해 주세요”.
- 모르는 테스트 이름은 9로 끝나고 실제 전환으로 떨어지지 않게 유지해요.
- AutoHotkey로 입력 테스트를 대체하지 않아요.

| 단계 | 매번 돌릴 것 | 그 단계에 더할 것 | 돌리지 말 것 |
|---|---|---|---|
| 0 | 3.4.9 `extrastest`, `settingstest`. Next `--smoketest`(0.5.0은 65, 0.4.0은 58), `--notetest`(0.5.0은 89, 0.4.0은 26, 그 전은 23) | `next/measure-baseline.ps1` → `baseline_memory.txt`. `version`으로 3.4.9 문자열 확인 | 프리셋 CLI, 실모니터 |
| 1 | 위 + 3.4.9 `notestest`, `imagetest` | Next 노트 테스트에 이미지 라운드트립, 삭제, 단축키 등록은 테스트 창의 메시지 흉내(전역 후킹 설치 없음), 읽기 전용. 0.5.0 기대값은 스모크 65, 노트 89 | 실제 메모 폴더, 전역 핫키가 사용자 키를 가로채는 실행 |
| 2 | 위 + `configtest` | 화면 밖 탭 순회, 패치 후 Pt/Presets/Desk 키 보존. 0.6.0 스모크 **83**, 노트 **89** | 시작프로그램 lnk 생성 |
| 3 | 위 + `icontest`(탐색기에 쓰지 않는 범위) | layouts.json 병합 픽스처 | 실제 바탕화면, `Desk.Apply`의 실리스트뷰 |
| 4 | 위 + `windowtest`, `watchbench` | 테스트 창만 이동 | 사용자 창 |
| 5 | 위 + `pttest` | 계산·단위·리사이즈 기하·WebP 샘플 파일. Zones 사각형 단위 | 저수준 후킹, HKCU 우클릭 키, `resize` GUI, `workspace` CLI |
| 6 | 위 + `extrastest` 시나리오를 Next 목으로 | `overlaytest`는 숨은 데스크톱 세션에서만 | MMT, 밝기 API, DisplayFusion/탐색기 종료, “지금 적용” |
| 7 | 위 + `kakaotest` | 인자·`/restore` | 실제 카톡 창 |
| 8 | 위 + `cloudtest` | 로컬 bare repo | `gh` 네트워크, `Baegovda/BongHub` push, 비밀 출력 |
| 9 | 0–8 전부 | 잠금 순서(3.4.8 코드 4, Next 읽기 전용)는 배포 트레이를 끈 수동 확인으로 기록 | 수동 확인을 자동 테스트로 대체하지 않음 |

`lockscreen` / `sleepauth` / `blankautologon`의 실인자는 어느 단계에서도 자동 실행하지 않아요. 로직은 `lockscreenpolicytest`처럼 프로세스 밖 정책을 건드리지 않는 복제품으로만 옮겨요. `blankcheck`는 종료 코드만 보므로 허용이에요.

스모크가 이미 하는 일(대비, 포커스 안 가져감, Mica 클로크, Alt+F4는 숨기기)은 셸을 건드리는 단계마다 유지해요. 0.2.0은 43개였어요. 0.3.0은 트레이를 더해 56개, 0.4.0은 소프트웨어 렌더 2개를 더해 58개예요. 0.5.0 스모크는 **65**개예요(글자 7–48, 메모 명령 4개, 핫키 미등록, 충돌 풍선, 모두 숨기기, 모두 보이기, 그 뒤에도 후킹 없음). 노트는 23 → 26(0.4.0) → **89**(0.5.0, 7개 복사본에 그림이 있을 때)예요. 개수가 줄면 이유를 계획 개정에 적어요.

CI는 저장소에 아직 없어요. 이 계획도 GitHub Actions를 필수로 두지 않아요. 시험 장소는 지금처럼 주인 PC의 격리 폴더예요. 봇 환경은 Windows 트레이를 띄울 수 있으면 화면 밖 스모크까지, 없으면 스크립트 컴파일과 문서 검토까지예요. 못 한 실사용(모니터 전환, 카톡, FancyZones, 잡아서 이동)은 “직접 확인해 주세요”로 남겨요.

---

## 6. 3.4.x를 얼리는 범위

허용: 크래시, 저장 잠금, 데이터 손실, 빌드 경고처럼 동작이 바뀌지 않는 수정. 버전은 3.4.9 이상으로만.

허용하지 않음: 새 탭, 새 단축키 기본값, 새 config 키, 스키마 2, 모니터 알고리즘 변경, UI 개편. 그런 요구는 Next 단계로 옮겨요.

3.4.8에 이미 있는 동작(잠금, 경고 정리)은 이 계획의 기능 목록에 포함돼요. Next가 그 잠금을 느슨하게 만들면 안 돼요.
