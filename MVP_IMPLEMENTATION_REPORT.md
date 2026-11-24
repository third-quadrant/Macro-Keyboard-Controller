# Fireworm MVP 구현 보고서

작성: 2026-11-24
대상: MINI KeyBoard (VID `0x1189` / PID `0x8890`) 독립 제어 프로그램
상태: **MVP 구현 및 실물 검증 완료. KEY1~KEY6 = F13~F18, 노브 좌/우/클릭, 물리 KEY2 액션 실행까지 확인.**

---

## 0. 한 줄 요약

벤더 프로그램 없이 동작하는 **.NET 8 WPF 트레이형 앱**을 구현했다.
Raw Input으로 대상 장치 입력을 받고, 6개 키/노브에 독립 액션을 지정하며, JSON 설정을 저장한다.
KEY1~KEY6을 F13~F18로 바꾸는 **장치 트리거 설정 마법사**를 구현했고, 사용자 승인 후 **KEY1~KEY6에 실제 적용·검증**했다. 노브 좌/우/클릭도 실제 조작으로 확인했으며, 왼쪽 회전 디바운스 버그를 발견해 수정했다.

---

## 1. 구현 범위

| 구분 | 내용 | 상태 |
|---|---|---|
| 장치 탐색 | VID/PID 기반 HID 인터페이스 열거, MI_01 식별, 2초 폴링 | 완료 |
| 입력 감지 | Raw Input `RIDEV_INPUTSINK`, WM_INPUT, 장치 경로 필터 | 완료 |
| 물리 키 식별 | F13~F18 내부 트리거, F13은 KEY1…F18은 KEY6 | 완료(장치 쓰기 전) |
| 노브 | Consumer `0xEA/0xE9/0xE2` → 왼쪽/오른쪽/클릭 | 완료 |
| 액션 엔진 | 키 입력/조합, 텍스트, 프로그램 실행, 미디어, 마우스 | 완료 |
| 설정 | JSON 저장/불러오기, 손상 파일 복구, 버전 필드 | 완료 |
| UI | 키 6개 + 노브 3방향 카드, 액션 편집기, 테스트, 로그 | 완료 |
| 장치 트리거 마법사 | 백업, KEY1 단일 시험, 나머지 적용, 전체 복구 | 완료 — 실제 적용·검증 |
| 프로필/레이어 | 후속(P2) | 미구현 |
| 키 LED 피드백 | 누른 키만 1초 점등(휘발성 슬롯 쓰기) | 완료 |

---

## 2. 프로젝트 구조

```
Fireworm.sln
src/
  Fireworm.Core/                 # 장치/프로토콜/입력/액션/설정 (net8.0)
    Models/TriggerId.cs          # F13~F18, 슬롯, Consumer 매핑
    Models/ActionDefinition.cs
    Models/AppSettings.cs
    Models/DeviceBackup.cs
    Input/RawInputService.cs     # WM_INPUT, 장치 필터, 키/노브 파싱
    Input/RawObservation.cs
    Input/TriggerEvent.cs
    Actions/KeyParser.cs         # C, Ctrl+Z, F11 파서
    Actions/InputSender.cs       # SendInput (키/텍스트/미디어/마우스)
    Actions/ActionEngine.cs
    Actions/TriggerRouter.cs     # 입력 → 액션 분리, 디바운스
    Device/HidEnumerator.cs
    Device/HidDeviceInfo.cs
    Device/VendorPacketBuilder.cs
    Device/VendorConfigWriter.cs # MI_01 65B/RID3, 사용자 버튼에서만 호출
    Device/DeviceService.cs
    Settings/SettingsStore.cs
  Fireworm.App/                  # WPF (net8.0-windows)
    App.xaml / App.xaml.cs
    MainWindow.xaml / .cs
    DeviceTriggerWindow.xaml / .cs
tests/
  Fireworm.Tests/                # 54개 자동 검증
  InputSink/                     # 수신 포커스 테스트용 WinForms TextBox
tools/phase2/                    # UIA/pcap/진단 스크립트
docs/settings.example.json
build.ps1 / run.ps1
```

---

## 3. 빌드 및 실행

### 3.1 빌드

```powershell
# .NET 8 SDK (user-local)
C:\Users\user\.dotnet8\dotnet.exe --version   # 8.0.428

.\build.ps1
# 또는
C:\Users\user\.dotnet8\dotnet.exe build Fireworm.sln -c Release
```

