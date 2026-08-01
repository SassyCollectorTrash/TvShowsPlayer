namespace TvShowsPlayer.Core;

/// <summary>
/// Опознание серии по относительному пути для сопоставления прогресса: без учёта
/// регистра, разделители приведены к '\\'.
/// </summary>
internal static class PathIdentity
{
    public static string Normalize(string rel) => rel.Replace('/', '\\').ToLowerInvariant();
}
