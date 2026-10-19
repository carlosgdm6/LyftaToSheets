using LyftaToSheets.Lyfta;
using LyftaToSheets.Models;
using LyftaToSheets.Services;
using LyftaToSheets.Sheets;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyftaToSheets.Tests;

public class ImportServiceTests
{
    private static (ImportService import, FakeSheetsGateway gateway, FakeLyftaClient lyfta, FakeSyncStateStore syncState)
        CriarCenario()
    {
        var gateway = FakeSheetsGateway.GrelhaPadrao();
        var mesociclo = new MesocicloService(gateway, NullLogger<MesocicloService>.Instance);
        var lyfta = new FakeLyftaClient();
        var syncState = new FakeSyncStateStore();
        var import = new ImportService(lyfta, mesociclo, syncState, NullLogger<ImportService>.Instance);
        return (import, gateway, lyfta, syncState);
    }

    private static LyftaWorkout WorkoutTreinoA(long id = 1, string data = "2025-01-02") => new()
    {
        Id = id,
        Title = "TREINO A (PUSH)",
        WorkoutPerformDate = data,
        Exercises = new List<LyftaExercise>
        {
            new() { ExerciseName = "Supino Reto", Sets = new List<LyftaSet> { new() { Reps = "10", Weight = "30" } } },
            new() { ExerciseName = "Remada Curvada", Sets = new List<LyftaSet> { new() { Reps = "8", Weight = "40" } } },
        }
    };

    private static LyftaWorkout WorkoutTreinoBComExercicioExtra(long id = 2, string data = "2025-01-03") => new()
    {
        Id = id,
        Title = "TREINO B (PULL)",
        WorkoutPerformDate = data,
        Exercises = new List<LyftaExercise>
        {
            new() { ExerciseName = "Agachamento Livre", Sets = new List<LyftaSet> { new() { Reps = "12", Weight = "50" } } },
            new() { ExerciseName = "Extra Exercicio", Sets = new List<LyftaSet> { new() { Reps = "5", Weight = "20" } } },
        }
    };

    [Fact]
    public async Task GerarPreviewAsync_ProposeTreinoNovoComMapeamentoPosicionalENaProximaSemanaLivre()
    {
        var (import, _, lyfta, _) = CriarCenario();
        lyfta.Workouts.Add(WorkoutTreinoA());

        var previews = await import.GerarPreviewAsync();

        var previewA = Assert.Single(previews, p => p.Nome == "TREINO A");
        Assert.Equal(2, previewA.Semana); // TREINO A já tem a semana 1 preenchida na grelha padrão
        Assert.Equal(1, previewA.LyftaWorkoutId);
        Assert.Equal("2025-01-02", previewA.DataLyfta);
        Assert.Empty(previewA.Avisos);
        Assert.Equal(2, previewA.Linhas.Count);
        Assert.Equal("Supino", previewA.Linhas[0].ExercicioFolha);
        Assert.Equal("Supino Reto", previewA.Linhas[0].ExercicioLyfta);
        Assert.Equal("1x10(30)", previewA.Linhas[0].Valor);
        Assert.Equal("Remada", previewA.Linhas[1].ExercicioFolha);
        Assert.Equal("1x8(40)", previewA.Linhas[1].Valor);
    }

    [Fact]
    public async Task GerarPreviewAsync_AdicionaAvisoQuandoNumeroDeExerciciosDifere()
    {
        var (import, _, lyfta, _) = CriarCenario();
        lyfta.Workouts.Add(WorkoutTreinoBComExercicioExtra()); // folha tem 1 exercício (Agachamento), Lyfta tem 2

        var previews = await import.GerarPreviewAsync();

        var previewB = Assert.Single(previews, p => p.Nome == "TREINO B");
        Assert.Equal(1, previewB.Semana);
        Assert.Single(previewB.Avisos);
        Assert.Contains("1 exercícios", previewB.Avisos[0]);
        Assert.Contains("Lyfta tem 2", previewB.Avisos[0]);
        Assert.Single(previewB.Linhas); // mapeamento posicional limitado ao menor dos dois tamanhos
        Assert.Equal("Agachamento", previewB.Linhas[0].ExercicioFolha);
        Assert.Equal("Agachamento Livre", previewB.Linhas[0].ExercicioLyfta);
        Assert.Equal("1x12(50)", previewB.Linhas[0].Valor);
    }

    [Fact]
    public async Task GerarPreviewAsync_NaoProposeTreinoCujoWorkoutMaisRecenteJaEstaRegistado()
    {
        var (import, _, lyfta, syncState) = CriarCenario();
        var workoutA = WorkoutTreinoA(id: 1, data: "2025-01-02");
        lyfta.Workouts.Add(workoutA);
        lyfta.Workouts.Add(WorkoutTreinoBComExercicioExtra());
        syncState.Estado["TREINO A"] = new LyftaToSheets.Sync.TreinoSyncState
        {
            Treino = "TREINO A",
            UltimoWorkoutId = workoutA.Id,
            UltimoWorkoutData = workoutA.WorkoutPerformDate
        };

        var previews = await import.GerarPreviewAsync();

        Assert.DoesNotContain(previews, p => p.Nome == "TREINO A");
        Assert.Contains(previews, p => p.Nome == "TREINO B");
    }

    [Fact]
    public async Task GravarAsync_EscreveNaFolhaERegistaOEstadoDeSincronizacaoComOIdCerto()
    {
        var (import, gateway, lyfta, syncState) = CriarCenario();
        lyfta.Workouts.Add(WorkoutTreinoA());
        var previews = await import.GerarPreviewAsync();
        var previewA = previews.Single(p => p.Nome == "TREINO A");

        await import.GravarAsync(previewA);

        var colSemana2 = SheetGridParser.ColunaDaSemana(2);
        Assert.Equal("1x10(30)", SheetGridParser.ObterValor(
            gateway.Rows.Select(r => (IList<object>)r).ToList(), linha1Based: 3, coluna0Based: colSemana2));
        Assert.Equal("1x8(40)", SheetGridParser.ObterValor(
            gateway.Rows.Select(r => (IList<object>)r).ToList(), linha1Based: 4, coluna0Based: colSemana2));

        var registo = Assert.Single(syncState.Registos);
        Assert.Equal("TREINO A", registo.treino);
        Assert.Equal(1, registo.workoutId);
        Assert.Equal("2025-01-02", registo.workoutData);
    }

    [Fact]
    public async Task GravarAsync_NaoRegistaEstadoSeAGravacaoNaFolhaFalhar()
    {
        var (import, gateway, lyfta, syncState) = CriarCenario();
        lyfta.Workouts.Add(WorkoutTreinoA());
        var previews = await import.GerarPreviewAsync();
        var previewA = previews.Single(p => p.Nome == "TREINO A");
        gateway.LancarExcecaoAoGravar = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => import.GravarAsync(previewA));

        Assert.Empty(syncState.Registos);
    }
}
