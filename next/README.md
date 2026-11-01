# BongHub.Next

**0.3.0** — .NET 10 WPF 프로토타입이에요. 메모·설정 UI는 0.2.0과 같고, 모니터 전환은 아직 3.4.x 트레이가 맡아요. 0단계는 기준선(프레임워크, 트레이, 측정)만 옮겨요.

## 경로

| 구분 | 경로 |
|---|---|
| 저장소(이 폴더) | `next/BongHub.Next/` |
| 공유 데이터 | `%LOCALAPPDATA%\BongHub\` (3.4.x와 동일) |
| Next 전용 설정 | `%LOCALAPPDATA%\BongHub.Next\settings.json` |
| 변경 이력 | `next/CHANGELOG.md` |

`next/.gitignore`는 `bin/`, `obj/`, `publish/`, `.vs/`, `*.user`를 제외해요. 저장소 루트 `global.json`은 SDK **10.0.403**을 기준으로 하고, 같은 10 기능 대역의 더 높은 SDK로 진행해요.

## 빌드

Windows, 저장소 루트:

```powershell
dotnet publish next\BongHub.Next\BongHub.Next.csproj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true
```

같은 설정은 `next/BongHub.Next/Properties/PublishProfiles/ReadyToRun.pubxml`에도 있어요.

대상: `net10.0-windows`, WPF, H.NotifyIcon.Wpf **2.4.1**. WinForms는 쓰지 않아요. 버전은 csproj·`AppEnv.Version` 모두 **0.3.0**. 게시는 ReadyToRun, `win-x64`, 프레임워크 종속이에요. 단일 파일·트림·Native AOT는 꺼져 있어요. `ConcurrentGarbageCollection`은 **false**예요. 기준선 표를 보기 전에는 바꾸지 않아요.

출력: `next\BongHub.Next\bin\Release\net10.0-windows\win-x64\publish\`.

## 시험

임시 데이터 폴더를 쓰고 `Local\BongHub_NotesWriter`를 잡지 않아요. `--off`는 화면 밖 (−30000, −30000)이고 Next 단일 실행 뮤텍스를 잡지 않아요. 스모크·노트 테스트는 알림 영역에 아이콘을 넣지 않아요.

```text
BongHub.Next.exe --smoketest
BongHub.Next.exe --notetest --data <notes 복사본>
```

0.2.0 기록(`next/test-logs/next_smoketest.txt`, `next_notetest.txt`)은 스모크 **43 PASS**, 노트 **23 PASS**예요. 0.3.0 스모크는 그 43개에 트레이 단언 13개를 더해 **56 PASS**가 기대값이에요. 노트는 그대로 **23 PASS**(읽기 전용 저장 차단 포함)예요. 노트 테스트에는 `data/notes`의 json·rtf 7개를 임시 폴더로 복사해 `--data`로 넘기세요. `data/` 원본은 고치지 마세요.

## 메모리 기준선

```powershell
powershell -ExecutionPolicy Bypass -File next\measure-baseline.ps1
```

BongHub.Next가 이미 켜져 있으면 그 Next만 종료한 뒤에 실행하세요. `BongHub.exe`(3.4.x)는 끄지 마세요. 프리셋 CLI, MMT, 실제 모니터, `%LOCALAPPDATA%\BongHub`, `data/` 쓰기는 하지 않아요. 트레이만·숨긴 설정 창 구간에는 알림 영역 아이콘이 잠깐 보였다가 그 프로세스가 끝나면 사라져요. 창은 화면 밖에 있어요.

결과 파일: `next/test-logs/baseline_memory.txt`. 시나리오 4개(트레이만, 설정 창, 숨긴 설정 창, 메모 7개) × GC 동시 수집 꺼짐/켜짐 × 3회, 각 중앙값. 준비된 뒤 20초 유휴의 `WorkingSet64`·`PrivateMemorySize64`예요. 이 저장소에는 아직 그 숫자가 없어요.

`next_startup.txt`(`--out`) 줄:

| 시나리오 | 있어야 하는 줄 | 없어야 하는 줄 |
|---|---|---|
| 트레이만 `--tray --off` | `tray_visible_ms=`, `main_window_created=0`, `startup_notes_opened=0` | `first_frame_ms`, `settings_hidden` |
| 설정 창 `--off` | `first_frame_ms=`, `main_window_created=1`, `startup_notes_opened=0` | `tray_visible_ms`, `settings_hidden` |
| 숨긴 설정 창 `--tray --off --idle-hidden` | `tray_visible_ms=`, `first_frame_ms=`, `main_window_created=1`, `settings_hidden=1`, `startup_notes_opened=0` | — |
| 메모 7개 `--off` + 노트 복사 | `first_frame_ms=`, `main_window_created=1`, `startup_notes_opened=7` | `tray_visible_ms`, `settings_hidden` |

## 사이드바

첫 항목이 **모니터 프리셋**이에요(읽기 전용 목록). 이어서 스티커 메모·일반·더 보기·정보. 스티커 메모 `Notes.Opacity`는 40~100% 슬라이더로, 3.4.8과 같이 메모 창 전체 불투명도에 적용해요.

## 메모 저장 잠금

3.4.8과 공유하는 뮤텍스: `Local\BongHub_NotesWriter`. 트레이 뮤텍스 `Local\BongHub_Tray`가 있으면 Next는 읽기 전용. Next는 `Local\BongHub_Tray`를 만들지 않아요. 자세한 순서는 `docs/HANDOFF.md`를 보세요.

## 주의

- 프리셋 CLI·실제 모니터 전환은 Next가 하지 않아요.
- 3.4.7(잠금 없는 배포본)과 Next를 동시에 켜면 짧은 겹침이 남을 수 있어요. 3.4.8로 트레이를 바꾸면 사라져요.
