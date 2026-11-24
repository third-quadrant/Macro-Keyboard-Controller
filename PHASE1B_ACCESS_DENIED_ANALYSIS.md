# Phase 1B — MI_00 `err=5` 원인 분리 분석

작성: 2026-11-24
목적: MI_00 키보드 원문 읽기 실패(`ERROR_ACCESS_DENIED`, 5)가 **CMD 실행 제한/테스트 환경** 때문인지, **Windows HIDClass의 키보드 컬렉션 보호** 때문인지 구분한다.
범위: 읽기 전용. 장치 설정 변경 없음. USBPcap/커널 드라이버 설치 없음.

---

## 0. 결론

**원인은 CMD/환경이 아니다. Windows HIDClass가 시스템 키보드 컬렉션(MI_00, Usage `0001:0006`)을 독점 보호하기 때문이다.**

근거 요약:

1. **동일 바이너리·동일 인자**로 PowerShell 직접 실행, CMD 실행, 관리자 PowerShell, SYSTEM 예약 작업을 모두 수행했다. 결과는 모두 `err=5`로 동일했다.
2. **CMD 정책은 이미 해제 상태**였다(`DisableCMD=0`). `cmd /c echo`도 정상 동작했다. 실패는 CMD를 거치지 않는 PowerShell 직접 실행에서도 동일하게 발생했다.
3. **경로는 정확히 MI_00**이다. `VID=1189`, `PID=8890`, `Usage=0001:0006`, `InputReportByteLength=9`를 확인했다. `\kbd` 접미사를 제거한 경로도 동일하게 실패했다.
4. **접근 권한을 분리하면** `FILE_READ_ATTRIBUTES(0x80)`는 성공하고, `FILE_READ_DATA(0x1)`/`GENERIC_READ(0x80000000)`만 `err=5`로 실패한다. 이는 단순한 공유 충돌(32)이 아니라 **읽기 데이터 권한에 대한 접근 거부**다.
5. **대조군:** 같은 코드/같은 `CreateFile` 인자로 MI_01(벤더 컬렉션)과 MI_02 COL02(컨슈머)는 `GENERIC_READ`로 정상 오픈된다. 따라서 코드/인자/경로 형식은 정상이며, 차단은 **키보드/마우스 TLC에 특정**된다.
6. Microsoft 문서와 HID FAQ가 이 동작을 명시한다: **시스템은 모든 키보드/마우스 컬렉션을 독점 사용**하며, 사용자 모드 앱은 Raw Input으로 입력을 받아야 한다.

**따라서 권한을 더 높이거나 CMD를 우회해도 MI_00 원문은 읽을 수 없다.** 남은 방법은 커널 필터 드라이버 또는 USB 캡처 드라이버뿐이다(별도 승인 필요).

---

## 1. 실제 장치 접근 코드와 `CreateFile` 인자

### 1.1 코드

`tools/phase0/HidProbe.cs`:

```csharp
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern IntPtr CreateFileW(
    string name, uint access, uint share, IntPtr sec,
    uint disp, uint flags, IntPtr tmpl);

// ...
IntPtr h = Native.CreateFileW(
    path,                                   // SetupDi가 반환한 장치 인터페이스 경로
    access,                                 // 0 / 0x80 / 0x1 / 0x80000000
    share,                                  // 0 / 1 / 2 / 3
    IntPtr.Zero,                            // SECURITY_ATTRIBUTES = NULL
    Native.OPEN_EXISTING,                   // 3
    0,                                      // flags = 0 (overlapped 아님)
    IntPtr.Zero);                           // template = NULL
```

런타임 로그에도 동일 인자를 출력한다.

```
CreateFile arguments: (path, access, share=READ|WRITE, NULL, OPEN_EXISTING, 0, NULL)
```