### 3.2 실행

```powershell
dist\Fireworm\Fireworm.App.exe
# 또는
.\run.ps1
```

- 프레임워크 의존 배포본은 **.NET 8 Desktop Runtime**이 필요하다(이 PC에는 8.0.14 설치됨).
- 배포본: `dist\Fireworm\Fireworm.App.exe`
- **자체 포함 배포본**: `dist\Fireworm-standalone\Fireworm.App.exe` (약 168MB, .NET 런타임 불필요)
- 설정 파일: `%LOCALAPPDATA%\Fireworm\settings.json`
- 백업 파일: `%LOCALAPPDATA%\Fireworm\device-backup.json`
- 진단 로그: `%LOCALAPPDATA%\Fireworm\fireworm.log`

---

## 4. 설정 파일

예시: `docs/settings.example.json`

```json
{
  "Version": 1,
  "Device": { "Vid": 4489, "Pid": 34960 },
  "Bindings": {
    "KEY1": { "Type": "keyboard", "Keys": ["C"], "Text": "", "Path": "", "Arguments": "", "Media": "volume_up", "Mouse": "left_click", "WheelDelta": 120 },
    "KEY2": { "Type": "launch", "Path": "C:\\Windows\\System32\\notepad.exe" }
  }
}
```

- `Bindings` 키: `KEY1`~`KEY6`, `KNOB_LEFT`, `KNOB_CLICK`, `KNOB_RIGHT`.
- `Type`: `none`, `keyboard`, `text`, `launch`, `media`, `mouse`.
- 잘못된 JSON은 `.bad-<타임스탬프>`로 격리하고 기본값으로 시작한다.
- 알 수 없는 경로/키는 앱을 종료시키지 않고 로그/상태에 오류를 표시한다.

샘플을 그대로 적용하려면:

```powershell
Copy-Item docs\settings.example.json "$env:LOCALAPPDATA\Fireworm\settings.json" -Force
```

> 현재 `%LOCALAPPDATA%\Fireworm\settings.json`에는 사용자가 UI에서 저장한 설정이 들어 있다. 위 `Copy-Item`은 이를 **덮어쓰므로**, 기존 설정을 백업한 뒤 선택적으로 사용한다.

---

## 5. UI

- 상단: 장치 연결 상태, VID/PID, MI_01 경로.
- 좌측: KEY1~KEY6 카드, 노브 3방향 카드, 테스트 패널.
- 우측: 선택한 버튼의 동작 종류/세부 설정/저장/테스트.
- 하단: 최근 활동 로그.
- `장치 트리거 설정` 버튼 → 마법사:
  1. 현재 동작 백업 (기본 A/C 또는 현재 동작 녹음).
  2. KEY1만 F13 시험 적용 → KEY1을 눌러 F13 감지 확인 → 10초 미감지 시 자동 복구.
  3. 나머지 5개 적용 → KEY2~KEY6 확인.
  4. 전체 복구.

F13~F18은 일반 액션 편집 화면에 표시하지 않는다. 마법사(진단/설정 화면)에서만 내부 트리거로 표시된다.

---

## 6. 테스트 결과

### 6.1 자동 테스트

```
dotnet run --project tests/Fireworm.Tests/Fireworm.Tests.csproj -c Release
54/54 checks passed.
```

검증 항목:
- F13~F18 VK/usage/슬롯 매핑, Consumer `0xEA/0xE9/0xE2` 매핑
- 벤더 패킷: `01 11 01 00 00 00` / `01 11 01 01 00 68` / `AA AA`
- KEY6/F18, 레이어 니블 `0x21`, LED `B0 18 01` / `AA A1`
- 키 파서: `C`, `Ctrl+Z`, `Ctrl+Shift+S`, `F11`, `F13`, 오류 케이스
- VKey→HID usage (A=0x04, C=0x06, F13=0x68)
- 설정 JSON 왕복, 손상 JSON 복구/격리
- 백업 기본값(A/C), 복구 패킷 18개
- 액션 엔진의 입력 검증(실제 입력은 보내지 않음)

### 6.2 스모크 테스트

```
Fireworm.App.exe --smoke
OK ... settings=...\settings.json device=연결됨 bindings=9
```

### 6.3 UI/실입력 테스트

