namespace TvShowsPlayer.Core;

/// <summary>
/// Поиск серии в собранном плейлисте по идентичности — чтобы после пересборки канал
/// продолжил РОВНО ту серию, что шла, а не начало карусели.

/// </summary>
public static class PlaylistIndex
{
    /// <summary>Индекс серии (сериал <paramref name="currentShow"/>, серия
    /// <c>progress[currentShow]</c>) в плейлисте; -1, если её нет.</summary>
    public static int OfCurrentEpisode(
        IReadOnlyList<string> playlist, string root,
        IReadOnlyDictionary<string, string> progress, string? currentShow)
    {
        if (string.IsNullOrEmpty(currentShow)
            || !progress.TryGetValue(currentShow, out var rel)
            || string.IsNullOrEmpty(rel))
            return -1;

        var target = PathIdentity.Normalize(Path.Combine(currentShow, rel));
        for (var i = 0; i < playlist.Count; i++)
        {
            if (PathIdentity.Normalize(Path.GetRelativePath(root, playlist[i])) == target)
                return i;
        }

        return -1;
    }
}
