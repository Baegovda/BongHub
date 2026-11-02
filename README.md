# BongHub

Windows 트레이 유틸리티입니다. 부제는 "석봉이의 유틸 창고"입니다.

## 현재 버전 (2026-11-02 KST)

- **PC에서 죽은 배포본:** BongHub **3.4.8** (2026-11-02 11:46 KST)과 **3.4.7** (2026-11-01 23:35 KST). UI 스레드 예외 뒤 `Application.Exit`가 열린 창 목록을 수정 중 열거하다 종료
- **이 저장소 소스:** 트레이 **3.4.9** (그 종료 경로와 창 프로시저 예외 수정). 배포 폴더 `C:\Users\Revaptor_FX\BongHub\`에 복사하기 전에는 PC exe가 바뀌지 않음
- **BongHub.Next 0.4.0:** `net10.0-windows`, H.NotifyIcon.Wpf 2.4.1, WinForms 없음, 동시 GC 켜짐. Windows에서 `c8c6629`는 스모크 58, 노트 26, 설정 첫 프레임 593ms, 메모리 목표는 통과. 절차는 `next/measure-baseline.ps1`. `app/` 3.4.x는 버그 수정만 해요
- 런타임 데이터: `%LOCALAPPDATA%\BongHub\` (`config.json`, `notes\`, …)
- GitHub Actions 등 **CI 없음**

## 구성

- `app/` — 트레이 앱 소스 (`BongHub.cs`, `build.ps1`, `CHANGELOG.md`, `res/`, `_dev/`). 트레이는 v3.4.9
- `next/` — BongHub.Next **0.4.0** (WPF, .NET 10, H.NotifyIcon). 3.4.x 데이터와 `Local\BongHub_NotesWriter`를 그대로 써요
- `legacy/BongAd/` — 카카오톡 광고 제거 도우미 원본
- `docs/HANDOFF.md` — 인수인계 문서
- `data/` — **클라우드 동기화 페이로드**. 앱이 `main`에 커밋합니다(`sync: …` 커밋, 2026-11-01 기준 약 101개). 사람이 직접 고치지 마세요

배포 폴더 `C:\Users\Revaptor_FX\BongHub\`는 저장소 `app/`에서 빌드한 exe·소스를 올려 쓰는 PC 쪽 경로입니다.

## 빌드

Windows에서 저장소 루트 기준:

```powershell
powershell app\build.ps1
```

.NET Framework 4.x의 `csc.exe`가 필요합니다. 출력은 `app\BongHub.exe`입니다. 배포할 때는 실행 중인 트레이를 끈 뒤 `C:\Users\Revaptor_FX\BongHub\`로 복사합니다.

Next:

```powershell
dotnet publish next\BongHub.Next\BongHub.Next.csproj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true
```

자세한 잠금·시험·경로는 `docs/HANDOFF.md`와 `next/README.md`를 보세요.
