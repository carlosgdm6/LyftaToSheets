using LyftaToSheets.Sync;

namespace LyftaToSheets.Tests;

public class JsonSyncStateStoreTests : IDisposable
{
    private readonly string _caminho = Path.Combine(Path.GetTempPath(), $"synctest-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_caminho)) File.Delete(_caminho);
    }

    [Fact]
    public async Task CarregarAsync_FicheiroInexistente_DevolveDicionarioVazio()
    {
        var store = new JsonSyncStateStore(_caminho);

        var estado = await store.CarregarAsync();

        Assert.Empty(estado);
    }

    [Fact]
    public async Task RegistarAsync_DepoisCarregarAsync_DevolveOTreinoGravado()
    {
        var store = new JsonSyncStateStore(_caminho);

        await store.RegistarAsync("TREINO A", 123L, "2025-01-02", 31, CancellationToken.None);
        var estado = await store.CarregarAsync();

        var chave = TreinoSyncState.Chave("TREINO A", 31);
        Assert.True(estado.ContainsKey(chave));
        Assert.Equal(123L, estado[chave].UltimoWorkoutId);
        Assert.Equal("2025-01-02", estado[chave].UltimoWorkoutData);
        Assert.Equal("TREINO A", estado[chave].Treino);
        Assert.Equal(31, estado[chave].Semana);
    }

    [Fact]
    public async Task RegistarAsync_DuasVezesParaTreinosDiferentes_NaoSePisam()
    {
        var store = new JsonSyncStateStore(_caminho);

        await store.RegistarAsync("TREINO A", 1L, "2025-01-02", 5, CancellationToken.None);
        await store.RegistarAsync("TREINO B", 2L, "2025-01-05", 5, CancellationToken.None);

        var estado = await store.CarregarAsync();

        Assert.Equal(2, estado.Count);
        Assert.Equal(1L, estado[TreinoSyncState.Chave("TREINO A", 5)].UltimoWorkoutId);
        Assert.Equal(2L, estado[TreinoSyncState.Chave("TREINO B", 5)].UltimoWorkoutId);
    }

    /// <summary>O histórico é por (treino, semana) — registar o MESMO par outra vez substitui só
    /// essa entrada; semanas diferentes do mesmo treino ficam todas guardadas, porque é esse
    /// histórico completo que permite ao reenvio manual encontrar o workout exato de qualquer
    /// semana já escrita (ver ImportService.GerarPreviewParaSemanaAsync).</summary>
    [Fact]
    public async Task RegistarAsync_MesmoTreinoSemanasDiferentes_MantemAmbasNoHistorico()
    {
        var store = new JsonSyncStateStore(_caminho);

        await store.RegistarAsync("TREINO A", 1L, "2025-01-02", 5, CancellationToken.None);
        await store.RegistarAsync("TREINO B", 2L, "2025-01-05", 5, CancellationToken.None);
        await store.RegistarAsync("TREINO A", 3L, "2025-01-09", 6, CancellationToken.None);

        var estado = await store.CarregarAsync();

        Assert.Equal(3, estado.Count);
        Assert.Equal(1L, estado[TreinoSyncState.Chave("TREINO A", 5)].UltimoWorkoutId); // semana 5 continua lá
        Assert.Equal(3L, estado[TreinoSyncState.Chave("TREINO A", 6)].UltimoWorkoutId); // nova entrada, semana 6
        Assert.Equal(2L, estado[TreinoSyncState.Chave("TREINO B", 5)].UltimoWorkoutId); // não foi afetado
    }

    [Fact]
    public async Task RegistarAsync_MesmoTreinoEMesmaSemana_SubstituiEssaEntrada()
    {
        var store = new JsonSyncStateStore(_caminho);

        await store.RegistarAsync("TREINO A", 1L, "2025-01-02", 5, CancellationToken.None);
        await store.RegistarAsync("TREINO A", 99L, "2025-01-02-corrigido", 5, CancellationToken.None);

        var estado = await store.CarregarAsync();

        var chave = TreinoSyncState.Chave("TREINO A", 5);
        Assert.Single(estado);
        Assert.Equal(99L, estado[chave].UltimoWorkoutId);
    }

    [Fact]
    public async Task CarregarAsync_FormatoAntigoUmRegistoPorTreino_MigraParaChaveComposta()
    {
        // Simula um ficheiro gravado por uma versão anterior da app, antes de existir histórico
        // por semana (só guardava o registo mais recente de cada treino, indexado pelo nome).
        await File.WriteAllTextAsync(_caminho, """
            { "Treinos": { "TREINO A": { "Treino": "TREINO A", "UltimoWorkoutId": 1, "UltimoWorkoutData": "2025-01-02", "Semana": 7 } } }
            """);
        var store = new JsonSyncStateStore(_caminho);

        var estado = await store.CarregarAsync();

        var chave = TreinoSyncState.Chave("TREINO A", 7);
        Assert.True(estado.ContainsKey(chave));
        Assert.Equal(7, estado[chave].Semana);
        Assert.Equal(1L, estado[chave].UltimoWorkoutId);
    }

    [Fact]
    public async Task CarregarAsync_FormatoAntigoSemCampoSemana_MigraComSemanaZero()
    {
        await File.WriteAllTextAsync(_caminho, """
            { "Treinos": { "TREINO A": { "Treino": "TREINO A", "UltimoWorkoutId": 1, "UltimoWorkoutData": "2025-01-02" } } }
            """);
        var store = new JsonSyncStateStore(_caminho);

        var estado = await store.CarregarAsync();

        var chave = TreinoSyncState.Chave("TREINO A", 0);
        Assert.True(estado.ContainsKey(chave));
        Assert.Equal(0, estado[chave].Semana);
    }
}
