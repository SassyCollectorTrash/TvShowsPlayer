using System.Security.Cryptography;
using System.Text;

namespace TvShowsPlayer.Core;

/// <summary>
/// Сигнатура состава библиотеки (sha1): если набор сериалов и файлов не менялся,
/// плейлист не пересобирают (бережём закладку). Состав считается так:
/// по каждому сериалу — байты имени, затем по каждой серии
/// разделитель \0 и байты пути, в конце сериала — \n.
/// </summary>
public static class CompositionSignature
{
    /// <param name="window">Параметры карусели входят в сигнатуру: их смена меняет
    /// сам плейлист, значит требует пересборки (иначе настройка «не работает»).</param>
    public static string Compute(IReadOnlyList<Show> shows, int window, int step, int capRotations)
    {
        return Compute(shows, $"|w={window},s={step},c={capRotations}");
    }

    public static string Compute(IReadOnlyList<Show> shows) => Compute(shows, suffix: null);

    private static string Compute(IReadOnlyList<Show> shows, string? suffix)
    {
        using var ms = new MemoryStream();

        foreach (var show in shows)
        {
            WriteUtf8(ms, show.Name);
            foreach (var episode in show.Episodes)
            {
                ms.WriteByte(0);
                WriteUtf8(ms, episode);
            }

            ms.WriteByte((byte)'\n');
        }

        if (suffix is not null)
            WriteUtf8(ms, suffix);

        var hash = SHA1.HashData(ms.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteUtf8(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