| 테스트 | 방법 | 결과 |
|---|---|---|
| WPF 창 실행 | `Fireworm.App.exe` 실행 | `MainWindowTitle=Fireworm — Macro Keyboard Controller`, `Responding=True` |
| 장치 연결 감지 | 스모크 + PnP | `device=연결됨` |
| UI → 프로그램 실행 | UIA로 `KEY2` 테스트 버튼 Invoke | 새 Notepad 프로세스 생성 확인 |
| UI 설정 저장 | UIA로 KEY1=Z 저장 | `settings.json`의 `KEY1.Type=keyboard`, `Keys=[Z]` 확인 |
| 키보드 액션 | InputSink에 포커스, `--test-keyboard C` | `TEXT=c`, 결과 `OK 키 입력: C`, `inputSize=40`, `fgProc=InputSink` |
| 미디어 액션 | `--test-media volume_up` | `OK 미디어: 볼륨 증가` |
| 트리거 마법사 UI | UIA로 창 열기 | 창 visible, `KEY1만 시험 적용`/`전체 복구`/`닫기` 버튼 확인, `나머지 5개 적용`은 KEY1 확인 전 비활성 |
| KEY1 → F13 실제 적용 | `ApplyConfig writekey 1 0x68 --confirm` + HidProbe | **vk=7C(F13) 감지, 성공** |
| KEY2~KEY6 → F14~F18 실제 적용 | `ApplyConfig writekey 2..6` + HidProbe | **vk=7D~81(F14~F18) 모두 감지, 성공** |
| 물리 KEY2 → 액션 실행 | 실제 KEY2 입력 + 앱 로그 | **`KEY 2 → 실행: C:\Windows\System32\notepad.exe` 확인** |
| 노브 좌/우/클릭 | 실제 조작 + 앱 로그 | 좌=볼륨-, 우=볼륨+, 클릭=음소거 확인. 왼쪽 디바운스 수정 후 성공 |
| 키 LED 피드백 | 실제 앱에서 KEY1~KEY6 입력 | 누른 키만 켜지고 1초 뒤 자동 소등 확인. 키 액션도 정상 |

### 6.4 실제 하드웨어 검증과 시뮬레이션 구분

- 실제 검증: 장치 연결 인식, UI 실행, UI→프로그램 실행, UI 설정 저장, InputSink에 실제 `SendInput` 키 입력, 미디어 키 전송, **KEY1~KEY6의 F13~F18 실제 출력**, **물리 KEY2 → Notepad 실행**, **노브 좌/우/클릭 → 미디어 액션**.
- 시뮬레이션/부분 검증: 마우스 액션은 실제 클릭 부작용을 피하기 위해 자동 테스트하지 않았다. 레이어/프로필 전환은 미구현.
- 미검증: 연결 해제/재연결, 레이어, 마우스 액션 실기, 전체 설정 읽기.

---

## 7. 장치 트리거 적용 — 완료

### 7.1 현재 장치 상태

- KEY1~KEY6: **F13~F18** (usage `0x68`~`0x6D`), layer 1
- 노브: `0xEA` 볼륨-, `0xE9` 볼륨+, `0xE2` 음소거 (기존 유지, 앱이 추가 액션 실행)
- 백업: `%LOCALAPPDATA%\Fireworm\device-backup.json` (KEY1=A, KEY2~6=C)
- 전체 설정 읽기(덤프)는 불가능. 백업은 **행동 수준**이다.

### 7.2 적용된 값

| 슬롯 | 물리 | 내부 트리거 | usage | 검증 |
|---|---|---|---|---|
| 1 | KEY1 | F13 | `0x68` | vk=7C 확인 |
| 2 | KEY2 | F14 | `0x69` | vk=7D 확인 |
| 3 | KEY3 | F15 | `0x6A` | vk=7E 확인 |
| 4 | KEY4 | F16 | `0x6B` | vk=7F 확인 |
| 5 | KEY5 | F17 | `0x6C` | vk=80 확인 |
| 6 | KEY6 | F18 | `0x6D` | vk=81 확인 |

패킷(각 슬롯):

```
RID 3 | slot 11 01 00 00 00
RID 3 | slot 11 01 01 00 usage
RID 3 | AA AA
```

### 7.3 검증 증거

