using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.App;

/// <summary>
/// Обновление одной кнопкой: скачать архив релиза, проверить его и поставить новую
/// версию. Заменить самого себя на ходу нельзя — работающий exe занят, поэтому
/// подмену делает отдельный сценарий: он ждёт закрытия программы, переименовывает
/// папки и запускает её снова.
///
/// Всё происходит РЯДОМ С ПРОГРАММОЙ: скачивание — в её папку, распаковка — в
/// соседнюю с тем же именем и суффиксом. Временная папка системы не годится: она
/// обычно на другом диске, а переносить папку между дисками Windows не позволяет.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>Имя главного файла — по нему проверяем, что скачали именно программу.</summary>
    private const string MainExecutable = "TvShowsPlayer.App.exe";

    /// <summary>Куда скачиваем архив — внутрь папки программы (уедет вместе с ней).</summary>
    public static string DownloadFolder(string installDir) => Path.Combine(installDir, "update");

    /// <summary>
    /// Получится ли обновиться на этом месте. Писать нужно и в саму папку программы
    /// (туда скачивается архив), и рядом с ней (там появляется новая версия, туда же
    /// отодвигается прежняя). В защищённых местах вроде «Program Files» этого нельзя —
    /// лучше сказать об этом до скачивания сотни мегабайт.
    /// </summary>
    public static bool CanUpdateInPlace(string installDir)
    {
        var parent = Path.GetDirectoryName(installDir);

        return IsWritable(installDir) && parent is not null && IsWritable(parent);
    }

    private static bool IsWritable(string folder)
    {
        var probe = Path.Combine(folder, $"localtv-{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Скачать архив обновления. Возвращает путь к нему или null при неудаче.</summary>
    public static async Task<string?> DownloadAsync(
        HttpClient http, UpdateInfo update, string installDir, IProgress<int> progress, CancellationToken ct)
    {
        if (update.DownloadUrl is null)
            return null;

        var folder = DownloadFolder(installDir);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, update.FileName ?? "update.zip");

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
        // Соседняя папка с тем же именем и суффиксом: подменить папку «изнутри неё
        // самой» нельзя, а сосед на том же диске переименовывается мгновенно.
        var staging = installDir + ".new";

        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);

        CleanupLeftovers(installDir);

        ZipFile.ExtractToDirectory(zipPath, staging);

        // Внутри архива верхняя папка (LocalTV) — нас интересует та, где лежит exe.
        var exe = Directory
            .EnumerateFiles(staging, MainExecutable, SearchOption.AllDirectories)
            .FirstOrDefault();

        if (exe is null)
        {
            Directory.Delete(staging, recursive: true);
            return null;
        }

        return Path.GetDirectoryName(exe);
    }

    /// <summary>Убрать папки, оставшиеся от прошлых обновлений (могли не удалиться).</summary>
    private static void CleanupLeftovers(string installDir)
    {
        var parent = Path.GetDirectoryName(installDir);
        var prefix = Path.GetFileName(installDir) + ".old-";
        if (parent is null || !Directory.Exists(parent))
            return;

        foreach (var folder in Directory.EnumerateDirectories(parent, prefix + "*").ToList())
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
    /// <param name="workFolder">Папка настроек программы: сценарий и его протокол
    /// кладём туда — она не переименовывается при подмене и переживёт обновление.</param>
    public static void LaunchSwap(string newVersionDir, string installDir, int processId, string workFolder)
    {
        Directory.CreateDirectory(workFolder);
        var script = Path.Combine(workFolder, "update.ps1");
        File.WriteAllText(script, SwapScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workFolder,   // не держим папку программы занятой
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

    // Сценарий подмены. Прежнюю версию не удаляем, а отодвигаем: если поставить новую
    // не выйдет, возвращаем её на место. Протокол ведём рядом со сценарием — без него
    // неудачное обновление выглядело бы как «программа пропала».
    private const string SwapScript = """
        param([string]$NewVersion, [string]$InstallDir, [int]$ProcessId)

        $ErrorActionPreference = 'Stop'
        $log = Join-Path $PSScriptRoot 'update.log'
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

            # Прежняя версия и пустая оболочка от распаковки больше не нужны. Не
            # удалились — уберём при следующем обновлении, пугать этим незачем.
            Start-Sleep -Seconds 5
            Remove-Item -LiteralPath $old -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath "$InstallDir.new" -Recurse -Force -ErrorAction SilentlyContinue
        } catch {
            Note "ошибка обновления: $($_.Exception.Message)"
            try { Start-Process -FilePath $exe -WorkingDirectory $InstallDir } catch { Note 'запустить программу не удалось' }
        }
        """;
}
