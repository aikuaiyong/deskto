using Deskup.Wpf.Services;
using Deskup.Wpf.Shell;
using System.Windows;

namespace Deskup.Wpf;

public partial class App : Application
{
    private AppState? _state;
    private AppShell? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 捕获所有未处理的 WPF 异常
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show("未处理异常:\n" + ex.Exception.ToString(), "Deskup - 崩溃",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            var msg = ex.ExceptionObject?.ToString() ?? "未知错误";
            MessageBox.Show("致命错误:\n" + msg, "Deskup - 崩溃",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);
        try
        {
            _state = new AppState();
            _shell = new AppShell(_state);
            _shell.Start();
            TrayIcon.Create(this, _shell);
        }
        catch (Exception ex2)
        {
            MessageBox.Show("启动失败:\n" + ex2.ToString(), "Deskup - 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _state?.Save();
        TrayIcon.Dispose();
        base.OnExit(e);
    }
}
