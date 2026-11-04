# BongHub 인수인계

지금 제품은 **BongHub 1.7.3** 하나예요. WPF 앱이고 실행 파일은 `BongHub.exe`예요. WinForms 3.4.x 소스(`app/`, `build.ps1`)는 저장소에 없어요. 메모·설정 파일과 `%LOCALAPPDATA%\BongHub`, `Local\BongHub_NotesWriter`는 3.4.x와 같이 읽혀요.

이 문서가 절차의 기준이에요. 예전에 있던 `docs/HANDOFF.md`와 `docs/PORTING_PLAN.md`는 0.x 계획과 3.4.9 배포 기록이 섞여 있어서 뺐어요. 그 글은 git 기록에 있어요. 버전별 사용자 변화는 `CHANGELOG.md`예요.

비밀값(토큰, 암호)은 여기 적지 않아요.

## 1. 구조

### 프로젝트

| 경로 | 역할 |
|---|---|
| `BongHub/` | WPF 앱. `net10.0-windows`, SDK `global.json`의 **10.0.403**. ReadyToRun, `win-x64`, 자기 포함. 단일 파일·트림·Native AOT는 꺼져 있어요. `ConcurrentGarbageCollection`은 켜져 있어요. |
| `BongHub.PatchTests/` | 창 없는 콘솔. 설정 바이트 패치와 모니터 프로필·창·아이콘 목을 돌려요. `ImplicitUsings`가 켜져 있어요. |
| `BongHub.RichEditTests/` | 창 없이 `NoteRtf`만 컴파일해요. 전역 using이 없어요. `NoteRtf.cs`는 그 파일 안에서 `using`을 닫아야 해요. |
| `legacy/BongAd/` | 카카오톡 광고 제거 도우미 원본. 앱이 쓰는 실행 파일은 `BongHub/res/BongAd.exe`예요. |
| `data/` | 클라우드 동기화 페이로드. **커밋되어 있고, 사람이 고치면 안 돼요.** |

설정 탭은 일곱 개예요. 모니터 프로필, 스티커 메모, 카톡 광고 제거, PowerToys, AirDock, 일반, 정보.

### 메모

| 파일 | 하는 일 |
|---|---|
| `StickyNoteWindow.xaml.cs` | 메모 창. 제목, 본문, 링크, 붙여넣기, 그림, 저장, 클릭. `OriginalSource`가 `Run`이나 `Paragraph`이면 비주얼이 아니라서, `NextParent`가 그 둘은 `LogicalTreeHelper`, `Visual`과 `Visual3D`만 `VisualTreeHelper`를 탑니다. |
| `NoteRtf.cs` | WPF `TextRange`로 RTF를 읽고 써요. msftedit는 쓰지 않아요. 화면에 남아 있는 그림보다 `\pict`가 적으면 `Choose`가 기존 바이트를 돌려요. 사용자가 지운 그림은 막지 않아요. `#F0F0F6`, `#ECECF0`, `#1C1C1E`에 걸린 `\cf`는 저장 때 빼요. |
| `RtfPict.cs` | `\pngblip` / `\jpegblip` 묶음. 화면은 긴 변 1280 이하로 디코딩하고, 저장할 때 원본 묶음을 다시 끼워요. |
| `NoteStore.cs` | `notes\<id>.json` / `.rtf`. 팔레트와 자동 글씨(`NoteColors.Ink`). |
| `NoteImages.cs` | 새 그림. 1600px 이하 PNG/JPEG는 바이트를 유지하고, GIF처럼 못 넣는 형식은 PNG로 바꿔요. |
| `NoteManager.cs` | 열고 닫고, 종료 때 `FlushAll`. |
| `NotePaste.cs`, `NoteClip.cs` | 클립보드를 조각으로 읽고, 시험에서는 `NoteClipSession.Fake`만 써요. |
| `NoteLinks.cs` | http, https, www. 화면에서만 칠하고 RTF에는 링크 서식을 넣지 않아요. |

유휴 메모는 `FlowDocumentScrollViewer`만 두고, `RichTextBox`는 고칠 때 만들어요.

### 모니터맵

