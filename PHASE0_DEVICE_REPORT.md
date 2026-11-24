# PHASE 0 — 장치 / USB HID 구조 보고서

작성: 2026-11-23
범위: **읽기 전용 분석**. Output/Feature Report 전송 없음. 장치 설정 변경 없음.

## 0. 분석 환경과 도구

| 항목 | 값 |
|---|---|
| OS | Windows 11 Pro Education 23H2, 10.0.22631.4602, x64 |
| 분석 도구 | `tools/phase0/HidProbe.exe` (C# / .NET Framework 4.0, x64, 읽기 전용) |
| 보조 도구 | HidSharp 2.6.4 (Report Descriptor 재구성), PowerShell 7, ilspycmd 9.1 |
| 장치 | USB Composite Device, `VID_1189` / `PID_8890`, `bcdDevice=0000`, USB 주소 3 |
| 제조사/제품/시리얼 문자열 | **모두 비어 있음** (`HidD_GetManufacturerString`/`ProductString`/`SerialNumberString` 실패 또는 빈 값) |

재현 명령:

```
tools\phase0\HidProbe.exe list
tools\phase0\HidProbe.exe opencheck
powershell -File tools\phase0\dump-descriptors.ps1
```

## 1. USB 토폴로지

정적 분석에서는 `mi_00`(키보드)과 `mi_01`(설정)만 가정했으나, 실제 장치는 **USB 인터페이스 4개, HID 컬렉션 5개**로 구성된 복합 장치다.

```
USB Composite Device  VID_1189&PID_8890  (주소 3)
├─ MI_00 ─ HID Keyboard        0001:0006  RID 1   In 9   Out 2
├─ MI_01 ─ Vendor-defined       FF00:0001  RID 3   In 65  Out 65
├─ MI_02 ─ HID Keyboard        0001:0006  RID 1   In 12  Out 2   (COL01)
│        └ Consumer Control   000C:0001  RID 2   In 3   Out 0   (COL02)
└─ MI_03 ─ HID Mouse           0001:0002  no RID In 5   Out 0
```

Windows PnP 매핑:

| 인터페이스 | PnP 장치 | 드라이버 |
|---|---|---|
| MI_00 | HID 키보드 장치 | `kbdhid` |
| MI_01 | HID 규격 공급업체 정의 장치 | (없음, 응용 프로그램이 직접 접근) |
| MI_02 & COL01 | HID 키보드 장치 | `kbdhid` |
| MI_02 & COL02 | HID 규격 사용자 컨트롤 장치 | (소비자 제어) |
| MI_03 | HID 규격 마우스 | `mouhid` |

하드웨어 ID 예:
- `HID\VID_1189&PID_8890&REV_0000&MI_01`
- `HID\VID_1189&UP:FF00_U:0001` (MI_01, Usage Page FF00 / Usage 0001)
- `HID\VID_1189&UP:000C_U:0001` (MI_02 COL02)

## 2. HID 컬렉션별 상세

| 컬렉션 | Usage Page:Usage | Input | Output | Feature | Report ID | 읽기 열기 |
|---|---|---:|---:|---:|---|---|
| MI_00 Keyboard | `0001:0006` | 9 | 2 | 0 | 1 | **거부 (err=5)** |
| MI_01 Vendor | `FF00:0001` | 65 | 65 | 0 | 3 | **성공** |
| MI_02 COL01 Keyboard | `0001:0006` | 12 | 2 | 0 | 1 | **거부 (err=5)** |
| MI_02 COL02 Consumer | `000C:0001` | 3 | 0 | 0 | 2 | **성공** |
| MI_03 Mouse | `0001:0002` | 5 | 0 | 0 | 없음 | **거부 (err=5)** |

- `In/Out/Feature`는 Windows `HIDP_CAPS`의 `*ReportByteLength`(Report ID 포함) 값이다.
- **MI_01만이 설정 프로토콜의 대상**이며, 이 장치의 descriptor에는 **Report ID 3 하나만** 존재한다. 정적 분석의 RID 0 / RID 2 후보는 이 펌웨어의 MI_01에는 없다.
- MI_02 COL02의 Report ID 2는 **컨슈머 컨트롤 리포트**이지 설정 프로토콜의 RID 2가 아니다. 정적 분석에서 본 `RID 2` 분기는 다른(구형) 장치/펌웨어를 위한 것일 가능성이 높다.
- MI_01은 출력 65바이트뿐 아니라 **입력 65바이트**도 가진다. 정적 분석에서 확인하지 못한 입력 채널이며, 이번 Phase 0에서는 **쓰기를 하지 않으므로** 응답/읽기 프로토콜은 미확인으로 남긴다.

### 읽기 접근성

- Windows의 `kbdhid`/`mouhid`가 MI_00, MI_02 COL01, MI_03을 독점하므로 `CreateFile(GENERIC_READ)`가 `ERROR_ACCESS_DENIED(5)`로 실패한다.
- MI_01(벤더)과 MI_02 COL02(컨슈머)는 읽기 열기가 가능하다.
- 따라서 **키보드/마우스 물리 입력 감지는 Raw Input(WM_INPUT)이 필요**하다. 이는 4장(B 방식 검토)의 핵심 근거다.

## 3. Report Descriptor

### 원문 접근에 대한 한계 (중요)

- 사용자 모드에서 `IOCTL_HID_GET_REPORT_DESCRIPTOR`를 호출하면 `ERROR_INVALID_FUNCTION(1)`이 반환된다. 이 IOCTL은 커널 모드 전용이다.
- 레지스트리 `HKLM\SYSTEM\CurrentControlSet\Enum\HID\...\Device Parameters\Report Descriptor`는 현재 계정(표준 사용자, 관리자 그룹 deny-only)에서 ACL로 거부된다.
- 따라서 **원문 바이트는 확보하지 못했다.** 아래는 Windows HID API(`HidP_GetCaps`/value caps)를 HidSharp 2.6.4가 재구성한 descriptor다. Report ID, 길이, Usage Page 수준은 신뢰할 수 있으나, **array 형식 항목(키 배열, 컨슈머 usage 배열)은 `Constant`로 잘못 재구성되는 한계**가 확인되었다. 실제 바이트 해석은 4장 입력 캡처로 보완한다.

### 재구성 결과

**MI_00 Keyboard — RID 1, Input 9B / Output 2B**

```
Input  RID=1: off 0, 8 bits, count 8, Variable   → 0007:00E0..00E7 (LeftCtrl..RightGUI)
             off 8, 56 bits, count 56, Constant  → (재구성 왜곡: 실제 키 배열로 추정)
Output RID=1: off 0, 3 bits  → 0008:0001..0003 (NumLock/CapsLock/ScrollLock)
             off 3, 5 bits  → Constant
```

**MI_02 COL01 Keyboard — RID 1, Input 12B / Output 2B**

```
Input  RID=1: off 0, 8 bits, count 8, Variable   → 0007:00E0..00E7
             off 8, 80 bits, count 80, Constant  → (재구성 왜곡: 실제 키 배열로 추정)
Output RID=1: off 0, 5 bits  → 0008:0001..0005
             off 5, 3 bits  → Constant
```

**MI_02 COL02 Consumer — RID 2, Input 3B**

```
Input  RID=2: off 0, 16 bits, count 16, Constant → (재구성 왜곡: 실제 컨슈머 usage 배열로 추정)
```

**MI_01 Vendor — RID 3, Input 65B / Output 65B**

```
Input  RID=3: off 0, 512 bits, 64 x 8-bit, Variable, Relative → FF00:0002
Output RID=3: off 0, 512 bits, 64 x 8-bit, Variable, Relative → FF00:0002
```

**MI_03 Mouse — RID 없음, Input 5B**

```
Input: off 0, 3 bits → 0009:0001..0003 (좌/우/중)
       off 3, 5 bits → Constant
       off 8,  8 bits → 0001:0030 (X, -127..127)
       off 16, 8 bits → 0001:0031 (Y, -127..127)
       off 24, 8 bits → 0001:0038 (Wheel, -127..127)
```

## 4. 정적 분석 결과와의 대조

| 항목 | 정적 분석(이전) | 실물 확인(Phase 0) | 판정 |
|---|---|---|---|
| VID/PID | 1189 / 8890 | 동일 | 확인 |
| 설정 인터페이스 | `mi_01` | `MI_01`, Usage `FF00:0001` | 확인 |
| 출력 리포트 | 65B, Report ID 1 + 64 | 65B, **RID 3 하나** | 일부 수정 |
| RID 후보 | 3 / 0 / 2 | 이 장치는 **3만 존재** | 수정 |
| 입력 리포트 | 90B 버퍼, 내용 미사용 | MI_01에 **65B 입력 존재** | 신규 |
| 인터페이스 수 | 2개(mi_00, mi_01) | **4개(MI_00~MI_03), 컬렉션 5개** | 수정 |
| 키보드 인터페이스 | mi_00 하나 | MI_00 + MI_02 COL01 두 개 | 수정 |
| 컨슈머/마우스 | 없음(정적 분석 범위 밖) | MI_02 COL02(컨슈머), MI_03(마우스) | 신규 |
| 장치 문자열 | 미확인 | 제조사/제품/시리얼 없음 | 신규 |
| 설정 읽기 | 불가(추정) | MI_01 입력 채널 존재, 읽기 명령은 미확인 | 미확인 |

## 5. 이 보고서의 결론

1. 설정 프로토콜의 물리 경로는 확정되었다: **`MI_01` / Usage `FF00:0001` / Report ID 3 / 65바이트 출력**.
2. 정적 분석의 RID 0·2 분기는 이 장치에 존재하지 않는다. 새 프로그램은 RID 3만 대상으로 하면 된다. (다른 펌웨어 호환을 위해 감지 로직은 남겨둘 수 있다.)
3. 키보드·마우스 컬렉션은 OS가 독점하므로 **직접 읽기로는 물리 입력을 관찰할 수 없다.** Raw Input이 필수다.
4. MI_01의 65바이트 입력 채널은 존재하지만, 쓰기 없이 확인할 수 있는 것은 "입력이 오는지"뿐이다. 설정 읽기/응답 프로토콜은 후속 쓰기 시험 계획에서 다룬다.
5. 원문 Report Descriptor는 사용자 모드에서 확보 불가. 재구성 descriptor와 실제 입력 바이트를 함께 근거로 사용해야 한다.

## 6. 미확인 / 후속

- MI_01 입력 채널의 의미(설정 응답? 키 이벤트? 주기적 상태?)
- Feature Report 없음(길이 0) 확인 — 읽기 전용이라 설정 읽기 불가
- 키 슬롯 ↔ 실제 키/노브 대응 (4장 입력 분석에서 확인)
- 원문 descriptor 바이트 (커널 필터 드라이버 또는 USB 허브 IOCTL + 관리자 권한 필요)
