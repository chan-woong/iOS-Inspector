using System.Diagnostics;
using IosInspector.Controls;
using IosInspector.Models;
using IosInspector.Services;

namespace IosInspector;

public sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(11, 18, 32);
    private static readonly Color Surface = Color.FromArgb(20, 29, 46);
    private static readonly Color SurfaceRaised = Color.FromArgb(29, 41, 61);
    private static readonly Color Border = Color.FromArgb(51, 65, 85);
    private static readonly Color Primary = Color.FromArgb(59, 130, 246);
    private static readonly Color TextPrimary = Color.FromArgb(241, 245, 249);
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
    private readonly IosDeviceService _service = new();
    private readonly DependencyInstaller _dependencies;
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private readonly SshTerminalSession _sshSession = new();
    private readonly ComboBox _devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = TextMuted, Text = "단말을 연결하고 새로고침하세요." };
    private readonly ThemedTabControl _tabs = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Point(20, 8),
        DrawMode = TabDrawMode.OwnerDrawFixed,
        SizeMode = TabSizeMode.Fixed,
        ItemSize = new Size(176, 42),
        HeaderBackColor = Surface
    };
    private readonly VncView _vncView = new() { Dock = DockStyle.Fill };
    private readonly TextBox _vncPort = new() { Text = "5901", Width = 82 };
    private readonly ListView _files = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false };
    private readonly TextBox _remotePath = new() { Text = "/", Width = 520 };
    private readonly RichTextBox _sshOutput = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(8, 11, 16), ForeColor = Color.FromArgb(134, 239, 172), BorderStyle = BorderStyle.None, Font = new Font("Cascadia Mono", 10) };
    private readonly TextBox _sshCommand = new() { Dock = DockStyle.Fill, Font = new Font("Cascadia Mono", 10) };
    private readonly TextBox _sshHost = new() { Width = 190 };
    private readonly TextBox _sshPort = new() { Width = 82 };
    private readonly TextBox _sshUsbPort = new() { Width = 82 };
    private readonly TextBox _sshUser = new() { Width = 120 };
    private readonly TextBox _sshPassword = new() { Width = 170, UseSystemPasswordChar = true };
    private readonly TextBox _ipaPath = new() { Width = 600, AllowDrop = true };
    private readonly TextBox _p12Path = new() { Width = 600 };
    private readonly TextBox _profilePath = new() { Width = 600 };
    private readonly TextBox _p12Password = new() { Width = 300, UseSystemPasswordChar = true };
    private readonly Label _ipaSignatureStatus = new() { AutoSize = false, Width = 820, Height = 48, ForeColor = TextMuted, Text = "IPA를 드래그하거나 찾아보기로 선택하세요." };
    private readonly Panel _standardSigningPanel = new() { Width = 980, Height = 250, Visible = false, BackColor = Surface };
    private readonly TabPage _sshTab;
    private readonly TabPage _mirrorTab;
    private readonly TabPage _filesTab;
    private readonly TabPage _appsTab;
    private readonly TabPage _setupTab;
    private Process? _sshTunnel;
    private Process? _vncTunnel;

    private DeviceInfo? CurrentDevice => _devices.SelectedItem as DeviceInfo;

    public MainForm()
    {
        _dependencies = new DependencyInstaller(_service.ToolDirectory);
        _settings = _settingsStore.Load();
        _sshHost.Text = _settings.SshHost;
        _sshPort.Text = _settings.SshPort.ToString();
        _sshUsbPort.Text = _settings.UsbLocalPort.ToString();
        _sshUser.Text = _settings.SshUser;
        Text = "iOS Inspector 1.1";
        MinimumSize = new Size(1120, 720);
        Size = new Size(1320, 880);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10.5f);
        DoubleBuffered = true;
        StyleInputs(_devices, _vncPort, _remotePath, _sshCommand, _sshHost, _sshPort, _sshUsbPort, _sshUser,
            _sshPassword, _ipaPath, _p12Path, _profilePath, _p12Password);
        _files.BackColor = Color.FromArgb(10, 16, 28);
        _files.ForeColor = TextPrimary;
        _files.BorderStyle = BorderStyle.FixedSingle;
        _sshOutput.BorderStyle = BorderStyle.FixedSingle;

        var header = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = Surface, Padding = new Padding(28, 18, 28, 14) };
        var title = new Label { Text = "iOS Inspector", Font = new Font("Segoe UI Semibold", 21), AutoSize = true, Location = new Point(28, 14), ForeColor = TextPrimary };
        var subtitle = new Label { Text = "Jailbroken iOS inspection workspace", AutoSize = true, Location = new Point(30, 52), ForeColor = TextMuted, Font = new Font("Segoe UI", 9.5f) };
        var deviceLabel = new Label { Text = "연결 단말", AutoSize = true, Location = new Point(420, 24), ForeColor = TextMuted };
        _devices.Location = new Point(490, 19);
        var refresh = Button("새로고침", 822, 17, async (_, _) => await RefreshDevicesAsync());
        refresh.Width = 104;
        _status.Location = new Point(420, 65);
        var headerBorder = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Border };
        header.Controls.AddRange([title, subtitle, deviceLabel, _devices, refresh, _status, headerBorder]);

        _sshTab = CreateSshTab();
        _mirrorTab = CreateMirrorTab();
        _filesTab = CreateFilesTab();
        _appsTab = CreateAppsTab();
        _setupTab = CreateSetupTab();
        _tabs.TabPages.AddRange([_sshTab, _mirrorTab, _filesTab, _appsTab, _setupTab]);
        _tabs.DrawItem += DrawTab;
        _tabs.Selecting += (_, e) =>
        {
            if (_sshSession.IsConnected || e.TabPage == _sshTab || e.TabPage == _setupTab) return;
            e.Cancel = true;
            _status.Text = "먼저 SSH에 연결해야 다른 기능을 사용할 수 있습니다.";
            MessageBox.Show("이 프로그램의 모든 점검 기능은 SSH 연결 후 사용할 수 있습니다.\nSSH 터미널 탭에서 IP 또는 USB SSH 연결을 먼저 완료하세요.",
                "SSH 연결 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        Controls.Add(_tabs);
        Controls.Add(header);
        Shown += async (_, _) => await InitializeAsync();
        FormClosing += (_, _) => { DisconnectSsh(); StopVnc(); };
    }

    private async Task InitializeAsync()
    {
        var status = _dependencies.GetStatus();
        if (!status.AllReady)
        {
            var missing = new List<string>(status.MissingCore);
            if (status.ZsignMissing) missing.Add("zsign");
            var answer = MessageBox.Show(
                "iPhone 연결에 필요한 도구가 없습니다.\n\n" + string.Join(", ", missing) +
                "\n\nGitHub 공개 릴리스에서 Windows x64 도구를 다운로드하여 이 프로그램의 tools 폴더에 설치할까요?\n" +
                "• github.com/jrjr/libimobiledevice-windows\n• github.com/L1ghtmann/libimobiledevice\n• github.com/zhlynn/zsign",
                "필수 도구 설치", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (answer == DialogResult.Yes)
            {
                try
                {
                    UseWaitCursor = true;
                    var progress = new Progress<string>(message => _status.Text = message);
                    await _dependencies.InstallAllAsync(progress);
                    _status.Text = "도구 설치 완료. 단말을 검색합니다…";
                }
                catch (Exception ex)
                {
                    ShowError("도구 자동 설치에 실패했습니다. 네트워크 또는 GitHub 접근 상태를 확인하세요.\n\n" + ex.Message);
                    if (!_dependencies.GetStatus().CoreReady)
                    {
                        _status.Text = "필수 도구 설치가 필요합니다.";
                        return;
                    }
                }
                finally { UseWaitCursor = false; }
            }
            else if (!status.CoreReady)
            {
                _status.Text = "필수 도구가 없어 단말 검색을 시작하지 않았습니다.";
                return;
            }
        }
        await RefreshDevicesAsync();
        _tabs.SelectedTab = _sshTab;
        _status.Text = "1단계: 단말 IP·계정·비밀번호를 입력하고 SSH에 연결하세요.";
        _sshPassword.Focus();
    }

    private TabPage CreateMirrorTab()
    {
        var page = Page("미러링 · 제어");
        var top = Toolbar(68);
        top.Controls.Add(new Label { Text = "VNC 포트", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        top.Controls.Add(_vncPort);
        top.Controls.Add(Button("TrollVNC 설치", 0, 0, async (_, _) => await InstallTrollVncPackageAsync()));
        top.Controls.Add(Button("USB 연결", 0, 0, async (_, _) => await ConnectVncAsync(true)));
        top.Controls.Add(Button("IP 연결", 0, 0, async (_, _) => await ConnectVncAsync(false)));
        top.Controls.Add(Button("중지", 0, 0, (_, _) => StopVnc()));
        top.Controls.Add(new Label { Text = "화면 클릭 후 키보드 입력 · Ctrl 단축키는 iOS Command로 전달", AutoSize = true, Margin = new Padding(16, 9, 0, 0), ForeColor = TextMuted });

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            Padding = new Padding(12, 9, 12, 8),
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Surface
        };
        navigation.Controls.AddRange([
            DeviceButton("‹  뒤로", IosRemoteAction.Back),
            DeviceButton("○  홈", IosRemoteAction.Home),
            DeviceButton("▣  앱 전환", IosRemoteAction.AppSwitcher),
            DeviceButton("잠금", IosRemoteAction.Lock),
            DeviceButton("음량 −", IosRemoteAction.VolumeDown),
            DeviceButton("음소거", IosRemoteAction.Mute),
            DeviceButton("음량 +", IosRemoteAction.VolumeUp),
            DeviceButton("화면 캡처", IosRemoteAction.Screenshot)
        ]);
        page.Controls.Add(_vncView);
        page.Controls.Add(navigation);
        page.Controls.Add(top);
        return page;
    }

    private Button DeviceButton(string text, IosRemoteAction action)
    {
        var button = Button(text, 0, 0, async (_, _) =>
        {
            try { await _vncView.SendDeviceActionAsync(action); _vncView.FocusViewer(); }
            catch (Exception ex) { ShowError("단말 버튼 전송 실패: " + ex.Message); }
        });
        button.Margin = new Padding(4, 0, 4, 0);
        button.Height = 38;
        return button;
    }

    private TabPage CreateSshTab()
    {
        var page = Page("SSH 터미널");
        var connection = Toolbar(112);
        connection.Controls.AddRange([
            FieldLabel("단말 IP"), _sshHost,
            FieldLabel("SSH 포트"), _sshPort,
            FieldLabel("사용자"), _sshUser,
            FieldLabel("비밀번호"), _sshPassword,
            Button("IP로 연결", 0, 0, async (_, _) => await ConnectSshAsync(false)),
            FieldLabel("USB 로컬 포트"), _sshUsbPort,
            Button("USB SSH 연결", 0, 0, async (_, _) => await ConnectSshAsync(true)),
            Button("연결 종료", 0, 0, (_, _) => DisconnectSsh())
        ]);
        var commandPanel = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 58, ColumnCount = 2, Padding = new Padding(10, 9, 10, 9), BackColor = Surface };
        commandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        commandPanel.Controls.Add(_sshCommand, 0, 0);
        commandPanel.Controls.Add(Button("전송", 0, 0, async (_, _) => await SendSshCommandAsync()), 1, 0);
        _sshCommand.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendSshCommandAsync(); } };
        page.Controls.Add(_sshOutput);
        page.Controls.Add(commandPanel);
        page.Controls.Add(connection);
        return page;
    }

    private TabPage CreateFilesTab()
    {
        var page = Page("파일 탐색기");
        _files.Columns.Add("이름", 480);
        _files.Columns.Add("유형", 100);
        _files.Columns.Add("크기", 120);
        _files.DoubleClick += async (_, _) => await OpenSelectedEntryAsync();
        var top = Toolbar(62);
        top.Controls.Add(Button("↑", 0, 0, async (_, _) => await GoParentAsync()));
        top.Controls.Add(_remotePath);
        top.Controls.Add(Button("이동", 0, 0, async (_, _) => await LoadFilesAsync()));
        top.Controls.Add(Button("새로고침", 0, 0, async (_, _) => await LoadFilesAsync()));
        var bottom = Toolbar(62, DockStyle.Bottom);
        bottom.Controls.Add(Button("업로드", 0, 0, async (_, _) => await UploadSelectedAsync()));
        bottom.Controls.Add(Button("다운로드", 0, 0, async (_, _) => await DownloadSelectedAsync()));
        bottom.Controls.Add(Button("새 폴더", 0, 0, async (_, _) => await CreateFolderAsync()));
        bottom.Controls.Add(Button("삭제", 0, 0, async (_, _) => await DeleteSelectedAsync()));
        page.Controls.Add(_files);
        page.Controls.Add(bottom);
        page.Controls.Add(top);
        return page;
    }

    private TabPage CreateAppsTab()
    {
        var page = Page("앱 설치 · 재서명");
        var panel = FormPanel();
        AddExistingField(panel, "IPA 파일", _ipaPath, 0);
        panel.Controls.Add(Button("찾아보기", 800, 26, async (_, _) => await BrowseIpaAsync()));
        _ipaSignatureStatus.Location = new Point(180, 96);
        panel.Controls.Add(_ipaSignatureStatus);
        panel.Controls.Add(Button("탈옥 단말로 설치", 180, 154, async (_, _) => await JailbreakInstallAsync()));
        var advanced = new CheckBox { Text = "일반 단말용 P12 / provisioning 서명 옵션", AutoSize = true, Location = new Point(180, 218), ForeColor = TextPrimary };
        advanced.CheckedChanged += (_, _) => _standardSigningPanel.Visible = advanced.Checked;
        panel.Controls.Add(advanced);

        _standardSigningPanel.Location = new Point(28, 260);
        AddExistingField(_standardSigningPanel, "P12 인증서", _p12Path, 0);
        AddExistingField(_standardSigningPanel, "프로비저닝", _profilePath, 1);
        AddExistingField(_standardSigningPanel, "P12 암호", _p12Password, 2);
        _standardSigningPanel.Controls.Add(Button("찾아보기", 800, 26, (_, _) => BrowseInto(_p12Path, "인증서 (*.p12;*.pfx)|*.p12;*.pfx|모든 파일 (*.*)|*.*")));
        _standardSigningPanel.Controls.Add(Button("찾아보기", 800, 98, (_, _) => BrowseInto(_profilePath, "Provisioning profile (*.mobileprovision)|*.mobileprovision|모든 파일 (*.*)|*.*")));
        _standardSigningPanel.Controls.Add(Button("일반 단말 서명 확인 후 설치", 180, 206, async (_, _) => await SmartInstallAsync()));
        _standardSigningPanel.Controls.Add(Button("재서명 파일만 저장", 440, 206, async (_, _) => await ResignAsync(_ipaPath.Text, _p12Path.Text, _p12Password.Text, _profilePath.Text)));
        panel.Controls.Add(_standardSigningPanel);

        _ipaPath.DragEnter += (_, e) => e.Effect = HasIpa(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        _ipaPath.DragDrop += async (_, e) =>
        {
            var path = GetDroppedIpa(e.Data);
            if (path is not null) await SelectIpaAsync(path);
        };
        page.Controls.Add(panel);
        return page;
    }

    private TabPage CreateSetupTab()
    {
        var page = Page("환경 점검");
        var label = new Label
        {
            Dock = DockStyle.Fill, Padding = new Padding(24), AutoSize = false,
            Text = "필수 구성\r\n\r\n1. Apple Devices 또는 iTunes 설치 (Apple USB 드라이버)\r\n2. libimobiledevice와 zsign은 최초 실행 시 자동 설치 가능\r\n3. 프로그램 시작 후 SSH를 먼저 연결해야 미러링·파일·앱 기능 사용 가능\r\n4. 미러링은 탈옥 단말에 TrollVNC rootless 패키지를 설치한 뒤 USB 연결 사용\r\n\r\n도구 설치 위치: " + _service.ToolDirectory + "\r\n\r\n지원 도구: idevice_id, ideviceinfo, idevicesyslog, ideviceinstaller, afcclient, iproxy, zsign"
        };
        page.Controls.Add(label);
        return page;
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            _status.Text = "단말 검색 중…";
            var devices = await _service.GetDevicesAsync();
            _devices.DataSource = devices.ToList();
            _devices.DisplayMember = nameof(DeviceInfo.DisplayName);
            _status.Text = devices.Count == 0 ? "연결된 단말이 없습니다. 잠금 해제 후 '이 컴퓨터를 신뢰'를 선택하세요." : $"{devices.Count}개 단말 연결됨";
        }
        catch (Exception ex) { ShowError(ex.Message); _status.Text = "도구 또는 드라이버를 확인하세요."; }
    }

    private async Task ConnectSshAsync(bool useUsbTunnel)
    {
        if (!int.TryParse(_sshPort.Text, out var sshPort) || sshPort is < 1 or > 65535 ||
            !int.TryParse(_sshUsbPort.Text, out var usbPort) || usbPort is < 1 or > 65535)
        { ShowError("올바른 SSH 포트와 USB 로컬 포트를 입력하세요."); return; }
        if (string.IsNullOrWhiteSpace(_sshHost.Text) || string.IsNullOrWhiteSpace(_sshUser.Text)) { ShowError("단말 IP와 사용자 이름을 입력하세요."); return; }
        if (string.IsNullOrEmpty(_sshPassword.Text)) { ShowError("SSH 비밀번호를 입력하세요. 비밀번호는 저장되지 않습니다."); return; }

        _settings.SshHost = _sshHost.Text.Trim();
        _settings.SshPort = sshPort;
        _settings.UsbLocalPort = usbPort;
        _settings.SshUser = _sshUser.Text.Trim();
        _settingsStore.Save(_settings);

        try
        {
            DisconnectSsh();
            var targetHost = _settings.SshHost;
            var targetPort = sshPort;
            if (useUsbTunnel)
            {
                if (CurrentDevice is null) { ShowError("먼저 단말을 선택하세요."); return; }
                _sshTunnel = _service.StartSshTunnel(CurrentDevice.Udid, usbPort,
                    line => BeginInvoke(() => _status.Text = "SSH 터널: " + line),
                    code => BeginInvoke(() => _status.Text = $"SSH 터널 종료 (코드 {code})"));
                targetHost = "127.0.0.1";
                targetPort = usbPort;
                _status.Text = $"USB SSH 터널 실행 중: {targetHost}:{targetPort} → iPhone:22";
                await Task.Delay(250);
            }
            _sshOutput.Clear();
            AppendSshOutput($"[연결] {_settings.SshUser}@{targetHost}:{targetPort}\r\n");
            await _sshSession.ConnectAsync(targetHost, targetPort, _settings.SshUser, _sshPassword.Text,
                ConfirmSshHostKey, AppendSshOutput);
            _status.Text = $"SSH 연결됨: {_settings.SshUser}@{targetHost}:{targetPort}";
            _status.Text += " · 미러링/파일/앱 설치 기능 사용 가능";
            _sshCommand.Focus();
        }
        catch (Exception ex) { AppendSshOutput($"\r\n[연결 실패] {ex.Message}\r\n"); ShowError("SSH 연결 실패: " + ex.Message); }
    }

    private async Task SendSshCommandAsync()
    {
        var command = _sshCommand.Text;
        if (string.IsNullOrWhiteSpace(command)) return;
        _sshCommand.Clear();
        _sshCommand.Enabled = false;
        try { await _sshSession.SendLineAsync(command); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _sshCommand.Enabled = true; _sshCommand.Focus(); }
    }

    private bool ConfirmSshHostKey(string fingerprint)
    {
        DialogResult Ask() => MessageBox.Show(
            "SSH 호스트 키를 확인하세요.\n\n" + fingerprint + "\n\n이 단말을 신뢰하고 연결할까요?",
            "SSH 호스트 키 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return InvokeRequired ? (DialogResult)Invoke(Ask) == DialogResult.Yes : Ask() == DialogResult.Yes;
    }

    private void AppendSshOutput(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendSshOutput(text)); return; }
        _sshOutput.AppendText(text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
        _sshOutput.SelectionStart = _sshOutput.TextLength;
        _sshOutput.ScrollToCaret();
    }

    private void DisconnectSsh()
    {
        _sshSession.Disconnect();
        if (_sshTunnel is { HasExited: false }) _sshTunnel.Kill(true);
        _sshTunnel?.Dispose();
        _sshTunnel = null;
        _status.Text = "SSH 연결 종료됨";
        if (Visible && !IsDisposed) _tabs.SelectedTab = _sshTab;
    }

    private async Task LoadFilesAsync()
    {
        if (CurrentDevice is null) { ShowError("먼저 단말을 선택하세요."); return; }
        try
        {
            _status.Text = $"폴더 읽는 중: {_remotePath.Text}";
            var entries = await _service.ListDirectoryAsync(CurrentDevice.Udid, NormalizeRemotePath(_remotePath.Text));
            _files.BeginUpdate();
            _files.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.Name) { Tag = entry, ImageIndex = entry.IsDirectory ? 0 : 1 };
                item.SubItems.Add(entry.TypeText);
                item.SubItems.Add(entry.SizeText);
                _files.Items.Add(item);
            }
            _files.EndUpdate();
            _status.Text = $"{entries.Count}개 항목";
        }
        catch (Exception ex) { _files.EndUpdate(); ShowError(ex.Message); _status.Text = "폴더를 읽지 못했습니다."; }
    }

    private async Task OpenSelectedEntryAsync()
    {
        if (_files.SelectedItems.Count == 0 || _files.SelectedItems[0].Tag is not AfcEntry entry || !entry.IsDirectory) return;
        _remotePath.Text = entry.FullPath;
        await LoadFilesAsync();
    }

    private async Task GoParentAsync()
    {
        var path = NormalizeRemotePath(_remotePath.Text).TrimEnd('/');
        var index = path.LastIndexOf('/');
        _remotePath.Text = index <= 0 ? "/" : path[..index];
        await LoadFilesAsync();
    }

    private async Task UploadSelectedAsync()
    {
        if (CurrentDevice is null) { ShowError("먼저 단말을 선택하세요."); return; }
        using var dialog = new OpenFileDialog { Title = "iPhone으로 업로드할 파일 선택" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var remote = CombineRemote(_remotePath.Text, Path.GetFileName(dialog.FileName));
        var result = await _service.UploadAsync(CurrentDevice.Udid, dialog.FileName, remote);
        ShowResult("업로드", result);
        if (result.Success) await LoadFilesAsync();
    }

    private async Task DownloadSelectedAsync()
    {
        if (CurrentDevice is null || _files.SelectedItems.Count == 0 || _files.SelectedItems[0].Tag is not AfcEntry entry) { ShowError("다운로드할 파일을 선택하세요."); return; }
        if (entry.IsDirectory) { ShowError("현재 버전에서는 파일을 선택해 다운로드하세요."); return; }
        using var dialog = new SaveFileDialog { FileName = entry.Name, Title = "저장 위치 선택" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        ShowResult("다운로드", await _service.DownloadAsync(CurrentDevice.Udid, entry.FullPath, dialog.FileName));
    }

    private async Task CreateFolderAsync()
    {
        if (CurrentDevice is null) { ShowError("먼저 단말을 선택하세요."); return; }
        var name = Prompt("새 폴더", "폴더 이름");
        if (string.IsNullOrWhiteSpace(name)) return;
        var result = await _service.CreateDirectoryAsync(CurrentDevice.Udid, CombineRemote(_remotePath.Text, name));
        ShowResult("폴더 생성", result);
        if (result.Success) await LoadFilesAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        if (CurrentDevice is null || _files.SelectedItems.Count == 0 || _files.SelectedItems[0].Tag is not AfcEntry entry) { ShowError("삭제할 항목을 선택하세요."); return; }
        if (MessageBox.Show($"'{entry.Name}'을(를) 삭제할까요?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var result = await _service.DeleteAsync(CurrentDevice.Udid, entry.FullPath, entry.IsDirectory);
        ShowResult("삭제", result);
        if (result.Success) await LoadFilesAsync();
    }

    private async Task ConnectVncAsync(bool useUsb)
    {
        if (!_sshSession.IsConnected) { ShowError("먼저 SSH 터미널 탭에서 탈옥 단말에 연결하세요."); return; }
        if (!int.TryParse(_vncPort.Text, out var port) || port is < 1 or > 65535) { ShowError("올바른 VNC 포트를 입력하세요."); return; }
        if (useUsb && CurrentDevice is null) { ShowError("USB 단말을 먼저 선택하세요."); return; }
        if (!useUsb && MessageBox.Show("현재 내장 VNC 클라이언트는 암호 없는 연결을 사용합니다. IP 연결은 같은 네트워크의 다른 사용자가 VNC 포트에 접근할 수 있습니다. 계속할까요?", "VNC 네트워크 노출 경고", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            StopVnc();
            var server = await FindTrollVncServerAsync();
            if (string.IsNullOrWhiteSpace(server))
            {
                ShowError("단말에 trollvncserver가 없습니다. 'TrollVNC 설치'에서 rootless .deb를 선택해 설치하세요.");
                return;
            }
            var bind = useUsb ? "127.0.0.1" : "0.0.0.0";
            _status.Text = "TrollVNC 서버 시작 중…";
            var start = await _sshSession.ExecuteCommandAsync(
                $"pkill -f trollvncserver >/dev/null 2>&1 || true; nohup {ShellQuote(server)} -b {bind} -p {port} -n IosInspector -s 0.75 -F 30 -B off -i off -I off -C off >/var/tmp/trollvnc.log 2>&1 &");
            if (!start.Success) { ShowRemoteResult("TrollVNC 시작", start); return; }
            await Task.Delay(700);

            var host = _settings.SshHost;
            if (useUsb)
            {
                _vncTunnel = _service.StartPortForward(CurrentDevice!.Udid, port, port,
                    line => BeginInvoke(() => _status.Text = "VNC 터널: " + line),
                    code => BeginInvoke(() => _status.Text = $"VNC 터널 종료 (코드 {code})"));
                host = "127.0.0.1";
                await Task.Delay(300);
            }
            await _vncView.ConnectAsync(host, port);
            _status.Text = $"TrollVNC 연결됨: {host}:{port}";
        }
        catch (Exception ex)
        {
            var log = await _sshSession.ExecuteCommandAsync("tail -30 /var/tmp/trollvnc.log 2>/dev/null");
            ShowError("미러링 연결 실패: " + ex.Message + (string.IsNullOrWhiteSpace(log.Output) ? "" : "\n\n" + log.Output));
        }
    }

    private void StopVnc()
    {
        _vncView.Disconnect();
        if (_vncTunnel is { HasExited: false }) _vncTunnel.Kill(true);
        _vncTunnel?.Dispose();
        _vncTunnel = null;
    }

    private async Task<string> FindTrollVncServerAsync()
    {
        if (!_sshSession.IsConnected) return "";
        var result = await _sshSession.ExecuteCommandAsync("command -v trollvncserver 2>/dev/null || find /var/jb/usr/bin /usr/bin -name trollvncserver -type f 2>/dev/null | head -1");
        return result.Output.Trim();
    }

    private async Task InstallTrollVncPackageAsync()
    {
        if (!_sshSession.IsConnected || CurrentDevice is null) { ShowError("먼저 SSH와 USB 단말을 연결하세요."); return; }
        var packagePath = FindBundledTrollVncPackage();
        if (packagePath is null)
        {
            using var dialog = new OpenFileDialog { Filter = "TrollVNC rootless 패키지 (*.deb)|*.deb", Title = "TrollVNC rootless .deb 선택" };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            packagePath = dialog.FileName;
        }
        try
        {
            UseWaitCursor = true;
            _status.Text = "내장 TrollVNC 패키지를 단말로 전송 중…";
            var name = Path.GetFileName(packagePath);
            _ = await _service.CreateDirectoryAsync(CurrentDevice.Udid, "/Downloads");
            var upload = await _service.UploadAsync(CurrentDevice.Udid, packagePath, "/Downloads/" + name);
            if (!upload.Success) { ShowResult("TrollVNC 전송", upload); return; }
            var remote = "/var/mobile/Media/Downloads/" + name;
            var install = await _sshSession.ExecuteCommandAsync($"dpkg -i {ShellQuote(remote)}");
            if (!install.Success)
                install = await _sshSession.ExecuteCommandAsync($"DEBIAN_FRONTEND=noninteractive apt-get -f install -y && dpkg -i {ShellQuote(remote)}");
            ShowRemoteResult("TrollVNC 설치", install);
            if (install.Success && string.IsNullOrWhiteSpace(await FindTrollVncServerAsync()))
                MessageBox.Show("설치는 완료됐지만 서버가 아직 로드되지 않았습니다. 단말을 respring한 뒤 다시 연결하세요.", "Respring 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { UseWaitCursor = false; }
    }

    private static string? FindBundledTrollVncPackage()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "packages");
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "com.82flex.trollvnc_*_iphoneos-arm64.deb")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private async Task JailbreakInstallAsync()
    {
        if (!Ready(_ipaPath.Text)) return;
        if (!_sshSession.IsConnected)
        {
            ShowError("먼저 SSH 터미널 탭에서 탈옥 단말에 연결하세요. 연결된 SSH 세션으로 appinst/ldid를 실행합니다.");
            return;
        }

        try
        {
            UseWaitCursor = true;
            _ipaSignatureStatus.ForeColor = Color.FromArgb(250, 204, 21);
            _ipaSignatureStatus.Text = "단말의 appinst / ldid 환경 확인 중…";
            var tools = await _sshSession.ExecuteCommandAsync(
                "printf 'INSTALLER=%s\\nLDID=%s\\nUNZIP=%s\\nZIP=%s\\nDPKG=%s\\nAPT=%s\\n' \"$(command -v appinst 2>/dev/null || command -v ipainstaller 2>/dev/null)\" \"$(command -v ldid 2>/dev/null)\" \"$(command -v unzip 2>/dev/null)\" \"$(command -v zip 2>/dev/null)\" \"$(command -v dpkg 2>/dev/null)\" \"$(command -v apt-get 2>/dev/null || command -v apt 2>/dev/null)\"");
            var values = tools.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2)).Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1]);
            var installer = values.GetValueOrDefault("INSTALLER", "");
            var ldid = values.GetValueOrDefault("LDID", "");
            if (string.IsNullOrWhiteSpace(installer))
            {
                installer = await InstallAppInstOnDeviceAsync(values);
                if (string.IsNullOrWhiteSpace(installer)) return;
            }

            _status.Text = "IPA를 단말로 전송 중…";
            _ = await _service.CreateDirectoryAsync(CurrentDevice!.Udid, "/Downloads");
            var fileName = Path.GetFileName(_ipaPath.Text);
            var afcPath = "/Downloads/" + fileName;
            var devicePath = "/var/mobile/Media/Downloads/" + fileName;
            var upload = await _service.UploadAsync(CurrentDevice.Udid, _ipaPath.Text, afcPath);
            if (!upload.Success) { ShowResult("IPA 전송", upload); return; }

            _status.Text = "appinst로 설치 중…";
            var install = await _sshSession.ExecuteCommandAsync($"{ShellQuote(installer)} {ShellQuote(devicePath)}");
            if (install.Success)
            {
                _ipaSignatureStatus.ForeColor = Color.FromArgb(74, 222, 128);
                _ipaSignatureStatus.Text = "탈옥 단말 설치 완료 · 인증서/provisioning 불필요";
                ShowRemoteResult("앱 설치", install);
                return;
            }

            var canFakeSign = !string.IsNullOrWhiteSpace(ldid) &&
                              !string.IsNullOrWhiteSpace(values.GetValueOrDefault("UNZIP", "")) &&
                              !string.IsNullOrWhiteSpace(values.GetValueOrDefault("ZIP", ""));
            if (!canFakeSign || MessageBox.Show(
                    "직접 설치에 실패했습니다. 단말의 ldid로 앱 내부 Mach-O 파일을 fake signing한 뒤 다시 설치할까요?\n\n" + install.Error,
                    "Fake signing 필요", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                ShowRemoteResult("앱 설치", install);
                return;
            }

            _status.Text = "단말에서 ldid fake signing 중…";
            var signedName = Path.GetFileNameWithoutExtension(fileName) + "-fakesigned.ipa";
            var script = "set -e; " +
                         "work=$(mktemp -d /var/tmp/iosinspector.XXXXXX); trap 'rm -rf \"$work\"' EXIT; " +
                         $"unzip -q {ShellQuote(devicePath)} -d \"$work\"; " +
                         "ent=\"$work/entitlements.plist\"; " +
                         $"find \"$work/Payload\" -type f | while IFS= read -r f; do if {ShellQuote(ldid)} -e \"$f\" >\"$ent\" 2>/dev/null; then if grep -q '<plist' \"$ent\"; then {ShellQuote(ldid)} -S\"$ent\" \"$f\"; else {ShellQuote(ldid)} -S \"$f\"; fi; fi; done; " +
                         $"signed=/var/tmp/{ShellQuote(signedName)}; (cd \"$work\" && zip -qry \"$signed\" Payload); {ShellQuote(installer)} \"$signed\"";
            var fakeSignedInstall = await _sshSession.ExecuteCommandAsync(script);
            ShowRemoteResult("Fake signing 후 설치", fakeSignedInstall);
            _ipaSignatureStatus.ForeColor = fakeSignedInstall.Success ? Color.FromArgb(74, 222, 128) : Color.FromArgb(248, 113, 113);
            _ipaSignatureStatus.Text = fakeSignedInstall.Success ? "ldid fake signing 및 설치 완료" : "ldid fake signing 또는 설치 실패";
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { UseWaitCursor = false; }
    }

    private async Task<string> InstallAppInstOnDeviceAsync(Dictionary<string, string> tools)
    {
        if (string.IsNullOrWhiteSpace(tools.GetValueOrDefault("DPKG", "")))
        {
            ShowError("단말에 appinst와 dpkg가 모두 없습니다. Sileo/Zebra 등 패키지 관리자에서 appinst를 설치하세요.");
            return "";
        }
        if (MessageBox.Show(
                "단말에 appinst가 없습니다.\n\nCat-Ling/appinst의 최신 rootless 패키지를 다운로드하여 단말에 설치할까요? 필요한 경우 apt가 의존성도 설치합니다.",
                "appinst 자동 설치", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return "";

        string? localPackage = null;
        try
        {
            _status.Text = "appinst rootless 패키지 다운로드 중…";
            var tempDirectory = Path.Combine(Path.GetTempPath(), "IosInspector", "appinst");
            localPackage = await _dependencies.DownloadLatestAssetAsync(
                "Cat-Ling/appinst",
                name => name.EndsWith("_iphoneos-arm64.deb", StringComparison.OrdinalIgnoreCase),
                tempDirectory);
            _ = await _service.CreateDirectoryAsync(CurrentDevice!.Udid, "/Downloads");
            var afcPath = "/Downloads/" + Path.GetFileName(localPackage);
            var devicePath = "/var/mobile/Media/Downloads/" + Path.GetFileName(localPackage);
            _status.Text = "appinst 패키지를 단말로 전송 중…";
            var upload = await _service.UploadAsync(CurrentDevice.Udid, localPackage, afcPath);
            if (!upload.Success) { ShowResult("appinst 전송", upload); return ""; }

            _status.Text = "단말에 appinst 설치 중…";
            var dpkg = tools["DPKG"];
            var install = await _sshSession.ExecuteCommandAsync($"{ShellQuote(dpkg)} -i {ShellQuote(devicePath)}");
            if (!install.Success && !string.IsNullOrWhiteSpace(tools.GetValueOrDefault("APT", "")))
            {
                var apt = tools["APT"];
                install = await _sshSession.ExecuteCommandAsync(
                    $"DEBIAN_FRONTEND=noninteractive {ShellQuote(apt)} -f install -y && {ShellQuote(dpkg)} -i {ShellQuote(devicePath)}");
            }
            if (!install.Success) { ShowRemoteResult("appinst 설치", install); return ""; }

            var detect = await _sshSession.ExecuteCommandAsync("command -v appinst 2>/dev/null || command -v ipainstaller 2>/dev/null");
            var installer = detect.Output.Trim();
            if (string.IsNullOrWhiteSpace(installer))
            {
                ShowError("패키지 설치는 완료됐지만 appinst 실행 파일을 찾지 못했습니다. 단말을 respring한 뒤 다시 시도하세요.");
                return "";
            }
            MessageBox.Show("appinst 설치가 완료됐습니다. 선택한 IPA 설치를 계속합니다.", "appinst 설치 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return installer;
        }
        finally
        {
            if (localPackage is not null) try { File.Delete(localPackage); } catch { }
        }
    }

    private async Task SmartInstallAsync()
    {
        if (!Ready(_ipaPath.Text)) return;
        try
        {
            UseWaitCursor = true;
            var signature = await AnalyzeIpaAsync(_ipaPath.Text);
            var ipaToInstall = _ipaPath.Text;
            if (!signature.IsValid)
            {
                var answer = MessageBox.Show(
                    signature.Summary + "\n\n현재 설정된 인증서로 재서명하시겠습니까?",
                    "IPA 재서명 필요", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
                ipaToInstall = await ResignForInstallAsync(_ipaPath.Text) ?? "";
                if (string.IsNullOrEmpty(ipaToInstall)) return;
            }

            _status.Text = "IPA 설치 중…";
            var install = await _service.InstallAsync(CurrentDevice!.Udid, ipaToInstall);
            if (install.Success)
            {
                ShowResult("앱 설치", install);
                _status.Text = "앱 설치 완료";
                return;
            }

            if (signature.IsValid && MessageBox.Show(
                    "서명 검사에서는 유효했지만 단말 설치에 실패했습니다. 프로비저닝에 현재 단말 UDID가 없거나 entitlement가 맞지 않을 수 있습니다.\n\n재서명 후 다시 설치할까요?\n\n" + install.Error,
                    "설치 실패", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                ipaToInstall = await ResignForInstallAsync(_ipaPath.Text) ?? "";
                if (string.IsNullOrEmpty(ipaToInstall)) return;
                ShowResult("재서명 후 앱 설치", await _service.InstallAsync(CurrentDevice.Udid, ipaToInstall));
            }
            else ShowResult("앱 설치", install);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { UseWaitCursor = false; }
    }

    private async Task<IpaSignatureStatus> AnalyzeIpaAsync(string ipa)
    {
        _ipaSignatureStatus.ForeColor = Color.FromArgb(250, 204, 21);
        _ipaSignatureStatus.Text = "서명 상태 확인 중…";
        var signature = await _service.CheckIpaSignatureAsync(ipa);
        _ipaSignatureStatus.ForeColor = signature.IsValid ? Color.FromArgb(74, 222, 128) : Color.FromArgb(251, 146, 60);
        _ipaSignatureStatus.Text = signature.Summary;
        return signature;
    }

    private async Task<string?> ResignForInstallAsync(string ipa)
    {
        if (!File.Exists(_p12Path.Text) || !File.Exists(_profilePath.Text))
        {
            ShowError("재서명하려면 P12 인증서와 provisioning profile을 먼저 선택하세요.");
            return null;
        }
        var directory = Path.GetDirectoryName(ipa)!;
        var output = Path.Combine(directory, Path.GetFileNameWithoutExtension(ipa) + "-signed.ipa");
        _status.Text = "IPA 재서명 중…";
        var result = await _service.ResignAsync(ipa, _p12Path.Text, _p12Password.Text, _profilePath.Text, output);
        if (!result.Success) { ShowResult("IPA 재서명", result); return null; }
        _ipaSignatureStatus.ForeColor = Color.FromArgb(74, 222, 128);
        _ipaSignatureStatus.Text = "재서명 완료 · " + output;
        return output;
    }

    private async Task BrowseIpaAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "iOS 앱 (*.ipa)|*.ipa", Title = "설치할 IPA 선택" };
        if (dialog.ShowDialog() == DialogResult.OK) await SelectIpaAsync(dialog.FileName);
    }

    private async Task SelectIpaAsync(string path)
    {
        _ipaPath.Text = path;
        _ipaSignatureStatus.ForeColor = Color.FromArgb(148, 163, 184);
        _ipaSignatureStatus.Text = "IPA 선택 완료 · 탈옥 단말 설치 시 P12/provisioning이 필요하지 않습니다.";
        await Task.CompletedTask;
    }

    private static void BrowseInto(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog() == DialogResult.OK) target.Text = dialog.FileName;
    }

    private static bool HasIpa(IDataObject? data) => GetDroppedIpa(data) is not null;
    private static string? GetDroppedIpa(IDataObject? data) =>
        data?.GetData(DataFormats.FileDrop) is string[] files
            ? files.FirstOrDefault(file => file.EndsWith(".ipa", StringComparison.OrdinalIgnoreCase))
            : null;

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
    private static void ShowRemoteResult(string title, RemoteCommandResult result) =>
        MessageBox.Show(result.Success ? $"{title} 완료\n\n{result.Output}" : $"{title} 실패\n\n{result.Error}\n{result.Output}", "iOS Inspector", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

    private async Task ResignAsync(string ipa, string p12, string password, string profile)
    {
        if (!File.Exists(ipa) || !File.Exists(p12) || !File.Exists(profile)) { ShowError("IPA, P12, 프로비저닝 파일을 확인하세요."); return; }
        using var dialog = new SaveFileDialog { Filter = "iOS 앱 (*.ipa)|*.ipa", FileName = Path.GetFileNameWithoutExtension(ipa) + "-signed.ipa" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var result = await _service.ResignAsync(ipa, p12, password, profile, dialog.FileName);
        ShowResult("재서명", result);
    }

    private bool Ready(string file)
    {
        if (CurrentDevice is null) { ShowError("먼저 단말을 선택하세요."); return false; }
        if (!File.Exists(file)) { ShowError("파일을 찾을 수 없습니다."); return false; }
        return true;
    }

    private static string NormalizeRemotePath(string path) => string.IsNullOrWhiteSpace(path) ? "/" : "/" + path.Trim().Trim('/');
    private static string CombineRemote(string parent, string name) => (NormalizeRemotePath(parent).TrimEnd('/') + "/" + name.Trim('/')).Replace("//", "/");

    private static string? Prompt(string title, string label)
    {
        using var form = new Form { Text = title, Width = 420, Height = 160, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var caption = new Label { Text = label, Left = 16, Top = 18, Width = 360 };
        var input = new TextBox { Left = 16, Top = 45, Width = 370 };
        var ok = new Button { Text = "확인", Left = 230, Top = 78, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "취소", Left = 311, Top = 78, Width = 75, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange([caption, input, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? input.Text : null;
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        var selected = e.Index == _tabs.SelectedIndex;
        var bounds = e.Bounds;
        var background = selected ? SurfaceRaised : Surface;
        using var fill = new SolidBrush(background);
        e.Graphics.FillRectangle(fill, bounds);
        if (selected)
        {
            using var accent = new SolidBrush(Primary);
            e.Graphics.FillRectangle(accent, bounds.Left + 10, bounds.Bottom - 3, bounds.Width - 20, 3);
        }
        TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, Font, bounds,
            selected ? TextPrimary : TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static FlowLayoutPanel Toolbar(int height, DockStyle dock = DockStyle.Top) => new()
    {
        Dock = dock,
        Height = height,
        Padding = new Padding(14, 11, 14, 10),
        WrapContents = true,
        FlowDirection = FlowDirection.LeftToRight,
        BackColor = Surface
    };

    private static void StyleInputs(params Control[] controls)
    {
        foreach (var control in controls)
        {
            switch (control)
            {
                case TextBox box:
                    StyleInput(box);
                    break;
                case ComboBox combo:
                    combo.BackColor = Color.FromArgb(10, 16, 28);
                    combo.ForeColor = TextPrimary;
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.Font = new Font("Segoe UI", 10.5f);
                    combo.Margin = new Padding(5, 4, 5, 4);
                    break;
            }
        }
    }

    private static void StyleInput(TextBox box)
    {
        box.BackColor = Color.FromArgb(10, 16, 28);
        box.ForeColor = TextPrimary;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font(box.Font.Name.Contains("Mono", StringComparison.OrdinalIgnoreCase) ? box.Font.Name : "Segoe UI", 10.5f);
        box.Margin = new Padding(5, 5, 5, 5);
    }

    private static TabPage Page(string text) => new(text) { BackColor = Background, ForeColor = TextPrimary, Padding = new Padding(16) };
    private static Panel FormPanel() => new() { Dock = DockStyle.Fill, Padding = new Padding(32), AutoScroll = true, BackColor = Background };
    private static TextBox AddField(Control parent, string label, string value, int row)
    {
        var y = 30 + row * 72;
        parent.Controls.Add(new Label { Text = label, Location = new Point(36, y + 8), Width = 132, ForeColor = TextMuted });
        var box = new TextBox { Text = value, Location = new Point(180, y), Width = 600 };
        StyleInput(box);
        parent.Controls.Add(box);
        return box;
    }
    private static void AddExistingField(Control parent, string label, TextBox box, int row)
    {
        var y = 30 + row * 72;
        parent.Controls.Add(new Label { Text = label, Location = new Point(36, y + 8), Width = 132, ForeColor = TextMuted });
        box.Location = new Point(180, y);
        parent.Controls.Add(box);
    }
    private static Button Button(string text, int x, int y, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(92, 36), Height = 36, Location = new Point(x, y), FlatStyle = FlatStyle.Flat, BackColor = SurfaceRaised, ForeColor = TextPrimary, Padding = new Padding(12, 3, 12, 3), Margin = new Padding(5, 3, 5, 3), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 56, 80);
        button.FlatAppearance.MouseDownBackColor = Primary;
        button.Click += handler;
        return button;
    }
    private static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(10, 10, 5, 0), ForeColor = TextMuted };
    private static void ShowError(string message) => MessageBox.Show(message, "iOS Inspector", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private static void ShowResult(string title, ToolResult result) => MessageBox.Show(result.Success ? $"{title} 완료\n\n{result.Output}" : $"{title} 실패\n\n{result.Error}", "iOS Inspector", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
}