자리비움과 전환 때 이름 카드는 이 페이지에 있고 일반에는 없어요. 화면 경계가 켜져 있으면 맞닿은 변을 넘는 커서를 EDID mm의 같은 실물 위치로 옮겨요. 기본은 꺼짐이고, 켜면 모니터맵 왼쪽에 실물 맞춤이 보여요. 20ms 폴링이고 마우스 후크는 없어요. 실물 배치에서 가로·세로 mm나 대각선 인치, mm 오프셋을 고치면 `settings.json`의 `Physical`이 EDID 대신 쓰여요. 값이 0이면 EDID이고, EDID도 없으면 그 변은 그대로 둬요. 프로필 항목은 고치는 즉시 `profiles.json`에 저장돼요. 주인이 프로필 안의 모니터 그림을 모니터맵이라고 불러요. `ProfilePage.cs`의 `DrawProfileCanvas`가 설정 창 안에 모니터와 창, 바탕화면 아이콘을 그려요. 주 모니터의 강조 테두리와 별은 이 맵의 타일에만 있고, 실제 화면에는 겹치지 않아요. 그림은 지금 Windows 화면 구성을 따라요. 위치, 해상도, 배율, 방향, 식별 번호(`\\.\DISPLAYn`)가 같고, 화면 구성이 바뀌면(`WM_DISPLAYCHANGE`) 다시 맞춰요. 프로필은 그 그림에서 고치는 목표 배치예요. "저장된 구성과 달라요" 안내는 없어요. 끊긴 화면은 흐리게 두고 지우는 단추는 없어요. 모니터를 끌면 맞닿은 변이 선분으로 겹치고 안내선이 나와요. 창 칩 틀은 모니터 칸과 같은 비율이고, 아이콘과 이름은 번호 배지를 피해요. 배지에 삼켜지는 작은 창은 번호 옆 칸에 작은 칩으로 두고, 번호와 해상도 글은 가리지 않아요. 이웃 창과는 4px, 모니터 가장자리 자석은 2px, 절반·최대는 변의 18%에 떨어뜨릴 때만이에요. 모니터는 가로·세로를 한 번에 맞출 수 있고, 위치 잠금이 기본이에요. 프로필을 떠나면 열린 창 배치를 남겼다가 돌아올 때 돌려요. 화면 배치는 한 번에 커밋해요. 복원 위치는 작업 영역 좌표예요. 세션에 없는 창도 최소화 뒤에 지금 모니터로 돌아와요. 변과 모서리, 숫자로 크기를 고쳐요. 단축키는 키를 누르는 칸이고 `RegisterHotKey`(id 910부터)예요. 전역 후크는 없어요. 저장은 `%LOCALAPPDATA%\BongHub\profiles.json`이에요. 없는 필드는 기본값이라 예전 파일도 읽혀요. `config.json`의 3.4.x 필드는 지우지 않아요.

### 서비스와 가짜

실제 장치로 가는 문은 게이트예요. `Allow`가 꺼져 있거나 시험·`--off`이면 `Demand`가 예외를 던지고, 네이티브 호출 수는 0이에요. 스모크와 노트 시험은 게이트를 무장하지 않아요.

| 게이트 | 실제 구현 | 시험이 쓰는 것 |
|---|---|---|
| `DisplayGate` | `DisplayNative` (모니터, 밝기) | `FakeDisplay`, `FakeBright` |
| `WindowGate` | `LiveWindowSurface` | `FakeWindowSurface`. 아이콘은 `AppIcon.Fake` |
| `DesktopGate` | `ExplorerIconSurface` (탐색기 목록) | 가짜 아이콘 목록 |
| `PtGate` | PowerToys 계산 외의 실제 동작 | 가짜 입력만 |
| `KakaoGate` | `KakaoNative` + `BongAd.exe` | `FakeKakaoHost` |
| `CloudGate` | `GitCloudTransport` | 메모리 원격. 시험은 git을 실행하지 않아요. |

클립보드 시험은 `NoteClipSession.Fake`와 `SequenceIsFake`예요.

## 2. 빌드와 시험

Windows, 저장소 루트:

