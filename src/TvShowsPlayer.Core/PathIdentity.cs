namespace TvShowsPlayer.Core;

/// <summary>
/// Идентичность относительного пути серии для сопоставления прогресса: без регистра,
/// сепараторы приведены к '\\'. Совместимо с <c>norm_rel</c> в generate_playlist.py.
/// </summary>
internal static class PathIdentity
{
    public static string Normalize(string rel) => rel.Replace('/', '\\').ToLowerInvariant();
}
