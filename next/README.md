# BongHub.Next

**0.5.0** — .NET 10 WPF예요. 1단계는 스티커 메모를 트레이 3.4.9와 맞춰요. 모니터 전환은 아직 3.4.x 트레이가 맡아요. 소프트웨어 렌더와 동시 GC는 0.4.0 그대로예요.

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

대상: `net10.0-windows`, WPF, H.NotifyIcon.Wpf **2.4.1**. WinForms는 쓰지 않아요. 버전은 csproj·`AppEnv.Version` 모두 **0.5.0**. 게시는 ReadyToRun, `win-x64`, 프레임워크 종속이에요. 단일 파일·트림·Native AOT는 꺼져 있어요. `ConcurrentGarbageCollection`은 **true**예요.

출력: `next\BongHub.Next\bin\Release\net10.0-windows\win-x64\publish\`.

## 시험

임시 데이터 폴더를 쓰고 `Local\BongHub_NotesWriter`를 잡지 않아요. `--off`는 화면 밖 (−30000, −30000)이고 Next 단일 실행 뮤텍스를 잡지 않아요. 스모크·노트 테스트는 알림 영역에 아이콘을 넣지 않아요.

```text
BongHub.Next.exe --smoketest
BongHub.Next.exe --notetest --data <notes 복사본>
```

0.2.0 기록(`next/test-logs/next_smoketest.txt`, `next_notetest.txt`)은 스모크 **43 PASS**, 노트 **23 PASS**예요. 0.3.0 스모크는 **56**, 0.4.0 스모크는 **58**, 노트는 **26**이에요. 0.5.0 스모크는 **65 PASS**, 노트는 `data/notes` 7개(그림 포함)를 복사했을 때 **89 PASS**예요. 늘린 단언은 글자 7–48, 메모 페이지의 모두 보이기·숨기기·복사·삭제, 트레이 메뉴 항목, 핫키 미등록, 충돌 풍선, 모두 숨기기/보이기, 그리고 노트 쪽의 삭제·단축키 흉내·맞춤 색·그림 바이트·`picwgoal`·그림 가드·뷰어·클램프·읽기 전용 삭제예요. 노트 테스트에는 `data/notes`의 json·rtf 7개를 임시 폴더로 복사해 `--data`로 넘기세요. `data/` 원본은 고치지 마세요. `--off`에서는 `RegisterHotKey`와 `SetWindowsHookEx`를 호출하지 않아요.

## 메모리 기준선

```powershell
powershell -ExecutionPolicy Bypass -File next\measure-baseline.ps1
```

BongHub.Next가 이미 켜져 있으면 그 Next만 종료한 뒤에 실행하세요. `BongHub.exe`(3.4.x)는 끄지 마세요. 프리셋 CLI, MMT, 실제 모니터, `%LOCALAPPDATA%\BongHub`, `data/` 쓰기는 하지 않아요. 트레이만·숨긴 설정 창 구간에는 알림 영역 아이콘이 잠깐 보였다가 그 프로세스가 끝나면 사라져요. 창은 화면 밖에 있어요.

결과 파일: `next/test-logs/baseline_memory.txt`. 시나리오 4개(트레이만, 설정 창, 숨긴 설정 창, 메모 7개) × GC 동시 수집 꺼짐/켜짐 × 3회, 각 중앙값. 준비된 뒤 20초 유휴의 `WorkingSet64`·`PrivateMemorySize64`예요. 판정은 **gc_concurrent=true**(csproj 기본값) 중앙값이고, **작업 집합과 private 둘 다** 아래 숫자 이하여야 해요. 작업 집합만 내리고 private가 그대로면 실패한 거예요. `render_mode=software`와, 창이 있는 시나리오의 `hwnd_render_mode=SoftwareOnly`가 스타트업 텍스트에 있어야 해요. `render_tier`는 하드웨어 능력을 적으므로 2여도 소프트웨어 렌더가 켜진 상태일 수 있어요. `idle_trim=1`은 정리가 돌았다는 표시일 뿐이고, 그 직후의 작업 집합 숫자는 로그에 없어요.

0.5.0은 이 경로에 창이나 후킹을 더하지 않아요. `--off`에서는 핫키 메시지 창을 만들지 않고, msftedit도 불러오지 않아요. 0.4.0에서 확인된 값보다 커지면 안 돼요.

0.3.0을 주인 PC에서 잰 값(2026-11-02, `ConcurrentGarbageCollection=false`): 트레이만 작업 집합 57MB / private 15MB, `tray_visible_ms` 341. 설정 창 192 / 162MB, `first_frame_ms` 중앙값 1648(1648 / 1522 / 3795). 메모 7개 276 / 250MB. 같은 날 동시 GC를 켜면 설정 첫 프레임 684ms, 메모 7개 264MB, 트레이 56MB / 293ms였어요. `38e6b9b`(동시 GC, 그림은 화면 크기로만 디코딩)는 트레이 60MB·약 290ms, 설정 첫 프레임 662ms, 설정 195 / 162MB, 메모 7개 240 / 212MB였어요. 관리 힙은 5–6MB라 목표를 넘는 부분은 네이티브 렌더 장치예요. `fb8d5f2`(소프트웨어 렌더)는 트레이 8/18MB·294ms, 설정 23/65MB, 메모 7개 22/95MB로 메모리 목표는 통과했고, 설정 첫 프레임 중앙값만 706ms(652 / 706 / 2386)였어요. 그 다음 변경(설정 창을 처음 열 때 불투명도 애니메이션과 방사 그라데이션을 첫 페인트에서 빼기)을 Windows에서 확인했어요. 스모크 **58**, 노트 **26**, 설정 첫 프레임 **593ms**. 유휴 작업 집합/private는 트레이 **8/18MB**, 설정 **20/64MB**, 메모 7개 **22/94MB**예요. 0.5.0도 이 숫자를 넘기면 안 돼요. 합격선은 여전히 아래 표예요. 기대값:

| 시나리오 (`gc_concurrent=true` 중앙값) | 작업 집합과 private | 시각 |
|---|---|---|
| 트레이만 | 둘 다 ≤ 120MB | `tray_visible_ms` 중앙값 ≤ 500 |
| 설정 창 | 둘 다 ≤ 150MB | `first_frame_ms` 중앙값 ≤ 700 |
| 메모 7개 | 둘 다 ≤ 180MB | — |

화면에서 한 번만 봐 주세요. 설정 창은 어두운 그라데이션이어야 하고, 거의 검정(틴트가 검정에 곱해진 상태)이거나 하양이면 안 돼요. 바탕화면이 틴트 뒤로 비치지는 않아요. 메모 색과 글자는 이전과 같아야 해요. 10분 유휴 증가 ≤ 10MB는 이 스크립트가 재지 않아요. 메모 7개 프로세스를 끝내기 전에 10분을 더 두고 작업 집합을 한 번 더 읽으면 확인할 수 있어요.

`next_startup.txt`(`--out`) 줄:

| 시나리오 | 있어야 하는 줄 | 없어야 하는 줄 |
|---|---|---|
| 트레이만 `--tray --off` | `render_mode=software`, `tray_visible_ms=`, `main_window_created=0`, `startup_notes_opened=0` | `first_frame_ms`, `settings_hidden` |
| 설정 창 `--off` | `render_mode=software`, `first_frame_ms=`, `hwnd_render_mode=SoftwareOnly`, `main_window_created=1`, `startup_notes_opened=0` | `tray_visible_ms`, `settings_hidden` |
| 숨긴 설정 창 `--tray --off --idle-hidden` | `render_mode=software`, `tray_visible_ms=`, `first_frame_ms=`, `hwnd_render_mode=SoftwareOnly`, `main_window_created=1`, `settings_hidden=1`, `startup_notes_opened=0` | — |
| 메모 7개 `--off` + 노트 복사 | `render_mode=software`, `first_frame_ms=`, `hwnd_render_mode=SoftwareOnly`, `main_window_created=1`, `startup_notes_opened=7` | `tray_visible_ms`, `settings_hidden` |

## 사이드바

첫 항목이 **모니터 프리셋**이에요(읽기 전용 목록). 이어서 스티커 메모·일반·더 보기·정보. 스티커 메모 `Notes.Opacity`는 40~100% 슬라이더로, 3.4.9와 같이 메모 창 전체 불투명도에 적용해요. 글자 크기는 7–48pt이고, 새 메모 단축키는 저장하는 즉시 다시 등록해요. Notes 키 하나를 저장할 때는 `config.json`에서 그 값만 바꾸고 나머지 바이트는 그대로 둬요.

## 메모 저장 잠금

3.4.8과 공유하는 뮤텍스: `Local\BongHub_NotesWriter`. 트레이 뮤텍스 `Local\BongHub_Tray`가 있으면 Next는 읽기 전용. Next는 `Local\BongHub_Tray`를 만들지 않아요. 자세한 순서는 `docs/HANDOFF.md`를 보세요.

## 주의

- 프리셋 CLI·실제 모니터 전환은 Next가 하지 않아요.
- 3.4.7(잠금 없는 배포본)과 Next를 동시에 켜면 짧은 겹침이 남을 수 있어요. 배포 트레이는 잠금이 있는 **3.4.9**예요(잠금은 3.4.8부터).
