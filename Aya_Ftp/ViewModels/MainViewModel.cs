using System;
using System.IO;
using System.Net.Sockets;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;

namespace Aya_Ftp.ViewModels;

public class MainViewModel : ViewModelBase
{
    private string _host, _port, _username, _password, _connectButtonText, _outputLog;
    private bool _connecting, _connected;

    public string Host
    {
        get => _host;
        set => this.RaiseAndSetIfChanged(ref _host, value);
    }

    public string Port
    {
        get => _port;
        set => this.RaiseAndSetIfChanged(ref _port, value);
    }

    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public string ConnectButtonText
    {
        get => _connectButtonText;
        set => this.RaiseAndSetIfChanged(ref _connectButtonText, value);
    }

    private bool Connecting
    {
        get => _connecting;
        set => this.RaiseAndSetIfChanged(ref _connecting, value);
    }

    private bool Connected
    {
        get => _connected;
        set => this.RaiseAndSetIfChanged(ref _connected, value);
    }

    private ReactiveCommand<Unit, Unit> _connectCommandButton;
    public ReactiveCommand<Unit, Unit> ConnectCommandButton
    {
        get => _connectCommandButton;
        set => this.RaiseAndSetIfChanged(ref _connectCommandButton, value);
    }
    private readonly ReactiveCommand<Unit, Unit> _connectCommand, _disconnectCommand;

    public string OutputLog
    {
        get => _outputLog;
        set => this.RaiseAndSetIfChanged(ref _outputLog, value);
    }

    public MainViewModel()
    {
        _host = _port = _username = _password = _outputLog = string.Empty;
        _connecting = _connected = false;
        _connectButtonText = "建立连接";

        var connectObs = this.WhenAnyValue(x => x.Connecting, b => !b);
        _connectCommandButton = _connectCommand = ReactiveCommand.CreateFromTask(Connect, connectObs);
        _disconnectCommand = ReactiveCommand.CreateFromTask(DisConnect, connectObs);

        this.WhenAnyValue<MainViewModel, bool, bool>(x => x.Connected, b => b).Subscribe(ChangeConnectButton);
        ChangeConnectButton(Connected);
    }

    private void ChangeConnectButton(bool connected)
    {
        ConnectCommandButton = connected ? _disconnectCommand : _connectCommand;
        ConnectButtonText = connected ? "断开连接" : "建立连接";
    }

    private async Task Connect()
    {
        Connecting = true;
        _outputLogSb.Clear();

        try
        {
            var res = await FtpConnect(
                Host == "" ? "localhost" : Host,
                Port == "" ? "22" : Port,
                Username == "" ? "anonymous" : Username,
                Password);
            Connected = res;
        }
        catch(Exception e)
        {
            PrintLineToOutputLog($"FtpConnect {e.Message}");
        }

        Connecting = false;
    }

    private async Task DisConnect()
    {
        Connecting = true;

        try
        {
            var res = await FtpDisconnect();
            Connected = !res;
        }
        catch (Exception e)
        {
            PrintLineToOutputLog($"FtpDisconnect {e.Message}");
        }

        Connecting = false;
    }

    private readonly StringBuilder _outputLogSb = new();
    /**
     * <summary>将一行文本输出到日志</summary>
     * <param name="line">要输出的文本（无需包含换行）</param>
     */
    private void PrintLineToOutputLog(string line)
    {
        _outputLogSb.AppendLine(line);
        OutputLog = _outputLogSb.ToString();
    }

    // 可能用到的变量：
    private TcpClient cmdServer;
    private TcpClient dataServer;
    private NetworkStream cmdStrmWtr;
    private StreamReader cmdStrmRdr;
    private NetworkStream dataStrmWtr;
    private StreamReader dataStrmRdr;
    private string cmdData;
    private byte[] szData;

    /**
     * <summary>建立 ftp 连接</summary>
     * <param name="host">目标地址，可能为 IP 或域名</param>
     * <param name="port">连接到的 Ftp 控制端口</param>
     * <param name="username">连接使用的用户名</param>
     * <param name="password">连接使用的密码</param>
     * <returns>连接操作是否成功</returns>
     */
    private async Task<bool> FtpConnect(string host, string port, string username, string password)
    {
        throw new NotImplementedException();
        // TODO: 完善操作，将 Log 使用 PrintLineToOutputLog 输出
    }

    /**
    * <summary>断开 ftp 连接</summary>
    * <returns>断开操作是否成功</returns>
    */
    private async Task<bool> FtpDisconnect()
    {
        throw new NotImplementedException();
        // TODO
    }
}
