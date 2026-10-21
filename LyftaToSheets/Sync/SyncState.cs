namespace LyftaToSheets.Sync;

/// <summary>Um workout do Lyfta escrito com sucesso numa semana concreta de um treino (A/B/C/D).
/// É a fonte de verdade para "já sincronizado" — identidade do workout, nunca comparação de texto —
/// e guarda-se UM registo por (treino, semana), não só o mais recente: é o que permite ao reenvio
/// manual (Visão Geral) ir buscar o workout exato de QUALQUER semana já escrita por esta app, em vez
/// de adivinhar por posição cronológica — o que desalinhava sempre que a folha tinha semanas sem
/// workout correspondente direto no Lyfta (semanas puladas, preenchidas à mão, etc.).</summary>
public sealed record TreinoSyncState
{
    public required string Treino { get; init; }
    public required int Semana { get; init; }
    public required long UltimoWorkoutId { get; init; }
    public required string UltimoWorkoutData { get; init; }

    /// <summary>Chave composta usada para indexar o histórico — mesmo formato usado para gravar e
    /// para procurar, para nunca haver duas versões ligeiramente diferentes da mesma chave.</summary>
    public static string Chave(string treino, int semana) => $"{treino}|{semana}";
}

/// <summary>Conteúdo persistido do ficheiro de estado de sincronização: histórico completo de todas
/// as semanas já escritas por esta app, indexado por <see cref="TreinoSyncState.Chave"/>.</summary>
public sealed record SyncStateFile
{
    public Dictionary<string, TreinoSyncState> Registos { get; init; } = new();
}
