# Phase 1 — 물리 키 식별 재검토 보고서

작성: 2026-11-24
범위: 기존 분석 자료 재검토, 벤더 입력 처리 코드 재확인, 사용자 모드 슬롯 ID 채널 시험
**장치 설정 변경 없음.** 단, 벤더 앱이 연결 시 보내는 것과 동일한 **0으로 채운 RID 3 리포트 1회**를 진단 목적으로 전송했다(커밋 `AA AA` 없음). 키 매핑을 기록하는 명령은 보내지 않았다. 또한 **SYSTEM 권한 읽기 시험용 임시 예약 작업 1회**를 만들고 실행 후 즉시 삭제했다(장치 쓰기 없음).

---

## 0. 결론

1. **벤더 프로그램에는 슬롯 식별 로직이 없다.** 기본 통신 경로는 MI_01 입력을 아예 구독하지 않고, 레거시 경로만 입력을 읽어 ASCII로 변환한 뒤 버린다.
2. **사용자 모드에서 읽을 수 있는 모든 채널에서 슬롯 ID가 관찰되지 않았다.**
   - MI_01 인터럽트 입력: 74초 동안 0건.
   - MI_01 `GetInputReport`: `AA 00...` 고정, 키 입력 중 변화 없음.
   - MI_02 COL01 키보드: Raw Input 이벤트 0건.
   - MI_03 마우스: 0건.
   - MI_02 COL02 컨슈머: 노브 이벤트만, 슬롯 ID 없음.
3. **유일한 미확인 지점은 MI_00 키보드 원문 리포트의 예약/상수 바이트다.** Windows가 독점하여 **일반 사용자·관리자·SYSTEM 모두** 읽을 수 없다(모두 `err=5`). 이 바이트에 슬롯 ID가 숨겨져 있을 가능성은 배제할 수 없다. 확인하려면 커널 필터 드라이버 또는 USB 캡처 드라이버가 필요하다.
4. **만약 원문이 키마다 bit-identical이라면, 어떤 소프트웨어도 키를 구분할 수 없다.** 이는 정보 이론적 한계다. 해결책은 ① 키마다 고유 출력을 부여하거나 ② 펌웨어를 수정하거나 ③ 원문에 슬롯 ID가 있어 커널 드라이버로 읽는 것뿐이다.
5. **현재 요구사항(§2.1 F13~F24 금지 + §8.2 KEY 1~6 독립 식별)은 하드웨어가 슬롯 ID를 제공하지 않으면 동시에 만족할 수 없다.** 이 충돌을 먼저 해소해야 한다.

---

## 1. 프로젝트/분석 자료 인벤토리

| 위치 | 내용 | 상태 |
|---|---|---|
| `analysis/rea/artifact.json` | REA managed artifact evidence | 유효 |
| `analysis/rea/members.json` | 30 types, 295 methods, 3,371 call edges, 388 strings | 유효 |
| `analysis/rea/native-boundaries.json` | 16 P/Invoke | 유효 |
| `analysis/rea/pe-imports.json` | mscoree only | 유효 |
| `analysis/rea/pe-resources.json` | icon/manifest/version | 유효 |
| `analysis/src/` | ILSpy 9.1 소스 | 참고용 |
| `tools/phase0/` | 읽기 전용 HID 도구, Phase 0 로그 | 유효 |
| `PHASE0_*.md`, `REA_MACRO_KEYBOARD_REPORT.md` | 이전 보고서 | 유효 |

- 새 프로그램 코드는 아직 없다. UI/액션 엔진을 먼저 만들지 않는다는 원칙에 따라, 식별 문제가 해결되기 전에는 스캐폴딩하지 않았다.

---

## 2. 벤더 프로그램 입력 처리 재확인

### 2.1 기본 경로 (`WriteMode = 1`, HidLibrary)

`HIDTester.HidLib`의 전체 코드는 다음이 전부다.

```csharp
wDevice = HidDevices.Enumerate(4489).FirstOrDefault();
foreach (HidDevice item in HidDevices.Enumerate(4489).ToList())
{
    if (item.DevicePath.IndexOf("mi_01") != -1)
    {
        DeviceList.Add(item);
        wDevice = item;
        wDevice.OpenDevice();
        wDevice.MonitorDeviceEvents = true;
        Dev_Sta = true;
        return true;
    }
}
```

- `HidDevice.DataReceived` 구독이 **없다.**
- `MonitorDeviceEvents = true`는 연결/해제 이벤트만 켠다. 입력 리포트와 무관하다.
- 따라서 기본 경로에서는 MI_01 입력을 **읽지도 않는다.**

