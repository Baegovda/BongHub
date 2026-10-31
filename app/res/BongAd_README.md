# BongAd

PC 카카오톡 하단 광고 배너를 숨기고, 비어 있는 공간까지 친구/채팅 목록을 늘리고, 화면 간격을 촘촘하게 줄여 주는 **12KB짜리** Windows 네이티브 프로그램입니다. 선택 기능으로 다크 모드 오버레이도 있습니다.

> ⚠️ 비공식 도구이며 개인 용도로만 쓰세요. Kakao와는 관계가 없습니다. 사용에 따른 책임은 사용자 본인에게 있습니다.

## 사용법
1. 이 저장소를 내려받습니다 (Code → Download ZIP 후 압축 해제).
2. `run.bat` 또는 `BongAd.exe`를 실행합니다. 창 없이 백그라운드에서 돌아갑니다 (중복 실행 방지).
3. 끄고 원래대로 되돌리기: `restore.bat` (`BongAd.exe /restore`).

윈도우 시작 시 자동 실행:
- 등록: `install-startup.bat` (시작프로그램 폴더에 `BongAd.lnk` 생성 후 바로 실행)
- 해제: `uninstall-startup.bat` (바로가기 삭제, 실행 중인 BongAd 종료, 창 복원)

옵션: `/once` (한 번만 적용하고 종료), `/restore` (실행 중인 BongAd 종료 + 숨긴 창 복원 + 레이아웃 원래대로), `/nocompact` (간격 줄이기 끄기), `/dark` (다크 모드 오버레이 켜기, 기본은 꺼짐).
다크 모드로 실행: `run-dark.bat`, 자동 시작까지 다크 모드로: `install-startup.bat /dark`.

## 간격 줄이기 (기본 켜짐)
카카오톡 자체 창(컨테이너)의 위치·크기만 바꿉니다. 125% 배율 기준 실측:
| 부분 | 이전 | 이후 |
|---|---|---|
| 상단 여백 (`OnlineMainView` 위쪽) | y=38 | y=30 (8px 위로, 최소화/닫기 버튼과 겹치지 않음) |
| 탭(채팅/오픈채팅)과 필터 줄 사이 | 필터 y=93 | y=77 |
| 필터 줄과 목록 사이 (`ChatRoomListCtrl`) | y=143 | y=121 |

v2.1: 사이드바 쪽으로 목록을 당기던 가로 이동은 뺐습니다. 배지(예: 756)가 잘리고 위쪽 줄이 어긋났기 때문입니다. 사이드바는 카카오톡이 직접 그리는 영역이라, 배지를 자르지 않고 그 간격만 줄일 방법이 없습니다.

**채팅방 한 줄 한 줄의 간격은 바꿀 수 없습니다.** 목록 행은 `ChatRoomListCtrl` 하나가 직접 그리는(owner-draw) 방식이라 바깥에서 건드릴 창이 없고, 카카오톡에 코드를 주입해야만 가능합니다. BongAd는 주입을 하지 않습니다.

