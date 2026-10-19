namespace LyftaToSheets.Sync;

/// <summary>Último workout do Lyfta sincronizado com sucesso para um treino (A/B/C/D).
/// É a fonte de verdade para "já sincronizado" — identidade do workout, nunca comparação de texto.</summary>
public sealed record TreinoSyncState
{
    public required string Treino { get; init; }
    public required long UltimoWorkoutId { get; init; }
    public required string UltimoWorkoutData { get; init; }
}

/// <summary>Conteúdo persistido do ficheiro de estado de sincronização (um registo por treino).</summary>
public sealed record SyncStateFile
{
    public Dictionary<string, TreinoSyncState> Treinos { get; init; } = new();
}
