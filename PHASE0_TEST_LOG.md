# PHASE 0 — 테스트 로그

작성: 2026-11-23
원칙: **조사만 수행. 기존 EXE/DLL 수정 없음. 장치 설정 변경 없음. Output/Feature Report 전송 없음.**

## 1. 환경

| 항목 | 값 |
|---|---|
| OS | Windows 11 Pro Education 23H2, `10.0.22631.4602`, x64 |
| 세션 시각 | 2026-11-23 23:26 ~ 23:31 (KST) |
| 장치 | USB Composite, `VID_1189` / `PID_8890`, `bcdDevice=0000`, 주소 3 |
| 드라이버 | `kbdhid`(MI_00, MI_02 COL01), `mouhid`(MI_03), `HidUsb`(부모) |
| 분석 도구 | `tools/phase0/HidProbe.exe` — C# 5, .NET Framework 4.8 (csc 4.8.9232.0), x64 |
| 보조 도구 | HidSharp 2.6.4 (netstandard2.0) |
| 디컴파일 | ilspycmd 9.1.0.7988 (ICSharpCode.Decompiler 9.1.0.7988), .NET 8 런타임 |
| 셸 | PowerShell 7.6.6, Windows PowerShell 5.1.22621.4391 |

> `HidProbe.exe`의 `Environment.OSVersion`은 .NET Framework 호환 동작으로 `6.2.9200`을 출력했다. 실제 OS 버전은 CIM(`Win32_OperatingSystem`)으로 확인한 `10.0.22631`이다.

## 2. 읽기 전용 보장

- `HidProbe.cs`에는 `WriteFile`, `HidD_SetFeature`, `HidD_SetOutputReport`, `HidD_SetNumInputBuffers` 호출이 **없다.**
- 장치 핸들은 `CreateFileW(path, 0, ...)`(정보 조회) 또는 `CreateFileW(path, GENERIC_READ, ...)`(읽기)로만 열었다.
- 설정 프로토콜(65바이트 출력, `AA AA` 커밋)은 **전송하지 않았다.**
- 원본 `MINI KeyBoard.exe`는 실행하지 않았다.
- 입력 수집은 `VID_1189&PID_8890` 장치 이름과 일치하는 이벤트만 기록했다. 일반 키보드/마우스 입력은 기록하지 않았다.
- 수집된 내용은 테스트 중 누른 `A`, `C`, 볼륨-, 볼륨+, 음소거 이벤트뿐이다.

## 3. 수행한 테스트

### T1. 장치 열거 및 컬렉션 식별

```
tools\phase0\HidProbe.exe list
```

결과:
- 대상 컬렉션 5개 발견.
- `MI_00` Keyboard, `MI_01` Vendor, `MI_02 COL01` Keyboard, `MI_02 COL02` Consumer, `MI_03` Mouse.
- 각 컬렉션의 Usage / In / Out / Feature 길이 확인.
- Manufacturer/Product/Serial 문자열은 모두 비어 있음.

출력: `tools/phase0/logs/list.txt`

### T2. 읽기 접근성 확인

```
tools\phase0\HidProbe.exe opencheck
```

결과:

```
mi_00        READ OPEN FAIL err=5
mi_01        READ OPEN OK   (In=65 Out=65 Feat=0)
mi_02&col01  READ OPEN FAIL err=5
mi_02&col02  READ OPEN OK   (In=3 Out=0 Feat=0)
mi_03        READ OPEN FAIL err=5
```

`err=5` = `ERROR_ACCESS_DENIED`. Windows 키보드/마우스 드라이버가 해당 컬렉션을 독점한다.

출력: `tools/phase0/logs/opencheck.txt`

