# BongHub.Next

결론부터: **0.2.0 프로토타입**이에요. 메모·설정 UI를 .NET 8 WPF로 다시 짠 시험본이고, 모니터 전환 같은 본체 기능은 아직 3.4.x 트레이가 맡아요.

## 경로

| 구분 | 경로 |
|---|---|
| 저장소(이 폴더) | `next/BongHub.Next/` |
| 원본 작업 폴더(PC) | `C:\Users\Revaptor_FX\Documents\Codex\2026-11-01\new-chat\BongHub\next\` |
| 공유 데이터 | `%LOCALAPPDATA%\BongHub\` (3.4.x와 동일) |
| Next 전용 설정 | `%LOCALAPPDATA%\BongHub.Next\settings.json` |

`next/.gitignore`는 `bin/`, `obj/`, `publish/`, `.vs/`, `*.user`를 제외해요.

## 빌드

```powershell
dotnet publish next\BongHub.Next\BongHub.Next.csproj -c Release -r win-x64
```

대상: `net8.0-windows`, WPF + Windows Forms. 버전은 csproj·`AppEnv.Version` 모두 **0.2.0**.

## 시험 (기록)

`next/test-logs/` (주인 PC, **2026-11-02 00:33 KST**, 잠금 패치 **이전** 로그):

- `next_smoketest.txt` — UI 스모크 **43 PASS**, 작업 집합 약 **227MB**(프라이빗 184MB)
- `next_notetest.txt` — 노트 **23 PASS**(3.4.7 메모 파일 7개 호환)

같은 날 잠금 패치를 넣은 소스를 격리 임시 폴더에서 다시 돌렸어요. `dotnet publish`(SDK 8.0.426, win-x64) PASS, 스모크 **43 PASS**, 노트 **23 PASS**(읽기 전용일 때 저장 차단 포함).

비교: Next 약 227–239MB, 배포 3.4.7 약 103MB.

```text
BongHub.Next.exe --smoketest
BongHub.Next.exe --notetest
```

테스트는 임시 데이터 폴더를 쓰고 `Local\BongHub_NotesWriter` 잠금을 잡지 않아요.

## 사이드바

첫 항목이 **모니터 프리셋**이에요(읽기 전용 목록). 이어서 스티커 메모·일반·더 보기·정보. 스티커 메모 `Notes.Opacity`는 40~100% 슬라이더로, 3.4.8과 같이 메모 창 전체 불투명도에 적용해요.

## 메모 저장 잠금

3.4.8과 공유하는 뮤텍스: `Local\BongHub_NotesWriter`. 트레이 뮤텍스 `Local\BongHub_Tray`가 있으면 Next는 읽기 전용. 자세한 순서는 `docs/HANDOFF.md`를 보세요.

## 주의

- 프리셋 CLI·실제 모니터 전환은 Next가 하지 않아요.
- 3.4.7(잠금 없는 배포본)과 Next를 동시에 켜면 짧은 겹침이 남을 수 있어요. 3.4.8로 트레이를 바꾸면 사라져요.
