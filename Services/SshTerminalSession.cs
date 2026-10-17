using Renci.SshNet;
using Renci.SshNet.Common;
using IosInspector.Models;
using System.Security.Cryptography;

namespace IosInspector.Services;

public sealed class SshTerminalSession : IDisposable
{
    private const string WorkingDirectoryMarker = "__IOS_INSPECTOR_PWD__";
    private SshClient? _client;
    private SftpClient? _sftpClient;
    private byte[]? _trustedHostFingerprint;
    private string? _trustedHostKeyName;
    private Action<string>? _onOutput;
    private string _hostName = "iPhone";
    private string _workingDirectory = "~";
    private string _homeDirectory = "";
    private string _user = "root";
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(string host, int port, string user, string password,
        Func<string, bool> confirmHostKey, Action<string> onOutput, CancellationToken token = default)
    {
        Disconnect();
        var client = new SshClient(host, port, user, password);
        client.HostKeyReceived += (_, args) =>
        {
            var fingerprint = Convert.ToHexString(args.FingerPrint).ToLowerInvariant();
            var trusted = confirmHostKey($"{args.HostKeyName} {fingerprint}");
            args.CanTrust = trusted;
            if (trusted)
            {
                _trustedHostFingerprint = args.FingerPrint.ToArray();
                _trustedHostKeyName = args.HostKeyName;
            }
        };
        client.ErrorOccurred += (_, args) => onOutput($"\r\n[SSH 오류] {args.Exception.Message}\r\n");
        await Task.Run(client.Connect, token);

        _client = client;
        _onOutput = onOutput;
        _user = user;
        _hostName = (await RunSimpleAsync("hostname", token)).Trim();
        if (string.IsNullOrWhiteSpace(_hostName)) _hostName = "iPhone";
        _workingDirectory = (await RunSimpleAsync("pwd", token)).Trim();
        if (string.IsNullOrWhiteSpace(_workingDirectory)) _workingDirectory = "~";
        _homeDirectory = (await RunSimpleAsync("printf '%s' \"$HOME\"", token)).Trim();
        onOutput(Prompt);
    }

    public async Task SendLineAsync(string command, CancellationToken token = default)
    {
        if (_client is null || !IsConnected) throw new InvalidOperationException("SSH가 연결되지 않았습니다.");
        if (string.IsNullOrWhiteSpace(command)) return;
        await _commandLock.WaitAsync(token);
        try
        {
            _onOutput?.Invoke(command + "\r\n");
            var script = $"cd -- {Quote(_workingDirectory)} && {{ {command}; }}; printf '\\n{WorkingDirectoryMarker}%s\\n' \"$PWD\"";
            using var sshCommand = _client.CreateCommand(script);
            var result = await Task.Run(sshCommand.Execute, token);
            var error = sshCommand.Error;
            var markerIndex = result.LastIndexOf(WorkingDirectoryMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var directory = result[(markerIndex + WorkingDirectoryMarker.Length)..].Trim();
                if (!string.IsNullOrWhiteSpace(directory)) _workingDirectory = directory;
                result = result[..markerIndex].TrimEnd('\r', '\n');
            }
            if (!string.IsNullOrEmpty(result)) _onOutput?.Invoke(result + "\r\n");
            if (!string.IsNullOrWhiteSpace(error)) _onOutput?.Invoke(error.TrimEnd() + "\r\n");
            _onOutput?.Invoke(Prompt);
        }
        finally { _commandLock.Release(); }
    }

    public async Task<RemoteCommandResult> ExecuteCommandAsync(string command, CancellationToken token = default)
    {
        if (_client is null || !IsConnected) throw new InvalidOperationException("SSH가 연결되지 않았습니다.");
        await _commandLock.WaitAsync(token);
        try
        {
            using var sshCommand = _client.CreateCommand(command);
            var output = await Task.Run(sshCommand.Execute, token);
            return new RemoteCommandResult(sshCommand.ExitStatus ?? -1, output, sshCommand.Error);
        }
        finally { _commandLock.Release(); }
    }

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<ulong>? progress = null,
        CancellationToken token = default)
    {
        if (_client is null || !IsConnected) throw new InvalidOperationException("SSH가 연결되지 않았습니다.");
        if (!File.Exists(localPath)) throw new FileNotFoundException("전송할 파일을 찾을 수 없습니다.", localPath);

        await _commandLock.WaitAsync(token);
        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    if (_sftpClient?.IsConnected != true)
                    {
                        _sftpClient?.Dispose();
                        _sftpClient = new SftpClient(_client.ConnectionInfo)
                        {
                            OperationTimeout = TimeSpan.FromMinutes(10),
                            KeepAliveInterval = TimeSpan.FromSeconds(15)
                        };
                        _sftpClient.HostKeyReceived += ValidateSftpHostKey;
                        await Task.Run(_sftpClient.Connect, token);
                    }

                    await using var input = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await Task.Run(() => _sftpClient.UploadFile(input, remotePath, true,
                        uploaded => progress?.Report(uploaded)), token);
                    return;
                }
                catch (Exception ex) when (attempt == 1 && ex is SshConnectionException or SshOperationTimeoutException or IOException)
                {
                    _sftpClient?.Dispose();
                    _sftpClient = null;
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "SSH SFTP 파일 전송에 실패했습니다. 단말의 OpenSSH와 sftp-server 설치 상태 및 저장 공간을 확인하세요.\n\n" + ex.Message, ex);
        }
        finally { _commandLock.Release(); }
    }

    public void Disconnect()
    {
        if (_sftpClient?.IsConnected == true) _sftpClient.Disconnect();
        _sftpClient?.Dispose();
        _sftpClient = null;
        if (_client?.IsConnected == true) _client.Disconnect();
        _client?.Dispose();
        _client = null;
        _trustedHostFingerprint = null;
        _trustedHostKeyName = null;
        _onOutput = null;
    }

    private void ValidateSftpHostKey(object? sender, HostKeyEventArgs args)
    {
        args.CanTrust = _trustedHostFingerprint is not null &&
                        string.Equals(args.HostKeyName, _trustedHostKeyName, StringComparison.Ordinal) &&
                        CryptographicOperations.FixedTimeEquals(args.FingerPrint, _trustedHostFingerprint);
    }

    private string Prompt => $"{_hostName}:{DisplayDirectory} {_user}# ";

    private string DisplayDirectory => !string.IsNullOrEmpty(_homeDirectory) &&
        (_workingDirectory.Equals(_homeDirectory, StringComparison.Ordinal) || _workingDirectory.StartsWith(_homeDirectory + "/", StringComparison.Ordinal))
        ? "~" + _workingDirectory[_homeDirectory.Length..]
        : _workingDirectory;

    private async Task<string> RunSimpleAsync(string command, CancellationToken token)
    {
        using var sshCommand = _client!.CreateCommand(command);
        return await Task.Run(sshCommand.Execute, token);
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    public void Dispose()
    {
        Disconnect();
        _commandLock.Dispose();
    }
}
