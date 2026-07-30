using System.Text;

namespace TvShowsPlayer.Core;

/// <summary>
/// Генерирует <c>mpv.conf</c> канала для prod-режима (провизионится в config-dir).
/// Зеркало живого mpv.conf из скриптового кита, но динамические поля (fs-screen, аудио,
/// громкость) берутся из <see cref="AppConfig"/>, а <c>input-ipc-server</c> НЕ
/// задаётся — pipe приложение передаёт аргументом по режиму (dev/prod).
/// </summary>
public static class MpvConfig
{
    public static string Generate(AppConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Сгенерировано приложением JETIX — меняй настройки в окне, не здесь.");
        sb.AppendLine("fullscreen=yes");
        sb.AppendLine($"fs-screen={config.FsScreen}");
        sb.AppendLine("loop-playlist=inf");
        sb.AppendLine("keep-open=no");
        sb.AppendLine("osc=no");
        sb.AppendLine("osd-level=0");
        sb.AppendLine("border=no");
        sb.AppendLine("cursor-autohide=always");
        sb.AppendLine("input-default-bindings=no");   // киоск: не реагировать на клавиши
        sb.AppendLine("title=jetix");
        sb.AppendLine("sub-auto=no");
        sb.AppendLine("sid=no");
        sb.AppendLine("audio-file-paths=Rus Sound;audio");   // внешняя рус-озвучка
        sb.AppendLine("audio-file-auto=fuzzy");
        sb.AppendLine("alang=rus,ru");                        // русская дорожка по языку
        sb.AppendLine("save-position-on-quit=no");           // позицию хранит resume.lua

        if (!string.IsNullOrEmpty(config.AudioDevice))
            sb.AppendLine($"audio-device={config.AudioDevice}");

        sb.AppendLine($"volume={config.Volume}");
        sb.AppendLine("af=dynaudnorm=f=400:g=31:r=0.9:p=0.9:s=8");   // выравнивание громкости

        return sb.ToString();
    }
}
