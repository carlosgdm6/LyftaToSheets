namespace LyftaToSheets.Sync;

/// <summary>Guarda, por treino (A/B/C/D), qual foi o último workout do Lyfta já sincronizado com a folha.
/// Substitui a antiga heurística de comparar o texto da célula com a semana anterior: aqui a decisão
/// "já sincronizado" é feita pela identidade do workout (Id), nunca pelo conteúdo que calhou escrever.</summary>
public interface ISyncStateStore
{
    Task<IReadOnlyDictionary<string, TreinoSyncState>> CarregarAsync(CancellationToken ct = default);

    Task RegistarAsync(string treino, long workoutId, string workoutData, CancellationToken ct = default);
}