- `access` 값: `0`, `FILE_READ_ATTRIBUTES(0x80)`, `FILE_READ_DATA(0x1)`, `GENERIC_READ(0x80000000)`.
- `share` 값: `0`, `FILE_SHARE_READ(1)`, `FILE_SHARE_WRITE(2)`, `READ|WRITE(3)`.
- `SECURITY_ATTRIBUTES` 없음, `OPEN_EXISTING`, `FILE_FLAG_OVERLAPPED` 없음.
- **`GENERIC_READ`는 `FILE_READ_DATA`를 포함한다.** 실패는 `GENERIC_READ`뿐 아니라 `FILE_READ_DATA` 단독에서도 재현됐다.

### 1.2 코드가 하는 일

1. `SetupDiGetClassDevs`/`SetupDiEnumDeviceInterfaces`/`SetupDiGetDeviceInterfaceDetail`로 HID 인터페이스 경로를 얻는다.
2. `HidD_GetAttributes`로 VID/PID를 확인한다.
3. `HidD_GetPreparsedData`/`HidP_GetCaps`로 Usage/리포트 길이를 확인한다.
4. 위 `CreateFileW`로 접근 권한별 오픈을 시도한다.
5. 성공한 경우에만 `HidD_GetAttributes`를 다시 호출해 동일 장치인지 확인한다.
6. **`WriteFile`/`HidD_SetFeature`/`HidD_SetOutputReport`는 이 경로에서 호출하지 않는다.**

---

## 2. 경로가 정확히 MI_00인지

표준 사용자 PowerShell 직접 실행 로그:

```
===== mi_00 vid=1189 pid=8890 usage=0001:0006 In=9
  path[0] = \\?\hid#vid_1189&pid_8890&mi_00#7&a182129&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}\kbd
  path[1] = \\?\hid#vid_1189&pid_8890&mi_00#7&a182129&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}
```

- `mi_00` 태그, `VID=1189`, `PID=8890`, `UsagePage=0001`, `Usage=0006`(Keyboard), `InputReportByteLength=9`.
- `path[0]`은 SetupDi가 반환한 원래 경로(`\kbd` 접미사 포함), `path[1]`은 접미사를 제거한 대조 경로다.
- **두 경로 모두** `FILE_READ_ATTRIBUTES`는 성공하고 `FILE_READ_DATA`는 `err=5`로 실패했다. 따라서 `\kbd` 접미사나 경로 오타가 원인이 아니다.
- `FILE_READ_ATTRIBUTES` 성공 시 `HidD_GetAttributes`가 `attr=True vid=1189 pid=8890`을 반환했다. 장치 객체가 존재하고 올바른 MI_00임을 확인했다.

---

## 3. 접근 권한/공유 모드별 결과

표준 사용자 PowerShell 직접 실행:

| access | share=0 | share=1 (READ) | share=2 (WRITE) | share=3 (READ\|WRITE) |
|---|---|---|---|---|
| `0` | FAIL 32 | **OK** | FAIL 32 | **OK** |
| `FILE_READ_ATTRIBUTES (0x80)` | FAIL 32 | **OK** | FAIL 32 | **OK** |
| `FILE_READ_DATA (0x1)` | **FAIL 5** | **FAIL 5** | **FAIL 5** | **FAIL 5** |
| `GENERIC_READ (0x80000000)` | **FAIL 5** | **FAIL 5** | **FAIL 5** | **FAIL 5** |

해석:

- **32 = `ERROR_SHARING_VIOLATION`**: 시스템이 이미 핸들을 잡고 있어 share=0/2 오픈이 충돌. share=1/3은 허용.
- **5 = `ERROR_ACCESS_DENIED`**: `FILE_READ_DATA` 권한 자체가 거부됨. 공유 모드와 무관하게 모든 share에서 실패.
- `FILE_READ_ATTRIBUTES`는 허용되므로 **장치 객체/경로는 유효**하고, **읽기 데이터 권한만** 차단된다.
- 이는 단순한 “이미 열려 있음”이 아니라 **보안 설명자/액세스 체크에 의한 거부**다.