```powershell
dotnet publish BongHub\BongHub.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true
dotnet run --project BongHub.PatchTests\BongHub.PatchTests.csproj -c Release
dotnet run --project BongHub.RichEditTests\BongHub.RichEditTests.csproj -c Release
```

게시 결과는 `BongHub\bin\Release\net10.0-windows\win-x64\publish\BongHub.exe`예요. 자기 포함이라 `coreclr.dll`이 있어야 하고, `BongHub.dll`도 따로 있어야 해요. 단일 파일이 아니에요.

기대값:

| 명령 | 결과 |
|---|---|
| `--smoketest` | **168 PASS**. 임시 폴더만 써요. `--data`와 `BONGHUB_DATA`는 무시해요. |
| `--notetest --data <복사본>` | **155 PASS**. `data/` 원본이 아니라 복사본만 넘겨요. |
| PatchTests | `RESULT PASS` |
| RichEditTests | 1×1 `\pngblip` 왕복 `RESULT PASS` |

플래그:

| 플래그 | 동작 |
|---|---|
| `--smoketest` | 화면 밖(`--off`). 설정과 메모를 그리고 종료해요. 저장권 뮤텍스를 잡지 않아요. |
| `--notetest` | 화면 밖. 복사한 데이터로 메모 시험을 하고 종료해요. |
| `--quit` | 이 프로세스는 데이터를 열지 않아요. `Local\BongHub.Quit`를 켜서 이미 떠 있는 앱이 `RequestShutdown`으로 저장한 뒤 끝나게 해요. |
| `--tray` | `--off`와 같이 쓰면 설정 창 없이 트레이만 올려요. 메모리 기준선용이에요. |
| `--off` | 창을 (-30000, -30000)에 두고 활성화하지 않아요. 게이트는 무장하지 않아요. |
| `--idle-hidden` | `--off`로 설정을 한 프레임 보여 준 뒤 숨겨요. |
| `--data <폴더>` | 그 폴더를 데이터 루트로 써요. 환경 변수 `BONGHUB_DATA`와 같아요. 스모크는 둘 다 무시해요. |
| `--out <폴더>` | `smoketest.txt`, `notetest.txt`를 여기 써요. |

