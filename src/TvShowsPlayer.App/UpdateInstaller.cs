using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.App;

/// <summary>
/// Обновление одной кнопкой: скачать архив релиза, проверить его и заменить файлы
/// программы. Заменить самого себя на ходу нельзя — работающий exe занят, поэтому
/// подмену делает отдельный сценарий: он ждёт закрытия программы, копирует новые
/// файлы поверх старых и запускает её снова.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>Имя главного файла — по нему проверяем, что скачали именно программу.</summary>
    private const string MainExecutable = "TvShowsPlayer.App.exe";

    public static string WorkFolder =>
        Path.Combine(Path.GetTempPath(), $"{Branding.AppName}-update");

    /// <summary>Скачать архив обновления. Возвращает путь к нему или null при неудаче.</summary>
    public static async Task<string?> DownloadAsync(
        HttpClient http, UpdateInfo update, IProgress<int> progress, CancellationToken ct)
    {
        if (update.DownloadUrl is null)
            return null;

        Directory.CreateDirectory(WorkFolder);
        var target = Path.Combine(WorkFolder, update.FileName ?? "update.zip");

        using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? update.DownloadSize;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(target))
        {
            var buffer = new byte[81920];
            long copied = 0;
            int read;
            var lastReported = -1;

            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;

                if (total <= 0)
                    continue;

                var percent = (int)(copied * 100 / total);
                if (percent != lastReported)
                {
                    lastReported = percent;
                    progress.Report(percent);
                }
            }
        }

        return target;
    }

    /// <summary>
    /// Распаковать новую версию РЯДОМ с установленной (папка «…\LocalTV.new») и
    /// убедиться, что внутри действительно программа. Рядом — потому что подмена
    /// делается переименованием папок: занятые файлы так не мешают, а переименование
    /// в пределах одного диска мгновенно.
    /// Возвращает путь к подготовленной папке или null.
    /// </summary>
    public static string? PrepareNewVersion(string zipPath, string installDir)
    {
        var staging = installDir + ".new";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);

        CleanupLeftovers(installDir);

        var unpacked = Path.Combine(WorkFolder, "unpacked");
        if (Directory.Exists(unpacked))
            Directory.Delete(unpacked, recursive: true);

        ZipFile.ExtractToDirectory(zipPath, unpacked);

        // В архиве верхняя папка (LocalTV) — нас интересует та, где лежит exe.
        var exe = Directory
            .EnumerateFiles(unpacked, MainExecutable, SearchOption.AllDirectories)
            .FirstOrDefault();

        if (exe is null)
            return null;

        Directory.Move(Path.GetDirectoryName(exe)!, staging);

        return staging;
    }

    /// <summary>Убрать папки, оставшиеся от прошлых обновлений (могли не удалиться).</summary>
    private static void CleanupLeftovers(string installDir)
    {
        var parent = Path.GetDirectoryName(installDir);
        var prefix = Path.GetFileName(installDir) + ".old-";
        if (parent is null || !Directory.Exists(parent))
            return;

        foreach (var folder in Directory.EnumerateDirectories(parent, prefix + "*"))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ещё занята — уберём в другой раз
            }
        }
    }

    /// <summary>
    /// Запустить подмену файлов и перезапуск. Программа после этого должна закрыться:
    /// сценарий ждёт именно её завершения.
    /// </summary>
    public static void LaunchSwap(string newVersionDir, string installDir, int processId)
    {
        Directory.CreateDirectory(WorkFolder);   // папки может не быть: временные чистятся
        var script = Path.Combine(WorkFolder, "update.ps1");
        File.WriteAllText(script, SwapScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),   // не держим папку программы занятой
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(script);
        info.ArgumentList.Add(newVersionDir);
        info.ArgumentList.Add(installDir);
        info.ArgumentList.Add(processId.ToString());

        Process.Start(info);
    }

    // Сценарий подмены. Пишем ПОВЕРХ, ничего заранее не удаляя: если копирование
    // сорвётся, старая версия останется рабочей. Обо всём ведём протокол рядом —
    // без него неудачное обновление выглядело бы как «программа пропала».
    private const string SwapScript = """
        param([string]$NewVersion, [string]$InstallDir, [int]$ProcessId)

        $ErrorActionPreference = 'Stop'
        $log = Join-Path $env:TEMP 'LocalTV-update\update.log'
        function Note($text) {
            "$([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss')) $text" | Out-File -FilePath $log -Append -Encoding utf8
        }

        $exe = Join-Path $InstallDir 'TvShowsPlayer.App.exe'
        $old = "$InstallDir.old-" + (Get-Date -Format 'yyyyMMdd-HHmmss')

        try {
            Note "жду закрытия программы (PID $ProcessId)"
            for ($i = 0; $i -lt 30; $i++) {
                if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) { break }
                Start-Sleep -Milliseconds 500
            }

            # Переименование, а не перезапись: занятые файлы (проигрыватель, сама
            # программа, антивирусная проверка) переписать нельзя, а переименовать
            # папку Windows разрешает даже во время работы.
            Note "отодвигаю прежнюю версию: $InstallDir -> $old"
            Move-Item -LiteralPath $InstallDir -Destination $old -ErrorAction Stop

            try {
                Note "ставлю новую версию: $NewVersion -> $InstallDir"
                Move-Item -LiteralPath $NewVersion -Destination $InstallDir -ErrorAction Stop
            } catch {
                # Новую поставить не вышло — возвращаем прежнюю, чтобы человек не
                # остался без программы.
                Note "не удалось поставить новую версию: $($_.Exception.Message)"
                Move-Item -LiteralPath $old -Destination $InstallDir -Force
                throw
            }

            # Запуск проверяем: программа должна не только стартовать, но и остаться
            # работать. Иначе человек после обновления остался бы без канала и без
            # единого следа о том, что случилось.
            $started = $false
            for ($try = 1; $try -le 3; $try++) {
                Note "запускаю $exe (попытка $try)"
                $launched = Start-Process -FilePath $exe -WorkingDirectory $InstallDir -PassThru
                Start-Sleep -Seconds 4
                if ($launched -and -not $launched.HasExited) { $started = $true; break }
                Note 'программа не удержалась — пробую снова'
                Start-Sleep -Seconds 3
            }

            Note $(if ($started) { 'обновление завершено' } else { 'ВНИМАНИЕ: программа не запустилась после обновления' })

            # Прежняя версия больше не нужна. Не удалилась — уберём при следующем
            # обновлении, это не повод пугать пользователя.
            Start-Sleep -Seconds 5
            Remove-Item -LiteralPath $old -Recurse -Force -ErrorAction SilentlyContinue
        } catch {
            Note "ошибка обновления: $($_.Exception.Message)"
            try { Start-Process -FilePath $exe -WorkingDirectory $InstallDir } catch { Note 'запустить программу не удалось' }
        }
        """;
}
