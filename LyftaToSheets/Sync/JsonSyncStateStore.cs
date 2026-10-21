using System.Text.Json;

namespace LyftaToSheets.Sync;

/// <summary>Persiste o estado de sincronização num ficheiro JSON local (fora do controlo do Git,
/// tal como o token_store do Google) para sobreviver a reinícios do processo. Guarda o histórico
/// completo de semanas já escritas (uma entrada por "treino|semana"), não só a mais recente de cada
/// treino — ver <see cref="TreinoSyncState"/>.</summary>
public sealed class JsonSyncStateStore : ISyncStateStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _caminho;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonSyncStateStore(string caminho)
    {
        _caminho = caminho;
    }

    public async Task<IReadOnlyDictionary<string, TreinoSyncState>> CarregarAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_caminho)) return new Dictionary<string, TreinoSyncState>();

        await _lock.WaitAsync(ct);
        try
        {
            return await LerDoFicheiroAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RegistarAsync(string treino, long workoutId, string workoutData, int semana, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var registos = File.Exists(_caminho)
                ? new Dictionary<string, TreinoSyncState>(await LerDoFicheiroAsync(ct))
                : new Dictionary<string, TreinoSyncState>();

            if (!File.Exists(_caminho))
            {
                var pasta = Path.GetDirectoryName(_caminho);
                if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
            }

            registos[TreinoSyncState.Chave(treino, semana)] = new TreinoSyncState
            {
                Treino = treino,
                Semana = semana,
                UltimoWorkoutId = workoutId,
                UltimoWorkoutData = workoutData
            };

            var ficheiro = new SyncStateFile { Registos = registos };
            var json = JsonSerializer.Serialize(ficheiro, JsonOpts);
            await File.WriteAllTextAsync(_caminho, json, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Lê e interpreta o ficheiro tal como está no disco — aceita o formato atual
    /// ("Registos", indexado por "treino|semana") e, para ficheiros gravados antes de existir
    /// histórico por semana, o formato antigo ("Treinos", um registo só por treino), migrando-o
    /// em memória para a chave composta. Nunca reescreve o ficheiro no formato antigo — a próxima
    /// chamada a <see cref="RegistarAsync"/> já grava tudo no formato atual.</summary>
    private async Task<IReadOnlyDictionary<string, TreinoSyncState>> LerDoFicheiroAsync(CancellationToken ct)
    {
        await using var stream = File.OpenRead(_caminho);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var raiz = doc.RootElement;
        var resultado = new Dictionary<string, TreinoSyncState>();

        if (raiz.TryGetProperty("Registos", out var registosEl) && registosEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in registosEl.EnumerateObject())
            {
                var estado = LerEstado(prop.Value, prop.Name, semanaPorOmissao: 0);
                if (estado is not null) resultado[prop.Name] = estado;
            }
        }
        else if (raiz.TryGetProperty("Treinos", out var treinosEl) && treinosEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in treinosEl.EnumerateObject())
            {
                var estado = LerEstado(prop.Value, prop.Name, semanaPorOmissao: 0);
                if (estado is not null) resultado[TreinoSyncState.Chave(estado.Treino, estado.Semana)] = estado;
            }
        }

        return resultado;
    }

    private static TreinoSyncState? LerEstado(JsonElement obj, string chaveOuNomeFallback, int semanaPorOmissao)
    {
        if (!obj.TryGetProperty("UltimoWorkoutId", out var idEl)) return null;

        var treino = obj.TryGetProperty("Treino", out var t) ? t.GetString() ?? chaveOuNomeFallback : chaveOuNomeFallback;
        var semana = obj.TryGetProperty("Semana", out var s) ? s.GetInt32() : semanaPorOmissao;
        var workoutData = obj.TryGetProperty("UltimoWorkoutData", out var d) ? d.GetString() ?? "" : "";

        return new TreinoSyncState
        {
            Treino = treino,
            Semana = semana,
            UltimoWorkoutId = idEl.GetInt64(),
            UltimoWorkoutData = workoutData
        };
    }
}