메모리 기준선은 `measure-baseline.ps1`예요. 이미 켜진 BongHub를 끝낸 다음에 실행해요. `data/`와 `%LOCALAPPDATA%\BongHub`는 읽지도 쓰지도 않아요. 창은 화면 밖이에요. 0.4.0에서 확인된 유휴(작업 집합/private)는 트레이 **8/18MB**, 설정 **20/64MB**, 메모 7개 **22/94MB**를 넘기면 안 돼요. 로그는 로컬 `test-logs\`에만 남기고 커밋하지 않아요.

## 3. CI

`.github/workflows/repo-check.yml`

- `ubuntu-latest`: `check-versions.sh` (csproj `Version` / `FileVersion`, `AppEnv.Version`, `AppInfo.Version`이 **1.7.3** / **1.7.3.0**으로 같은지), PR에서 `sync:`가 아닌 `data/` 변경이 있으면 실패, PatchTests.
- `windows-latest`: 게시 후 스모크, 그다음 노트 시험. 노트 시험 단계는 `if: always()`라서 스모크가 실패해도 돌아요. 스모크는 120초 안에 프로세스가 끝나지 않으면 실패예요. 노트 시험은 `Start-Process -Wait -PassThru`로 기다리고 종료 코드로 판단해요. 로그는 `--out`과 exe 옆을 둘 다 봐요. 산출물은 `wpf-tests`로 올라가요.

Linux에서는 `BongHub.exe`가 실행되지 않아요. GUI 시험은 Windows CI가 담당이에요.

## 4. 배포

배포 폴더는 `C:\Users\Revaptor_FX\BongHub`예요. 자동 시작은 `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\BongHub.lnk`가 그 안의 `BongHub.exe`를 가리켜요.

순서:

1. 떠 있는 앱을 `BongHub.exe --quit`으로 끝내요. 이벤트가 `Local\BongHub.Quit`예요. 받는 쪽은 메모를 저장하고 `Local\BongHub.SingleInstance`와 `Local\BongHub_NotesWriter`를 놓은 뒤 종료해요. 프로세스가 남아 있으면 파일을 덮지 않아요.
2. 3.4.x 트레이가 `Local\BongHub_Tray`를 잡고 있으면 WPF는 메모를 읽기 전용으로만 열어요. 그 프로세스도 끝낸 뒤에 복사해요. WPF는 그 뮤텍스를 만들지 않고, 있는지만 봐요.
3. 지금 폴더를 `backup\<시각>`으로 옮긴 뒤, `backup\`에는 **가장 최근 백업만** 남겨요. 더 오래된 백업 폴더는 지워요.
4. 위의 게시 결과를 배포 폴더에 복사해요. 런타임은 그 폴더 안에 같이 있어요. `coreclr.dll`이 있어야 자기 포함 게시가 맞아요.

1.3.2를 적을 때 배포 폴더의 exe는 파일 버전 **1.3.1.0**이었어요. 1.4.0을 복사하기 전에 떠 있는 프로세스를 끝내고, 덮어쓰기 전에 파일 버전을 다시 봐요.

## 5. 데이터 호환

런타임은 `%LOCALAPPDATA%\BongHub\`예요.

| 파일 | 호환 |
|---|---|
| `config.json` | 3.4.x 필드 유지. 한 키를 고칠 때 그 값의 바이트만 바꾸고, 같으면 다시 쓰지 않아요. |
| `notes\<id>.json` | 3.4.7 `NoteMeta` 필드 순서와 `\/Date(ms)\/`. `Title`은 비어 있으면 생략해서 옛 리더가 앞 필드를 그대로 읽어요. |
| `notes\<id>.rtf` | `\pngblip` / `\jpegblip`. 화면용으로 줄인 비트맵을 파일에 다시 그리지 않아요. |
| `notes\<id>.rtf.pending-<시각>` | 이미지 가드가 기존 파일을 덮지 못할 때 새 RTF. 메모마다 하나예요. 정상 저장이 되거나, pending이 rtf보다 오래되면 지워요. 다음에 열 때 pending이 더 새로우면 그 쪽을 읽어요. |
| `desktop\layouts.json` | 3.4.x 아이콘 배치. |
| `profiles.json` | 1.1.0 이후 모니터 프로필. 옛 config를 대체하지 않아요. |
| `settings.json` | `ReduceMotion`, `MonitorLocked`, `ChipsVisible`, `CursorCross`, `Physical`. 3.4.x와 공유하지 않아요. `CursorCross`가 없으면 꺼짐이에요. `Physical`의 mm가 0이면 EDID를 써요. |

저장권은 `Local\BongHub_NotesWriter`예요. 기본 데이터 폴더를 쓰는 일반 실행만 잡아요. 시험과 `--data` 임시 폴더는 잡지 않아요. 못 잡거나 3.4 트레이가 떠 있으면 읽기 전용이에요.

이미지 가드: 저장 RTF의 `\pict`가 화면에 남아 있는 그림보다 적으면 `error.log`에 `image guard: kept existing rtf`를 남기고, 새 바이트는 pending 하나에만 써요. 화면 그림 수와 `\pict` 수가 같으면 저장해요. 사용자가 지운 그림이 보관 묶음에 남아 있어도 그 묶음은 세지 않아요. GIF·메타파일처럼 WPF가 `\pict`로 못 쓰는 화면 그림은 그 전에 PNG로 바꿔 묶어요. 이미 PNG/JPEG인 원본 바이트는 유지해요.

창 크기: 최소화·최대화·접힌 상태의 사각형은 저장하지 않아요. `GetWindowRect`가 그때 돌려주는 값은 복원 크기가 아니에요. 최소화하면 높이가 34처럼 작거나 너비가 모니터를 훨씬 넘고, 그다음 배치가 그 값을 모니터 너비와 60으로 다시 적었어요. 너비와 높이는 저장할 때와 화면에 놓을 때 물리 픽셀로 `NoteMinWidth`–`NoteMaxWidth`, `NoteMinHeight`–`NoteMaxHeight` 안에 맞춰요. WPF의 최소·최대는 DIP라서, 그 값은 DPI로 나눈 뒤에 넣어요. 125%에서 90 DIP는 113px이 되므로 그대로 두면 안 돼요. 이미 한계 밖에 저장된 파일은 사용자가 노트를 옮길 때까지 그대로 두고, 창은 그 물리 픽셀 최소·최대로 보여요.

옛 글자색: RTF에 박힌 `#F0F0F6`, `#ECECF0`, 자동 어두운 글씨 `#1C1C1E`는 열 때 지우고 노트 색의 `NoteColors.Ink`를 상속해요. 저장할 때 그 색은 다시 쓰지 않아요. 직접 고른 색(예: 파랑)은 남아요. 중간 밝기라 검정과 밝은 글씨가 둘 다 7:1에 못 미치면 색조를 유지한 채 표면만 조금 옮겨요. 레몬과 노랑 원색은 그대로예요.