## 다크 모드 (`/dark`)
카카오톡은 자체 UI라 Windows 다크 모드 API(DwmSetWindowAttribute, SetWindowTheme)가 목록·사이드바에 먹지 않습니다. v2.2부터는 GDI만 씁니다: `PrintWindow`로 카카오톡 메인 창을 작은 비트맵(창 크기 그대로, 약 1.2MB)에 그려 받고, 밝기만 뒤집는(색조 유지) 계산을 직접 한 뒤, 카카오톡 바로 위에 놓인 클릭 통과 레이어드 창에 띄웁니다. v2.3부터는 무채색(흰색/회색/검정) 부분만 어둡게 바꾸고, 채도가 있는 부분(프로필 사진, 노란색, 빨간 배지)은 원래 색 그대로 둡니다. v2.3.1: 글자의 ClearType 색 번짐은 색으로 치지 않아(양옆 픽셀과 색 계열이 같아야 색으로 판정) 글자가 깨끗하게 보이고, 대부분이 색인 8x8 칸(사진 안쪽)은 통째로 원본을 유지합니다. 흰 배경 → #1C1C1C, 검은 글자 → #E6E6E6. 채팅방 창에는 적용하지 않습니다(아래 카카오톡 자체 설정 사용).
- 갱신: 평소 1초에 1번, 카카오톡 위에서 마우스/키보드 입력이 있으면 0.8초 동안 초당 15번 (저수준 입력 훅은 BongAd 안에서만 돌고 카카오톡에 주입하지 않습니다). 화면이 안 바뀌었으면 다시 그리지 않습니다. 카카오톡을 최소화하거나 트레이로 숨기면 오버레이와 비트맵을 모두 해제합니다.
- v2.1까지 쓰던 Windows 돋보기(Magnification) API는 그래픽 드라이버(D3D) 메모리 때문에 개인 메모리가 약 188MB였습니다.
- 새로 실행하면 이미 돌고 있는 BongAd를 끄고 대신 실행됩니다.
- 채팅방은 카카오톡 설정으로 어둡게: 설정(왼쪽 아래 톱니바퀴) → 화면 → 채팅방 스타일 → 배경화면 설정 → 검정색 선택 → 전체 적용하기. 되돌리기는 같은 곳에서 원래 배경 선택.
- 카카오톡 파일(skin\\v3\\color\\color.xml)을 직접 고치는 방법은 시험해 봤지만, 카카오톡이 "파일이 손상되었습니다"라며 실행을 거부해서 쓰지 않습니다.
- v2.4: 채팅 목록·친구 목록의 프로필 사진(단체방 2x2 사진 포함, 내 프로필 포함)은 원본 픽셀 그대로 보여 줍니다. 매 화면마다 목록 창(`ChatRoomListCtrl`/`ContactListCtrl`) 왼쪽 사진 칸(목록 왼쪽 8~96px)을 스캔해 높이 28px 이상인 사진 덩어리를 찾으므로 스크롤해도 맞습니다. 둥근 모서리와 단체 사진 사이 틈의 흰 배경은 계속 어둡게 바뀝니다.
- 사진 테두리 흰 띠(halo)를 없애는 시도(v2.3.2, v2.3.3)는 사진 색을 더 망가뜨려서 되돌렸습니다. 픽셀만 보고 사진과 UI를 완벽히 구분할 수는 없습니다.
- 한계(레이아웃 의존): 사진 칸 위치는 현재 카카오톡 목록 배치(125% 배율)에 맞춘 값이라 카카오톡이 배치를 바꾸면 다시 맞춰야 합니다. 사진 칸 밖의 사진(예: 친구 목록 '업데이트한 친구' 줄의 두 번째 이후 사진)은 예전 방식대로 처리돼 흰색·회색 부분이 어두워질 수 있습니다.
- 한계: 색이 적은(흰빛·회색빛) 프로필 사진은 일부가 어둡게 바뀌어 얼룩져 보일 수 있고, 배지 가장자리가 살짝 각져 보일 수 있습니다. 사진 속 흰색·회색 부분과 흑백 사진·이모티콘의 무채색 부분은 여전히 뒤집힙니다. 입력 없이 새 메시지가 오면 최대 1초 늦게 반영됩니다. 다른 창이 카카오톡을 가리면 그 창이 그대로 보입니다.

## 리소스 사용량 (실측, Windows 11)
| 버전 | 메모리(Working Set) | Private Bytes | CPU (10초) |
|---|---|---|---|
| 이전 PowerShell 버전 | 약 99MB | 약 87MB | 0.7초마다 폴링 |
| **BongAd.exe** (기본) | **약 0.4MB** | **약 2.0MB** | **≈0ms** (이벤트 대기) |
| v2.1 `/dark` (돋보기 API) | 약 3.3MB | 약 188MB | 20초에 0.42~0.63초 |
| v2.2 `/dark` (GDI) | **약 1.8MB** | **약 3.2MB** | **20초에 0초 (BongAd) + 카카오톡 0.19초** |

폴링 대신 `SetWinEventHook`으로 카카오톡 창이 생기거나 움직일 때만 깨어나고(150ms 디바운스), 10초마다 한 번 안전 점검을 합니다. C 런타임 없이 `user32.dll`/`kernel32.dll`/`gdi32.dll`만 씁니다. 좌표는 카카오톡 창과 같은 DPI 모드로 읽어 배율이 다른 모니터에서도 맞습니다.

