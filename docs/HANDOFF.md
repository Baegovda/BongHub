# BongHub 인수인계 문서 (HANDOFF)

> **결론 (2026-11-02 KST):** `main` 소스는 트레이 **3.4.8** + BongHub.Next **0.3.0**이에요. PR **#4·#5·#6은 병합**됐고, PR **#7**(Next 0.4.0 메모리)은 **열려 있고 main에 없어요.** PC 배포 폴더 exe 버전은 **PC에서 확인할 것**이에요. 클라우드 `data/` 동기화는 **켜져 있고** 저장소에 커밋됩니다. 이 문서가 현재 기준본이에요. 예전에 붙어 있던 3.3.1 시대 “아직/TODO”는 아래 **완료·폐기**로 정리했어요.

- 언어: 한국어 해요체. 비밀값(토큰·암호)은 넣지 않아요.
- 실제 모니터 전환·밝기·아이콘 복원·후킹·프리셋 CLI는 봇이 실행하지 않아요.

---

## 1. 한눈에 보기

| 항목 | 값 |
|---|---|
| 앱 | **BongHub** (“석봉이의 유틸 창고”). C# WinForms, .NET Framework 4.x |
| 예전 이름 | MonitorMode → BongMonitor → (BongUtil 검토) → BongHub |
| **배포 exe** | **PC에서 확인할 것.** 경로 `C:\Users\Revaptor_FX\BongHub\BongHub.exe`. 예전 기록은 3.4.7·약 103MB였고, `data/devices` 하트비트는 3.4.8을 적지만 배포 폴더와 같은지는 확인하지 않았어요 |
| **`main` 소스** | 트레이 **3.4.8** (`app/BongHub.cs`, `app/version.txt`) + Next **0.3.0** (`next/BongHub.Next/`) |
| **PR** | **#4·#5·#6 병합.** **#7 열림** (`cursor/next-memory-04-c069`, Next 0.4.0, main에 없음) |
| Next 시험 로그 | 저장소에 커밋된 로그는 **0.2.0** 스모크 **43** / 노트 **23** PASS (`next/test-logs/`, 00:33 KST). 0.3.0이 기대하는 스모크 **56**과 `baseline_memory.txt`는 아직 없어요 |
| Next 메모리 | 그 0.2.0 스모크 로그는 작업 집합 **227MB** / 프라이빗 **184MB**. 트레이만의 기준선은 비어 있어요 |
| 런타임 | `%LOCALAPPDATA%\BongHub\` |
| 클라우드 | **Enabled=true**. `data/`가 `main`에 커밋됨. 커밋 대부분은 `sync: REVAPTOR-FX …` (수는 `git log --grep='^sync:'`로 확인) |
| CI | `.github/workflows/repo-check.yml`. 버전 문자열, PR에서 `sync:`가 아닌 `data/` 수정, Notes 패치 단위 검사. Windows 빌드·스모크는 없음 |
| 자동 시작 | `Startup\BongHub.lnk` → `C:\Users\Revaptor_FX\BongHub\BongHub.exe` |

**지금 할 일:** 배포 exe 파일 버전을 PC에서 확인해요. 남은 잠금 확인은 “Next를 먼저 켠 뒤 3.4.8이 종료 코드 4로 끝나는지”예요(2026-11-02에는 배포 트레이가 같은 세션에 떠 있어서 못 했어요). PR #7은 Windows에서 다시 본 뒤에 병합해요. 프리셋 CLI는 실행하지 마세요.

---

## 2. 경로

### 2.1 PC (Revaptor-FX, 사용자 Revaptor_FX)

| 경로 | 내용 |
|---|---|
| `C:\Users\Revaptor_FX\BongHub\` | **배포 폴더**. exe 버전은 PC에서 확인할 것. 백업·`_dev\`·`res\`·`build.ps1` 등이 여기 있음 |
| `%LOCALAPPDATA%\BongHub\` | 런타임 (`config.json`, `notes\`, `desktop\`, 동기화 켜면 `cloud\` 등) |
| `%LOCALAPPDATA%\MonitorMode\` | 옛 데이터. 로드 시 대체 경로(`Core.LegacyDir`) |
| `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\BongHub.lnk` | 자동 시작 |
| `C:\Users\Revaptor_FX\Documents\Codex\2026-11-01\new-chat\BongHub\next\` | **Next 원본** 작업 폴더. 저장소 `next/`는 여기서 가져온 복사본 |

### 2.2 2026-10-31에 이미 지운 것 (문서에 “남아 있다”고 쓰여 있으면 구식)

- `C:\Users\Revaptor_FX\MonitorPresets\`
- 바탕화면 `모니터 - 작업.lnk`, `모니터 - 자리비움.lnk`
- `C:\Users\Revaptor_FX\BongAd\`
- `C:\Users\Revaptor_FX\BongHub\_dev33\`, `v33dev.tgz`, `settingstest_error.txt`

### 2.3 저장소 (`Baegovda/BongHub`)

| 경로 | 내용 |
|---|---|
| `app/` | 트레이 소스·`build.ps1`·CHANGELOG·`res/`·`_dev/` |
| `next/` | Next **0.3.0** + `test-logs/` + `next/.gitignore`. 패치 단위 검사는 `next/BongHub.Next.PatchTests/` |
| `legacy/BongAd/` | 옛 BongAd 원본 |
| `docs/HANDOFF.md` | 이 문서 |
| `data/` | 클라우드 동기화 페이로드(**커밋됨**. 사람 편집 금지) |
| 루트 `.gitignore` | 빌드·IDE 산출물만 제외. **`data/`는 무시하지 않음** (클라우드 동기화가 계속 커밋) |

machineId 참고: 작업 PC `d3fb99ad-25ca-45c1-81af-41e5ca09104a`(Revaptor-FX). 데스크톱에서 올 때 다른 id가 붙을 수 있어요.

---

## 3. 버전과 병합된 것

### 3.1 3.4.8 (`main`, 배포 여부는 PC에서 확인)

- 3.4.7에 **메모 동시 저장 방지**만 더한 거예요. 모니터 전환 로직은 그대로. PR **#4**로 `main`에 들어갔어요.
- 버전 문자열: `AppInfo.Version`, `AssemblyVersion`/`FileVersion`/`InformationalVersion`, `app/version.txt`, `app/_dev/version.txt` → 모두 **3.4.8**.
- Next가 없을 때의 트레이 동작은 3.4.7과 같아요.
- 배포 폴더에 이 exe가 올라가 있는지는 이 문서가 단정하지 않아요. 파일 버전을 PC에서 보세요.

### 3.2 메모 저장 잠금 (`Local\BongHub_NotesWriter`)

먼저 잡은 쪽이 `%LOCALAPPDATA%\BongHub` 메모의 유일한 저장자예요. 프로세스 종료까지 유지하고, abandoned면 다음 프로세스가 이어받아요.

**3.4.8 트레이** — 잠금을 트레이 뮤텍스(`Local\BongHub_Tray`)보다 **먼저** 잡아요.

- 얻으면 트레이 실행.
- 못 얻고 트레이도 없으면 Next가 저장 중 → 안내 후 **종료 코드 4** (설정·메모·클라우드 시작 안 함).
- 못 얻고 트레이가 있으면 **코드 1** (이미 실행 중).
- 잠금은 얻었는데 트레이를 다른 3.4.x가 갖고 있으면 잠금 놓고 **코드 1** (옛 3.4.7 저장권을 뺏지 않음).

**Next** — 단일 인스턴스 후, 기본 데이터 폴더일 때만.

1. 트레이 뮤텍스 있으면 읽기 전용.
2. 없으면 writer 잠금. 실패 시 읽기 전용.
3. 잡은 직후 트레이를 한 번 더 봄. 뜨면 잠금 놓고 읽기 전용.
4. 쓰기 직전에도 트레이를 봄. 잠금 모르는 **3.4.7 exe**가 나중에 켜지면 그 저장부터 멈추고, 그 프로세스는 다시 쓰지 않아요.

프리셋 CLI(`BongHub.exe 작업` 등), `resize`, `workspace`는 이 잠금을 보지 않아요. **실행하지 마세요.**

### 3.3 BongHub.Next 0.3.0 (`main`, PR #6)

- `next/BongHub.Next/`, `net10.0-windows`, WPF, H.NotifyIcon.Wpf 2.4.1. WinForms 없음. SDK 기준은 루트 `global.json`의 **10.0.403**이에요.
- 사이드바 첫 항목: **모니터 프리셋**(표시만, 전환은 3.4.x).
- `Notes.Opacity` 40~100% → 메모 창 전체 불투명도.
- `ConfigStore.SetNotesValue`는 Notes 한 키의 JSON 값만 갈아끼워요. 다른 키와 들여쓰기는 바이트 그대로고, 값이 같으면 파일을 다시 쓰지 않아요. 안전하게 패치하지 못하면 파일을 통째로 덮어쓰지 않아요.
- 빌드: `dotnet publish next\BongHub.Next\BongHub.Next.csproj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true`
- 패치 검사(Windows 불필요): `dotnet run --project next\BongHub.Next.PatchTests\BongHub.Next.PatchTests.csproj -c Release`
- 자세한 것: `next/README.md`. 이식 순서는 `next/PORTING_PLAN.md` (PR #5).
- PR **#7**은 0.4.0 메모리 작업이에요. `main`의 Next는 아직 **0.3.0**이에요.

### 3.4 Windows 시험 (2026-11-02, 주인 PC, 격리 임시 폴더 + 데이터 복사본)

프리셋 CLI와 실제 모니터는 건드리지 않았어요.

| 항목 | 결과 |
|---|---|
| `app\build.ps1` / `csc` 로 3.4.8 | **PASS**, 오류 0. 당시 경고 4건(CS0108 `PaletteForm.Top`, CS0219 미사용 `pos`, CS0675 부호 확장 OR 2곳, CS0649 `PaintBack`). 이 커밋에서 그 경고를 없앰. 경고 0 재빌드는 아직 |
| Next `dotnet publish` SDK **8.0.426** win-x64 (당시 0.2.0) | **PASS**. 0.3.0은 SDK 10 기준이고, 그 게시 시험은 이 표에 없어요 |
| `extrastest` | **26/0 PASS** |
| `settingstest` | **PASS** |
| Next `--smoketest` | **43 PASS** |
| Next `--notetest` | **23 PASS** (읽기 전용일 때 저장 차단 포함) |
| 3.4.x를 먼저 켜고 Next | Next `readonly=True` **확인** |
| Next를 먼저 켜고 3.4.8 | 종료 코드 4는 **아직 미확인**. 같은 세션에 배포 트레이가 실행 중이라, 그 프로세스를 끈 뒤에 다시 봐야 함 |
| 배포 폴더 exe | 이 기록 당시 문서는 3.4.7이라고 했어요. **지금은 PC에서 확인할 것** |

---

## 4. 사용자·규칙

- 사용자: Exodus / 호칭 “석봉이”. GitHub **Baegovda**. Asia/Seoul.
- 봇은 존댓말 해요체, 짧게 결론부터.
- **금지·필수**
  1. AutoHotkey 제안 금지.
  2. 테스트 창을 사용자 화면에 띄우지 않음(숨은 데스크톱·화면 밖 좌표).
  3. 실제 모니터/밝기/아이콘/후킹 테스트 실행 금지.
  4. 위험 기능 코드 기본값은 꺼짐(사용자 `config`는 켜져 있을 수 있음 → 5장).
  5. 배포 전 `backup\`에 백업.
  6. 표기: **BongHub**, 탭 “모니터 프리셋” / “카톡 광고 제거”.
  7. 설정은 바뀌는 즉시 저장. “적용” 버튼 금지.
  8. 그라데이션 다크·커스텀 타이틀바·블록 아이콘 고정.
  9. 빌드마다 버전·CHANGELOG. 못 한 테스트는 “직접 확인해 주세요”.
  10. 메일/레포 삭제 등 외부 영향은 명시 요청 있을 때만.

---

## 5. 런타임 설정 요약

데이터 루트: `%LOCALAPPDATA%\BongHub\` = `C:\Users\Revaptor_FX\AppData\Local\BongHub\`.

동기화된 `data/config.json`(2026-11-01 기준) 요지:

- 프리셋: **작업**, **자리비움**, **게임**(Ctrl+Alt+1 / 0 / 5). 작업에 Workspace `6d0f354b` 연계.
- General: Idle 15분→자리비움, OverlayOpacity **70**.
- Notes: Opacity **80**, Hotkey Ctrl+Alt+N, 메모 파일 **7**개.
- **Cloud.Enabled = true**, Repo `Baegovda/BongHub`, Branch main, PullMinutes 2.
- Pt: 사용자 설정상 Zones/Grab/Palette/Paste/Peek 등이 **켜짐**. 코드 기본값(Grab/Palette/Paste/Peek=꺼짐, Paste 기본키 Win+Shift+V 등)과 다를 수 있어요. **문서·코드 기본 ≠ PC 실제 설정**을 항상 구분하세요.

`data/` 레이아웃: `meta.json`, `config.json`+`config.stamp.json`, `notes/`, `desktop/layouts.json`, `devices/<기기>.json`. 충돌은 파일 mtime, 삭제는 `_deleted.json`. 인증은 PC의 `gh`(keyring).

---

## 6. 기능 개요 (본체 3.4.x)

상세 이력은 `app/CHANGELOG.md`(3.0.0~3.4.8)를 보세요. 요지만:

- 모니터 프리셋(MMT) + 밝기 + 아이콘 배치 + 작업 영역 연계(3.3.1+)
- 전환 오버레이, 창 위치 규칙, 카톡 광고 제거(내장 BongAd), 스티커 메모, 바탕화면 아이콘
- PowerToys 탭: FancyZones, 잡아서 이동, 작업 영역, 명령 팔레트, 고급 붙여넣기, Peek(WebP libwebp 1.6.0), Image Resizer
- 일반: 자동 시작, 자리비움, 클라우드, 잠금화면 숨기기, 절전 복귀 인증, 로컬 무암호 자동 로그인 등(3.4.x)

**코드 기본 단축키(3.4.0+):**

| 단축키 | 기능 |
|---|---|
| Ctrl+Alt+1 / 0 | 작업 / 자리비움 프리셋 |
| Ctrl+Alt+N | 새 스티커 메모 |
| Win+Alt+Space | 명령 팔레트(기능 ON일 때) |
| Win+Shift+V / Ctrl+Win+Alt+V | 고급 붙여넣기 / 서식 없이 |
| Space | Peek(탐색기 비입력, 기능 ON) |

단축키는 **설정 저장 즉시** 재등록해요(3.4.0). “창을 닫을 때”는 구식 설명이에요.

---

## 7. 빌드·배포

### 7.1 저장소에서 빌드

```powershell
# 저장소 루트
powershell app\build.ps1
# 출력: app\BongHub.exe
```

- `csc.exe` (.NET FX 4.x), `/codepage:65001 /optimize+`
- 리소스: MultiMonitorTool.exe, work_layout.cfg, BongAd.exe, **dwebp.exe**, **webpmux.exe**, icon_blocks.ico
- AppInfo.Version과 AssemblyFileVersion(`x.y.z.0`)이 다르면 실패

개발: `app\_dev\dev.ps1` → `_dev\BongHub.exe`, 테스트 헬퍼 `T`.

### 7.2 PC 배포 (배포 폴더)

```powershell
cd C:\Users\Revaptor_FX\BongHub
$v = (Get-Item BongHub.exe).VersionInfo.FileVersion -replace '\.0$',''
$ts = Get-Date -Format yyyyMMdd_HHmmss
Copy-Item BongHub.cs,BongHub.exe,build.ps1,CHANGELOG.md backup\...  # 관례대로 백업
Get-Process BongHub -EA 0 | ? Path -eq "$PWD\BongHub.exe" | Stop-Process
# 저장소에서 빌드한 산출물·소스를 이 폴더에 반영한 뒤
powershell -ExecutionPolicy Bypass -File .\build.ps1   # 또는 복사한 exe 사용
Start-Process .\BongHub.exe
```

롤백은 `backup\BongHub.<ver>.<ts>.exe/.cs`로 되돌리면 돼요. 새 키가 있는 config를 옛 exe가 저장하면 키가 사라질 수 있어요.

---

## 8. 테스트

### 8.1 원칙

- `_dev\testdesk.ps1` 숨은 데스크톱, 또는 `TestEnv.Off` 화면 밖(-30000,-30000).
- 결과는 exe 폴더 txt/png. 예외 시 `crash_<모드>.txt`, exit 3.
- 모르는 `*test`/`*bench` → exit **9** (실제 전환으로 떨어지지 않음).

### 8.2 모드 (Program.Main)

| 모드 | 의미 |
|---|---|
| configtest, windowtest, kakaotest, notestest, notesbench, imagetest | 설정·창·카톡·메모·이미지 |
| settingstest, tabbench, icontest, cloudtest | UI·아이콘·가짜 기기 동기화 |
| pttest, perfbench, jittertest, extrastest, overlaytest, watchbench | PowerToys·성능·오버레이(overlay는 숨은 데스크톱만) |
| uiperf, inputtest, interactiontest | UI/입력 회귀 |
| lockscreen, sleepauth, blankautologon, blankcheck, lockscreenpolicytest | 잠금화면·절전인증·무암호 자동로그인 |
| workspace / resize / version | CLI (workspace·실제 창 이동 — **실행 금지**에 가깝게 취급) |
| paritytest | TestEnv.Off만 켜고 **case 없음** → exit 9 |

### 8.3 절대 실행 금지

`BongHub.exe 작업|자리비움|work|away`, MMT enable/disable, RealBright, 실제 아이콘 복원, `workspace <이름>`, 사용자 데스크톱에서 설정창 테스트, 실제 `data/`에 테스트 쓰기.

---

## 9. 브랜치·이력·남은 일

### 9.1 원격 브랜치

- `main` — 소스 3.4.8 + Next 0.3.0 + 동기화 `data/`
- 병합됨: PR #1–#3 (3.3.1–3.3.3), PR #4 (`cursor/next-writer-lock-c7a7`), PR #5 (`cursor/next-porting-plan-d692`), PR #6 (`cursor/next-phase0-net10-f746`)
- 열림: PR #7 `cursor/next-memory-04-c069` (Next 0.4.0). `main`에 없음
- 원격에 브랜치가 남아 있음: `cursor/backup-bonghub-331-576e`, `cursor/smooth-resize-332-576e`, `cursor/resize-1to1-333-576e`, `cursor/next-writer-lock-c7a7`, `cursor/next-porting-plan-d692`, `cursor/next-phase0-net10-f746`. #7 브랜치는 PR이 열려 있는 동안 지우지 않아요

### 9.2 짧은 타임라인

30일 AHK→C# MonitorMode → BongMonitor → BongHub. 3.0~3.3.1(프리셋↔작업영역). 3.3.2~3.4.8 UI·메모·PowerToys·WebP·로그인·메모 저장 잠금. 10-31 PC 옛 경로 정리. 11-01 클라우드 sync. 11-02 PR #4·#5·#6 병합, PR #7은 열린 채.

### 9.3 3.3.1 시대 TODO → 현재

| 예전 TODO | 상태 |
|---|---|
| 3.3.1 배포·보고 | **완료** (이후 3.4.7까지 배포) |
| MonitorPresets·모니터 lnk·BongAd 폴더·_dev33·v33dev·settingstest_error 정리 | **2026-10-31 삭제 완료** |
| 클라우드 미사용 | **폐기** — 지금은 Enabled, `data/` 커밋 중 |
| 단축키는 창 닫을 때 적용 | **폐기** — 저장 즉시 재등록 |
| 메모 2개 | **폐기** — 현재 7개 |

### 9.4 지금 남은 일

1. 배포 exe 파일 버전을 PC에서 확인 (`FileVersion`). 소스 `main`은 3.4.8
2. 배포 트레이를 끈 세션에서만, Next를 먼저 켜고 3.4.8이 안내 후 **종료 코드 4**로 끝나는지 확인. 반대 순서(3.4.x 먼저 → Next 읽기 전용)는 2026-11-02에 확인됨
3. PR #7을 Windows에서 재현(스모크 56, 노트 25, `next/measure-baseline.ps1`)한 뒤 병합하거나 돌려보내기. 0.3.0 `baseline_memory.txt`는 아직 없음
4. 경고를 없앤 3.4.8 소스로 `build.ps1` 경고 0인지는 PC에서 한 번 더 확인. 배포 폴더를 바꿀지는 1번 결과에 따름
5. (선택) 이미 병합된 `cursor/*` 브랜치 삭제. `cursor/next-memory-04-c069`는 PR #7이 열린 동안 유지
6. 사용자가 직접: 실제 모니터 전환·카톡·FancyZones 등 실사용 확인
7. BongAd **봇** 삭제 여부는 사용자 확인

---

## 10. 이어받는 봇 체크리스트

```powershell
cd C:\Users\Revaptor_FX\BongHub
Get-Process BongHub -EA 0 | select Id,Path,StartTime,@{n='MB';e={[int]($_.WS/1MB)}}
(Get-Item BongHub.exe).VersionInfo.FileVersion
# 배포 폴더 파일 버전은 PC에서 확인. 소스 main은 3.4.8.0
Test-Path $env:LOCALAPPDATA\BongHub\cloud
& 'C:\Program Files\GitHub CLI\gh.exe' auth status
# 저장소: main = 3.4.8 + Next 0.3.0. 열린 PR은 #7
```

보고 예: “인계받았어요. main 소스는 트레이 3.4.8, Next 0.3.0이에요. 배포 exe는 PC에서 확인해야 하고, PR #7이 열려 있어요. 클라우드 `data/`는 동기화 중이에요.”

---

## 11. 용어

| 용어 | 뜻 |
|---|---|
| 모니터 프리셋 | 모니터 on/off·주 모니터·밝기·아이콘·작업영역 묶음 |
| MMT | NirSoft MultiMonitorTool |
| 작업 영역 | 여러 창 배치 저장/실행(PowerToys Workspaces) |
| NotesWriter | `Local\BongHub_NotesWriter` 메모 단일 저장자 |
| Next | BongHub.Next 0.3.0 WPF 프로토타입 (`net10.0-windows`). 0.4.0은 PR #7 |
| 숨은 데스크톱 | `_dev\testdesk.ps1`, CreateDesktop `BongHubTest` |

---

## 12. 문서 검증 (이번 라운드)

| 확인 | 결과 |
|---|---|
| `main` `app/BongHub.cs` Version·NotesWriterLock | 3.4.8, 종료 코드 4 경로는 소스에 있음. PC에서 그 경로를 끝까지 본 기록은 없음 |
| Next AppEnv / csproj | **0.3.0**, `net10.0-windows`. 커밋된 test-logs는 0.2.0의 43/23·227MB |
| `data/config.json` | Cloud.Enabled, 프리셋 3개(게임 포함), Notes 7개·Opacity 80, OverlayOpacity 70, Pt 여러 항목 켜짐. 코드 기본 단축키는 Win+Alt+Space / Win+Shift+V / Ctrl+Win+Alt+V / Space. 동기화 파일의 붙여넣기 키는 Ctrl+Alt+V |
| `data/` sync 커밋 | `main`의 대부분. 정확한 수는 `git log --grep='^sync:'` |
| BongAd.exe | 15,360 bytes |
| GitHub Actions | `repo-check.yml` 한 개. 버전·`data/` PR·Notes 패치. Windows 러너는 없음 |
| 열린 PR | #7 Next 0.4.0. #4·#5·#6은 병합 |
| 2026-11-02 격리 폴더 시험 | 당시 build.ps1 오류 0, 0.2.0 publish PASS, extrastest 26/0, settingstest PASS, Next 43/23 PASS, 3.4.x→Next 읽기 전용 확인 |

**아직 아닌 것:** 배포 exe 파일 버전(PC에서 확인), Next를 먼저 켠 뒤 3.4.8이 코드 4로 끝나는지, 0.3.0 Windows 스모크 56과 메모리 기준선, PR #7 재현, 실제 모니터 전환.
