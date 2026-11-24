# MINI KeyBoard 완전 분해 및 독립 대체 프로그램 설계 보고서

**분석 기술:** REA(Reverse Engineer Anything) 6.10.1 managed static provider + ILSpy 9.1 + 실물 USB HID 계측
**대상:** `MINI KeyBoard.exe` (v1.0.0.0, ClickOnce 1.0.0.2), `HidLibrary.dll` 3.1.0.0, 실물 6키+1노브 매크로 키보드
**작성일:** 2026-11-23
**범위:** 프로그램 전체 분해, 장치 프로토콜 명세, 독립 대체 프로그램 아키텍처, Phase 1 검증 계획
**원칙:** 이번 단계는 **분석/설계**다. 장치 설정 변경과 구현은 아직 수행하지 않았다.

---

## 0. 결론 요약

1. **입력 경로는 확정됐다.** 6개 키는 `MI_00` 표준 키보드(RID 1)로, 노브는 `MI_02 COL02` 컨슈머(RID 2)로 PC에 도착한다. 키보드/마우스 컬렉션은 Windows가 독점하므로 **Raw Input이 유일한 감지 경로**다.
2. **설정 경로도 확정됐다.** `MI_01` / Usage `FF00:0001` / **Report ID 3** / 65바이트 출력. 이 장치에는 RID 0·2가 없다.
3. **기존 앱은 완전히 대체 가능하다.** 새 앱은 ① Raw Input으로 입력을 받고 ② 자체 액션 엔진으로 출력을 만들고 ③ 자체 설정 기록기로 키를 고유 트리거(F13~F18, F19~F21)로 재매핑하면 된다. 벤더 EXE/DLL은 런타임에 필요 없다.
4. **단, "완벽한 제어"의 전제는 키 재매핑이다.** 현재 키 2~6이 모두 `C`를 보내므로, 재매핑 없이는 키별 매크로를 구분할 수 없고 원래 타이핑/볼륨 동작도 그대로 발생한다.
5. **LED는 전역 모드 3종만 확인됐다.** 각 키에 LED가 있어도 벤더 프로토콜은 개별 색/밝기를 노출하지 않는다. 개별 제어는 미확인이다.

**핵심 질문에 대한 답:** *기존 펌웨어를 수정하지 않고도* 새 PC 프로그램이 물리 입력을 **감지**하고 **자체 출력**을 만드는 것은 가능하다. 기존 출력(타이핑/볼륨)을 없애고 독점 제어하려면 **펌웨어가 아니라 사용자 키 매핑**을 고유·무해한 값으로 바꿔야 하며, 이 쓰기 경로는 아직 실물 검증 전이다.

---

## 1. 분석 대상과 방법

### 1.1 대상 파일

| 파일 | 크기 | SHA-256 | 식별 |
|---|---:|---|---|
| `Release\MINI KeyBoard.exe` | 217,088 | `4AE53D3A40921E4F57444C489FA1B38FE91A2E907FEDA17630450FC97A7CD7B1` | PE32 x86, .NET Framework 4.0, assembly 1.0.0.0 |
| `Release\HidLibrary.dll` | 45,056 | `8BAFD3E1C6F88822DBF738C929F7D3162D08243067C69C4A85E52ED13A78D12B` | HidLibrary 3.1.0.0 |
| `Release\Theraot.Core.dll` | 787,968 | `4406B6AFFDE4FBEC5C47AED24D0050C0148D94772D3F2C11E6C96CE2AD527799` | Theraot.Core 1.0.3.0 |
| `Release\app.publish\MINI KeyBoard.exe` | 218,096 | `CD20AC6E9CA5D6786ACD8AE1A7240420A8F838D79F5D2C5CA7DC5DCE2D5C6385` | 다른 빌드(프로토콜 동일) |

- PDB 2개(`MINI KeyBoard.pdb`, `HidLibrary.pdb`)가 포함되어 소스 파일명·메서드명까지 복원됐다.
- ClickOnce 매니페스트: publisher/product `MINI KeyBoard`, version `1.0.0.2`, x86, `.NETFramework,Version=v4.0`.
- 내부 네임스페이스/어셈블리명은 `HIDTester`다. UI 리소스가 영어/중국어인 설정 도구다.

### 1.2 도구와 버전

| 도구 | 버전 | 용도 |
|---|---|---|
| REA (`rea-agents`) | 6.10.1 | managed PE 정적 증거: identity, metadata, members, P/Invoke, imports, resources |
| Node.js (portable) | 24.22.3 | REA 요구사항 충족용. 시스템 Node 24.19.0은 요구 버전 미달 |
| ILSpy (`ilspycmd`) | 9.1.0.7988 | C# 소스 복원, 전체 로직 확인 |
| HidSharp | 2.6.4 | 실물 descriptor 재구성, HID 열거/읽기 검증 |
| HidProbe (자체) | Phase 0 | 읽기 전용 HID 열거, Raw Input/직접 읽기 캡처 |
| Windows PowerShell / pwsh | 5.1.22621 / 7.6.6 | 계측 스크립트 |

### 1.3 증거 등급

| 표기 | 의미 |
|---|---|
| **[F]** | 실물 계측 또는 REA/PE 증거로 직접 확인 |
| **[S]** | 디컴파일 소스/IL 정적 분석으로 확인 |
| **[I]** | 확인된 사실에서의 추론 |
| **[U]** | 읽기만으로 확인 불가. 쓰기 시험 또는 사용자 확인 필요 |

### 1.4 이번 단계의 한계

- 장치 **쓰기 없음**: Output Report, Feature Report, `AA AA` 커밋, 키 매핑 기록을 수행하지 않았다.
- 원문 Report Descriptor는 사용자 모드에서 접근 불가(커널 전용 IOCTL, 레지스트리 ACL). Windows HID API 재구성 결과를 사용했다.
- 키보드 HID 원문 바이트는 OS 독점으로 관측 불가. Raw Input의 VKey/MakeCode를 사용했다.
- 실제 키 매핑 슬롯, 다중 엔트리 의미, 레이어/읽기/지우기/개별 LED는 **[U]**다.

---

## 2. REA 정적 분석 결과

### 2.1 Artifact identity [F]

```
format            PE32, x86 (machine 0x014C)
CLI               IL-only, 32-bit-required, no strong name
runtime           v4.0.30319
target framework  .NETFramework,Version=v4.0
assembly          MINI KeyBoard 1.0.0.0
module MVID       830e3432-592a-4fe8-a60e-4e46348e689c
entry point       0x06000081 (managed)
PE import         mscoree.dll!_CorExeMain only
```

### 2.2 Metadata inventory [F]