`BongAd.exe` (15,360 bytes) SHA256: `4e87998cb143640434ad6a178dc9c881f890c81ed77fa9b5f1bc228340363b49`

| **v2.4 `/dark`** (카카오톡 앞, 20초) | **약 3.3MB** | **약 3.2MB** | **0.06초 (BongAd)** |

## 동작 원리
- `user32.dll`의 EnumWindows/EnumChildWindows/ShowWindow/SetWindowPos/SetWindowRgn (+ `/dark`일 때 PrintWindow/UpdateLayeredWindow, 저수준 입력 훅)만 사용합니다. 바이너리 패치, 프로세스 주입, 네트워크, hosts/레지스트리 수정이 **없습니다**. 파일은 복원용 목록 `%TEMP%\BongAd.hidden.txt`만 씁니다.
- 카카오톡 창 중 제목에 `광고`, `AdView`, `BannerAd`, `AdFit`, `MOMENT`가 들어간 광고 웹뷰(`Chrome_WidgetWin_1` 'MOMENT 광고')를 찾아 그 광고 팝업(`EVA_Window_Dblclk`, 제목 없음)을 숨깁니다. 채팅방 창 안에서 발견되면 해당 컨테이너를 숨깁니다.
- 메인 창(`EVA_Window_Dblclk` '카카오톡') 하단의 제목 없는 `EVA_ChildWindow` 광고 자리를 숨기고, `OnlineMainView_*` / `LockModeView_*`와 그 안의 목록의 **높이만** 창 하단 1px 위까지 늘립니다 (위치는 건드리지 않음).

### 상단 검은 줄 수정
이전 버전은 뷰를 늘릴 때 위치까지 다시 지정하다가 `OnlineMainView`가 1px 아래(카카오톡 원래 y=38 → 39)로 밀려, 그 틈으로 메인 창의 검은 배경이 상단 가로줄처럼 보였습니다. 이제 `SWP_NOMOVE`로 높이만 바꿔 카카오톡 원래 배치(광고 없을 때와 동일한 위쪽 좌표)를 유지합니다.

## 빌드
- Windows: MinGW-w64 gcc를 PATH에 두고 `build.bat`
- Linux/macOS: `./build.sh` (mingw-w64 또는 `pip install ziglang`). 커밋된 exe는 zig 0.x + `/Brepro`로 빌드되어 같은 결과가 재현됩니다.

## 한계
- 간격 값은 125% 배율에서 맞췄습니다. 다른 배율에서는 `BongAd.c`의 픽셀 값을 조정해야 할 수 있습니다.
- 카카오톡 업데이트로 창 클래스/제목이 바뀌면 동작하지 않을 수 있습니다.
- 채팅방 내부 광고는 실제 사례로 검증하지 못했습니다(테스트 당시 없음).
- 작업 관리자로 강제 종료하면 숨긴 창이 남습니다. `restore.bat`을 실행하세요.

## English
BongAd is a 7.5 KB native Windows program that hides the KakaoTalk PC ad banner and stretches the list over the freed space. It uses only user32/kernel32 (no CRT, no injection, no network), sleeps on `SetWinEventHook` events instead of polling: ~0.4 MB working set, ~1.9 MB private bytes, ~0% CPU (the old PowerShell version used ~99 MB). Run `run.bat`; `restore.bat` stops it and restores; `install-startup.bat` / `uninstall-startup.bat` manage auto-start. Build with `build.bat` (MinGW-w64) or `build.sh` (mingw-w64 or zig). Fixed: a 1px black bar at the top caused by the old version moving the main view down; now only the height is changed. v2 adds a compact layout (moves KakaoTalk's own container windows: top -8px, filter row and list pulled up; per-row spacing is owner-drawn and cannot be changed without injection) and an opt-in `/dark` overlay using the Magnification API (v2.2 replaces the Magnification API with GDI PrintWindow + a layered window: ~3.2 MB private bytes, ~0% CPU idle). Unofficial, personal use, MIT License.
