# BongHub.Next

## 0.3.0

0단계 기준선. 매일 쓰는 기능은 0.2.0 메모·설정 프로토타입과 같아요.

- 대상 프레임워크를 `net10.0-windows`로 올렸어요. SDK 기준은 10.0.403이고, 같은 10 기능 대역의 더 높은 SDK로 진행할 수 있어요.
- 트레이를 H.NotifyIcon.Wpf 2.4.1로 바꿨어요. `UseWindowsForms`는 뺐어요. 왼쪽 클릭은 설정을 열고, 오른쪽 클릭은 다크 메뉴, 종료만 프로세스를 끝내요.
- 게시는 ReadyToRun, `win-x64`, 프레임워크 종속이에요. 단일 파일, 트림, Native AOT는 꺼 두었어요.
- 메모리 기준선은 `next/measure-baseline.ps1`예요. 트레이만, 설정 창, 설정 창을 숨긴 뒤, 메모 7개를 각 3회 재고, `ConcurrentGarbageCollection`을 켠 경우와 끈 경우를 같이 남겨요. 기본값은 여전히 꺼짐이에요.
- `next_startup.txt`에 트레이 아이콘이 보인 시각과 첫 `ContentRendered`를 같이 적어요.
- `%LOCALAPPDATA%\BongHub`의 형식과 `Local\BongHub_NotesWriter` 잠금 순서는 그대로예요. `Local\BongHub_Tray`는 만들지 않아요.
- Notes 설정 한 키를 저장할 때 `config.json`에서 그 값만 바꾸고, 나머지 바이트는 그대로 둬요. 값이 같으면 파일을 다시 쓰지 않아요.