대조군(같은 코드, 같은 인자, 같은 실행):

| 컬렉션 | `GENERIC_READ`, share=3 |
|---|---|
| MI_00 Keyboard | **FAIL 5** |
| MI_01 Vendor | **OK** |
| MI_02 COL01 Keyboard | **FAIL 5** |
| MI_02 COL02 Consumer | **OK** |
| MI_03 Mouse | **FAIL 5** |

→ 키보드/마우스 TLC만 거부된다. 코드/인자 문제가 아니다.

---

## 4. 관리자/SYSTEM 테스트가 동일 코드/인자였는지

### 4.1 동일 바이너리

```
C3C2E7D41404B7E138E1CFC049106B1EC4FD60DA2DF9AA5D3A9C011C58575813  repo\tools\phase0\HidProbe.exe
C3C2E7D41404B7E138E1CFC049106B1EC4FD60DA2DF9AA5D3A9C011C58575813  C:\Users\Public\HidProbe.exe
```

- 관리자 PowerShell: repo의 exe를 직접 실행.
- SYSTEM 예약 작업: Public에 복사한 **동일 해시**의 exe를 직접 실행.
- 두 실행 모두 같은 `CreateFileW` 인자(`GENERIC_READ`, share=3)를 사용한다.

### 4.2 관리자 PowerShell (CMD 미사용)

```
user=GALAXYBOOK2PROS\user elevated-admin=True auth=NTLM
===== mi_00 ...
  access=FILE_READ_DATA  share=0..3 FAIL err=5
  access=GENERIC_READ    share=0..3 FAIL err=5
```

- `Start-Process powershell -Verb RunAs`로 **PowerShell 자체를 관리자로** 실행했다. CMD는 관여하지 않는다.
- 결과는 표준 사용자와 동일하다.

### 4.3 SYSTEM 예약 작업 (CMD 미사용, exe 직접 실행)

작업 XML:

```xml
<Principals>
  <Principal id="Author">
    <UserId>S-1-5-18</UserId>          <!-- NT AUTHORITY\SYSTEM -->
    <RunLevel>HighestAvailable</RunLevel>
  </Principal>
</Principals>
...
<Command>C:\Users\Public\HidProbe.exe</Command>
<Arguments>readraw 5 C:\Users\Public\sys_raw_direct.log</Arguments>
```

실행 로그:

```
# identity user=NT AUTHORITY\SYSTEM elevated-admin=True
# OPEN FAIL mi_00 path=...\kbd err=5
# OPEN FAIL mi_00 (stripped \kbd) path=... err=5
# OPEN FAIL mi_02&col01 path=...\kbd err=5
# OPEN FAIL mi_02&col01 (stripped \kbd) path=... err=5
```

- 작업 동작은 **CMD가 아니라 exe 직접 실행**이다. `cmd.exe`는 작업 체인에 없다.
- `UserId=S-1-5-18` = SYSTEM, 실행 로그도 `NT AUTHORITY\SYSTEM`.
- **SYSTEM 권한에서도 동일하게 `err=5`.**

---

## 5. CMD 차단 환경과 PowerShell 환경의 차이

### 5.1 정책 상태

```
HKCU\Software\Policies\Microsoft\Windows\System\DisableCMD = 0
cmd /c "echo cmd-ok"  →  cmd-ok
```

- 사용자가 제공한 명령을 표준 권한에서 실행하면 레지스트리 ACL 때문에 실패했다:
  `Set-ItemProperty: Requested registry access is not allowed.`
- 관리자 PowerShell에서 실행하면 성공했다:
  ```
  user=GALAXYBOOK2PROS\user admin=True
  set-ok
  DisableCMD=0
  cmd-ok
  ```
- 즉, **정책은 이미 0**이었고 CMD는 정상 동작한다.