- **KEY1 시험 적용**: `ApplyConfig writekey 1 0x68 --confirm` → HidProbe Raw Input에서 `mi_00 RAWKEY vk=7C` 감지. 성공.
- **KEY2~KEY6**: `writekey 2..6` → `vk=7D, 7E, 7F, 80, 81` 모두 감지. 성공.
- **물리 KEY2 → 액션**: 앱 로그에 `KEY 2 → 실행: C:\Windows\System32\notepad.exe` 기록. 성공.
- **노브 왼쪽**: 장치 원문 `02 EA 00` / `02 00 00` 확인 → 앱 로그 `노브 왼쪽 → 미디어: 볼륨 감소`.
- **노브 오른쪽/클릭**: `노브 오른쪽 → 볼륨 증가`, `노브 클릭 → 음소거` 확인.
- **디바운스 수정**: 같은 Consumer usage가 release 없이 연속될 때 영구 억제되던 문제를 시간 기반(120ms)으로 수정. 수정 후 왼쪽 회전이 정상 동작.

### 7.4 복구

```powershell
# 전체 A/C 복구 (테스트 도구)
tools\phase2\ApplyConfig\bin\Release\net8.0\ApplyConfig.exe restore --confirm
```

또는 앱의 **장치 트리거 설정 → 전체 복구** 버튼을 사용한다.
복구는 `device-backup.json`의 행동 수준 값(A/C)으로 수행된다. 백업 이후 다른 도구로 설정을 바꿨다면 원래 값과 다를 수 있다.

### 7.5 키 LED 피드백 (키 누를 때만 켜짐)

키 슬롯에도 LED 명령(type 8)을 쓸 수 있다는 것을 실측으로 확인했다. 전역 LED 모드(`B0`)와 달리 **슬롯별 명령은 즉시 반영**된다.

| 명령 | 패킷 | 결과 |
|---|---|---|
| 슬롯 LED 켜기 (휘발성) | `[slot 18 01 00 00 00]` | 해당 키 LED만 켜짐 |
| 슬롯 LED 끄기 (휘발성) | `[slot 18 00 00 00 00]` | 해당 키 LED만 꺼짐 |
| 커밋 | `AA A1` | 플래시에 저장(앱은 **사용하지 않음**) |

- **휘발성 쓰기**: 커밋(`AA A1`)을 보내지 않으면 플래시에 기록되지 않고 즉시 반영된다. 앱은 이 방식만 사용하므로 플래시 수명에 영향을 주지 않는다.
- **앱 동작**: KEY1~KEY6 중 하나가 눌리면 해당 슬롯 LED를 켜고, **1초 뒤** 자동으로 끈다. 여러 키를 연속으로 눌러도 각 슬롯이 독립적으로 켜졌다 꺼진다.
- **실측**: 앱 실행 상태에서 6개 키를 눌러 누른 키만 켜지고 1초 뒤 꺼지는 것을 확인. 이후 KEY1~KEY6이 F13~F18을 계속 출력하고 액션이 정상 실행되는 것도 확인(키 매핑 무영향).
- **전역 모드 실험 결과**: `B0` 모드 0/1/2는 이 장치에서 육안 변화가 없었다. 슬롯별 type 8만 실제로 동작했다. 커밋된 슬롯 모드는 1(누른 키 반응)로 두었다.
- **한계**: 켜짐 유지 시간은 앱 하드코딩 1초(`KeyLedFeedback.HoldMilliseconds`). LED 밝기/색상/개별 주소 지정은 프로토콜에서 발견되지 않았다.

---

## 8. 알려진 제한 사항

