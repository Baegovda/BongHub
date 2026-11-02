# BongHub

Windows 트레이 유틸리티입니다. 부제는 "석봉이의 유틸 창고"입니다.

## 현재 버전 (2026-11-02 KST)

- **PC에 배포됨:** 트레이 **3.4.9**. 파일 버전 **3.4.9.0** (`C:\Users\Revaptor_FX\BongHub\BongHub.exe`, PC에서 확인). 3.4.8은 `backup\3.4.8_20261102_123333`에 백업
- **`main` 소스:** 트레이 **3.4.9** (`app/version.txt`, `AppInfo.Version`) + BongHub.Next **0.3.0** (`net10.0-windows`, H.NotifyIcon.Wpf 2.4.1, WinForms 없음)
- **3.4.9:** UI 스레드 예외 뒤 트레이가 죽던 경로 수정 (PR #9). 3.4.8과 3.4.7은 그 종료 경로로 PC에서 죽은 적이 있음
- **병합된 PR:** #4 메모 저장 잠금과 Next 도입, #5 이식 계획, #6 Next 0.3.0 (.NET 10 기준선), #9 트레이 3.4.9 크래시 수정
- **곧 들어올 PR:** #7 Next 0.4.0 메모리 (아직 `main`에 없음)
- 런타임 데이터: `%LOCALAPPDATA%\BongHub\` (`config.json`, `notes\`, …)
- 검사: `.github/workflows/repo-check.yml` 이 버전 문자열이 어긋나거나, PR에서 `sync:` 가 아닌 커밋이 `data/`를 건드리면 실패해요. Windows 빌드·스모크는 아직 없어요

## 구성

- `app/` — 트레이 앱 소스 (`BongHub.cs`, `build.ps1`, `CHANGELOG.md`, `res/`, `_dev/`). `main`은 **3.4.9**
- `next/` — BongHub.Next **0.3.0** (WPF, .NET 10, H.NotifyIcon). 3.4.x 데이터와 `Local\BongHub_NotesWriter`를 그대로 써요. Notes 설정 한 키는 그 값만 고치고 나머지 바이트는 그대로 둬요
- `legacy/BongAd/` — 카카오톡 광고 제거 도우미 원본
- `docs/HANDOFF.md` — 인수인계 문서
- `data/` — **클라우드 동기화 페이로드**. 앱이 `main`에 `sync: …` 커밋으로 올려요. 사람이 직접 고치지 마세요. PR에서 그 외 커밋이 `data/`를 바꾸면 검사가 실패해요

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
