# Phase 2 — USBPcap 설치 전 안전성 검토

작성: 2026-11-24
범위: **검토만 수행. USBPcap/Wireshark 설치 없음.** 드라이버 적용 없음.
대상: USBPcap 1.5.4.0 (공식 최신), Windows 11 Pro Education 23H2 (10.0.22631.4602), Samsung Galaxy Book2 Pro

---

## 0. 결론 요약

| 항목 | 판정 |
|---|---|
| Windows 11 23H2 공식 지원 | **없음** (공식 지원: Windows 7/8/10) |
| 설치 프로그램 서명/SHA-256 | **정상** (Valid, Tomasz Moń) |
| 드라이버 패키지 서명 | **Microsoft WHQL 카탈로그 서명 확인** |
| HVCI/Secure Boot 호환성 | **구조상 가능** (W+X 섹션 없음, WHQL 서명) — Windows 11 실측은 미확인 |
| 시스템 복원 | **가능** — 복원 지점 44 생성 성공 |
| `--devices` 필터 | **장치 주소 전용**. 엔드포인트 제한 없음 |
| 제거 완전성 | 소스상 서비스/필터 제거 확인. 실제 런타임 검증은 설치 후 필요 |
| 종합 판정 | **조건부 GO** — 사용자가 잔여 위험을 명시적으로 승인할 때만. 그렇지 않으면 **NO-GO** |

**핵심 잔여 위험:** USBPcap은 USB 클래스 전체의 **UpperFilter**다. 드라이버가 Windows 11 23H2에서 비정상 동작하면 키보드뿐 아니라 **모든 USB 장치**에 영향을 줄 수 있다. 공식 지원 OS가 아니며, 2020년 빌드다.

---

## 1. Windows 11 23H2 호환성 및 알려진 문제

### 1.1 공식 자료

- USBPcap 공식 사이트: **"works on Windows 7, 8 and 10"**, 설치 후 **재부팅 필요**.
  <https://desowin.org/usbpcap/>
- 1.5.4.0 릴리스 노트: **"compatible with Windows 7, 8 and 10 - both 32 and 64-bit"**. Windows 11 언급 없음.
  <https://github.com/desowin/usbpcap/releases/tag/1.5.4.0>
- 참고: winget 패키지 페이지는 Windows 11을 언급하지만, 이는 벤더 공식 문서가 아니다.
- Microsoft 문서: HVCI(메모리 무결성)는 커널 드라이버 호환성에 민감하며, 호환되지 않는 드라이버는 로드 실패 또는 부팅 실패를 일으킬 수 있다.
  <https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity>

### 1.2 설치 프로그램의 Windows 11 처리

NSIS 스크립트(`nsis/USBPcap.nsi`) 분석:

```nsis
${IfNot} ${AtLeastWin7}
  MessageBox MB_OK "Unsupported Windows version. Only Windows 7, 8 and 10 are supported..."
  Quit
${EndIf}
...
${If} ${RunningX64}
  ${If} ${AtLeastWin10}
    File "..\Release\Windows10\x64\USBPcap.inf"
    File "..\Release\Windows10\x64\USBPcap.sys"
    File "..\Release\Windows10\x64\USBPcapamd64.cat"
```

- Windows 11은 `AtLeastWin10`을 만족하므로 **Windows 10용 드라이버를 선택**한다.
- 설치 프로그램은 Windows 11을 **차단하지 않는다.** 즉 "설치 가능"이지 "공식 지원"은 아니다.
- `RequestExecutionLevel admin` — UAC 필수.
- `InstType "Full"`에 **"Detect USB 3.0"** 섹션이 포함되어 `USBPcapCMD.exe -I`를 실행한다. 이는 USB 3.0 루트 허브의 비표준 HWID를 등록하는 단계다. **Minimal 설치를 선택하면 USB 3.0 캡처가 실패할 수 있다.**

### 1.3 이 PC의 보안 구성

| 항목 | 값 | 의미 |
|---|---|---|
| VBS | `VirtualizationBasedSecurityStatus=2` | VBS 실행 중 |
| SecurityServicesRunning | `{2,3,4}` | HVCI(2), System Guard(3), SMM(4) |
| CodeIntegrityPolicyEnforcementStatus | `2` | 코드 무결성 정책 **강제** |
| Secure Boot | `True` | UEFI Secure Boot 켜짐 |
| `testsigning` | 출력 없음 | 테스트 서명 모드 아님 |

**HVCI가 켜져 있으므로 드라이버는 HVCI 호환 + 신뢰된 서명이어야 한다.**

### 1.4 드라이버 패키지 서명/구조 검사