`data/`는 클라우드가 `sync: <기기> yyyy-MM-dd HH:mm:ss`로 커밋해요. PR에서 그 접두사가 아닌 커밋이 `data/`를 바꾸면 CI가 실패해요.

## 6. 버전 1.0.0–1.7.3

번호는 사용자에게 나간 WPF 앱이에요. 0.x는 `CHANGELOG.md`의 이식 단계예요.

| 버전 | 요지 | 시험 |
|---|---|---|
| 1.0.0 | 3.4.x 트레이를 빼고 이 WPF가 `BongHub.exe`가 됐어요. 데이터와 저장권은 그대로예요. | 스모크 107, 노트 89 |
| 1.0.1 | 문서와 부제에 남은 옛 제품 이름을 뺐어요. | 107 / 89 |
| 1.0.2 | 글과 그림을 함께 복사해요. 이미 적용 중인 프리셋은 다시 적용하지 않아요. | 109 / 89 |
| 1.1.0 | 모니터 프리셋, 창 위치, 바탕화면 아이콘을 모니터 프로필 한 탭으로 모았어요. | 102 / 89 |
| 1.1.1 | 메모 제목, 도구 막대, 스크롤·캐럿, 글과 그림 복사. | 106 / 93 |
| 1.2.0 | 프로필을 화면 설정처럼 단순하게 정리했어요. 창 규칙 목록과 작업 영역 페이지는 뺐어요. | 112 / 93 |
| 1.3.0 | 링크는 화면에만 칠해요. 복사가 클립보드에 남아요. 대비가 부족한 중간 색만 조금 밝혀요. 옛 기본 글자색은 자동 글씨로 바꿔요. 그림을 다 못 담으면 pending에 남겨요. | 123 / 127 |
| 1.3.1 | Run·Paragraph 클릭이 예외를 던지지 않아요. | 123 / 128 |
| 1.3.2 | 접힌·최소·최대 크기가 복원 크기를 덮지 않아요. 그림 가드는 화면에 남은 그림만 봐요. 게시는 자기 포함 `win-x64`예요. | 123 / 144 |
| 1.4.0 | 모니터 프로필을 화면 설정처럼 깊게 고쳤어요. 라이브 배치, 가장자리 스냅, 창 크기, 조준, 단축키, 아이콘 미리보기. | 139 / 144 |
| 1.4.1 | 프로필을 바꿀 때 화면·창·메모가 그대로 남아요. 캔버스 조작은 잠금, 실제 비율, 약한 가장자리 자석, 모서리 스냅이에요. | 150 / 146 |
| 1.4.2 | 번호 배지가 칩 아이콘을 피해요. 최소화 복원은 작업 영역 좌표예요. 카톡 다크 오버레이는 인자로 나가지 않아요. 메모 색은 덜 진하고, 제목은 제목 막대에 있어요. | 152 / 149 |
| 1.4.3 | 화면 밖 설정 창은 포커스를 받지 않아요. 첫 프레임 뒤에 다시 돌려요. 번호에 가려지는 작은 창은 번호 옆에 두고 단추는 남겨요. | 152 / 149 |
| 1.4.4 | 프로필 캔버스가 화면 설정에 더 가깝고, 자리비움과 전환 카드가 돌아와요. | 154 / 149 |
| 1.4.5 | 식별 카드는 모니터 픽셀에 붙고, 최소화된 창은 현재 화면 안으로 돌아와요. 절반·최대는 작업 영역을 쓰고, 주 모니터 표시는 모니터맵에만 있어요. | 155 / 150 |
| 1.4.6 | 꺼 둔 모니터는 CCD로 빠지고, 전환 중 창은 바꾸기 전 자리에 남아요. 전환 카드는 경과 시간을 보여요. | 158 / 150 |
| 1.5.0 | 메모는 블록, 슬래시 명령, 마크다운 단축으로 이어지고, 좁은 메모의 제목과 도구는 창 안에 남아요. | 159 / 154 |
| 1.6.0 | 단축키를 누르고 있는 동안 커서 옆에 AirDock이 떠요. 등록한 프로그램만 실행해요. | 161 / 154 |
| 1.7.0 | 맞닿은 변을 넘는 커서를 EDID mm의 같은 실물 위치로 옮겨요. 기본은 꺼짐이고, 후크는 없어요. | 162 / 154 |
| 1.7.1 | 프로필 편집 창, 스위치는 누른 것만 움직이고, 현재 프로필 표시, 실물 배치에서 mm를 고쳐요. | 165 / 154 |
| 1.7.2 | CCD 대상 이름은 420바이트예요. 모니터마다 경로 하나만 켜고, 장치 경로·EDID·GDI 이름 순으로 맞춰요. | 166 / 154 |
| 1.7.3 | 메모 제목은 글자 너비만큼만 고치고, 빈 제목 막대는 창을 끌어요. | 168 / 155 |