| 테이블 | 행 수 |
|---|---:|
| TypeDef | 30 |
| MethodDef | 295 |
| Field | 331 |
| Param | 461 |
| MemberRef | 198 |
| ImplMap (P/Invoke) | 16 |
| ManifestResource | 8 |
| CustomAttribute | 31 |
| AssemblyRef | 6 |
| ModuleRef | 6 |

- REA `inspect-managed-members`: **3,371 call edges**, **3,039 field accesses**, **388 user strings**.
- 커버리지 상태: complete. 토큰은 빌드 로컬 좌표이므로 위 SHA-256/MVID와 함께만 의미가 있다.

### 2.3 P/Invoke 경계 [F]

REA `inspect-managed-native-boundaries`가 확인한 16개:

| DLL | 함수 |
|---|---|
| `hid.dll` | `HidD_GetHidGuid`, `HidD_GetAttributes`, `HidD_GetSerialNumberString`, `HidD_GetPreparsedData`, `HidD_FreePreparsedData`, `HidP_GetCaps` |
| `setupapi.dll` | `SetupDiGetClassDevs`, `SetupDiDestroyDeviceInfoList`, `SetupDiEnumDeviceInterfaces`, `SetupDiGetDeviceInterfaceDetail` |
| `kernel32.dll` | `CreateFile`, `CloseHandle`, `ReadFile`, `WriteFile` |
| `user32.dll` | `RegisterDeviceNotification`, `UnregisterDeviceNotification` |

**중요:** `ReadFile`과 `WriteFile`은 선언만 되어 있고 실제 호출 간선이 없다. 실제 I/O는 `FileStream`/`HidLibrary`가 담당한다. `RegisterHIDNotification`도 호출되지 않는다. 즉, 원본은 장치 알림을 쓰지 않고 **30ms 타이머 폴링**으로 연결 상태를 확인한다.

### 2.4 타입 지도 [F]

| 타입 | 메서드 | 필드 | 역할 |
|---|---:|---:|---|
| `HIDTester.FormMain` | 50 | 48 | 메인 폼, 전역 상태, 다운로드/커밋 |
| `HIDTester.BasicKeys` | 103 | 100 | 기본 키 100개 버튼 |
| `HIDTester.FunKey` | 42 | 37 | Ctrl/Shift/Alt/Win 조합, Shift 기호 |
| `HIDTester.MULKey` | 10 | 7 | 미디어 6키 |
| `HIDTester.MouseKey` | 16 | 12 | 마우스 버튼/휠/수정자 |
| `HIDTester.LEDkey` | 8 | 4 | LED 모드 3종 |
| `HIDTester.LayerFun` | 6 | 7 | 레이어 3종 라디오 |
| `HIDTester.HidLib` | 5 | 3 | HidLibrary 래퍼 |
| `HID.Hid` | 36 | 14 | 자체 Win32 HID 래퍼 |
| `HIDTester.AutoSizeFormClass` | 5 | 2 | 폼 크기 스케일링 |
| `HIDTester.Program` | 1 | 0 | WinForms 진입점 |
| `HIDTester.Properties.Resources` | 4 | 2 | 리소스 접근자 |
| `HIDTester.Properties.Settings` | 4 | 1 | `DefaultLanguage` |
| `HID.report` | 1 | 2 | Report ID + byte[] |
| `HID.HIDP_CAPS` 등 구조체/enum | 0~1 | 3~15 | Win32 구조체 |
| `KeyParam` (FormMain 중첩) | 2 | 15 | 전역 전송 버퍼/상태 |

### 2.5 핵심 호출 흐름 [F]

```
Program.Main
└─ Application.Run(new FormMain)
   ├─ myHid.DataReceived += myhid_DataReceived
   ├─ myHid.DeviceRemoved += myhid_DeviceRemoved
   ├─ MenuList()                 // 6개 페이지 버튼 + BasicKeys
   ├─ KEY_Colour_Init()
   ├─ Time_Display_Text()        // 30ms System.Timers.Timer
   ├─ Hide_Dowload_Text()
   ├─ LayerFunList()
   └─ Lanuage_Set_EN()

Timer1_Elapsed (30ms, threadpool)
├─ AutoCheckUsb()
│  ├─ WriteMode==0: myHid.OpenDevice(0x1189, 0x8890) → KeyBoardVersion_Check()
│  └─ WriteMode==1: myHidLib.Connect_Device() → KeyBoardVersion_Check()
│                   / myHidLib.Check_Disconnect()
├─ Key_Clear_Fun() (페이지 전환 플래그)
├─ SetText/SetFunText 갱신
└─ 다운로드 표시 타이머

Download_Click
├─ myHidLib.Get_Dev_Sta()
├─ (RID != 0) Send_SwLayer()
├─ HidLib.WriteDevice(rid, buf) 또는 HID.Hid.Write(report)
├─ Send_WriteFlash_Cmd()   // AA AA
└─ (LED) Send_WriteFlashLED_Cmd() // AA A1

HidLib.Connect_Device
├─ HidDevices.Enumerate(0x1189).FirstOrDefault()
├─ HidDevices.Enumerate(0x1189).ToList()
├─ DevicePath.IndexOf("mi_01") != -1
├─ OpenDevice()
└─ MonitorDeviceEvents = true

HidLib.WriteDevice
├─ CreateReport()
├─ ReportId = id
├─ Data[0..7] = payload[0..7]
└─ WriteReport(report, 500)

HID.Hid.Write
├─ byte[] 65
├─ [0] = reportID
├─ [1..] = reportBuff
└─ FileStream.Write(array, 0, 65)
```

### 2.6 문자열/리소스 [F]

- **388개 user string** 중 주요 값:
  - 페이지: `KEY`, `Ctrl Shift Alt`, `Multimedia`, `LED`, `Mouse`
  - 상태: `Connected`, `Not Connected`, `设备移除`
  - 결과: `Download success`, `Download failed`, `下载成功`, `下载失败`
  - 언어: `en-US`, `zh-CN`
  - 외부 URL: `http://www.cnblogs.com/hebaichuanyeah/p/4504855.html`
- PE 네이티브 리소스 3개: 아이콘 그룹(67,624B), 매니페스트(828B), 버전(20B).
- .NET ManifestResource 8개: `FormMain`, `BasicKeys`, `FunKey`, `MULKey`, `MouseKey`, `LEDkey`, `LayerFun`, `Properties.Resources`의 `.resources`.
- 위성 리소스: `en-US`, `zh-CN` 각각 `MINI KeyBoard.resources.dll`.

### 2.7 REA 한계 [F]