공식 1.5.4.0 릴리스 패키지의 `Release\Windows10\x64`:

| 파일 | 서명 상태 | 서명자 | 유효기간/타임스탬프 |
|---|---|---|---|
| `USBPcap.sys` | Valid | `CN=Tomasz Moń` (Certum Code Signing CA SHA2) | 만료 2021-05-22, **Symantec 타임스탬프 2029** |
| `usbpcapamd64.cat` | Valid | **`CN=Microsoft Windows Hardware Compatibility Publisher`** | 만료 2021-03-06, **Microsoft 타임스탬프 2021-01-22** |

- **카탈로그가 Microsoft WHQL 서명**이다. Secure Boot/HVCI 환경에서 드라이버 패키지 신뢰의 핵심 조건을 충족한다.
- `.sys`의 개인 서명 인증서는 만료됐지만, **유효한 타임스탬프**가 있어 만료 후에도 서명 시점 기준으로 검증된다.
- PE 섹션 검사:

```
.text   EXEC,CODE
.rdata  -
.data   WRITE
.pdata  -
PAGE    EXEC,CODE
INIT    EXEC,CODE
.rsrc   -
.reloc  -
```

- **쓰기+실행(W+X) 섹션이 없다.** HVCI가 거부하는 전형적인 패턴은 아니다.
- 따라서 "서명/섹션 구조"만 보면 HVCI에서 로드될 가능성이 높다. 그러나 **Windows 11 23H2 실측은 아직 없다.**

### 1.5 알려진 문제/주의사항

| 문제 | 내용 | 이번 환경 영향 |
|---|---|---|
| USB 3.0 루트 허브 | USBPcap TODO: 비표준 HWID를 가진 USB 3.0 루트 허브에서 동작하지 않을 수 있음. `-I`로 NonStandardHWIDs 등록 필요 | **해당** — 루트 허브가 `ROOT_HUB30`. Full 설치의 "Detect USB 3.0" 필요 |
| 재부팅 필요 | 공식 사이트: 설치 후 재부팅 | **해당** — 캡처 전 재부팅 계획 |
| `EXRESOURCE_TIMEOUT_LIVEDUMP` | GitHub issue #131 (2023, open): Windows 10 19041에서 `USBPcap.sys` 스택의 ERESOURCE timeout 라이브덤프 보고. Windows 11 23H2에서 재현 확인은 없음 | **잔여 위험** |
| usbipd 비호환 | `usbipd`가 USBPcap 필터와 충돌한다고 경고 | **미설치** — 해당 없음 |
| USB 클래스 전체 필터 | `UpperFilters`에 등록되어 모든 USB 장치에 영향 | **최대 위험** — 키보드 외 장치도 영향 가능 |
| Windows 11 미지원 | 공식 지원 목록에 없음 | **잔여 위험** |
| 테스트 서명 | README의 "TESTSIGNING" 문구는 소스 빌드용. 공식 설치본은 WHQL 카탈로그 서명 | 공식 설치본은 테스트 서명 불필요로 판단. 실패 시 중단 |

---

## 2. 설치 프로그램 서명/SHA-256 (설치 직전 재검증 대상)

현재 검증 결과:

| 항목 | 값 |
|---|---|
| 파일 | `USBPcapSetup-1.5.4.0.exe` |
| 크기 | 195,040 bytes |
| SHA-256 | `87A7EDF9BBBCF07B5F4373D9A192A6770D2FF3ADD7AA1E276E82E38582CCB622` |
| Authenticode | **Valid** — `CN=Tomasz Moń`, Certum |
| NSIS | Nullsoft Install System v3.05, `requireAdministrator` |

**설치 직전 재검증 명령:**

```powershell
$f = "C:\Users\user\AppData\Local\Temp\opencode\usbpcap\USBPcapSetup-1.5.4.0.exe"
$expected = "87A7EDF9BBBCF07B5F4373D9A192A6770D2FF3ADD7AA1E276E82E38582CCB622"
$hash = (Get-FileHash $f -Algorithm SHA256).Hash
$sig  = Get-AuthenticodeSignature $f
if ($hash -ne $expected -or $sig.Status -ne "Valid") { throw "USBPcap installer verification failed" }
```

불일치/서명 오류 시 **설치 중단**.

---

## 3. 시스템 복원 가능 여부 및 복원 지점 생성

### 3.1 상태

