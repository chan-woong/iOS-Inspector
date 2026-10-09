using Renci.SshNet;
using IosInspector.Models;

namespace IosInspector.Services;

public sealed class SshTerminalSession : IDisposable
{
    private const string WorkingDirectoryMarker = "__IOS_INSPECTOR_PWD__";
    private SshClient? _client;
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
            args.CanTrust = confirmHostKey($"{args.HostKeyName} {fingerprint}");
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

    public void Disconnect()
    {
        if (_client?.IsConnected == true) _client.Disconnect();
        _client?.Dispose();
        _client = null;
        _onOutput = null;
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