### 2.2 레거시 경로 (`WriteMode = 0`, 자체 Win32 래퍼)

`HID.Hid.OpenDevice`는 VID/PID가 맞는 첫 HID 경로를 열고 `BeginAsyncRead()`를 호출한다.

```csharp
readResult = hidDevice.BeginRead(array, 0, InputReportLength, ReadCompleted, array);
```

`ReadCompleted`는 Report ID를 제거하고 `report(reportID, reportBuff)`를 발생시킨다.

```csharp
byte[] array2 = new byte[array.Length - 1];
for (int i = 1; i < array.Length; i++) array2[i - 1] = array[i];
report e = new report(array[0], array2);
OnDataReceived(e);
BeginAsyncRead();
```

`FormMain`의 유일한 구독자:

```csharp
protected void myhid_DataReceived(object sender, report e)
{
    RecDataBuffer = e.reportBuff;
    new ASCIIEncoding().GetString(RecDataBuffer);
}
```

- `RecDataBuffer`는 **어디에서도 읽히지 않는다.**
- `new ASCIIEncoding().GetString(...)`의 반환값은 **버려진다.**
- 슬롯 번호, 비트맵, 키 인덱스, Report ID별 분기 등 **어떤 파싱도 없다.**
- `myHid.DeviceRemoved`는 상태 라벨만 바꾼다.

### 2.3 UI에도 "학습/테스트" 기능이 없다

- 메인 폼의 `SetText`/`SetFunText`는 **선택한 키의 설정 버퍼**를 표시한다. 실시간 입력 표시가 아니다.
- `KEY1`~`KEY16`, `K1_*`, `K2_*` 버튼은 모두 **설정 대상 선택**이다. 입력 감지 버튼이 아니다.
- `Download`는 선택한 슬롯을 장치에 기록한다. `Key_Clear`는 전송 버퍼만 비운다.
- 입력 이벤트로 UI를 갱신하는 코드는 없다.

### 2.4 결론

벤더 프로그램은 **쓰기 전용 설정 도구**다. 슬롯 ID를 읽거나 표시하는 기능이 없다. 따라서 "벤더 앱 내부에 슬롯 식별 로직이 있는가?"의 답은 **없다**이다.

---

## 3. HID 구조 재확인

| 컬렉션 | Usage | In | Out | Feature | RID | 읽기 |
|---|---|---:|---:|---:|---|---|
| MI_00 Keyboard | `0001:0006` | 9 | 2 | 0 | 1 | `GENERIC_READ` 실패 `err=5` |
| MI_01 Vendor | `FF00:0001` | 65 | 65 | 0 | 3 | 성공 |
| MI_02 COL01 Keyboard | `0001:0006` | 12 | 2 | 0 | 1 | `err=5` |
| MI_02 COL02 Consumer | `000C:0001` | 3 | 0 | 0 | 2 | 성공 |
| MI_03 Mouse | `0001:0002` | 5 | 0 | 0 | 없음 | `err=5` |

- MI_00 입력은 표준 6KRO 키보드 리포트(9B = RID 1 + modifier + reserved + 6 keys)로 보인다.
- MI_02 COL01은 12B(11B 데이터)로, 10KRO 계열 키보드 인터페이스로 추정된다. 현재 설정에서는 침묵.
- MI_01은 64B 벤더 데이터. 현재 설정에서는 침묵.
- MI_03은 표준 5B 마우스. 현재 설정에서는 침묵.

---

## 4. Phase 1/2a 실험 결과

### 4.1 Raw Input 캡처 (이전 세션)

- 6개 키: KEY1=`A`(VKey 0x41, MakeCode 0x1E), KEY2~6=`C`(VKey 0x43, MakeCode 0x2E).
- 노브: `02 EA 00`(볼륨-), `02 E9 00`(볼륨+), `02 E2 00`(음소거).
- MI_01, MI_02 COL01, MI_03: **0건**.
- 로그: `tools/phase0/logs/session_raw.log`.

### 4.2 `GetInputReport` 폴링

MI_01(RID 3)과 MI_02 COL02(RID 2)를 20ms 간격으로 폴링하며 키/노브를 조작했다.

```
29.2ms mi_01          AA 00 00 00 ... (65B)
29.2ms mi_02&col02    AA 00 00
(45초 동안 변화 없음)
```

- 키를 눌러도 `AA`가 유지됐다. **슬롯 ID/키 상태가 반영되지 않는다.**
- `AA`는 고정 상태/에코 버퍼로 보인다. 마지막 출력 리포트나 상태 코드일 가능성이 있으나, 이번 시험만으로 의미를 확정할 수 없다.