- 토큰은 빌드 로컬 좌표. SHA-256/MVID 없이는 재현 불가.
- CIL 앵커는 파일 기반 메서드 본문만 디코드. 런타임 값/실행 결과는 아님.
- 상수 필드는 Constant 테이블 선언이며 런타임 필드 값이 아님.
- user string은 `ldstr`로 로드된 것만 포함.
- 원본 앱의 **동작**은 정적 분석만으로 100% 보장되지 않는다. 실물 계측과 교차 검증했다.

---

## 3. 프로그램 해부

### 3.1 실행/연결 루프 [S]

- `FormMain` 생성자에서 HID 이벤트를 구독하고, 30ms `System.Timers.Timer`를 시작한다.
- 타이머 콜백이 `AutoCheckUsb()`를 호출해 연결 상태를 갱신한다.
- `WriteMode` 기본값은 **1**(HidLibrary 경로). `WriteMode=0`은 자체 Win32 래퍼 경로이며 사실상 레거시다.
- `AutoCheckUsb()`는 `WriteMode=1`에서:
  - 연결 전: `myHidLib.Connect_Device()`
  - 연결 후: `myHidLib.Check_Disconnect()`
- `Connect_Device()`는 `HidDevices.Enumerate(0x1189)`를 **두 번** 호출하고, 경로에 `mi_01`이 포함된 첫 장치를 연다. **PID를 확인하지 않는다.**

### 3.2 Report ID 감지 [S]

`KeyBoardVersion_Check()`는 65바이트 0 배열을 RID 3으로 먼저 쓴다.

1. RID 3 쓰기 성공 → `ReportID = 3`
2. 실패 → RID 0 시도 → 성공 시 `ReportID = 0`
3. 실패 → RID 2 시도 → 성공 시 `ReportID = 2`

이 장치는 **[F]** `MI_01` descriptor에 RID 3만 있으므로 3에서 성공한다. 새 앱은 쓰기 프로브 없이 descriptor에서 RID 3을 읽을 수 있다.

### 3.3 전역 버퍼 모델 [S]

`FormMain.KeyParam`은 정적(전역) 상태다.

| 필드 | 값/의미 |
|---|---|
| `Data_Send_Buff[65]` | 전송 버퍼. D0~D23 사용 |
| `KeySet_KeyNum = 0` | 키 슬롯 |
| `KeyType_Num = 1` | 타입 플래그 (하위 니블) |
| `KeyGroupCharNum = 2` | 그룹 수 |
| `KeySet_KeyValNum = 3` | 예약 |
| `Key_Fun_Num = 4` | 예약 |
| `KEY_Char_Num = 5` | 첫 엔트리 오프셋 |
| `KeyChar[100]` | 표시용 문자열 |
| `FunKeyChar[100]` | 수정자 표시용 |
| `ReportID` | 0/2/3 |
| `KEY_Cur_Layer` | 1~3 |
| `KEY_Cur_Page` | 1~5 |
| `Language_Set` | 0=en, 1=zh |

키 엔트리는 `buff[5..]`에 2바이트씩(수정자, usage) 기록된다. UI는 최대 5개 엔트리(`KeyChar[0,2,4,6,8]`)를 표시하지만, 코드에는 5개 제한 검사가 없다.

### 3.4 페이지/버튼 [S]

| 페이지 | 클래스 | 버튼 수 | 동작 |
|---|---|---:|---|
| KEY | `BasicKeys` | 100 | 기본 키 usage + Shift 기호 |
| Ctrl Shift Alt | `FunKey` | 42 | 수정자 조합, Shift 기호 21종 |
| Multimedia | `MULKey` | 10 | 재생/볼륨/이전/다음/음소거 |
| LED | `LEDkey` | 8 | 모드 0/1/2 |
| Mouse | `MouseKey` | 16 | 좌/우/중, 휠, Ctrl/Shift/Alt+휠 |
| (레이어) | `LayerFun` | 6 | 레이어 1~3 라디오 |

메인 폼에는 16개 키 버튼(`KEY1`~`KEY16`)과 6개 노브 버튼(`K1_Left/Centre/Right`, `K2_...`)이 함께 있다. **슬롯 충돌:** `KEY13`~`KEY16`은 슬롯 13~16을, `K1_Left/Centre/Right`는 13~15를, `K2_*`는 16~18을 쓴다. 이는 16키 모델과 2노브 모델을 한 UI로 지원하려다 생긴 별칭(alias)이다. 6키+1노브 실물에는 슬롯 1~6과 13~15만 해당한다. **[I]**

### 3.5 다운로드 파이프라인 [S]

`Download_Click`의 실제 순서:

```
slot = buff[0]
if slot == 0: return
if ReportID == 0:
    D1 = type & 0x0F
else:
    Send_SwLayer()                 // A1 layer
    D1 = (layer << 4) | type

switch (type & 0x0F):
  case 1:  // keyboard
    D2 = groupCount
    for b = 0..groupCount:
        D3 = b
        D4,D5 = group[b]          // b=0: modifier only, usage=0
        write()
    Send_WriteFlash_Cmd()         // AA AA

  case 2:  // media
    D2 = buff[5]; D3 = buff[6]
    write(); Send_WriteFlash_Cmd()

  case 3:  // mouse
    D2..D6 = buff[5..9]
    write(); Send_WriteFlash_Cmd()

  case 8:  // LED
    D2 = buff[2]                  // mode 0..2
    write(); Send_WriteFlashLED_Cmd() // AA A1
```

- 키 하나마다 **즉시 플래시 커밋**한다. 18슬롯 × 3레이어면 최대 54회 커밋.
- 레이어를 작업 후 1로 복원하지 않는다.
- 연결이 끊겼거나 슬롯이 0이면 **조용히 반환**한다(사용자 피드백 없음).
- `HidLibrary.WriteReport`의 반환값은 “OS 쓰기 성공”이지 “펌웨어가 수락/저장”이 아니다. 검증 절차가 없다.

### 3.6 키 타입 [S]

| 타입 | 하위 니블 | 의미 | 페이로드 |
|---|---|---|---|
| Keyboard | 1 | 키보드 usage/수정자 | count, index, modifier, usage |
| Media | 2 | 컨슈머 | media[0], media[1] |
| Mouse | 3 | 마우스 | buttons, dx, dy, wheel, modifier |
| LED | 8 | LED 모드 | mode 0..2 |

### 3.7 레이어/LED [S]

- 레이어 1~3. `A1 layer`로 편집 레이어를 선택하고, D1 상위 니블에도 레이어를 넣는다.
- `LayerFun` 라디오 변경 시 `KEY_Cur_Layer` 설정 + `PageBet_Inte_Cmd=1`로 UI 클리어.
- LED는 `slot 0xB0`, `D2=0/1/2`, `AA A1` 커밋. **개별 키 색/밝기 필드는 없다.**
- 원본 UI의 LED 버튼 텍스트는 `LED_Mode0/1/2`이며, 주 리소스에는 Off/On/Breath 같은 의미 이름이 없다. 위성 리소스의 번역은 미확인. **[S]**

