using System.IO;
using System.Windows;

namespace LinkDrop;

public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "LinkDrop — błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        if (e.Args.Contains("--playlist-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await SelfTests.RunPlaylistNetwork(e.Args[1], e.Args.Last()));
            return;
        }
        if (e.Args.Contains("--network-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await SelfTests.RunNetwork(e.Args.Last()));
            return;
        }
        if (e.Args.Contains("--self-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var destination = e.Args.SkipWhile(a => a != "--self-test").Skip(1).FirstOrDefault() ?? "test-results";
            int result = await SelfTests.Run(destination);
            Shutdown(result);
            return;
        }
        bool testUi = e.Args.Contains("--ui-test");
        instance = new Mutex(true, testUi ? "Local\\LinkDrop.UiTest" : "Local\\LinkDrop.Personal", out bool first);
        if (!first) { MessageBox.Show("LinkDrop jest już uruchomiony. Sprawdź pasek zadań.", "LinkDrop"); Shutdown(); return; }
        string? dataDir = testUi ? Path.Combine(e.Args.Last(), "profile") : null;
        var window = new MainWindow(dataDir);
        MainWindow = window;
        window.Show();
        if (testUi) await SelfTests.RunUi(window, e.Args.Last());
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