### 4.3 0-리포트 프로브 (벤더 앱의 연결 시 동작 재현)

벤더 앱의 `KeyBoardVersion_Check`가 연결 시 보내는 것과 동일한 65바이트 0 리포트(RID 3)를 **1회** 전송했다.

```
# WriteFile zero-report ok=True written=65
```

- 커밋(`AA AA`)은 보내지 않았다. 키 매핑을 기록하는 명령이 아니다.
- 전송 후 `GetInputReport`가 **응답 없이 블록**되어 프로세스를 종료했다. 장치가 GET_REPORT에 응답하지 않거나, 응답 대기 상태에 들어간 것으로 보인다.
- MI_01 인터럽트 입력에서도 응답이 관찰되지 않았다.
- **이 프로브는 읽기 채널을 활성화하지 못했다.** 장치 상태/설정은 변경되지 않았을 것으로 판단되나, 물리 키 동작 재확인은 후속 세션에서 수행한다.

### 4.4 접근성 매트릭스

| 접근 | MI_00 | MI_01 | MI_02 COL01 | MI_02 COL02 | MI_03 |
|---|---|---|---|---|---|
| `CreateFile(access=0, share=READ)` | 성공 | 실패(32) | 성공 | 실패(32) | 성공 |
| `CreateFile(GENERIC_READ, share=3)` | **실패(5)** | 성공 | **실패(5)** | 성공 | **실패(5)** |
| `HidD_GetInputReport` | 실패(1) | 성공(`AA...`) | 실패(1) | 성공(`AA 00 00`) | 실패(1) |
| Raw Input | 성공(키) | 0건 | 0건 | 성공(노브) | 0건 |

- `err=5`는 접근 거부. `err=1`은 해당 컬렉션이 GET_REPORT를 지원하지 않음.
- MI_00/MI_02 COL01/MI_03은 **인터럽트 입력 스트림만** 원문을 가지며, 그 스트림은 OS가 독점한다.

### 4.5 관리자/SYSTEM 권한 읽기 시험 (실패)

관리자 권한으로 `readopen`을 실행한 결과:

```
user=GALAXYBOOK2PROS\user elevated-admin=True
===== mi_00
  access=80000000 share=0..3 FAIL err=5
```

SYSTEM 권한으로 임시 예약 작업을 만들어 `readraw`를 실행한 결과:

```
# SYSTEM raw read started 2026-11-24T00:33:40
# OPEN FAIL mi_00 err=5
# OPEN FAIL mi_02&col01 err=5
# CAPTURE START ...
# CAPTURE END ...
```

- **일반 사용자, 관리자, SYSTEM 모두 MI_00/MI_02 COL01의 `GENERIC_READ`가 거부됐다.**
- 이는 단순 ACL 문제가 아니라 HID 클래스 드라이버/kbdhid가 키보드 원문 스트림을 보호하는 구조다.
- 예약 작업은 실행 후 즉시 삭제됐다. 장치 설정은 변경되지 않았다.
- **사용자 모드에서 원문을 읽는 경로는 없다.** 커널 필터 드라이버 또는 USB 캡처 드라이버만 남았다.
- 상세 원인 분리(코드/인자, 경로, 권한, 관리자/SYSTEM, CMD, HIDClass 공식 문서)는 **`PHASE1B_ACCESS_DENIED_ANALYSIS.md`** 참조.

---

## 5. 이 결과가 의미하는 것

### 5.1 확인된 사실

- 슬롯 ID를 **사용자 모드에서 읽을 수 있는 채널은 없다.**
- 벤더 앱은 슬롯 ID를 **사용하지 않는다.**
- 키 입력은 MI_00의 표준 키보드 리포트로만 도착한다.
- 현재 KEY1=A, KEY2~6=C는 장치 펌웨어가 **저장된 매핑을 직접 실행**한 결과로 보인다. 즉, 장치는 "슬롯 이벤트"가 아니라 "매핑된 HID 출력"을 보낸다. **[I]**

### 5.2 남은 가능성

**MI_00 원문 9바이트의 예약/상수 바이트에 슬롯 ID가 숨겨져 있을 가능성.**