### T3. Report Descriptor 재구성

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\phase0\dump-descriptors.ps1
```

결과:
- HidSharp 2.6.4로 각 컬렉션의 report ID, report 길이, data item, usage, bit offset을 출력.
- MI_01: RID 3, 65B in/out, `FF00:0002`, 64×8bit.
- MI_00: RID 1, 9B in.
- MI_02 COL01: RID 1, 12B in.
- MI_02 COL02: RID 2, 3B in.
- MI_03: RID 없음, 5B in.

출력: `tools/phase0/logs/descriptors.txt`

### T4. 원문 Report Descriptor 접근 시도 (실패)

- 사용자 모드 `DeviceIoControl(IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0007)` → `ERROR_INVALID_FUNCTION(1)`.
  - 이 IOCTL은 커널 모드 전용임을 확인.
- 레지스트리 `HKLM\SYSTEM\CurrentControlSet\Enum\HID\...\Device Parameters\Report Descriptor` → `Requested registry access is not allowed`.
  - 현재 계정은 표준 사용자(관리자 그룹 deny-only)이며, 레지스트리 편집도 정책으로 비활성.
- 결론: **원문 바이트는 확보하지 못했다.** T3의 재구성 descriptor를 사용한다.

### T5. 물리 입력 캡처

실행:

```
HidProbe.exe capture 300 <temp>\session_cap.log
HidProbe.exe raw     300 <temp>\session_raw.log quiet
```

- `capture`: MI_01, MI_02 COL02를 직접 읽기. 키보드/마우스는 `err=5`로 제외.
- `raw`: Raw Input `RIDEV_INPUTSINK` 등록.
  - `0001:0006` Keyboard
  - `000C:0001` Consumer
  - `0001:0002` Mouse
  - `FF00:0001` Vendor (장치가 선언한 usage)
- 사용자가 메모장에 포커스를 두고 6개 키와 노브를 조작.
- 세션 종료 시 사용자가 "끝"을 알려 즉시 캡처를 중지했다(설정한 300초보다 이르게 종료). 마지막 이벤트는 종료 직전에 기록되었다.

결과 요약:

| 대상 | 결과 |
|---|---|
| MI_00 키보드 | VKey `A`, `C` 이벤트 수신 (6개 키 그룹) |
| MI_01 Vendor | 입력 0건 |
| MI_02 COL01 | 입력 0건 |
| MI_02 COL02 | `02 E9/EA/E2 00` + 릴리즈 수신 (노브) |
| MI_03 Mouse | 입력 0건 |

출력: `tools/phase0/logs/session_cap.log`, `session_raw.log`

### T6. 교차 검증

- MI_02 COL02 이벤트를 직접 읽기와 Raw Input이 동시에 수집.
- 동일 바이트, 동일 시각(±6ms) 확인.
- Raw Input의 `mi_00` 키보드 이벤트와 직접 읽기 불가(`err=5`)가 일치.

## 4. 실패/제약 기록

| 항목 | 결과 | 원인/대응 |
|---|---|---|
| 원문 descriptor | 실패 | 커널 전용 IOCTL + 레지스트리 ACL. 재구성 descriptor로 대체 |
| MI_00 직접 읽기 | 실패 `err=5` | `kbdhid` 독점. Raw Input 사용 |
| MI_02 COL01 직접 읽기 | 실패 `err=5` | `kbdhid` 독점 |
| MI_03 직접 읽기 | 실패 `err=5` | `mouhid` 독점 |
| 키 원문 HID 바이트 | 관측 불가 | Raw Input은 `RAWKEYBOARD`만 제공. VKey/스캔으로 역산 |
| 레이어/LED/노브2 | 이벤트 0건 | 실물에 없거나 하드웨어 전용. 사용자 확인 필요 |
| 노브 우/클릭 3회째 | 2회만 기록 | 사용자 조작 횟수 또는 세션 종료와 경합. 미확정 |

## 5. 사용자 확인이 필요한 항목

1. 실물 키 개수: 로그상 **6개**. 12개인가?
2. 노브 개수: 로그상 **1개**. 2개인가?
3. 레이어 전환 버튼 / LED 버튼이 실물에 있는가? 있다면 눌렀을 때 LED/동작이 바뀌는가?
4. 키 2~6이 모두 `C`로 설정된 것이 의도된 상태인가?

## 6. 재현 파일

```
tools/phase0/HidProbe.cs            분석 도구 소스 (읽기 전용)
tools/phase0/HidProbe.exe           컴파일된 도구
tools/phase0/dump-descriptors.ps1   descriptor 덤프
tools/phase0/logs/list.txt          T1 출력
tools/phase0/logs/opencheck.txt     T2 출력
tools/phase0/logs/descriptors.txt   T3 출력
tools/phase0/logs/session_cap.log   T5 직접 읽기 로그
tools/phase0/logs/session_raw.log   T5 Raw Input 로그
```

## 7. 하지 않은 것 (승인 후 Phase 1)

- Output Report 전송, 키 매핑 기록, `AA AA` 커밋
- 키 지우기/레이어 전환/LED 모드 시험
- 설정 읽기(GetReport/Feature) 시험
- F13~F24 재매핑 및 원복
- 다중 엔트리(콤보/시퀀스) 판별

**구현은 시작하지 않았다.** Phase 0 결과 보고 후 승인 대기.