### 3.8 원본의 결함/제약 (대체 이유) [S]

| # | 문제 | 영향 |
|---|---|---|
| 1 | 설정 읽기/백업 없음 | 장치에 무엇이 저장됐는지 알 수 없음 |
| 2 | 키 지우기 명령 없음 | Clear는 버퍼만 비우고 전송하지 않음 |
| 3 | 프로필 저장 없음 | `DefaultLanguage`만 저장. 키 설정은 장치에만 존재 |
| 4 | 30ms 폴링 | 연결 확인을 위해 초당 33회 HID 열거. CPU/UI 부하 |
| 5 | UI 스레드 위반 | `System.Timers.Timer` 콜백에서 컨트롤 접근 |
| 6 | VID만 필터 | 같은 VID의 다른 장치를 잘못 선택할 수 있음 |
| 7 | RID 감지를 쓰기로 수행 | 불필요한 출력, 실패 판정이 OS 수준 |
| 8 | 쓰기 결과 미검증 | 저장 성공/실패를 알 수 없음 |
| 9 | 레이어 원복 없음 | 편집 후 장치 레이어가 남을 수 있음 |
| 10 | 슬롯 별칭 | KEY13-16과 K1/K2가 같은 슬롯을 공유 |
| 11 | 5개 초과 엔트리 미검사 | 6개 이상 선택 시 잘못된/중복 서브리포트 |
| 12 | 개별 LED 없음 | 전역 모드 3종뿐 |
| 13 | 고정 크기 WinForms | DPI/레이아웃/한국어 미지원 |
| 14 | 다국어 en/zh만 | 한국어 없음 |
| 15 | 죽은 코드 | `ReadFile`/`WriteFile`/`RegisterHIDNotification` 미사용 |
| 16 | 취소/진행 표시 없음 | 대량 전송 시 UI 블로킹(500ms 타임아웃) |

---

## 4. 실물 장치 구조 [F]

### 4.1 USB 토폴로지

```
USB Composite Device  VID_1189&PID_8890  (bcdDevice 0000, 주소 3)
├─ MI_00 ─ HID Keyboard        0001:0006  RID 1   In 9   Out 2
├─ MI_01 ─ Vendor-defined       FF00:0001  RID 3   In 65  Out 65
├─ MI_02 ─ HID Keyboard        0001:0006  RID 1   In 12  Out 2   (COL01)
│        └ Consumer Control   000C:0001  RID 2   In 3   Out 0   (COL02)
└─ MI_03 ─ HID Mouse           0001:0002  no RID In 5   Out 0
```

### 4.2 컬렉션/접근성

| 컬렉션 | Usage | In | Out | Feature | RID | 읽기 열기 |
|---|---|---:|---:|---:|---|---|
| MI_00 Keyboard | `0001:0006` | 9 | 2 | 0 | 1 | 실패 `err=5` |
| MI_01 Vendor | `FF00:0001` | 65 | 65 | 0 | 3 | **성공** |
| MI_02 COL01 Keyboard | `0001:0006` | 12 | 2 | 0 | 1 | 실패 `err=5` |
| MI_02 COL02 Consumer | `000C:0001` | 3 | 0 | 0 | 2 | **성공** |
| MI_03 Mouse | `0001:0002` | 5 | 0 | 0 | 없음 | 실패 `err=5` |

- 제조사/제품/시리얼 문자열은 **비어 있음**.
- `MI_01` 입력 65B는 존재하지만 74초 계측 동안 **0건**이었다.
- `MI_02 COL01`과 `MI_03`도 이번 조작에서 0건.

### 4.3 실측 입력

| 조작 | 경로 | Report ID | 데이터 | 이벤트 |
|---|---|---|---|---|
| KEY1 | MI_00 | 1 | VKey `41`('A'), MakeCode `1E` | Keyboard down/up, 3회 |
| KEY2~6 | MI_00 | 1 | VKey `43`('C'), MakeCode `2E` | Keyboard down/up, 각 3회 |
| 노브 왼쪽 | MI_02 COL02 | 2 | `02 EA 00` / `02 00 00` | Consumer `0x00EA` Volume- |
| 노브 오른쪽 | MI_02 COL02 | 2 | `02 E9 00` / `02 00 00` | Consumer `0x00E9` Volume+ |
| 노브 클릭 | MI_02 COL02 | 2 | `02 E2 00` / `02 00 00` | Consumer `0x00E2` Mute |
| MI_01 Vendor | MI_01 | 3 | (없음) | 0건 |
| MI_02 COL01 | MI_02 | 1 | (없음) | 0건 |
| MI_03 Mouse | MI_03 | — | (없음) | 0건 |

- 키는 **일반 키보드 이벤트**로 이미 PC에 전달된다. 원래 타이핑이 발생한다.
- 노브는 **이미 시스템 볼륨/음소거를 바꾼다**.
- 키보드 원문 HID 바이트는 OS 독점으로 관측 불가. VKey/MakeCode로 역산했다.
- 상세 로그: `tools/phase0/logs/session_raw.log`, `session_cap.log`.

### 4.4 물리 구성

사용자 확인: **키 6개 + 노브 1개 + 각 키 LED**. 관측된 그룹도 키 6 + 노브 1(좌/우/클릭)과 일치한다. **[F]**

---

## 5. 완전한 프로토콜 명세

> 아래는 **[S]** 정적 분석으로 복원한 설정 프로토콜이다. 쓰기 동작은 아직 **[U]**다. 새 앱은 이 명세를 구현하되, Phase 1에서 단일 키로 검증한 뒤 전체에 적용해야 한다.

### 5.1 전송 형식

```
wire[0]    = Report ID (이 장치는 3)
wire[1..64]= D0..D63 (64바이트 데이터)
총 65바이트
```

- `MI_01` 출력 리포트는 65바이트(RID 포함). **[F]**
- 원본은 `HidLibrary.HidReport` 또는 자체 `FileStream.Write(array,0,65)`로 전송한다.
- Feature Report 없음(0B). **[F]**

### 5.2 명령 표

| 목적 | D0 | D1 | D2.. | 커밋 |
|---|---|---|---|---|
| 프로토콜 감지 | `00` | `00` | 0 | 없음 |
| 레이어 선택 | `A1` | layer(1~3) | — | 없음 |
| 키 — 키보드 | slot | `layer<<4 \| 1` | count, index, mod, usage | `AA AA` |
| 키 — 미디어 | slot | `layer<<4 \| 2` | media0, media1 | `AA AA` |
| 키 — 마우스 | slot | `layer<<4 \| 3` | buttons, dx, dy, wheel, mod | `AA AA` |
| LED 모드 | `B0` | `layer<<4 \| 8` | mode(0~2) | `AA A1` |
| 키 커밋 | `AA` | `AA` | — | — |
| LED 커밋 | `AA` | `A1` | — | — |