### 5.2 CMD vs PowerShell 직접 실행 비교

| 실행 환경 | 사용자 | MI_00 `FILE_READ_DATA` |
|---|---|---|
| PowerShell 직접 exe | 표준 사용자 | FAIL 5 |
| CMD 직접 exe | 표준 사용자 | FAIL 5 |
| 관리자 PowerShell 직접 exe | 관리자 | FAIL 5 |
| SYSTEM 예약 작업(exe 직접) | SYSTEM | FAIL 5 |

- CMD/PowerShell 로그의 MI_00 결과는 **완전히 동일**하다.
- 실패는 **CMD를 전혀 거치지 않는 PowerShell 직접 실행**과 **exe 직접 실행 SYSTEM 작업**에서도 동일하다.
- 따라서 **CMD 차단/실행 환경은 원인이 아니다.**

---

## 6. Windows HIDClass가 키보드 원문 읽기를 제한하는 구조인지

### 6.1 확인된 드라이버 스택

- MI_00: Usage `0001:0006` (Keyboard), PnP 장치 `HID 키보드 장치`, 서비스 `kbdhid`.
- MI_02 COL01: 동일하게 Keyboard, `kbdhid`.
- MI_03: Mouse, `mouhid`.
- MI_01: Vendor `FF00:0001`, 독점 드라이버 없음 → 읽기 가능.

Windows HID 스택:

```
HIDCLASS.sys
 ├─ KBDHID.sys (keyboard mapper) → KBDCLASS.sys
 ├─ MOUHID.sys (mouse mapper)    → MOUCLASS.sys
 └─ vendor collection (MI_01)    → no system mapper
```

### 6.2 공식 문서 근거

- Microsoft, **Developing Keyboard and Mouse HID Client Drivers**:
  > "The system opens all keyboard and mouse collections for its exclusive use."
  https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/keyboard-and-mouse-hid-client-drivers

- Jan Axelson, **HID FAQ**:
  > "Windows 2000 and later have exclusive read/write access to HIDs that are configured as system keyboards or mice. An application can obtain a handle to a system keyboard or mouse by not requesting READ or WRITE access with CreateFile. ... How can applications receive Input reports from a system mouse or keyboard? The Windows raw input API enables applications to read Input reports..."
  http://janaxelson.com/hidfaq.htm

- HIDAPI 소스 주석:
  > "System devices, such as keyboards and mice, cannot be opened in read-write mode, because the system takes exclusive control over them. This is to prevent keyloggers."
  https://github.com/libusb/hidapi/blob/master/windows/hid.c

- Microsoft, **HidD_GetInputReport**:
  - TLC 핸들이 필요하고, 리포트 ID/길이 규칙을 따른다.
  - 키보드 TLC는 시스템 독점이므로 일반 앱이 `GENERIC_READ` 핸들을 얻을 수 없다.
  https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getinputreport

### 6.3 이 장치에 적용한 해석

- `FILE_READ_ATTRIBUTES`는 허용되지만 `FILE_READ_DATA`는 거부된다. 이는 키보드 컬렉션의 **보안 설명자/독점 정책**과 일치한다.
- 같은 코드로 MI_01/MI_02 COL02는 `GENERIC_READ`가 성공한다. 따라서 “HIDAPI/코드/경로가 잘못됐다”는 가설은 배제된다.
- `HidD_GetInputReport`가 MI_00에서 `ERROR_INVALID_FUNCTION(1)`을 반환하는 것은 키보드 TLC의 GET_REPORT 제약/독점과 일치한다. (MI_01에서는 성공하지만 `AA 00...` 고정).
- **Raw Input이 Microsoft가 키보드/마우스 입력에 대해 지원하는 정식 경로**다. 다만 Raw Input은 `RAWKEYBOARD`(VKey/MakeCode)만 주고 HID 원문 바이트는 주지 않는다.

---

## 7. 질문별 답

