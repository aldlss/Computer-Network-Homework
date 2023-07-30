using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Avalonia.Controls.Documents;
using Avalonia.Metadata;
using Avalonia.Platform.Storage;
using Aya_Ftp.Model;
using Aya_Ftp.Views;
using ReactiveUI;

namespace Aya_Ftp.ViewModels;

public class MainViewModel : ViewModelBase
{
    private string _outputLog;

    private const int TimeoutSecond = 10;
    private readonly TimeSpan _timeoutTimeSpan = TimeSpan.FromSeconds(TimeoutSecond);
    
    #region Connect

    private string _host, _port, _username, _password, _connectButtonText;
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

    #endregion

    #region LocalFile

    private bool _uploading;

    private string _localPath;

    private FileSystemWatcher? _localFileSystemWatcher;

    private List<LocalFile> _localFilesList = new();

    public string LocalPath
    {
        get => _localPath;
        set => this.RaiseAndSetIfChanged(ref _localPath, value);
    }

    public List<LocalFile> LocalFilesList
    {
        get => _localFilesList;
        set => this.RaiseAndSetIfChanged(ref _localFilesList, value);
    }

    public List<LocalFile> SelectedFiles { get; } = new();

    public ReactiveCommand<Unit, Unit> SelectLocalFolderCommand { get; }

    public ReactiveCommand<Unit, Unit> UploadFileCommand { get; }

    private bool Uploading
    {
        get => _uploading;
        set => this.RaiseAndSetIfChanged(ref _uploading, value);
    }

    #endregion

    #region RemoteFile

    private bool _downloading;

    private List<LocalFile> _remoteFilesList = new();

    public List<LocalFile> SelectedRemoteFiles { get; } = new();

    public ReactiveCommand<Unit, Unit> RefreshRemoteFilesCommand { get; }

    public ReactiveCommand<Unit, Unit> DownloadFileCommand { get; }

    public List<LocalFile> RemoteFilesList
    {
        get => _remoteFilesList;
        set => this.RaiseAndSetIfChanged(ref _remoteFilesList, value);
    }

    public bool Downloading
    {
        get => _downloading;
        set => this.RaiseAndSetIfChanged(ref _downloading, value);
    }

    #endregion

    public string OutputLog
    {
        get => _outputLog;
        set => this.RaiseAndSetIfChanged(ref _outputLog, value);
    }

