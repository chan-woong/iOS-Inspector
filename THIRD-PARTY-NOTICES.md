# Third-party notices

This project uses or interoperates with the following third-party components. Each component remains subject to its own license.

## TrollVNC

- Project: <https://github.com/owngoal-dev/TrollVNC>
- Source revision vendored as a Git submodule: `a3e40816ea5b93a7c80c09625175893d15bd1070`
- License: GNU General Public License version 2
- License text: `third_party/TrollVNC/COPYING`
- Bundled binary: `packages/com.82flex.trollvnc_3.2-272_iphoneos-arm64.deb`
- SHA-256: `BF30364DBBBD08F55AD934AC02CA72D61B737DEF6FC6646DB498C864A3C6C32A`

The TrollVNC package is installed on the iOS device as a separate program and communicates with iOS Inspector through the VNC protocol and SSH-launched command line.

## SSH.NET

- Project: <https://github.com/sshnet/SSH.NET>
- NuGet package: `SSH.NET` 2026.0.0
- License: MIT

SSH.NET brings transitive dependencies including BouncyCastle.Cryptography and Microsoft.Extensions abstractions. Refer to the NuGet package metadata and each upstream project for the exact license text applicable to the distributed version.

## Runtime-downloaded tools

When the user approves automatic dependency installation, the application downloads Windows release artifacts from:

- libimobiledevice Windows builds: <https://github.com/jrjr/libimobiledevice-windows>
- ideviceinstaller/libimobiledevice builds: <https://github.com/L1ghtmann/libimobiledevice>
- zsign Windows builds: <https://github.com/zhlynn/zsign>
- appinst iOS package: <https://github.com/Cat-Ling/appinst>

These tools are not committed as part of this repository. Their upstream licenses apply.
