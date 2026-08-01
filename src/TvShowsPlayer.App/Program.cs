using Avalonia;
using Avalonia.Controls;

namespace TvShowsPlayer.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Любая необработанная ошибка должна попасть в журнал. Иначе пользователь
        // видит окно Windows с кодом вроде 0xE0434352 и адресом в памяти, по которым
        // понять причину невозможно — а мы теряем единственный шанс её узнать.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Write("НЕОБРАБОТАННАЯ ОШИБКА: " + Describe(e.ExceptionObject));

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write("ОШИБКА В ФОНОВОЙ ЗАДАЧЕ: " + Describe(e.Exception));
            e.SetObserved();   // фоновая задача не должна ронять канал
        };

        try
        {
            AppLog.Write($"запуск: {Environment.ProcessPath}");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            AppLog.Write("работа завершена штатно");
        }
        catch (Exception ex)
        {
            AppLog.Write("ОШИБКА ПРИ ЗАВЕРШЕНИИ РАБОТЫ: " + Describe(ex));
            throw;
        }
    }

    private static string Describe(object? error) =>
        error is Exception ex ? $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : error?.ToString() ?? "неизвестно";

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
