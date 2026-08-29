namespace Looy.WindowsController;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-input-layout", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = WindowsController.IsNativeInputLayoutValid ? 0 : 87;
            return;
        }
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-native-input", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = WindowsController.IsNativeInputEngineValid ? 0 : 87;
            return;
        }
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-screen-recognition", StringComparison.OrdinalIgnoreCase)))
        {
            var reportPath = Path.Combine(Path.GetTempPath(), "looy-ocr-self-test.txt");
            try
            {
                var report = ScreenRecognitionService
                    .RunComponentSelfTestAsync()
                    .GetAwaiter()
                    .GetResult();
                File.WriteAllText(reportPath, report);
                Environment.ExitCode = 0;
            }
            catch (Exception exception)
            {
                File.WriteAllText(reportPath, exception.ToString());
                Environment.ExitCode = 87;
            }
            return;
        }
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-netease-automation", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = NeteaseMusicAutomation.RunComponentSelfTest() ? 0 : 87;
            return;
        }
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-screen-automation", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = ScreenAutomationHeuristics.RunComponentSelfTest() ? 0 : 87;
            return;
        }
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-safe-system-tools", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = WindowsSystemTools.RunComponentSelfTest() ? 0 : 87;
            return;
        }
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-welcome-page", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = WelcomeForm.RunComponentSelfTest() ? 0 : 87;
            return;
        }
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-window-close-routing", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = InstalledAppResolver.RunCloseRoutingSelfTest() ? 0 : 87;
            return;
        }
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-release-protection", StringComparison.OrdinalIgnoreCase)))
        {
#if RELEASE_PROTECTED
            Environment.ExitCode = 0;
#else
            Environment.ExitCode = 87;
#endif
            return;
        }
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.Equals("--self-test-device-license", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = DeviceLicenseClient.RunComponentSelfTest() ? 0 : 87;
            return;
        }

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

        var commandLineArguments = Environment.GetCommandLineArgs();
        var skipWelcomePage = commandLineArguments.Any(argument =>
            argument.Equals("--autostart", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("--elevated-restart", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("--skip-welcome", StringComparison.OrdinalIgnoreCase));
        if (!skipWelcomePage)
        {
            using var welcomeForm = new WelcomeForm();
            welcomeForm.ShowDialog();
        }

        try
        {
            using var licenseClient = new DeviceLicenseClient();
            using var activationForm = new ActivationForm(licenseClient);
            if (activationForm.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            Application.Run(new MainForm(licenseClient));
        }
        catch (Exception exception)
        {
            ShowFatalError(exception);
        }
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
                $"路遥智控遇到错误：\n\n{exception.Message}\n\n错误日志已保存到：\n{logDirectory}",
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