## 7. 시험할 때 지키면 안 되는 것

- 프리셋 CLI, `workspace`, `resize` 인자로 모니터나 창을 움직이지 않아요. BongHub에도 그 인자로 화면을 바꾸는 기능이 없어요.
- 실제 모니터, 밝기, 바탕화면 아이콘을 읽거나 고치지 않아요. 게이트가 시험과 `--off`에서 닫혀 있어야 하고, 그 호출 수는 0이어야 해요.
- GUI 시험은 숨긴 데스크톱에서, 창은 화면 밖 (-30000, -30000)에서만 해요. 포커스를 가져가지 않아요.
- 노트 시험의 `--data`는 **복사본**만 받아요. `%LOCALAPPDATA%\BongHub`를 넘기지 않아요.
- `data/`는 고치지 않아요. 클라우드 커밋이 아닌 PR에 그 차이가 들어가면 안 돼요.
- AutoHotkey를 쓰지 않아요. 전역 후킹(`SetWindowsHookEx`)도 단축키에 쓰지 않아요. 메모 단축키와 프로필 단축키는 `RegisterHotKey`이고, 시험은 그 API를 호출하지 않아요. 화면 경계 보정은 후크가 없고, 시험은 가짜 모니터와 가짜 EDID mm만 계산해요.
- 스모크·노트 시험·`--off`·CI는 git, GitHub, 카톡 창, `BongAd.exe`, 탐색기 목록, DDC/CI를 실행하지 않아요.

## 8. 알려진 문제와 남은 일

- 동기화된 `data/notes` 일부는 아직 한계 밖의 크기를 갖고 있어요. 1.3.2는 그 파일을 열 때 고치지 않아요. 창만 최소·최대로 맞추고, 사용자가 옮기면 그 한계 안의 크기를 저장해요.
- 가드가 막은 저장을 다시 열면 pending이 더 새로워서 그 내용이 보여요. 화면 그림 수와 맞는 다음 저장은 rtf를 갱신하고 pending을 지워요.
- 실제 모니터·밝기·아이콘·카톡·git 경로는 자동화 시험이 없어요. 목과 “호출하면 예외”만 확인해요.
- 모니터맵의 창은 모니터 픽셀 비율이에요. 이웃 창 스냅은 4px, 절반·최대·1/4은 작업 영역(작업 표시줄을 뺀 칸)의 얇은 가장자리예요. 끊긴 모니터는 흐린 채로 남아요.
- 클라우드 `sync:` 커밋이 `main`에 자주 올라와요. 문서 PR은 그 커밋 위에 두고, `data/`는 끌어오지 않은 채로 충돌을 만들지 않아요.