1. **장치 트리거를 F13~F18로 변경했다.** 원래 A/C 동작은 `device-backup.json`과 전체 복구로만 되돌릴 수 있다.
2. **F13~F18은 포그라운드 앱에도 전달된다.** Raw Input은 관찰만 하며 입력을 차단하지 않는다. 대부분의 앱에서 무해하지만, F13~F18에 단축키를 등록한 앱이 있으면 함께 동작할 수 있다.
3. **노브의 원래 볼륨/음소거 동작은 그대로 발생한다.** 앱이 노브 액션을 추가하면 이중 동작이 된다. 노브 재매핑은 P2.
4. **전체 설정 읽기/백업 불가.** `device-backup.json`은 관측된 동작만 기록한다.
5. **KEY1~KEY6 슬롯 매핑은 실측 확인됨.** 노브 슬롯 13~15의 쓰기/재매핑은 아직 검증하지 않았다.
6. **F13~F18 펌웨어 지원은 실측 확인됨.** 레이어 2/3와 노브 슬롯 재매핑은 미검증이다. 키 LED는 슬롯별 type 8 휘발성 쓰기로 동작 확인.
7. **레이어/프로필/시퀀스/고급 설정 미구현** (P2).
8. **자동 시작/설치 패키지/코드 서명 없음.** exe를 직접 실행한다.
9. **마우스 액션 UI 테스트 미수행.** 코드는 SendInput 표준 플래그를 사용한다.
10. **연결 해제/재연결 실제 시험 미수행.** 2초 폴링과 상태 표시는 구현됨.
11. **UI 시뮬레이터의 키보드 액션은 Fireworm 창으로 전달될 수 있다.** 실제 사용은 물리 키 입력 시 포그라운드 앱으로 전달된다.

---

## 9. 후속 개발 목록

### P1
- 노브 슬롯(13~15) 재매핑 검증 및 원래 볼륨 동작 제거
- 연결 해제/재연결 테스트
- 프로필 저장/전환
- 자동 시작(트레이) 및 단일 인스턴스
- 마우스/미디어 액션 실기 테스트

### P2
- LED 모드 제어 (`B0`, `AA A1`)
- 다중 레이어/시퀀스/지연/반복
- 설정 가져오기/내보내기, 백업 관리
- 설치 패키지(MSIX/Inno), 코드 서명
- F13~F18 충돌 감지/대체 트리거 전략

---

## 10. 변경/생성 파일

- `src/Fireworm.Core/**` — 코어 라이브러리
- `src/Fireworm.App/**` — WPF 앱
- `tests/Fireworm.Tests/**` — 54개 자동 테스트
- `tests/InputSink/**` — 포커스 입력 테스트 도구
- `tools/phase2/**` — UIA/진단 스크립트
- `docs/settings.example.json`
- `build.ps1`, `run.ps1`, `.gitignore`
- `dist/Fireworm/**` — 실행 배포본
- `MVP_IMPLEMENTATION_REPORT.md` — 본 보고서

---

## 11. 완료 상태 및 복구

### 11.1 최종 완료 조건

| 조건 | 상태 |
|---|---|
| 제조사 프로그램 없이 독립 실행 | **충족** — WPF 앱 단독 실행 |
| KEY1~KEY6 독립 식별 | **충족** — F13~F18 실측 확인 |
| 노브 좌/우/클릭 독립 식별 | **충족** — Consumer `0xEA/0xE9/0xE2` 실측 |
| 슬롯별 다른 액션 지정 | **충족** — UI/JSON |
| 프로그램 실행·키 입력 등 실제 작동 | **충족** — 물리 KEY2 → Notepad, InputSink 키 입력 |
| 설정 저장/재실행 유지 | **충족** — JSON 저장/불러오기 |
| 키 누를 때 해당 LED만 점등 | **충족** — 슬롯별 휘발성 LED 쓰기, 1초 자동 소등 |
| 연결 해제/재연결 대응 | 구현됨, 물리 시험 미수행 |
| 벤더 프로그램/DLL 의존 없음 | **충족** |
| F13을 필수 식별 수단으로 노출하지 않음 | **충족** — 내부 트리거로만 사용, 일반 UI 비노출 |

### 11.2 현재 장치 상태

- KEY1~KEY6: **F13~F18** (검증 완료)
- 키 LED: 슬롯별 모드 1(누름 반응) 커밋됨. 앱이 1초 뒤 휘발성 끄기
- 노브: 기존 Consumer(볼륨/음소거) 유지 + 앱 액션
- 백업: `%LOCALAPPDATA%\Fireworm\device-backup.json` (A/C)

### 11.3 복구

```powershell
tools\phase2\ApplyConfig\bin\Release\net8.0\ApplyConfig.exe restore --confirm
```

또는 앱의 **장치 트리거 설정 → 전체 복구**.

복구는 행동 수준 백업(A/C)으로 수행된다. 전체 설정 덤프는 지원되지 않는다.

### 11.4 남은 후속 작업

P1/P2 목록(노브 재매핑, 연결 해제/재연결, 프로필/레이어, 마우스 실기, 설치 패키지)은 `9. 후속 개발 목록` 참조.
