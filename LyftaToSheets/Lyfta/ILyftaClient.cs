namespace LyftaToSheets.Lyfta;

/// <summary>Cliente da API do Lyfta. Interface para permitir testar o resto da app com um dublê.</summary>
public interface ILyftaClient
{
    /// <summary>Puxa TODAS as páginas de workouts e devolve a lista completa.</summary>
    Task<IReadOnlyList<LyftaWorkout>> GetWorkoutsAsync(CancellationToken ct = default);
}