| 항목 | 값 |
|---|---|
| VSS 서비스 | `Stopped` / `Manual` (필요 시 시작) |
| Shadow Storage (C:) | 사용 1.09 GB, 할당 1.56 GB, 최대 2.41 GB |
| 기존 복원 지점 | Sequence 43 — `Windows 업데이트` |
| **새 복원 지점** | **Sequence 44 — `Phase2-USBPcap-Safety`** |
| 생성 시각 | 2026-11-23 16:13:44 (로컬) |
| 유형 | `DEVICE_DRIVER_INSTALL` (10) |
| `Checkpoint-Computer` 결과 | **OK** |

### 3.2 판정

- **복원 지점 생성 가능**을 실제로 확인했다. 설치 전 요건 충족.
- 설치 프로그램도 내부적으로 `SysRestore::StartRestorePoint`를 호출하지만, **실패해도 설치를 계속**한다. 따라서 우리의 사전 체크가 게이트다.
- **설치 직전에 복원 지점 44가 존재하는지 확인**하고, 없으면 다시 생성한다. 생성 실패 시 **설치 중단**.

확인 명령:

```powershell
Get-ComputerRestorePoint | Where-Object Description -eq "Phase2-USBPcap-Safety"
```

---

## 4. `--devices` 필터의 실제 의미

### 4.1 소스 확인

`USBPcapCMD/iocontrol.c`:

```c
BOOLEAN USBPcapInitAddressFilter(PUSBPCAP_ADDRESS_FILTER filter, PCHAR list, BOOLEAN filterAll)
{
    USBPCAP_ADDRESS_FILTER tmp;
    ...
    tmp.filterAll = filterAll;
    while (*list) {
        if (isdigit(*list)) {
            int number = atoi(list);
            USBPcapSetDeviceFiltered(&tmp, number);
            ...
        }
    }
    ...
}
```

`USBPcapSetDeviceFiltered`:

```c
static BOOLEAN USBPcapGetAddressRangeAndIndex(int address, UINT8 *range, UINT8 *index)
{
    if ((address < 0) || (address > 127)) return FALSE;
    *range = address / 32;
    *index = address % 32;
    return TRUE;
}

filter->addresses[range] |= (1 << index);
```

`USBPcap.h`의 필터 구조:

```c
typedef struct _USBPCAP_ADDRESS_FILTER
{
    UINT32 addresses[4];   // 0-127 address bits
    BOOLEAN filterAll;
} USBPCAP_ADDRESS_FILTER;
```

### 4.2 판정

- `--devices 3`은 **USB 장치 주소 3번만** 캡처한다.
- **엔드포인트까지 제한하는 기능은 없다.** 엔드포인트는 캡처 후 우리 파서(`--endpoint 0x81`)가 필터링한다.
- 주소는 루트 허브별로 유일하다. 올바른 루트 허브(`ROOT_HUB30\4&21A3A369&0&0`)를 선택해야 한다.
- 장치 재열거로 주소가 바뀔 수 있으므로 캡처 직전에 PnP에서 주소를 다시 읽는다.

---

## 5. 캡처 중 키보드 입력 정상 동작 확인 방법

USBPcap은 USB 클래스 UpperFilter이므로, 캡처 중 키보드 입력이 계속 정상인지 **독립적으로** 확인해야 한다.

### 5.1 Raw Input 병행 로깅

USBPcap 캡처와 동시에:

```powershell
tools\phase0\HidProbe.exe raw 180 <capture_dir>\rawinput.log quiet
```

- `RIDEV_INPUTSINK`로 키 이벤트를 수신하며, **입력을 소비/차단하지 않는다.**
- `VID_1189&PID_8890` 장치만 필터링한다.
- VKey/MakeCode/타임스탬프가 기록되므로, USB 원문 패킷과 시간축으로 대조할 수 있다.
- 이 로그에 KEY1~6 입력이 정상적으로 찍히면 **캡처 중 키보드 동작이 유지**된 것이다.

### 5.2 시각적 확인

- 메모장에 포커스를 두고 실제 `A`/`C`가 입력되는지 확인.
- 캡처 종료 후 키를 다시 눌러 정상 입력 확인.

### 5.3 실패 판정

- Raw Input 로그에 키 이벤트가 없거나, 메모장 입력이 끊기면 **즉시 캡처 중단**.
- USBPcap 프로세스 종료 → 드라이버 제거/복원 지점 복원 검토.

---

## 6. 설치/로드/인식 실패 복구 절차

### 6.1 사전 안전장치

- 복원 지점 44 (`Phase2-USBPcap-Safety`) 확인.
- 노트북 내장 키보드/터치패드로 복구 가능.
- USBPcap 설치 파일과 uninstaller 경로 기록: `C:\Program Files\USBPcap\Uninstall.exe`.
- 캡처 파일/로그는 C: 이외 경로 또는 백업.

