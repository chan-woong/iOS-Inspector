# 문제 해결

## 연결된 단말이 표시되지 않음

- iPhone 잠금을 해제합니다.
- **이 컴퓨터를 신뢰**를 다시 확인합니다.
- 충전 전용 케이블이 아닌 데이터 케이블을 사용합니다.
- Apple Devices/iTunes를 재설치한 후 Windows를 재부팅합니다.
- Apple Devices와 iTunes를 동시에 설치했다면 드라이버 충돌 여부를 확인합니다.

## `idevice_id` 또는 다른 도구를 찾을 수 없음

프로그램을 다시 실행해 자동 설치에 동의합니다. 계속 실패하면 GitHub 접근이 차단되었는지 확인하고, 배포 폴더의 `tools`에 libimobiledevice 실행 파일과 DLL이 함께 있는지 확인합니다.

## SSH 연결 실패

- 단말에 OpenSSH가 설치되어 실행 중인지 확인합니다.
- IP 연결에서는 PC와 iPhone이 같은 네트워크인지 확인합니다.
- USB 연결에서는 상단 USB 단말 선택과 `iproxy.exe` 존재 여부를 확인합니다.
- 로컬 2222 포트를 다른 프로그램이 사용 중이면 USB 로컬 포트를 변경합니다.
- host key가 변경되었다면 `%USERPROFILE%\.ssh\known_hosts`의 해당 항목을 신중히 확인하고 정리합니다.

## TrollVNC 설치 실패

SSH 터미널에서 다음 항목을 확인합니다.

```sh
dpkg --print-architecture
dpkg -l | grep -i trollvnc
command -v trollvncserver
```

내장 패키지는 rootless `iphoneos-arm64`용입니다. `dpkg` 의존성 오류는 패키지 관리자에서 필수 패키지를 갱신한 뒤 다시 시도합니다. 설치 완료 후 실행 파일이 보이지 않으면 respring합니다.

## 미러링 연결 실패

- SSH 연결 상태를 먼저 확인합니다.
- `command -v trollvncserver`가 경로를 출력하는지 확인합니다.
- USB 연결 시 `iproxy`가 포함되어 있는지 확인합니다.
- 5901 포트를 다른 프로그램이 사용하면 VNC 포트를 변경합니다.
- `/var/tmp/trollvnc.log`의 마지막 오류를 확인합니다.

```sh
tail -50 /var/tmp/trollvnc.log
```

보안을 위해 가능하면 **IP 연결**보다 **USB 연결**을 사용하세요.

## IPA 설치 실패

- `Could not connect to lockdownd: receive timeout(-7)`이 표시된다면 v1.1.1 이상인지 확인합니다. v1.1.1부터 앱 설치 파일은 AFC가 아니라 SSH/SFTP로 전송됩니다.
- SFTP 오류가 표시되면 단말에 OpenSSH의 `sftp-server`가 설치되어 있는지와 `/var/tmp`의 여유 공간을 확인합니다.
- `command -v appinst` 또는 `command -v ipainstaller`를 확인합니다.
- `command -v ldid`를 확인합니다.
- 단말 저장 공간과 IPA 아키텍처/iOS 호환성을 확인합니다.
- 앱이 이미 설치되어 있다면 번들 ID 충돌과 데이터 보존 여부를 먼저 확인합니다.