### 5.3 키보드 타입 (type 1)

```
D2 = 엔트리 수 N (1~5)
D3 = 서브리포트 인덱스 b (0..N)
D4 = 수정자 비트
D5 = HID keyboard usage (1바이트)
```

`b=0`은 **수정자만 누르는 단계**다. `D4 = 1번 엔트리의 수정자`, `D5 = 0`.
`b>=1`은 `D4 = b번 엔트리 수정자`, `D5 = b번 엔트리 usage`.

수정자 비트:

| 비트 | 값 | 키 |
|---|---:|---|
| 0 | `01` | Left Ctrl |
| 1 | `02` | Left Shift |
| 2 | `04` | Left Alt |
| 3 | `08` | Left Win |
| 4 | `10` | Right Ctrl |
| 5 | `20` | Right Shift |
| 6 | `40` | Right Alt |
| 7 | `80` | Right Win |

- 원본은 `b=0..N`을 항상 전송한다. 새 앱도 동일하게 재현하는 것이 안전하다.
- N개 엔트리가 **동시 입력(콤보)** 인지 **순차 입력(시퀀스)** 인지는 **[U]**. 2개 엔트리(A, B)를 써서 텍스트 필드에서 확인해야 한다.

### 5.4 미디어 타입 (type 2)

- 이 장치의 설정 RID는 3이므로, 원본 코드의 RID 3 분기를 따른다.
- `D2 = 8비트 컨슈머 usage`, `D3 = 0`.
- 사용 값: `E9` 볼륨+, `EA` 볼륨-, `E2` 음소거, `CD` 재생/일시정지, `B5` 다음, `B6` 이전.
- RID 0/2는 레거시 비트필드/다른 오프셋이며 이 장치의 MI_01에는 해당 없음. **[F]**

### 5.5 마우스 타입 (type 3)

```
D2 = buttons (1 좌, 2 우, 4 중)
D3 = dx   (int8)
D4 = dy   (int8)
D5 = wheel (1 / FF)
D6 = modifier (1 Ctrl, 2 Shift, 4 Alt)
```

### 5.6 LED 타입 (type 8)

```
D0 = B0
D1 = layer<<4 | 8
D2 = mode (0, 1, 2)
커밋 = AA A1
```

- 전역 모드 3종. **개별 키 LED/색/밝기 필드는 발견되지 않았다.** **[S]**
- 각 키에 LED가 있어도 벤더 앱은 전역 모드만 노출한다. 개별 제어는 **[U]**.

### 5.7 레이어/커밋

- 레이어 1~3.
- `A1 layer`를 키 서브리포트 전에 보내 편집 대상을 선택한다.
- D1 상위 니블에도 레이어를 넣는다.
- 키 커밋은 `AA AA`, LED 커밋은 `AA A1`.
- 원본은 레이어를 1로 복원하지 않는다. `A1`이 실제 타이핑 레이어까지 바꾸는지 **[U]**.

### 5.8 슬롯 지도

| 슬롯 | 실물 | 비고 |
|---|---|---|
| 1~6 | 키 1~6 | 현재 KEY1=A, KEY2~6=C |
| 13 | 노브 왼쪽 | 원본 `K1_Left` |
| 14 | 노브 클릭 | 원본 `K1_Centre` |
| 15 | 노브 오른쪽 | 원본 `K1_Right` |
| 16~18 | (노브2, 미탑재) | 원본 `K2_*` |
| `B0` | LED 모드 | 키 아님 |

**주의:** 원본 UI의 `KEY13`~`KEY16`도 슬롯 13~16을 쓴다. 6키+1노브 모델에서는 1~6, 13~15만 사용한다. 키 1~6 및 노브 방향-슬롯 대응은 원본 UI 라벨 기준 **[I]**이며, Phase 1 쓰기 시험으로 확인해야 한다.

### 5.9 쓰기 예시

**키1 → F13** (레이어 1):

```
RID 3 | 01 11 01 00 00 00    // slot1, layer1|type1, N=1, b=0, mod=0, usage=0
RID 3 | 01 11 01 01 00 68    // slot1, layer1|type1, N=1, b=1, mod=0, usage=F13
RID 3 | AA AA                // 커밋
```

**키2 → Ctrl+C**:

```
RID 3 | 02 11 01 00 01 00
RID 3 | 02 11 01 01 01 06    // usage 0x06 = C
RID 3 | AA AA
```

**노브 왼쪽(슬롯 13) → F19**:

```
RID 3 | 0D 11 01 00 00 00
RID 3 | 0D 11 01 01 00 70    // F19 = 0x70
RID 3 | AA AA
```

**LED 모드 1**:

```
RID 3 | B0 18 01 00 ...      // layer1<<4|8 = 0x18, mode=1
RID 3 | AA A1
```

**레이어 2의 키1 → F14**:

```
RID 3 | A1 02                // 편집 레이어 2
RID 3 | 01 21 01 00 00 00    // layer2|type1
RID 3 | 01 21 01 01 00 69    // F14
RID 3 | AA AA
```

### 5.10 키 코드

표준 USB HID Keyboard/Keypad usage page `0x07`. 원본 앱이 UI로 노출하는 범위:

| 그룹 | usage |
|---|---|
| A~Z | `04`~`1D` |
| 1~9, 0 | `1E`~`27` |
| Enter, Esc, Backspace, Tab, Space | `28`~`2C` |
| `- = [ ] \ ; ' ` , . /` | `2D`~`38` (일부 생략) |
| CapsLock | `39` |
| F1~F12 | `3A`~`45` |
| PrintScreen, ScrollLock, Pause | `46`~`48` |
| Insert, Home, PageUp, Delete, End, PageDown | `49`~`4E` |
| Right, Left, Down, Up | `4F`~`52` |
| NumLock, KP / * - + Enter, KP1~9, KP0, KP. | `53`~`63` |
| Menu | `65` |
| **F13~F24 (원본 UI 미노출, 유효)** | **`68`~`73`** |
| Left/Right Ctrl, Shift, Alt, GUI | `E0`~`E7` |

Shift 기호는 `Shift` 수정자 + 기본 usage로 표현한다: `~`=35, `!`=1E, `@`=1F, `#`=20, `$`=21, `%`=22, `^`=23, `&`=24, `*`=25, `(`=26, `)`=27, `_`=2D, `+`=2E, `{`=2F, `}`=30, `|`=31, `:`=33, `"`=34, `<`=36, `>`=37, `?`=38.