    public MainViewModel()
    {
        _outputLog = string.Empty;

        #region Connect

        _host = _port = _username = _password = string.Empty;
        _connecting = _connected = false;
        _connectButtonText = "建立连接";

        var connectingNegObs = this.WhenAnyValue(x => x.Connecting, b => !b);
        _connectCommandButton = _connectCommand = ReactiveCommand.CreateFromTask(Connect, connectingNegObs);
        _disconnectCommand = ReactiveCommand.CreateFromTask(DisConnect, connectingNegObs);

        this.WhenAnyValue<MainViewModel, bool, bool>(x => x.Connected, b => b).Subscribe(ChangeConnectButton);
        ChangeConnectButton(Connected);

        #endregion

        #region LocalFile

        SelectLocalFolderCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var folderList = await MainWindow.Instant.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions()
                {
                    AllowMultiple = false,
                    Title = "选择打开的文件夹",
                });
            if (folderList.Count != 0)
                LocalPath = Uri.UnescapeDataString(folderList[0].Path.AbsolutePath);
        });

        this.WhenAnyValue(x => x.LocalPath).Subscribe(localPath =>
        {
            LocalFilesList = new();
            _localFileSystemWatcher = null;
            if (Directory.Exists(localPath))
            {
                UpdateLocalFilesList(localPath);
                _localFileSystemWatcher = new()
                {
                    Path = localPath,
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName,
                };
                _localFileSystemWatcher.Renamed += (_, _) => UpdateLocalFilesList(localPath);
                _localFileSystemWatcher.Created += (_, _) => UpdateLocalFilesList(localPath);
                _localFileSystemWatcher.Deleted += (_, _) => UpdateLocalFilesList(localPath);
                _localFileSystemWatcher.EnableRaisingEvents = true;
            }
        });

        var canUpload = this.WhenAnyValue(x => x.Uploading, x => x.Connected,
            (uploading, connected) => !uploading && connected);
        UploadFileCommand = ReactiveCommand.CreateFromTask(UploadFile, canUpload);

        #endregion

        #region RemoteFile

        var connectedObs = this.WhenAnyValue(x => x.Connected);
        RefreshRemoteFilesCommand = ReactiveCommand.CreateFromTask(RefreshRemoteFiles, connectedObs);

        var canDownloadFile = this.WhenAnyValue(x => x.Downloading, x => x.Connected, x => x.LocalPath,
            (downloading, connected, localPath) => !downloading && connected && Directory.Exists(localPath));
        DownloadFileCommand = ReactiveCommand.CreateFromTask(DownloadRemoteFile, canDownloadFile);

        #endregion
    }

    // 可能用到的变量：
    private TcpClient? _cmdServer;
    private TcpClient? _dataServer;
    private NetworkStream? _cmdStreamWriter;
    private StreamReader? _cmdStreamReader;
    private NetworkStream? _dataStreamWriter;
    private StreamReader? _dataStreamReader;
    private byte[] _cmdData;

    #region OutputLog

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

    /**
     * <summary>获取 FTP 的响应（发送指令后用），同时将指令输出至 Log</summary>
     * <returns>获取到的响应</returns>
     */
    private async Task<string?> GetFtpResp()
    {
        if(_cmdStreamReader == null)
            throw new Exception("未连接到服务器");
        var resp = await _cmdStreamReader.ReadLineAsync();
        if (resp is not null)
        {
            PrintLineToOutputLog(resp);
        }

        return resp;
    }

    private int GetRespCode(string resp)
    {
        return int.Parse(resp.Substring(0, 3));
    }
    
    #endregion

    #region Connect

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
                Port == "" ? "21" : Port,
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

    private async Task FtpDataConnect()
    {
        if (_cmdServer == null)
            throw new Exception("未连接到服务器");
        if (_dataServer is not null)
            return;

        _cmdData = "PASV\n"u8.ToArray();
        await _cmdStreamWriter!.WriteAsync(_cmdData, 0, _cmdData.Length).WaitAsync(_timeoutTimeSpan);
        var resp = await GetFtpResp().WaitAsync(_timeoutTimeSpan);

        if (resp is null || GetRespCode(resp) != 227)
            throw new Exception("PASV 指令失败");

        var split = resp.Split(',');
        var host = string.Join('.', split[..4]);
        host = host.Split('(')[1];
        var port = int.Parse(split[4]) * 256 + int.Parse(split[5][..^2]);

        _dataServer = new();
        await _dataServer.ConnectAsync(host, port).WaitAsync(_timeoutTimeSpan);
        _dataStreamWriter = _dataServer.GetStream();
        _dataStreamReader = new(_dataServer.GetStream());
    }

    private async void FtpDataDisconnect()
    {
        if(_dataServer is null)
            return;

        _dataStreamReader!.Close();
        _dataStreamWriter!.Close();
        await GetFtpResp();


        _cmdData = "ABOR\n"u8.ToArray();
        _cmdStreamWriter!.Write(_cmdData, 0, _cmdData.Length);
        await GetFtpResp();
        _dataServer.Close();
        _dataServer = null;
    }

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
        _cmdServer = new();
        await _cmdServer.ConnectAsync(host, int.Parse(port)).WaitAsync(_timeoutTimeSpan);
        _cmdStreamWriter = _cmdServer.GetStream();
        _cmdStreamReader = new(_cmdServer.GetStream());
        await GetFtpResp().WaitAsync(_timeoutTimeSpan);

        // 使用该行代码将指令转为可被服务端接收的格式
        _cmdData = Encoding.ASCII.GetBytes($"USER {username}\n");
        // 发送指令给服务端，并异步等待一定时间（这里是 10 秒）
        await _cmdStreamWriter.WriteAsync(_cmdData, 0, _cmdData.Length).WaitAsync(_timeoutTimeSpan);
        // 获取服务端的响应，并输出至 Log
        await GetFtpResp().WaitAsync(_timeoutTimeSpan);

        _cmdData = Encoding.ASCII.GetBytes($"PASS {password}\n");
        await _cmdStreamWriter.WriteAsync(_cmdData, 0, _cmdData.Length).WaitAsync(_timeoutTimeSpan);
        // 判断服务端的响应码是否大于 500（即是否出错），出错返回错误
        var resp = await GetFtpResp().WaitAsync(_timeoutTimeSpan);
        if (resp == null || GetRespCode(resp) >= 500)
            return false;
        return true;
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

    #endregion

    #region LocalFile

    private void UpdateLocalFilesList(string localPath)
    {
        List<LocalFile> newLocalFilesList = new();
        Directory.EnumerateFiles(localPath).ToList().ForEach(path =>
        {
            newLocalFilesList.Add(new LocalFile()
            {
                Name = Path.GetFileName(path)
            });
        });
        LocalFilesList = newLocalFilesList;
    }

    private async Task UploadFile()
    {
        if (SelectedFiles.Count == 0 )
            return;
        string fullPath = Path.Join(LocalPath, SelectedFiles[0].Name);
        if(!File.Exists(fullPath))
            return;
        Uploading = true;
        
        try
        {
            var res = await FtpUploadFile(fullPath, Path.GetFileName(fullPath));
        }
        catch (Exception e)
        {
            PrintLineToOutputLog($"FtpUploadFile {e.Message}");
        }
        
        Uploading = false;
    }

    /**
     * <summary>将文件通过 FTP 上传至服务端</summary>
     * <param name="fileFullPath">要上传的文件的绝对路径（包括文件名）</param>
     * <param name="name">要上传的文件的文件名</param>
     * <returns>上传操作是否成功</returns>
     */
    private async Task<bool> FtpUploadFile(string fileFullPath, string name)
    {
        throw new NotImplementedException();
        // TODO
    }
    #endregion

    #region RemoteFile

    private async Task RefreshRemoteFiles()
    {
        try
        {
            var res = await FtpListFiles();
            RemoteFilesList = res;
        }
        catch (Exception e)
        {
            PrintLineToOutputLog($"FtpListFiles {e.Message}");
        }
    }

    private async Task DownloadRemoteFile()
    { 
        if (SelectedRemoteFiles.Count == 0)
            return;
        Downloading = true;

        try
        {
            var res = await FtpDownloadFile(LocalPath, SelectedRemoteFiles[0].Name);
        }
        catch (Exception e)
        {
            PrintLineToOutputLog($"FtpDownloadFile {e.Message}");
        }

        Downloading = false;
    }

    /**
     * <summary>获取服务端当前目录下的文件列表</summary>
     * <returns>服务端当前目录下的文件列表</returns>
     */
    private async Task<List<LocalFile>> FtpListFiles()
    {
        await FtpDataConnect().WaitAsync(_timeoutTimeSpan);

        _cmdData = "LIST\n"u8.ToArray();
        _cmdStreamWriter!.Write(_cmdData, 0, _cmdData.Length);
        await GetFtpResp().WaitAsync(_timeoutTimeSpan);
        
        List<LocalFile> remoteFilesList = new();
        while (await _dataStreamReader!.ReadLineAsync() is { } line)
        {
            var file = line.Split(' ');
            remoteFilesList.Add(new LocalFile()
            {
                Name = file[^1]
            });
        }
        
        FtpDataDisconnect();
        return remoteFilesList;
    }

    /**
     * <summary>将服务端的文件下载到本地</summary>
     * <param name="localPath">要下载到的本地目录</param>
     * <param name="name">要下载的文件名</param>
     * <returns>下载操作是否成功</returns>
     */
    private async Task<bool> FtpDownloadFile(string localPath, string name)
    {
        throw new NotImplementedException();
        // TODO
    }

    #endregion
}
