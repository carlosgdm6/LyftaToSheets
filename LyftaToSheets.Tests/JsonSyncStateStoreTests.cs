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

        await store.RegistarAsync("TREINO A", 123L, "2025-01-02", CancellationToken.None);
        var estado = await store.CarregarAsync();

        Assert.True(estado.ContainsKey("TREINO A"));
        Assert.Equal(123L, estado["TREINO A"].UltimoWorkoutId);
        Assert.Equal("2025-01-02", estado["TREINO A"].UltimoWorkoutData);
        Assert.Equal("TREINO A", estado["TREINO A"].Treino);
    }

    [Fact]
    public async Task RegistarAsync_DuasVezesParaTreinosDiferentes_NaoSePisam()
    {
        var store = new JsonSyncStateStore(_caminho);

        await store.RegistarAsync("TREINO A", 1L, "2025-01-02", CancellationToken.None);
        await store.RegistarAsync("TREINO B", 2L, "2025-01-05", CancellationToken.None);

        var estado = await store.CarregarAsync();

        Assert.Equal(2, estado.Count);
        Assert.Equal(1L, estado["TREINO A"].UltimoWorkoutId);
        Assert.Equal(2L, estado["TREINO B"].UltimoWorkoutId);
    }

    [Fact]
    public async Task RegistarAsync_SobrescreveApenasOTreinoAtualizado()
    {
        var store = new JsonSyncStateStore(_caminho);

        await store.RegistarAsync("TREINO A", 1L, "2025-01-02", CancellationToken.None);
        await store.RegistarAsync("TREINO B", 2L, "2025-01-05", CancellationToken.None);
        await store.RegistarAsync("TREINO A", 3L, "2025-01-09", CancellationToken.None);

        var estado = await store.CarregarAsync();

        Assert.Equal(2, estado.Count);
        Assert.Equal(3L, estado["TREINO A"].UltimoWorkoutId);
        Assert.Equal(2L, estado["TREINO B"].UltimoWorkoutId); // não foi afetado
    }
}
