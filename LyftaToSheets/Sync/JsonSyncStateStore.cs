using System.Text.Json;

namespace LyftaToSheets.Sync;

/// <summary>Persiste o estado de sincronização num ficheiro JSON local (fora do controlo do Git,
/// tal como o token_store do Google) para sobreviver a reinícios do processo.</summary>
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
            await using var stream = File.OpenRead(_caminho);
            var ficheiro = await JsonSerializer.DeserializeAsync<SyncStateFile>(stream, JsonOpts, ct);
            return ficheiro?.Treinos ?? new Dictionary<string, TreinoSyncState>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RegistarAsync(string treino, long workoutId, string workoutData, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            SyncStateFile ficheiro;
            if (File.Exists(_caminho))
            {
                await using var leitura = File.OpenRead(_caminho);
                ficheiro = await JsonSerializer.DeserializeAsync<SyncStateFile>(leitura, JsonOpts, ct) ?? new SyncStateFile();
            }
            else
            {
                ficheiro = new SyncStateFile();
                var pasta = Path.GetDirectoryName(_caminho);
                if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
            }

            ficheiro.Treinos[treino] = new TreinoSyncState
            {
                Treino = treino,
                UltimoWorkoutId = workoutId,
                UltimoWorkoutData = workoutData
            };

            var json = JsonSerializer.Serialize(ficheiro, JsonOpts);
            await File.WriteAllTextAsync(_caminho, json, ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}
