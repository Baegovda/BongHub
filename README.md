# BongHub

Windows 트레이 유틸리티입니다. 부제는 "석봉이의 유틸 창고"입니다.

## 현재 버전 (2026-11-02 KST)

- **PC에 배포·실행 중:** BongHub **3.4.7** (`C:\Users\Revaptor_FX\BongHub\BongHub.exe`). 3.4.8은 아직 이 경로에 올리지 않음
- **이 저장소 PR #4 (`cursor/next-writer-lock-c7a7`) 소스:** 트레이 **3.4.8** + BongHub.Next **0.2.0**. 2026-11-02에 격리된 임시 폴더(데이터 복사본)에서 빌드·시험 PASS. 배포 exe는 그대로 3.4.7
- 런타임 데이터: `%LOCALAPPDATA%\BongHub\` (`config.json`, `notes\`, …)
- GitHub Actions 등 **CI 없음**

## 구성

- `app/` — 트레이 앱 소스 (`BongHub.cs`, `build.ps1`, `CHANGELOG.md`, `res/`, `_dev/`). PR 브랜치는 v3.4.8, `main`은 v3.4.7
- `next/` — BongHub.Next 프로토타입 v0.2.0 (WPF + WinForms, .NET 8). 저장소 복사본. 원본 작업 폴더는 `C:\Users\Revaptor_FX\Documents\Codex\2026-11-01\new-chat\BongHub\next\`
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
dotnet publish next\BongHub.Next\BongHub.Next.csproj -c Release -r win-x64
```

자세한 잠금·시험·경로는 `docs/HANDOFF.md`와 `next/README.md`를 보세요.
