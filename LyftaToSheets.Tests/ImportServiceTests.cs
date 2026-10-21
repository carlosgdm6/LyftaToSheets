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
        syncState.Definir("TREINO A", semana: 0, workoutA.Id, workoutA.WorkoutPerformDate);

        var previews = await import.GerarPreviewAsync();

        Assert.DoesNotContain(previews, p => p.Nome == "TREINO A");
        Assert.Contains(previews, p => p.Nome == "TREINO B");
    }

    /// <summary>Reproduz o relato real do utilizador: a app tinha escrito corretamente, mas depois a
    /// célula ficou vazia na folha (ex: apagada à mão sem querer). A memória local continua a dizer
    /// "já sincronizado" — mas como a folha já não tem os dados, a app tem de propor o treino outra
    /// vez em vez de confiar cegamente no que acha que já fez.</summary>
    [Fact]
    public async Task GerarPreviewAsync_VoltaAProporTreinoSeACelulaRegistadaComoSincronizadaEstiverVaziaNaFolha()
    {
        var (import, _, lyfta, syncState) = CriarCenario();
        var workoutA = WorkoutTreinoA(id: 1, data: "2025-01-02");
        lyfta.Workouts.Add(workoutA);
        // Estado diz que este workout já foi escrito na semana 5 — mas a grelha padrão não tem
        // nada escrito na semana 5 para TREINO A (só a semana 1 do Supino está preenchida).
        syncState.Definir("TREINO A", semana: 5, workoutA.Id, workoutA.WorkoutPerformDate);

        var previews = await import.GerarPreviewAsync();

        var previewA = Assert.Single(previews, p => p.Nome == "TREINO A");
        Assert.Equal(1, previewA.LyftaWorkoutId); // continua a ser o mesmo workout — não há nada de novo no Lyfta
    }

    /// <summary>Contraprova do teste anterior: se a semana registada AINDA tem dados na folha, o
    /// estado continua a contar como sincronizado — não se propõe o treino outra vez à toa.</summary>
    [Fact]
    public async Task GerarPreviewAsync_NaoProposeTreinoSeACelulaRegistadaAindaTemDadosNaFolha()
    {
        var (import, _, lyfta, syncState) = CriarCenario();
        var workoutA = WorkoutTreinoA(id: 1, data: "2025-01-02");
        lyfta.Workouts.Add(workoutA);
        // A grelha padrão TEM dados na semana 1 para TREINO A (Supino) — estado aponta exatamente para lá.
        syncState.Definir("TREINO A", semana: 1, workoutA.Id, workoutA.WorkoutPerformDate);

        var previews = await import.GerarPreviewAsync();

        Assert.DoesNotContain(previews, p => p.Nome == "TREINO A");
    }

    /// <summary>Reproduz o segundo relato real do utilizador: clicar numa célula já preenchida na
    /// Visão Geral para a reenviar dava "não encontrei o treino do Lyfta correspondente", mesmo a
    /// célula tendo dados. Causa: a posição cronológica (PorDiaDeTreino) desalinha sempre que há
    /// menos dias de Lyfta do que a semana pedida — ex: a folha tem semanas sem workout
    /// correspondente direto (preenchidas à mão, semanas puladas). Com o histórico exato guardado
    /// por esta app, o reenvio vai buscar o workout certo por identidade, nunca por posição.</summary>
    [Fact]
    public async Task GerarPreviewParaSemanaAsync_UsaIdentidadeDoHistoricoQuandoPosicaoCronologicaNaoChegaLa()
    {
        var (import, _, lyfta, syncState) = CriarCenario();
        var workoutAntigo = WorkoutTreinoA(id: 10, data: "2025-01-01");
        var workoutRecente = WorkoutTreinoA(id: 20, data: "2025-01-08");
        lyfta.Workouts.Add(workoutAntigo);
        lyfta.Workouts.Add(workoutRecente);
        // Só há 2 dias de TREINO A no Lyfta — por posição cronológica, a semana 31 nunca seria
        // alcançável (PorDiaDeTreino só tem 2 entradas). O histórico sabe exatamente qual workout
        // foi escrito lá.
        syncState.Definir("TREINO A", semana: 31, workoutId: workoutRecente.Id, workoutRecente.WorkoutPerformDate);

        var preview = await import.GerarPreviewParaSemanaAsync("TREINO A", 31);

        Assert.NotNull(preview);
        Assert.Equal(20, preview!.LyftaWorkoutId);
        Assert.Equal(31, preview.Semana);
    }

    /// <summary>Semanas escritas antes de este histórico existir (ou nunca sincronizadas por esta
    /// app) continuam a funcionar pelo método antigo — melhor esforço por posição cronológica.</summary>
    [Fact]
    public async Task GerarPreviewParaSemanaAsync_SemHistorico_UsaPosicaoCronologicaComoRecurso()
    {
        var (import, _, lyfta, _) = CriarCenario();
        var workout1 = WorkoutTreinoA(id: 1, data: "2025-01-02");
        var workout2 = WorkoutTreinoA(id: 2, data: "2025-01-09");
        lyfta.Workouts.Add(workout1);
        lyfta.Workouts.Add(workout2);

        var preview = await import.GerarPreviewParaSemanaAsync("TREINO A", 2);

        Assert.NotNull(preview);
        Assert.Equal(2, preview!.LyftaWorkoutId); // 2º dia de calendário = workout2
    }

    [Fact]
    public async Task GerarPreviewParaSemanaAsync_TreinoNaoExisteNaFolha_DevolveNull()
    {
        var (import, _, lyfta, _) = CriarCenario();
        lyfta.Workouts.Add(WorkoutTreinoA());

        var preview = await import.GerarPreviewParaSemanaAsync("TREINO Z", 1);

        Assert.Null(preview);
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
        Assert.Equal(2, registo.semana);
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