소비자 페이지 `0x0C`: `B5` 다음, `B6` 이전, `CD` 재생/일시정지, `E2` 음소거, `E9` 볼륨+, `EA` 볼륨-.

마우스: 버튼 `1/2/4`, X/Y/Wheel int8, 수정자 `1/2/4`.

> 전체 벤더 UI 키 목록(100개)은 `analysis/src/HIDTester/BasicKeys.cs`의 각 `KEY_*_Click`에서 기계적으로 추출할 수 있다. 본 보고서는 구현에 필요한 표준 usage로 정리했다.

### 5.11 Report ID 레거시

| RID | 레이어 | 미디어 | 이 장치 |
|---|---|---|---|
| 3 | 있음 | 8비트 usage (`D2`) | **해당** |
| 0 | 없음 | 비트필드 (`D2/D3`) | 없음 |
| 2 | 있음 | 다른 오프셋 (`D2/D3`) | 없음 |

새 앱은 descriptor에서 RID 3을 확인하고, 다른 펌웨어 호환용 폴백(3→0→2)은 옵션으로만 둔다.

---

## 6. 독립 대체 프로그램 아키텍처 (B 방식)

### 6.1 핵심 원리: 고유 트리거

현재 키 2~6이 모두 `C`이므로, PC 앱이 키를 구분할 수 없다. 또한 원래 타이핑/볼륨이 발생한다. 해결은 **장치 키 매핑을 고유·무해한 usage로 바꾸는 것**이다.

```
물리 키/노브
  → 장치 펌웨어가 고유 usage 발생 (F13~F18, F19~F21)
  → Raw Input이 장치 경로와 함께 감지
  → 새 앱의 액션 엔진이 사용자 정의 출력 실행
```

- **F13~F24**는 대부분의 앱에서 아무 동작도 하지 않고, 일반 키보드와 충돌할 확률이 낮다.
- 키 1~6 → F13~F18, 노브 좌/클릭/우 → F19/F20/F21.
- 레이어는 **PC 앱 내부 상태**로 처리한다. 장치의 3레이어를 쓰지 않아도 된다. 장치에는 항상 같은 F키가 나가고, 앱이 현재 프로필/레이어에 따라 액션을 바꾼다.
- 이 방식은 벤더 앱 없이 동작하며, 장치를 다른 PC에 옮겨도 같은 앱이 있으면 동일하게 동작한다.

**트레이드오프:** 새 앱이 없으면 F13~F18은 아무 동작도 하지 않는다. 필요하면 키 6을 "레이어 전환" 트리거로 쓰거나, 별도 "패스스루" 프로필을 제공한다.

### 6.2 입력 계층: Raw Input

- `RegisterRawInputDevices` + `RIDEV_INPUTSINK` + message-only 창.
- 등록 usage: `0001:0006`(키보드), `000C:0001`(컨슈머), `0001:0002`(마우스), `FF00:0001`(벤더, 선택).
- `WM_INPUT` → `GetRawInputData`:
  - `RAWKEYBOARD`: VKey, MakeCode, Flags(BREAK/E0/E1), Message.
  - `RAWHID`: Report ID 포함 원문.
  - `RAWMOUSE`: 버튼/이동/휠.
- `hDevice` → `RIDI_DEVICENAME` → `vid_1189&pid_8890` 및 `mi_XX` 필터.
- 다른 키보드/마우스 입력은 무시. 프라이버시상 기본 로그 없음.
- `RIDEV_NOLEGACY`는 사용하지 않는다. 다른 앱의 입력을 막지 않는다.

**왜 HID 직접 읽기가 아닌가:** MI_00/MI_02 COL01/MI_03은 `kbdhid`/`mouhid`가 독점하여 `err=5`. Raw Input만 키 입력을 볼 수 있다. **[F]**

### 6.3 출력 계층: 액션 엔진

| 액션 | 구현 |
|---|---|
| 키 시퀀스 | `SendInput` (scan code/usage, 수정자, down/up, 지연) |
| 텍스트 | `SendInput` `KEYEVENTF_UNICODE` (한글/유니코드) |
| 프로그램 실행 | `Process.Start` / `ShellExecute` (경로, 인자, 작업 디렉터리) |
| 셸 명령 | 명시적 사용자 설정 + 확인 |
| 미디어 | `SendInput` consumer usage 또는 `keybd_event` 대체 |
| 마우스 | `SendInput` mouse move/click/wheel |
| 레이어 전환 | 앱 내부 상태 변경 (장치 쓰기 없음) |
| LED | 장치 `B0` 모드 쓰기 (선택) |
| 지연/반복 | 액션 큐의 sleep/loop, 취소 토큰 |

- 액션은 **UI 스레드가 아닌 직렬화된 워커**에서 실행한다.
- Raw Input 콜백은 이벤트를 큐에 넣기만 한다.
- 자신이 주입한 키는 Raw Input에서 다른 장치 경로로 보이므로 재트리거되지 않는다. 추가로 재진입 가드를 둔다.
- 텍스트/키 시퀀스는 현재 포커스 창으로 전달된다. 필요하면 대상 창 활성화 액션을 제공한다.

### 6.4 장치 설정 계층

- HidSharp로 `MI_01` (Usage `FF00:0001`) 열기.
- 65바이트 버퍼, RID 3.
- 프로토콜 빌더: type 1/2/3/8, 레이어, 커밋.
- **자동 쓰기 금지.** 사용자가 "장치에 적용"을 눌렀을 때만.
- 쓰기는 직렬화하고, 각 쓰기 후 10~50ms 대기, 커밋 후 100ms 대기.
- Report ID는 descriptor에서 확인. 쓰기 프로브는 폴백으로만.
- 벤더 앱이 MI_01을 열고 있으면 실패할 수 있으므로, 감지 후 안내.

### 6.5 프로필/레이어

```json
{
  "schema": 1,
  "device": { "vid": "1189", "pid": "8890", "model": "6key-1knob" },
  "triggers": {
    "key1": "F13", "key2": "F14", "key3": "F15",
    "key4": "F16", "key5": "F17", "key6": "F18",
    "knob_left": "F19", "knob_click": "F20", "knob_right": "F21"
  },
  "layers": [
    {
      "name": "기본",
      "bindings": {
        "key1": { "type": "launch", "path": "C:\\Tools\\app.exe" },
        "key2": { "type": "text", "text": "hello" },
        "key3": { "type": "keys", "keys": ["CTRL", "C"] }
      }
    }
  ],
  "led": { "mode": 1 }
}
```