- 표준 6KRO 리포트는 `[RID][modifier][reserved][key1..key6]` 9바이트다.
- `reserved`는 Windows가 무시한다. 펌웨어가 여기에 슬롯 번호를 넣어도 OS/일반 앱은 보지 못한다.
- Raw Input은 `RAWKEYBOARD`(VKey/MakeCode)만 주고 원문 바이트를 주지 않는다.
- `HidD_GetInputReport`는 MI_00에서 `ERROR_INVALID_FUNCTION`으로 실패한다.
- 따라서 **커널 수준 캡처 또는 필터 드라이버 없이는 확인할 수 없다.**

### 5.3 정보 이론적 한계

두 물리 키가 **모든 인터페이스에서 bit-identical 리포트**를 낸다면, 어떤 사용자 모드 프로그램도 둘을 구분할 수 없다. 이는 소프트웨어 설계의 문제가 아니라 데이터에 차이가 없기 때문이다.

- 현재 KEY2~6은 모두 `C`(modifier 0, usage 0x06)로 관측되었다.
- 원문 예약 바이트가 동일하다면, KEY2와 KEY3는 **구분 불가**다.
- 이 경우 남은 해결책은 ① 키마다 고유 출력 부여 ② 펌웨어 수정 ③ 원문 슬롯 ID가 있을 때 커널 드라이버로 읽기뿐이다.

---

## 6. Phase 2 결정적 시험 계획

우선순위 순.

### 6.1 (시도 완료 — 실패) 관리자/SYSTEM 권한으로 MI_00 원문 읽기

- `CreateFile(MI_00, GENERIC_READ, share=3)`를 관리자 권한으로 시도 → `err=5`.
- SYSTEM 권한 임시 예약 작업으로 시도 → `err=5`.
- **성공하지 못했다.** 사용자 모드(관리자/SYSTEM 포함)에서 MI_00 원문을 읽는 경로는 없다.
- 따라서 6.2가 유일한 다음 단계다.

### 6.2 (1순위, 남은 결정적 시험) USBPcap/Wireshark 원문 USB 캡처

- USBPcap 필터 드라이버 설치(관리자) → Wireshark 또는 `USBPcapCMD`로 캡처.
- KEY1~6을 하나씩 누르며 MI_00 interrupt IN(EP 0x81)과 MI_01(EP 0x83?)의 원문을 비교.
- 예약 바이트/추가 바이트/Report ID 차이를 확인.
- 캡처 파일과 디코딩 결과를 증거로 보관.
- 장치 설정 변경 없음. 드라이버 설치만 필요.
- **이 시험 없이는 슬롯 ID 유무를 확정할 수 없다.**

### 6.3 (3순위) 설정 쓰기 차등 시험

- **선행 조건:** 6.1/6.2로 슬롯 ID가 없음을 확인하거나, 최소한 복구 프로필을 확보.
- 벤더 앱으로 KEY1을 A로 **동일 값 재기록** → 키 동작 유지 확인(슬롯 매핑 검증).
- KEY1을 B로 변경 → KEY1이 B를 내는지 확인 → A로 복구.
- KEY2~6도 동일하게 검증.
- 이 시험은 **쓰기**이므로, 관측된 현재 동작(A/C/볼륨)을 복구 프로필로 기록한 뒤 수행한다.
- 정확한 설정 백업(전체 슬롯 덤프)은 읽기 명령이 없어 불가능하다. **행동 수준 백업**만 가능하다.

### 6.4 (4순위) 벤더 앱을 이용한 프로토콜 차등

- 벤더 앱으로 KEY1을 Media Play로 설정 → 입력이 MI_02 COL02로 가는지 확인.
- KEY1을 Mouse Left로 설정 → MI_03으로 가는지 확인.
- 이를 통해 "타입별 인터페이스 라우팅"과 "슬롯 ID 채널"을 분리해 확인.
- 쓰기 필요. 6.3과 함께 수행.

---

## 7. 의사결정이 필요한 지점

### A. 슬롯 ID가 원문에 존재하는 경우 (6.1/6.2에서 확인)

- **커널 HID 필터 드라이버**를 만들어 원문 리포트의 슬롯 바이트를 사용자 모드 앱에 전달한다.
- 이 방식은 F13~F24를 쓰지 않고, 사용자에게 직접 매핑을 제공한다.
- 대가: 드라이버 개발/서명/설치, Secure Boot 정책, 유지보수 부담.
- 사용자 모드만으로는 원문을 읽을 수 없으므로, 이 경우에도 드라이버는 필수다. **대안:** Raw Input으로 원문을 얻을 수 있는 다른 API가 있는지 추가 조사.

### B. 슬롯 ID가 없는 경우 (가장 가능성 높음)

