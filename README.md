# iOS Inspector

Windows에서 탈옥 iPhone/iPad를 점검하기 위한 데스크톱 도구입니다. SSH 연결을 중심으로 화면 미러링과 원격 입력, 파일 탐색, IPA 설치·재서명을 한 화면에서 제공합니다.

> 본인 소유이거나 명시적인 점검 권한을 받은 단말과 앱에서만 사용하세요.

## 주요 기능

- 프로그램 내부 SSH 터미널과 USB SSH 포트 포워딩
- TrollVNC 기반 화면 미러링 및 마우스·휠·키보드 입력
- 홈, 뒤로, 앱 전환, 잠금, 음량, 음소거, 화면 캡처 버튼
- AFC 기반 파일 탐색, 업로드, 다운로드, 폴더 생성·삭제
- IPA 서명 상태 확인과 탈옥 단말의 `ldid` fake signing
- `appinst` 또는 `ipainstaller`를 이용한 IPA 설치
- IPA, TrollVNC, appinst 패키지를 SSH/SFTP로 전송해 lockdownd 의존성 제거
- 필요한 Windows용 libimobiledevice 및 zsign 도구 자동 설치
- TrollVNC rootless 패키지 내장 및 SSH를 통한 단말 설치

Windows와 iPhone 사이의 클립보드 공유 기능은 포함하지 않습니다. Ctrl+C/V/X/A/Z는 미러링 화면에서 iOS의 Command 단축키로만 전달됩니다.

## 빠른 시작

1. GitHub의 **Releases**에서 `iOS-Inspector-win-x64.zip`을 내려받아 새 폴더에 압축 해제합니다.
2. Microsoft Store의 **Apple Devices** 또는 Apple 웹사이트의 iTunes를 설치해 Apple USB 드라이버를 준비합니다.
3. iPhone을 USB로 연결하고 잠금을 해제한 뒤 **이 컴퓨터를 신뢰**합니다.
4. `IosInspector.exe`를 실행합니다. 필수 Windows 도구가 없으면 자동 설치 여부를 묻습니다.
5. 첫 화면에서 탈옥 단말의 IP, SSH 포트, 사용자 이름과 비밀번호를 입력해 연결합니다.
6. 이후 미러링, 파일 탐색기, 앱 설치 탭을 사용할 수 있습니다.

TrollVNC 설치부터 미러링 연결까지의 자세한 순서는 [설치 가이드](docs/INSTALLATION.md)와 [사용 가이드](docs/USAGE.md)를 확인하세요. 문제가 생기면 [문제 해결](docs/TROUBLESHOOTING.md)을 참고하세요.

## 요구 사항

- Windows 10 또는 Windows 11 x64
- Apple Devices 또는 iTunes의 Apple USB 드라이버
- USB 데이터 케이블
- OpenSSH가 설치된 탈옥 iPhone/iPad
- iOS 14 이상을 지원하는 rootless TrollVNC 패키지

GitHub Release 빌드는 .NET 8 Desktop Runtime을 포함하는 self-contained 배포본이므로 별도의 .NET 설치가 필요하지 않습니다. 소스에서 기본 `dotnet publish`만 실행한 framework-dependent 빌드는 .NET 8 Desktop Runtime이 필요합니다.

## 내장 TrollVNC 패키지

- 파일: `packages/trollvnc-rootless-arm64.deb`
- SHA-256: `BF30364DBBBD08F55AD934AC02CA72D61B737DEF6FC6646DB498C864A3C6C32A`
- 라이선스: GNU GPL v2
- 대응 소스: `third_party/TrollVNC` Git submodule

배포본의 **미러링 · 제어 → TrollVNC 설치** 버튼은 이 패키지를 자동으로 사용합니다. 패키지가 없는 개발 빌드에서는 `.deb` 파일 선택 창이 대신 열립니다.

## 개발 및 빌드

```powershell
git clone --recurse-submodules <repository-url>
cd <repository-directory>
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

다른 PC에 전달할 self-contained 배포본 생성:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

GitHub Actions는 push와 pull request마다 Windows x64 배포 ZIP을 만들며, `v`로 시작하는 태그를 push하면 GitHub Release에 ZIP과 SHA-256 파일을 게시합니다.

```powershell
git tag v1.1.0
git push origin v1.1.0
```

## 보안과 데이터

- SSH 비밀번호는 파일에 저장하지 않습니다.
- SSH 주소, 포트, 사용자 이름만 `%LocalAppData%\IosInspector\settings.json`에 저장됩니다.
- 미러링은 외부 네트워크에 VNC 포트를 노출하지 않는 **USB 연결** 사용을 권장합니다.
- 자동 설치 도구는 각 프로젝트의 GitHub 공개 릴리스에서 내려받아 프로그램 옆 `tools` 폴더에 저장합니다.
- 인증서(`.p12`), provisioning profile, IPA, 로컬 설정은 Git에서 제외됩니다.

## 제3자 구성요소

TrollVNC, SSH.NET 및 자동으로 내려받는 도구는 각각 별도의 라이선스를 따릅니다. 자세한 출처와 고지는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 확인하세요.