- 프로필은 PC에 JSON으로 저장. 장치 설정은 "마지막으로 적용한 트리거"만 기록.
- 레이어 전환: 특정 키의 액션을 `layer.next`로 지정하거나, 키 조합(예: key1+key6)으로 전환.
- 장치 읽기 백업은 불가하므로, 첫 적용 전 사용자에게 현재 동작을 확인시키고 "관측된 기본값" 프로필(A/C/볼륨)을 제공한다.

### 6.6 UI (.NET 8 + WPF)

```
[트레이 아이콘]  연결됨 · RID 3 · 프로필 "기본"
┌─ 메인 창 ─────────────────────────────┐
│  장치: MINI KeyBoard (6키 + 노브)      │
│  [장치 설정] [프로필] [진단] [정보]    │
│                                       │
│   KEY1   KEY2   KEY3                  │
│   KEY4   KEY5   KEY6   (노브)          │
│                                       │
│  선택: KEY1 → [프로그램 실행 ▾] ...    │
│  트리거: F13 (장치에 적용됨)           │
│  레이어: [기본] [업무] [게임]          │
│  LED: ( )Off (•)On ( )Breath           │
│  [장치에 적용] [테스트] [복구]         │
└───────────────────────────────────────┘
```

- 6키+1노브 레이아웃. 각 키/노브는 클릭 가능한 카드.
- 액션 편집기: 프로그램 실행, 키 시퀀스, 텍스트, 미디어, 마우스, 지연, 레이어.
- "테스트"는 현재 장치 트리거와 마지막 Raw Input 이벤트를 보여준다.
- 진단 탭: VID/PID, 컬렉션, RID, 접근성, 마지막 오류. 읽기 전용.
- 한국어 기본, 영어 지원.

### 6.7 프로세스/시작

- 단일 인스턴스(Mutex), 트레이 상주.
- message-only 창에서 Raw Input 수신. 포커스/백그라운드 무관.
- 자동 시작: 작업 스케줄러(로그온) 또는 `HKCU\...\Run`. 관리자 권한 불필요.
- 장치 분리/재연결: Raw Input 재등록, 상태 갱신.
- 설정 쓰기 중 장치 분리: 타임아웃/오류 표시, 부분 적용 주의.

### 6.8 데이터 흐름

```
[물리 입력]
   │
   ├─ 키 → MI_00 → Raw Input(RAWKEYBOARD) ─┐
   ├─ 노브 → MI_02 COL02 → Raw Input(RAWHID) ─┤
   └─ (선택) MI_03 → Raw Input(RAWMOUSE) ────┤
                                             ▼
                                   DeviceFilter (VID/PID/MI)
                                             │
                                             ▼
                                   TriggerRouter (F13~F21)
                                             │
                                             ▼
                                   Layer/Profile 선택
                                             │
                                             ▼
                                   ActionEngine (SendInput/Launch/...)
                                             │
                                             ▼
                                   [PC 출력]

[설정]
   UI → ConfigWriter → MI_01 (RID3, 65B) → 키 매핑/LED → AA AA / AA A1
```

### 6.9 실패/복구

| 상황 | 대응 |
|---|---|
| 장치 미연결 | 트레이 상태 표시, Raw Input 재등록 대기 |
| MI_01 열기 실패 | 벤더 앱/다른 인스턴스 확인 안내 |
| 쓰기 실패 | 오류 표시, 부분 적용 경고, 재시도 |
| 키가 죽음 | "관측된 기본값" 프로필(A/C) 또는 벤더 앱으로 복구 |
| 트리거가 안 옴 | 진단 탭에서 Raw Input/장치 경로 확인 |
| F13 미지원 | 저수준 훅 또는 다른 트리거 전략으로 폴백 (최후 수단) |
| 프로필 손상 | JSON 스키마 검증, 백업에서 복구 |

### 6.10 보안/프라이버시

- 장치 경로 필터를 통과한 이벤트만 처리. 일반 타이핑 미수집.
- 로그 기본 비활성. 디버그 시 사용자가 명시적으로 켬.
- 셸/프로그램 실행 액션은 사용자가 직접 설정한 것만. 외부 프로필 가져오기 시 경고.
- 네트워크 통신 없음. 자동 업데이트 없음(선택).
- 관리자 권한 불필요. 드라이버 설치 없음.

---

## 7. 운영 시나리오

### 7.1 첫 설치

1. 앱 실행 → 장치 감지(`VID_1189/PID_8890`, MI_01 RID 3).
2. "장치 설정" 마법사:
   - 현재 키 동작을 확인/기록 (A/C/볼륨).
   - "앱 전용 트리거로 설정" 선택 → 키 1~6=F13~F18, 노브=F19~F21, LED 모드 적용.
   - 각 키를 눌러 Raw Input에서 F13~F18이 오는지 확인.
   - 실패 시 "관측된 기본값"으로 복구.
3. 프로필 편집 → 각 트리거에 액션 지정.
4. 자동 시작 등록.

### 7.2 일상 사용

- 트레이 상주. 키 입력 → Raw Input → 프로필/레이어 → 액션 실행.
- 장치의 원래 타이핑/볼륨 동작은 발생하지 않음(F키로 재매핑된 경우).
- LED는 설정된 전역 모드 유지.

### 7.3 재설정

- 프로필 변경 → "장치에 적용"은 트리거가 바뀔 때만 필요.
- 액션만 바뀌면 장치 쓰기 없이 즉시 적용.
- 레이어는 PC 내부 상태이므로 장치 쓰기 불필요.

### 7.4 제거/복구

- "기본값 복구" → 키 1~6=A/C 등 관측된 기본값, 노브=볼륨/음소거, LED 모드 0.
- 앱 제거 후에도 장치는 마지막 트리거를 유지할 가능성이 높다(**[I]**). 제거 전 복구 권장.

---

## 8. Phase 1 검증 계획 (쓰기)

> Phase 0는 읽기 전용이었다. 아래는 **별도 승인 후** 수행한다. 각 단계는 복구 가능한 단일 슬롯부터 시작한다.

### 8.1 테스트 매트릭스

| # | 시험 | 쓰기 | 통과 기준 |
|---|---|---|---|
| 1 | 키1 → F13, 레이어1 | 2+1 패킷 | Raw Input VKey `0x7C`, 'A' 타이핑 없음 |
| 2 | 키1 복구 → A | 2+1 | VKey `0x41` 복귀 |
| 3 | 키1~6 → F13~F18 | 6×(2+1) | 6개 모두 고유 감지 |
| 4 | 노브 13~15 → F19~F21 | 3×(2+1) | 회전/클릭이 F19~F21, 볼륨 변화 없음 |
| 5 | LED 모드 0/1/2 | 1+1 | LED 변화 관찰 |
| 6 | 레이어 2/3 매핑 | A1+2+1 | 레이어별 동작 |
| 7 | 다중 엔트리(A,B) | 2+1 | 콤보/시퀀스 판별 |
| 8 | 지우기(type 0/빈) | 2+1 | 매핑 해제 여부 |
| 9 | 읽기 요청 | 쓰기 | MI_01 입력 응답 여부 |
| 10 | F13 미지원 시 | 2+1 | 대체 트리거 결정 |