- **요구사항 §2.1(F13 금지)과 §8.2(독립 식별)는 동시에 만족 불가.**
- 선택지:
  1. **키마다 고유 출력 부여** — 사용자 경험은 직접 매핑이지만, 내부적으로는 고유 HID usage를 쓴다. F13~F24를 피하고, 예를 들어 **사용되지 않는 벤더/컨슈머 usage**를 쓸 수 있는지 시험. 그래도 "임시 키" 금지 정신에는 어긋난다.
  2. **펌웨어 수정** — 슬롯 ID를 포함한 벤더 리포트를 추가. 난이도/위험 매우 높음. 펌웨어 업데이트 인터페이스가 노출되어 있지 않다.
  3. **요구사항 완화** — "동일 출력 키는 구분 불가"를 인정하고, 현재 고유 출력이 있는 KEY1(A)과 노브만 직접 매핑. KEY2~6은 사용자가 각각 다른 출력으로 재설정해야 함.
- 권고: **1번**이 제품화 가능성이 가장 높다. 다만 사용자의 §2.1 금지 조항을 명시적으로 완화해야 한다. "F13~F24를 사용자에게 보이는 트리거로 쓰지 않는다"는 의미라면, 내부 고유 출력은 허용될 수 있다. 이 해석을 확인해야 한다.

### C. 백업 문제

- 전체 설정 읽기 명령이 없다. 현재는 **행동 수준 백업**(관측된 A/C/볼륨 복구 프로필)만 가능하다.
- §7.5("기존 설정을 백업하지 않고 덮어쓰지 않는다")를 엄격히 적용하면, 읽기 명령을 찾기 전까지 **쓰기 시험 자체가 금지**된다.
- 읽기 명령 탐색은 프로토콜 추측(위험) 또는 펌웨어 분석이 필요하다. 벤더 앱에는 읽기 명령이 없다.
- 권고: 쓰기 시험 전에 사용자가 "행동 수준 백업"을 승인하거나, 6.1/6.2로 읽기 채널을 먼저 확보한다.

---

## 8. 다음 단계

1. ~~6.1 관리자/SYSTEM MI_00 읽기~~ → **실패 (err=5)**
2. **6.2 USBPcap 원문 캡처**를 수행한다. (드라이버 설치 승인 필요)
3. 캡처로 슬롯 ID 유무를 확정한다.
4. 슬롯 ID가 있으면 필터 드라이버 아키텍처를 설계한다.
5. 없으면 사용자와 요구사항 충돌을 해소한 뒤, 고유 출력/펌웨어/범위 축소 중 하나를 선택한다.
6. 그 전까지 UI/액션 엔진 구현은 시작하지 않는다. (원칙 §7.6)

---

## 9. 변경/생성 파일

- `tools/phase0/HidProbe.cs` — `inputprobe`, `pollinput`, `probe`, `readopen`, `readraw` 모드 추가 (진단용)
- `tools/phase0/HidProbe.exe` — 재컴파일
- `PHASE1_INPUT_IDENTIFICATION.md` — 본 보고서
- `tools/phase0/logs/session_*.log` — 기존 입력 로그
- `C:\Users\Public\phase0_raw.log` — SYSTEM 읽기 시험 로그
- `C:\Users\Public\phase0_task.txt` — 임시 예약 작업 생성/실행/삭제 로그
- `C:\Users\Public\HidProbe.exe`, `phase0_*.cmd` — SYSTEM 시험용 복사본/래퍼
- 새 로그: `%TEMP%\opencode\phase0\poll_keys.log`, `probe_zero.log`
- **예약 작업 `Phase0Raw`는 실행 후 삭제됨. 장치 설정 변경 없음.**

---

## 10. 요약

- **벤더 앱은 슬롯 ID를 읽지 않는다.** 입력 코드는 장식이다.
- **현재 사용자 모드 채널에는 슬롯 ID가 없다.**
- **관리자·SYSTEM 권한으로도 MI_00 원문 읽기 실패(err=5).** 동일 바이너리/인자, CMD 미사용, SYSTEM `S-1-5-18`로 확인. 상세: `PHASE1B_ACCESS_DENIED_ANALYSIS.md`.
- **MI_00 원문 예약 바이트만 미확인.** USB 캡처(USBPcap/Wireshark)로만 확인 가능하다.
- **슬롯 ID가 없으면, F13 금지 + 독립 식별은 동시에 불가능하다.** 요구사항 충돌을 먼저 해소해야 한다.
- 다음 결정: USBPcap/Wireshark 설치 승인, 그리고 (필요 시) 쓰기 시험 전 백업 수준 승인.
