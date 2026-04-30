using System.Windows;
using ygo_draw.services;
using MessageBox = System.Windows.MessageBox;

namespace ygo_draw;

public partial class DownloadDialog : Window
{
    private readonly ProjectPaths _paths;
    private readonly ProcessService _processService;
    private readonly DatabaseConnectionSettings _databaseSettings;
    private bool _completed;

    public DownloadDialog(
        ProjectPaths paths,
        ProcessService processService,
        DatabaseConnectionSettings databaseSettings)
    {
        _paths = paths;
        _processService = processService;
        _databaseSettings = databaseSettings;
        InitializeComponent();
        LoadDatabaseSettings();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        Progress.IsIndeterminate = true;
        LogBox.Clear();
        _completed = false;

        try
        {
            SaveDatabaseSettings();
            AppendLog("开始下载并导入资源...");
            var command = await BuildDownloadCommandAsync();
            AppendLog($"使用 {command.FileName} 运行下载脚本。");
            var code = await _processService.RunAsync(command.FileName, command.Arguments, _paths.AssetToolsDir, AppendLog);
            if (code != 0)
            {
                AppendLog($"下载脚本失败，退出码 {code}");
                MessageBox.Show(this, "下载或导入失败，请查看日志。", "下载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _completed = true;
            AppendLog("资源更新完成。");
            MessageBox.Show(this, "资源更新完成。", "下载完成", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "下载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Progress.IsIndeterminate = false;
            StartButton.IsEnabled = true;
        }
    }

    private async Task<DownloadCommand> BuildDownloadCommandAsync()
    {
        var scriptArguments = BuildScriptArguments();
        if (await _processService.CanStartAsync("uv", "--version", _paths.AssetToolsDir))
        {
            return new DownloadCommand("uv", ["run", "python", "download_assets.py", ..scriptArguments]);
        }

        return new DownloadCommand("python", ["download_assets.py", ..scriptArguments]);
    }

    private IReadOnlyList<string> BuildScriptArguments()
    {
        return
        [
            "--db-host",
            _databaseSettings.Host,
            "--db-port",
            _databaseSettings.Port.ToString(),
            "--db-user",
            _databaseSettings.User,
            "--db-password",
            _databaseSettings.Password,
            "--db-name",
            _databaseSettings.Database,
            "--db-sslmode",
            _databaseSettings.SslMode.ToLowerInvariant()
        ];
    }

    private void LoadDatabaseSettings()
    {
        HostBox.Text = _databaseSettings.Host;
        PortBox.Text = _databaseSettings.Port.ToString();
        UserBox.Text = _databaseSettings.User;
        PasswordBox.Password = _databaseSettings.Password;
        DatabaseBox.Text = _databaseSettings.Database;
        SslModeBox.Text = _databaseSettings.SslMode;
    }

    private void SaveDatabaseSettings()
    {
        _databaseSettings.Host = HostBox.Text.Trim();
        _databaseSettings.Port = int.TryParse(PortBox.Text, out var port) ? port : 5432;
        _databaseSettings.User = UserBox.Text.Trim();
        _databaseSettings.Password = PasswordBox.Password;
        _databaseSettings.Database = DatabaseBox.Text.Trim();
        _databaseSettings.SslMode = SslModeBox.Text.Trim().ToLowerInvariant();
    }

    private void AppendLog(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _completed;
        Close();
    }

    private sealed record DownloadCommand(string FileName, IReadOnlyList<string> Arguments);
}
