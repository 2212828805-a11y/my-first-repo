namespace Looy.WindowsController;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowFatalError(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ShowFatalError(exception);
            }
        };

        Application.Run(new MainForm());
    }

    private static void ShowFatalError(Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LooyWindowsController");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "startup-error.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}]\n{exception}\n\n");

            MessageBox.Show(
                $"路遥电脑控制器遇到错误：\n\n{exception.Message}\n\n错误日志已保存到：\n{logDirectory}",
                "运行错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // The UI may already be shutting down.
        }
    }
}
