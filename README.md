# BongHub

Windows 트레이 유틸리티입니다. 부제는 "석봉이의 유틸 창고"입니다. 1.0.0부터 WinForms 3.4.x 트레이는 없고, 이 WPF 앱이 `BongHub.exe`예요. 지금 버전은 **1.4.0**이에요.

## 현재 버전

- **소스:** **1.4.0** (`BongHub/BongHub.csproj`, `AppEnv.Version`, `AppInfo.Version`, 파일 버전 **1.4.0.0**)
- **시험:** 스모크 **132**, 노트 **144**. 0.4.0에서 확인된 유휴(작업 집합/private)는 트레이 **8/18MB**, 설정 **20/64MB**, 메모 7개 **22/94MB**를 넘기면 안 돼요
- **데이터:** `%LOCALAPPDATA%\BongHub\` (`config.json`, `notes\`, `desktop\layouts.json`). 형식은 3.4.x와 같아요. 모니터 프로필은 `profiles.json`으로 옆에 더해요
- **저장권:** `Local\BongHub_NotesWriter`. 먼저 잡은 프로세스만 그 폴더에 써요
- **로그:** `%LOCALAPPDATA%\BongHub\error.log`

`data/`는 클라우드 동기화 페이로드예요. 사람이 직접 고치지 마세요. PR에서 `sync:`가 아닌 커밋이 `data/`를 바꾸면 검사가 실패해요.

## 구성

- `BongHub/` — WPF 앱 (`net10.0-windows`, H.NotifyIcon.Wpf 2.4.1, WinForms 없음)
- `BongHub.PatchTests/` — 설정 바이트와 목 동작
- `BongHub.RichEditTests/` — 창 없이 RTF 그림을 읽고 씀
- `legacy/BongAd/` — 카카오톡 광고 제거 도우미 원본. 실행 파일은 `BongHub/res/BongAd.exe`
- `docs/HANDOVER.md` — 인수인계 (구조, 시험 금지, 배포, 데이터)

## 빌드

Windows에서 저장소 루트 기준:

```powershell
dotnet publish BongHub\BongHub.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true
```

출력: `BongHub\bin\Release\net10.0-windows\win-x64\publish\BongHub.exe`. 자기 포함이라 `coreclr.dll`이 있고, `BongHub.dll`도 따로 있어요.

SDK는 루트 `global.json`의 **10.0.403**이에요. 단일 파일·트림·Native AOT는 꺼져 있고, `ConcurrentGarbageCollection`은 켜져 있어요.

## 시험

```text
BongHub.exe --smoketest
BongHub.exe --notetest --data <notes 복사본>
```

`--smoketest`와 `--notetest`는 화면 밖이고 저장권 잠금, git, 카톡 창, 모니터 전환을 하지 않아요. 기대값은 스모크 **132 PASS**, 노트 **144 PASS**예요. 탭은 모니터 프로필, 스티커 메모, 카톡 광고 제거, PowerToys, 일반, 정보 여섯 개예요. `--quit`은 떠 있는 프로세스를 끝내고, `--tray`는 `--off`와 함께 트레이만 띄워요. 데이터 폴더는 `--data` 또는 `BONGHUB_DATA`예요. 스모크는 그 경로를 쓰지 않아요. 절차와 금지 사항은 `docs/HANDOVER.md`에 있어요.

패치 검사:

```powershell
dotnet run --project BongHub.PatchTests\BongHub.PatchTests.csproj -c Release
```

메모리 기준선은 `measure-baseline.ps1`예요. 이미 켜진 BongHub만 종료한 뒤에 실행하세요. `data/`와 `%LOCALAPPDATA%\BongHub`는 읽지도 쓰지도 않아요.