### 8.2 안전 절차

1. 현재 동작을 기록(키1=A, 키2~6=C, 노브=볼륨/음소거).
2. **키1 하나만** F13으로 쓰고 커밋.
3. 키1을 눌러 Raw Input 확인. 실패하면 즉시 A로 복구.
4. 성공하면 키2~6, 노브 순으로 확장.
5. 각 단계 후 로그와 스크린샷을 남긴다.
6. 전체 완료 후 "관측된 기본값" 프로필을 저장한다.

### 8.3 롤백

- F13이 안 되면 `usage=0x04`(A)로 다시 쓰고 커밋.
- 키2~6은 `usage=0x06`(C)로 복구.
- 노브는 type 2로 `EA/E9/E2` 복구(현재 동작).
- LED는 mode 0으로.
- 그래도 실패하면 벤더 앱으로 수동 복구.

---

## 9. 위험과 미확인

| # | 항목 | 상태 | 영향/대응 |
|---|---|---|---|
| 1 | 쓰기 패킷 수용/커밋 | **[U]** | Phase 1 단일 키 시험 |
| 2 | 슬롯 1~6/13~15 대응 | **[I]** | 쓰기 후 물리 키로 확인 |
| 3 | F13~F24 수용 | **[U]** | 미지원 시 훅/대체 트리거 |
| 4 | 노브 재매핑 가능 | **[U]** | 불가 시 볼륨 부작용 허용 또는 훅 |
| 5 | 개별 LED 제어 | **[U]** | 전역 모드 3종만 보장 |
| 6 | 설정 읽기/백업 | **[U]** | 백업 불가 전제, 로컬 프로필 |
| 7 | 다중 엔트리 의미 | **[U]** | 콤보/시퀀스 시험 |
| 8 | 레이어 A1 의미 | **[U]** | 실제 레이어 변경 여부 시험 |
| 9 | 지우기 표현 | **[U]** | type 0/빈 페이로드 시험 |
| 10 | 원문 descriptor | **[U]** | 커널/관리자 필요 |
| 11 | 키 원문 HID 바이트 | **[U]** | Raw Input은 VKey/스캔만 |
| 12 | 장기 내구성(플래시) | **[U]** | 커밋 횟수 최소화 |

**Go/No-Go:** Phase 1의 1~3번이 통과하면 F13 기반 독립 앱을 진행한다. 3번이 실패하면 저수준 훅 또는 다른 트리거 전략을 재설계한다.

---

## 10. 부록

### A. 증거 파일

```
analysis/rea/artifact.json                 REA managed artifact evidence
analysis/rea/members.json                  types/methods/fields/call edges/strings
analysis/rea/native-boundaries.json        16 P/Invoke declarations
analysis/rea/pe-imports.json               PE imports (mscoree only)
analysis/rea/pe-resources.json             native resources
analysis/rea/hidlibrary-artifact.json      HidLibrary identity
analysis/src/                              ILSpy decompiled source (참고용, 커밋 금지)
tools/phase0/                              read-only HID probe + logs
PHASE0_*.md                                Phase 0 상세 보고서 (증거 부록)
```

### B. REA 명령

```powershell
# Node 24.22.3 portable
npx rea-agents@latest inspect-managed-artifact "MINI KeyBoard.exe" --json
npx rea-agents@latest inspect-managed-members "MINI KeyBoard.exe" --json
npx rea-agents@latest inspect-managed-native-boundaries "MINI KeyBoard.exe" --json
npx rea-agents@latest inspect-pe-imports "MINI KeyBoard.exe" --json
npx rea-agents@latest inspect-pe-resources "MINI KeyBoard.exe" --json
```

### C. ILSpy 명령

```powershell
ilspycmd -p -o analysis/src "MINI KeyBoard.exe"
ilspycmd -t HidLibrary.HidDevice "HidLibrary.dll"
```

### D. 실물 계측 명령

```powershell
tools\phase0\HidProbe.exe list
tools\phase0\HidProbe.exe opencheck
tools\phase0\HidProbe.exe capture 300 <log>
tools\phase0\HidProbe.exe raw 300 <log> quiet
```

### E. 핵심 수치

| 항목 | 값 |
|---|---|
| TypeDef / MethodDef / Field | 30 / 295 / 331 |
| P/Invoke | 16 |
| Call edges / field accesses | 3,371 / 3,039 |
| User strings | 388 |
| HID 컬렉션 | 5 |
| 설정 리포트 | RID 3, 65B out (Feature 0) |
| 입력 | 키 MI_00 RID1, 노브 MI_02 COL02 RID2 |
| 실측 입력 | A×3, C×15, Volume-×3, Volume+×2, Mute×2 |
| 물리 | 6키 + 1노브 + 키별 LED |

---

## 11. 최종 권고

1. **입력은 Raw Input 단일 경로**로 구현한다. HID 직접 읽기는 키보드에 쓸 수 없다.
2. **장치 트리거를 F13~F18/F19~F21로 재매핑**한다. 이것이 독립 앱의 성립 조건이다.
3. **레이어/프로필은 PC 앱 내부**에서 처리한다. 장치의 3레이어에 의존하지 않는다.
4. **LED는 전역 모드 3종**만 보장한다. 개별 LED는 Phase 1에서 탐색하되 기대하지 않는다.
5. **쓰기 전 복구 경로**를 반드시 제공한다. 읽기 백업이 불가능하므로 "관측된 기본값" 프로필을 내장한다.
6. **Phase 1의 단일 키 F13 시험**을 먼저 통과시킨 뒤 전체 구현에 들어간다.

**구현은 아직 시작하지 않았다.** 이 보고서 승인 후 Phase 1 쓰기 시험 → MVP → 전체 앱 순서로 진행한다.

---

*Tools used: REA 6.10.1 (managed static provider), ILSpy 9.1.0.7988, HidSharp 2.6.4, HidProbe (Raw Input/HID), PowerShell 7.6.6, Node.js 24.22.3.*
*건너뛴 것: 쓰기 기반 프로토콜 검증, 원문 Report Descriptor, 개별 LED/읽기 백업 확인. 위험: 잘못된 커밋은 키 매핑을 깨뜨릴 수 있고, F13 재매핑은 사용자의 현재 키 동작을 변경한다.*
