# BongHub

Windows 트레이 유틸리티입니다. 부제는 "석봉이의 유틸 창고"입니다.

## 구성

- `app/` — 트레이 앱 소스 v3.4.1 (`BongHub.cs`, `build.ps1`, `CHANGELOG.md`, `res/`, `_dev/`)
- `legacy/BongAd/` — 카카오톡 광고 제거 도우미 원본
- `docs/HANDOFF.md` — 인수인계 문서

`data/`는 클라우드 동기화용이라 이 저장소에 두지 않습니다.

## 빌드

Windows에서 저장소 루트 기준:

```powershell
powershell app\build.ps1
```

.NET Framework 4.x의 `csc.exe`가 필요합니다. 출력은 `app\BongHub.exe`입니다.
