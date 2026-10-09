# 설치 가이드

## 1. Windows 준비

1. Windows 10/11 x64 PC에 **Apple Devices** 앱 또는 Apple 웹사이트 버전 iTunes를 설치합니다.
2. iPhone을 USB 데이터 케이블로 연결합니다.
3. iPhone의 잠금을 해제하고 **이 컴퓨터를 신뢰**를 선택합니다.
4. Release ZIP을 별도 폴더에 완전히 압축 해제합니다. ZIP 안에서 EXE를 직접 실행하지 마세요.
5. `IosInspector.exe`를 실행합니다.

첫 실행 시 다음 도구가 없으면 설치 확인 창이 표시됩니다.

- `idevice_id`, `ideviceinfo`, `ideviceinstaller`, `afcclient`, `iproxy`
- `zsign`

동의하면 프로그램 폴더의 `tools` 하위에 자동 설치합니다. 시스템 PATH와 레지스트리는 변경하지 않습니다. 회사망 등에서 GitHub가 차단되면 인터넷이 되는 PC에서 도구를 준비해 `tools` 폴더 전체를 복사하세요.

## 2. 탈옥 단말 준비

단말에는 OpenSSH가 설치되어 있고 Windows PC에서 접근할 수 있어야 합니다. rootless 탈옥의 root 홈은 보통 `/var/jb/var/root`처럼 표시될 수 있습니다.

SSH 연결 방법은 두 가지입니다.

- **IP로 연결:** PC와 iPhone이 같은 네트워크에 있을 때 단말 IP와 SSH 포트를 사용합니다.
- **USB SSH 연결:** `iproxy`가 로컬 포트(기본값 2222)를 iPhone의 22번 포트로 전달합니다.

비밀번호는 저장하지 않습니다. 단말의 초기 비밀번호를 그대로 사용하지 말고 반드시 변경하세요.

## 3. TrollVNC 설치

1. 프로그램에서 SSH와 USB 단말 연결을 완료합니다.
2. **미러링 · 제어** 탭을 엽니다.
3. **TrollVNC 설치**를 누릅니다.
4. 프로그램이 내장된 rootless `.deb`를 `/var/mobile/Media/Downloads`로 전송하고 `dpkg -i`를 실행합니다.
5. 의존성 문제가 있으면 `apt-get -f install`을 자동 시도합니다.
6. 설치 후 서버가 검색되지 않는다는 안내가 나오면 단말을 respring하고 SSH를 다시 연결합니다.

내장 패키지는 `iphoneos-arm64` rootless 패키지입니다. rootful 또는 다른 아키텍처 환경에서는 호환되는 TrollVNC 패키지를 수동으로 준비해야 할 수 있습니다.

## 4. Windows SmartScreen

개인 서명 인증서가 없는 빌드는 Windows SmartScreen 경고가 표시될 수 있습니다. GitHub Release의 SHA-256과 내려받은 ZIP의 해시를 비교한 뒤, 신뢰할 수 있는 저장소에서 받은 파일일 때만 실행하세요.
