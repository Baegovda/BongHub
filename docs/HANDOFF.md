# BongHub 인수인계 문서 (HANDOFF)

> **결론 (2026-11-02 KST):** PC 배포 폴더에서 돌아가는 exe는 아직 **3.4.7**이에요. PR #4(`cursor/next-writer-lock-c7a7`) 소스(트레이 **3.4.8** + Next **0.2.0**)는 같은 날 주인 PC의 **격리된 임시 폴더**(데이터 복사본)에서 빌드·시험 PASS예요. 배포 폴더에는 아직 넣지 않았어요. 클라우드 `data/` 동기화는 **켜져 있고** 저장소에 커밋됩니다. 이 문서가 현재 기준본이에요. 예전에 붙어 있던 3.3.1 시대 “아직/TODO”는 아래 **완료·폐기**로 정리했어요.

- 언어: 한국어 해요체. 비밀값(토큰·암호)은 넣지 않아요.
- 실제 모니터 전환·밝기·아이콘 복원·후킹·프리셋 CLI는 봇이 실행하지 않아요.

---

## 1. 한눈에 보기

| 항목 | 값 |
|---|---|
| 앱 | **BongHub** (“석봉이의 유틸 창고”). C# WinForms, .NET Framework 4.x |
| 예전 이름 | MonitorMode → BongMonitor → (BongUtil 검토) → BongHub |
| **배포·실행 중** | **3.4.7** @ `C:\Users\Revaptor_FX\BongHub\BongHub.exe` (메모리 약 **103MB**). 3.4.8은 아직 이 경로에 없음 |
| **PR #4 소스** | 트레이 **3.4.8** (`app/BongHub.cs`) + Next **0.2.0** (`next/BongHub.Next/`). 2026-11-02 임시 폴더 빌드·시험 PASS, 배포 전 |
| `main` 소스 버전 | 3.4.7 (`app/version.txt`) |
| Next 시험 | 잠금 패치 전 로그: 스모크 **43** / 노트 **23** PASS (`next/test-logs/`, 00:33 KST). 패치 후 재시험도 스모크 43, 노트 23(읽기 전용 저장 차단 포함) PASS |
| Next 메모리 | 약 **227–239MB** (스모크 로그 WS 227MB / 프라이빗 184MB) |
| 런타임 | `%LOCALAPPDATA%\BongHub\` |
| 클라우드 | **Enabled=true**. `data/`가 `main`에 커밋됨. sync 커밋 약 **101**개 (`sync: REVAPTOR-FX …`) |
| CI | **없음** (GitHub Actions 워크플로 0) |
| 자동 시작 | `Startup\BongHub.lnk` → `C:\Users\Revaptor_FX\BongHub\BongHub.exe` |

**지금 할 일:** 격리 폴더 시험은 통과했어요. 남은 확인은 “Next를 먼저 켠 뒤 3.4.8이 종료 코드 4로 끝나는지”뿐이에요(이번엔 같은 세션에 3.4.7 트레이가 떠 있어서 못 했어요). 그다음 배포 폴더의 트레이를 3.4.8로 바꾸면 돼요. 프리셋 CLI는 실행하지 마세요.

---

## 2. 경로

### 2.1 PC (Revaptor-FX, 사용자 Revaptor_FX)

| 경로 | 내용 |
|---|---|
| `C:\Users\Revaptor_FX\BongHub\` | **배포 폴더**. 실행 중 exe는 3.4.7. 백업·`_dev\`·`res\`·`build.ps1` 등이 여기 있음 |
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
| `next/` | Next 0.2.0 + `test-logs/` + `next/.gitignore` |
| `legacy/BongAd/` | 옛 BongAd 원본 |
| `docs/HANDOFF.md` | 이 문서 |
| `data/` | 클라우드 동기화 페이로드(**커밋됨**. 사람 편집 금지) |
| 루트 `.gitignore` | 빌드·IDE 산출물만 제외. **`data/`는 무시하지 않음** (클라우드 동기화가 계속 커밋) |

machineId 참고: 작업 PC `d3fb99ad-25ca-45c1-81af-41e5ca09104a`(Revaptor-FX). 데스크톱에서 올 때 다른 id가 붙을 수 있어요.

---

## 3. 버전과 PR #4에서 바뀐 것

### 3.1 3.4.8 (PR 소스, 미배포)

- 3.4.7에 **메모 동시 저장 방지**만 더한 거예요. 모니터 전환 로직은 그대로.
- 버전 문자열: `AppInfo.Version`, `AssemblyVersion`/`FileVersion`/`InformationalVersion`, `app/version.txt`, `app/_dev/version.txt` → 모두 **3.4.8**.
- Next가 없을 때의 트레이 동작은 3.4.7과 같아요.

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

### 3.3 BongHub.Next 0.2.0

- `next/BongHub.Next/`, `net8.0-windows`, WPF+WinForms.
- 사이드바 첫 항목: **모니터 프리셋**(표시만, 전환은 3.4.x).
- `Notes.Opacity` 40~100% → 메모 창 전체 불투명도.
- 빌드: `dotnet publish next\BongHub.Next\BongHub.Next.csproj -c Release -r win-x64`
- 자세한 것: `next/README.md`

### 3.4 Windows 시험 (2026-11-02, 주인 PC, 격리 임시 폴더 + 데이터 복사본)

프리셋 CLI와 실제 모니터는 건드리지 않았어요.

| 항목 | 결과 |
|---|---|
| `app\build.ps1` / `csc` 로 3.4.8 | **PASS**, 오류 0. 당시 경고 4건(CS0108 `PaletteForm.Top`, CS0219 미사용 `pos`, CS0675 부호 확장 OR 2곳, CS0649 `PaintBack`). 이 커밋에서 그 경고를 없앰. 경고 0 재빌드는 아직 |
| Next `dotnet publish` SDK **8.0.426** win-x64 | **PASS** |
| `extrastest` | **26/0 PASS** |
| `settingstest` | **PASS** |
| Next `--smoketest` | **43 PASS** |
| Next `--notetest` | **23 PASS** (읽기 전용일 때 저장 차단 포함) |
| 3.4.x를 먼저 켜고 Next | Next `readonly=True` **확인** |
| Next를 먼저 켜고 3.4.8 | 종료 코드 4는 **아직 미확인**. 같은 세션에 배포본 3.4.7 트레이가 실행 중이라, 3.4.7을 끈 뒤에 다시 봐야 함 |
| 배포 폴더 exe | 여전히 **3.4.7**. 3.4.8은 배포하지 않음 |

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

- `main` — 배포 기준 소스 3.4.7 + 동기화 `data/`
- `cursor/next-writer-lock-c7a7` — PR #4 (3.4.8 + Next)
- 잔여(팁이 main에 안 붙음, 아직 삭제 안 함): `cursor/backup-bonghub-331-576e`, `cursor/smooth-resize-332-576e`, `cursor/resize-1to1-333-576e`

### 9.2 짧은 타임라인

30일 AHK→C# MonitorMode → BongMonitor → BongHub. 3.0~3.3.1(프리셋↔작업영역). 3.3.2~3.4.7 UI·메모·PowerToys·WebP·로그인 관련. 10-31 PC 옛 경로 정리. 11-01 클라우드 sync 본격화·Next 시험·PR #4 작성.

### 9.3 3.3.1 시대 TODO → 현재

| 예전 TODO | 상태 |
|---|---|
| 3.3.1 배포·보고 | **완료** (이후 3.4.7까지 배포) |
| MonitorPresets·모니터 lnk·BongAd 폴더·_dev33·v33dev·settingstest_error 정리 | **2026-10-31 삭제 완료** |
| 클라우드 미사용 | **폐기** — 지금은 Enabled, `data/` 커밋 중 |
| 단축키는 창 닫을 때 적용 | **폐기** — 저장 즉시 재등록 |
| 메모 2개 | **폐기** — 현재 7개 |

### 9.4 지금 남은 일

1. 3.4.7 트레이를 끈 뒤, Next를 먼저 켜고 3.4.8이 안내 후 **종료 코드 4**로 끝나는지 확인. 반대 순서(3.4.x 먼저 → Next 읽기 전용)는 2026-11-02에 확인됨
2. 경고를 없앤 소스로 `build.ps1`을 한 번 더 돌려 경고 0인지 확인한 뒤, 배포 폴더 트레이를 3.4.8로 교체
3. (선택) leftover `cursor/*` 브랜치 삭제
4. 사용자가 직접: 실제 모니터 전환·카톡·FancyZones 등 실사용 확인
5. BongAd **봇** 삭제 여부는 사용자 확인

---

## 10. 이어받는 봇 체크리스트

```powershell
cd C:\Users\Revaptor_FX\BongHub
Get-Process BongHub -EA 0 | select Id,Path,StartTime,@{n='MB';e={[int]($_.WS/1MB)}}
(Get-Item BongHub.exe).VersionInfo.FileVersion
# 기대: 지금은 3.4.7.x / 교체 후 3.4.8.x
Test-Path $env:LOCALAPPDATA\BongHub\cloud
& 'C:\Program Files\GitHub CLI\gh.exe' auth status
# 저장소
# gh api repos/Baegovda/BongHub/contents/data --jq '.[].name'
# 브랜치: main, cursor/next-writer-lock-c7a7, leftover cursor/* 세 개
```

보고 예: “인계받았어요. PC는 3.4.7 실행 중이고, PR에는 3.4.8+Next가 있어요. 클라우드 `data/`는 동기화 중이에요.”

---

## 11. 용어

| 용어 | 뜻 |
|---|---|
| 모니터 프리셋 | 모니터 on/off·주 모니터·밝기·아이콘·작업영역 묶음 |
| MMT | NirSoft MultiMonitorTool |
| 작업 영역 | 여러 창 배치 저장/실행(PowerToys Workspaces) |
| NotesWriter | `Local\BongHub_NotesWriter` 메모 단일 저장자 |
| Next | BongHub.Next 0.2.0 WPF 프로토타입 |
| 숨은 데스크톱 | `_dev\testdesk.ps1`, CreateDesktop `BongHubTest` |

---

## 12. 문서 검증 (이번 라운드)

| 확인 | 결과 |
|---|---|
| PR 브랜치 `app/BongHub.cs` Version·NotesWriterLock·Main switch | 3.4.8, exit 4, 테스트 모드 추가분 확인 |
| Next AppEnv/MainWindow/csproj/test-logs | 0.2.0, 사이드바·Opacity·43/23 PASS·227MB |
| `data/config.json` | Cloud.Enabled, 프리셋 3개(게임 포함), Notes 7개·Opacity 80, OverlayOpacity 70, Pt 여러 항목 켜짐. 코드 기본 단축키는 Win+Alt+Space / Win+Shift+V / Ctrl+Win+Alt+V / Space |
| `data/` sync 커밋 | `main` 기준 101개 |
| `app/BongHub.cs` 크기 | 문서 검토 시점 699,850 bytes (그 다음 경고 정리로 조금 달라짐) |
| BongAd.exe | 15,360 bytes |
| GitHub Actions | 워크플로 없음 |
| leftover 브랜치 | `cursor/backup-bonghub-331-576e`, `cursor/smooth-resize-332-576e`, `cursor/resize-1to1-333-576e` (팁이 `main`의 조상 아님) |
| 2026-11-02 격리 폴더 시험 | build.ps1 오류 0, publish PASS, extrastest 26/0, settingstest PASS, Next 43/23 PASS, 3.4.x→Next 읽기 전용 확인 |

**아직 아닌 것:** Next를 먼저 켠 뒤 3.4.8이 코드 4로 끝나는지(당시 3.4.7 트레이가 같은 세션에 떠 있었음), 경고 수정 후 재빌드, 배포 폴더를 3.4.8로 교체, 실제 모니터 전환.