### 6.2 설치 실패 (UAC 취소, 설치 오류)

1. 설치 창을 닫는다.
2. 다음이 없음을 확인:
   - `Get-Service USBPcap`
   - `HKLM\SYSTEM\CurrentControlSet\Services\USBPcap`
   - USB 클래스 `UpperFilters`의 `USBPcap`
3. 남아 있으면 `C:\Program Files\USBPcap\Uninstall.exe` 실행.
4. 필요 시 복원 지점 44로 복원.

### 6.3 드라이버 로드 실패

증상: `sc query USBPcap` 오류, Device Manager의 USB 컨트롤러/허브에 노란 느낌표, USB 장치 인식 불가.

1. **즉시 재부팅.**
2. 재부팅 후 `Get-Service USBPcap` / `sc query USBPcap` 확인.
3. 실패 시:
   ```
   "C:\Program Files\USBPcap\Uninstall.exe" /S
   ```
4. 그래도 USB가 불안정하면 **시스템 복원**:
   - 설정 → 시스템 → 복구 → 시스템 복원 → `Phase2-USBPcap-Safety`.
   - 또는 WinRE(Shift+재시작) → 문제 해결 → 시스템 복원.
5. 부팅 불가 시 WinRE → 안전 모드 → USBPcap 제거/복원.

### 6.4 장치 인식 이상 (키보드/허브)

1. 다른 USB 포트에 재연결.
2. 장치 관리자 → USB 컨트롤러 → 하드웨어 변경 사항 검색.
3. 재부팅.
4. USBPcap 제거 후 재부팅.
5. 복원 지점 복원.

### 6.5 캡처 실패 (USBPcapN 없음 / 패킷 0건)

1. `USBPcapCMD.exe`를 관리자로 실행해 필터 목록 확인.
2. "USBPcap UpperFilter entry appears to be present. Most likely you have not restarted..." 메시지가 나오면 **재부팅**.
3. "Detect USB 3.0"(`-I`)가 실행됐는지 확인. Full 설치였는지 확인.
4. 올바른 루트 허브를 선택했는지 확인(`ROOT_HUB30\4&21A3A369&0&0`).
5. 장치 주소 재확인(재열거로 3→다른 값 가능).
6. `--devices` 대신 `-A`로 전체 캡처해 장치가 보이는지 진단(단, 다른 장치 데이터는 즉시 필터/삭제).

---

## 7. 제거 완전성

### 7.1 소스상 제거 동작

`USBPcap.inx`:

```ini
[DefaultUninstall.NTamd64]
DelFiles  = @USBPcap.sys
DelReg    = USBPcap.DelReg

[USBPcap.DelReg]
HKLM, System\CurrentControlSet\Control\Class\{36FC9E60-C465-11CF-8056-444553540000}, UpperFilters, 0x00018002, USBPcap

[DefaultUninstall.NTamd64.Services]
DelService = USBPcap,0x00000200
```

`nsis/USBPcap.nsi` Uninstall 섹션:

```nsis
ExecWait '$SYSDIR\RUNDLL32.EXE SETUPAPI.DLL,InstallHinfSection DefaultUninstall 132 .\USBPcap.inf'
Delete $INSTDIR\Uninstall.exe
Delete $INSTDIR\USBPcap.inf
Delete $INSTDIR\USBPcap.sys
Delete $INSTDIR\USBPcapamd64.cat
Delete $INSTDIR\USBPcapCMD.exe
RMDir /REBOOTOK $INSTDIR
DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\USBPcap"
```

- 서비스 `USBPcap` 삭제 (`DelService`).
- USB 클래스 `UpperFilters`에서 `USBPcap` 제거 (`DelReg`).
- 파일/언인스톨 키 삭제. 폴더는 재부팅 후 삭제(`/REBOOTOK`).

### 7.2 제거 후 확인 명령

```powershell
sc.exe query USBPcap
Get-Service USBPcap -ErrorAction SilentlyContinue
Test-Path "HKLM:\SYSTEM\CurrentControlSet\Services\USBPcap"
(Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{36FC9E60-C465-11CF-8056-444553540000}" -Name UpperFilters).UpperFilters
Get-PnpDevice | Where-Object FriendlyName -match "USBPcap"
pnputil /enum-drivers | Select-String -Pattern "usbpcap" -SimpleMatch
Test-Path "C:\Program Files\USBPcap"
```

기대 결과: 서비스/레지스트리/필터/장치/폴더 모두 없음. 드라이버 저장소에 패키지가 남아 있으면:

```powershell
pnputil /enum-drivers
pnputil /delete-driver oemXX.inf /uninstall /force
```

- **주의:** `UpperFilters`는 다른 필터(예: `kbdclass`)를 포함할 수 있으므로 `USBPcap`만 제거됐는지 확인한다.
- 제거 후 **재부팅**해 필터가 완전히 분리됐는지 다시 확인한다.

### 7.3 현재 한계

- 실제 제거 런타임 검증은 설치 후에만 가능하다. 소스상 서비스/필터 제거는 확인했지만, **Windows 11에서의 실제 동작은 미검증**이다.
- 드라이버 저장소(`pnputil`) 잔여 패키지는 언인스톨러가 자동 제거하지 않을 수 있다.

---

## 8. 설치 전 최종 체크리스트

- [ ] USBPcap 설치 파일 SHA-256 재검증 + Authenticode Valid.
- [ ] 복원 지점 44 `Phase2-USBPcap-Safety` 존재 확인. 없으면 재생성, 실패 시 **중단**.
- [ ] HVCI/Secure Boot 상태 재확인(변경 없음).
- [ ] 설치 유형 **Full** 선택 ("Detect USB 3.0" 포함).
- [ ] 설치 후 **재부팅**.
- [ ] 재부팅 후 `USBPcapCMD.exe` 필터 목록/`-I` 확인.
- [ ] 장치 주소 재확인(3→변경 가능).
- [ ] 올바른 루트 허브 선택.
- [ ] USBPcap 캡처 + `HidProbe raw` 병행.
- [ ] 캡처 중 키보드 입력 정상 확인. 이상 시 즉시 중단.
- [ ] 캡처 후 `--devices` 외 장치 데이터는 필터/삭제.
- [ ] 캡처 완료 후 USBPcap 제거 + 재부팅 + 제거 확인.

---

## 9. 최종 권고

### 조건부 GO

다음을 모두 만족할 때만 설치를 권고한다.

1. 사용자가 **Windows 11 비공식 지원 + USB 클래스 전체 필터**의 잔여 위험을 명시적으로 승인.
2. 복원 지점 44가 존재하고, 설치 직전 재검증이 통과.
3. 설치 후 즉시 재부팅, 캡처, **캡처 완료 후 USBPcap 제거**.
4. 캡처 중 키보드/다른 USB 장치 이상 시 즉시 중단.

### NO-GO 조건

- 복원 지점 생성/확인 실패.
- 설치 프로그램 해시/서명 불일치.
- HVCI/Secure Boot 정책이 드라이버를 차단.
- 설치 후 USB 장치 인식 이상.
- 사용자가 USB 클래스 전체 필터 위험을 수용하지 않음.

### 더 안전한 대안

- **Linux VM(USB 패스스루) + usbmon**: 호스트 커널 드라이버를 건드리지 않는다. 다만 이 PC에는 VirtualBox/VMware/Hyper-V 도구가 확인되지 않아 추가 설치가 필요하다.
- **별도 Linux PC/Raspberry Pi + usbmon**: 가장 안전. 장치를 옮겨 캡처.
- **하드웨어 USB 분석기**: 가장 확실하지만 장비가 필요하다.

현재 이 PC에는 VM 도구가 없고, 호스트 USBPcap이 가장 현실적이다. 그러나 위 잔여 위험을 고려하면 **"설치 승인"은 사용자의 명시적 결정**이어야 한다.

---

## 10. 증거/명령 요약

| 항목 | 증거 |
|---|---|
| 공식 지원 OS | desowin.org/usbpcap, GitHub 1.5.4.0 release notes |
| 설치 프로그램 서명 | Authenticode Valid, Tomasz Moń, SHA-256 `87A7...` |
| 드라이버 카탈로그 | `CN=Microsoft Windows Hardware Compatibility Publisher` Valid |
| 드라이버 섹션 | `.text`/`PAGE`/`INIT` EXEC, `.data` WRITE, W+X 없음 |
| HVCI/Secure Boot | VBS=2, services {2,3,4}, CodeIntegrity=2, SecureBoot=True |
| 복원 지점 | Sequence 44, `Phase2-USBPcap-Safety`, type 10, 생성 OK |
| `--devices` | `iocontrol.c`: address bit array만, endpoint 없음 |
| 제거 | `USBPcap.inx` DelService/DelReg, NSIS Uninstall |
| 키보드 확인 | `HidProbe.exe raw` 병행 + 메모장 |
| 알려진 문제 | USB 3.0 HWID(`-I`), 재부팅, issue #131 livedump, usbipd 비호환(미설치) |