| # | 질문 | 답 |
|---|---|---|
| 1 | 실제 접근 코드/`CreateFile` 인자 | `CreateFileW(path, access, share, NULL, OPEN_EXISTING, 0, NULL)`. access 0/0x80/0x1/0x80000000, share 0/1/2/3. 코드/인자 정상. |
| 2 | 경로가 MI_00인지 | `mi_00`, VID 1189/PID 8890, Usage 0001:0006, In=9. `\kbd` 포함/제거 모두 동일 결과. |
| 3 | GENERIC_READ/공유/권한 | `FILE_READ_ATTRIBUTES` OK, `FILE_READ_DATA`/`GENERIC_READ` FAIL 5. share 0/2는 32, share 1/3은 OK(단, read data는 여전히 5). |
| 4 | 관리자/SYSTEM 동일 코드/인자 | 동일 SHA-256, 관리자 PowerShell 및 SYSTEM 작업(exe 직접) 모두 같은 인자, 모두 FAIL 5. SYSTEM `S-1-5-18`. |
| 5 | CMD vs PowerShell | `DisableCMD=0`, `cmd /c` 정상. CMD/PS/관리자/SYSTEM 결과 동일. CMD 미사용 경로에서도 실패. **CMD 원인 아님.** |
| 6 | HIDClass 제한 구조 | **그렇다.** 시스템이 키보드/마우스 TLC를 독점. `FILE_READ_DATA` 거부, Raw Input만 지원. 공식 문서/대조군으로 확인. |

---

## 8. 남은 선택지

1. **USBPcap/Wireshark 원문 USB 캡처** (별도 승인 필요)
   - MI_00 interrupt IN의 9바이트를 키별로 비교.
   - 예약 바이트에 슬롯 ID가 있는지 확인.
2. **커널 HID 필터 드라이버** (별도 승인 필요)
   - 원문 리포트를 커널에서 읽어 슬롯 ID를 사용자 앱에 전달.
   - 슬롯 ID가 원문에 존재할 때만 유효.
3. **사용자 모드 재시도 중단**
   - 권한 상승/CMD 우회/경로 변형으로는 해결되지 않는다. 이번 시험으로 확정.

USBPcap 설치나 커널 드라이버 변경은 **아직 수행하지 않았다.**

---

## 9. 증거 파일

```
%TEMP%\opencode\phase0\ps_readopen.txt          PowerShell 직접(표준)
%TEMP%\opencode\phase0\cmd_readopen.txt         CMD 직접(표준)
%TEMP%\opencode\phase0\elev_ps_readopen.txt     관리자 PowerShell 직접
C:\Users\Public\sys_raw_direct.log              SYSTEM 작업(exe 직접)
C:\Users\Public\sys_task_setup.txt              작업 XML/생성/실행/삭제
C:\Users\Public\disablecmd_result.txt           DisableCMD 설정 결과
```

- 진단 exe SHA-256: `C3C2E7D41404B7E138E1CFC049106B1EC4FD60DA2DF9AA5D3A9C011C58575813`
- 예약 작업 `Phase0SysDirect`는 실행 후 삭제됨.

---

## 10. 최종 판정

**`err=5`는 CMD 실행 제한이나 테스트 환경의 문제가 아니다.**
**Windows HIDClass가 시스템 키보드 컬렉션(MI_00)의 `FILE_READ_DATA`를 독점 보호하기 때문에 발생한 것이다.**

- 일반 사용자/관리자/SYSTEM 모두 동일.
- PowerShell/CMD 모두 동일.
- `\kbd` 경로/제거 경로 모두 동일.
- 같은 코드로 MI_01/컨슈머는 정상 오픈 → 코드/인자 문제 아님.

**결론: 사용자 모드에서 MI_00 원문을 읽는 것은 불가능하다. 다음 단계는 USBPcap 또는 커널 필터 드라이버이며, 별도 승인이 필요하다.**
