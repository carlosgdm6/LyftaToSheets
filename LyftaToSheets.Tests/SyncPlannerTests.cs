using System.Globalization;
using LyftaToSheets.Lyfta;
using LyftaToSheets.Sync;

namespace LyftaToSheets.Tests;

public class SyncPlannerTests
{
    private static readonly IReadOnlyDictionary<string, TreinoSyncState> EstadoVazio =
        new Dictionary<string, TreinoSyncState>();

    private static LyftaWorkout Workout(long id, string titulo, string data)
        => new() { Id = id, Title = titulo, WorkoutPerformDate = data };

    [Fact]
    public void EscolherCandidatos_TreinosQueAtravessamFronteiraDeSemanaIso_SaoAmbosEscolhidos()
    {
        // TREINO A à quinta-feira, TREINO B ao domingo da semana ISO seguinte. A versão antiga
        // ("semana ISO mais recente entre todos os treinos") só devolvia um dos dois, porque
        // olhava para a semana ISO do conjunto, não para o dia de calendário de cada treino.
        const string dataQuinta = "2025-01-02";
        const string dataDomingoSemanaSeguinte = "2025-01-12";

        var semanaQuinta = ISOWeek.GetWeekOfYear(DateTime.Parse(dataQuinta, CultureInfo.InvariantCulture));
        var semanaDomingo = ISOWeek.GetWeekOfYear(DateTime.Parse(dataDomingoSemanaSeguinte, CultureInfo.InvariantCulture));
        Assert.NotEqual(semanaQuinta, semanaDomingo); // confirma a premissa do teste antes de validar o fix

        var workouts = new List<LyftaWorkout>
        {
            Workout(1, "TREINO A (PUSH)", dataQuinta),
            Workout(2, "TREINO B (PULL)", dataDomingoSemanaSeguinte),
        };

        var candidatos = SyncPlanner.EscolherCandidatos(workouts, EstadoVazio);

        Assert.Equal(2, candidatos.Count);
        Assert.True(candidatos.ContainsKey("TREINO A"));
        Assert.True(candidatos.ContainsKey("TREINO B"));
        Assert.Equal(1, candidatos["TREINO A"].Id);
        Assert.Equal(2, candidatos["TREINO B"].Id);
    }

    [Fact]
    public void EscolherCandidatos_ConteudoIgualEmSemanasDiferentes_NaoDaFalsoPositivoNemFalsoNegativo()
    {
        // A versão antiga decidia "já gravado" comparando o TEXTO da célula — repetir o mesmo
        // peso/reps duas semanas seguidas dava falso positivo e saltava o treino novo.
        var antigo = Workout(100, "TREINO A", "2025-01-02");
        var recente = Workout(200, "TREINO A", "2025-01-09"); // mesmo treino, conteúdo "igual", mais recente

        // 1) Com estado vazio, escolhe o mais recente (Id 200).
        var candidatosIniciais = SyncPlanner.EscolherCandidatos(new[] { antigo, recente }, EstadoVazio);
        Assert.True(candidatosIniciais.ContainsKey("TREINO A"));
        Assert.Equal(200, candidatosIniciais["TREINO A"].Id);

        // 2) Simula o RegistarAsync desse workout (Id 200) — construção do estado tal como o
        // JsonSyncStateStore guardaria.
        var estadoAposRegistar = new Dictionary<string, TreinoSyncState>
        {
            ["TREINO A"] = new TreinoSyncState { Treino = "TREINO A", Semana = 9, UltimoWorkoutId = 200, UltimoWorkoutData = "2025-01-09" }
        };

        // 3) Repetindo a escolha com os MESMOS workouts (incluindo o antigo, de conteúdo igual),
        // não deve voltar a propor nada para TREINO A — o workout mais recente já foi sincronizado.
        var candidatosRepetidos = SyncPlanner.EscolherCandidatos(new[] { antigo, recente }, estadoAposRegistar);
        Assert.False(candidatosRepetidos.ContainsKey("TREINO A"));

        // 4) Um workout GENUINAMENTE novo (Id diferente, data mais recente) para o mesmo treino
        // volta a aparecer como candidato.
        var novo = Workout(300, "TREINO A", "2025-01-16");
        var candidatosComNovo = SyncPlanner.EscolherCandidatos(new[] { antigo, recente, novo }, estadoAposRegistar);
        Assert.True(candidatosComNovo.ContainsKey("TREINO A"));
        Assert.Equal(300, candidatosComNovo["TREINO A"].Id);
    }

    [Fact]
    public void PorDiaDeTreino_ColapsaWorkoutsDoMesmoDiaEOrdenaCronologicamente()
    {
        var workouts = new List<LyftaWorkout>
        {
            Workout(1, "TREINO A", "2025-01-02T08:00:00"), // dia 1, mais cedo
            Workout(2, "TREINO A", "2025-01-02T18:00:00"), // dia 1, mais tarde -> deve prevalecer
            Workout(3, "TREINO A", "2025-01-09T10:00:00"), // dia 2
            Workout(4, "TREINO A", "2025-01-16T10:00:00"), // dia 3
        };

        var porDia = SyncPlanner.PorDiaDeTreino(workouts, "TREINO A");

        Assert.Equal(3, porDia.Count);
        Assert.Equal(2, porDia[0].Id); // dia 1 — o mais recente das duas entradas desse dia
        Assert.Equal(3, porDia[1].Id); // dia 2
        Assert.Equal(4, porDia[2].Id); // dia 3
    }

    [Fact]
    public void PorDiaDeTreino_IgnoraOutrosTreinosEDevolveVazioSeNaoHouverDados()
    {
        var workouts = new List<LyftaWorkout>
        {
            Workout(1, "TREINO B", "2025-01-02"),
        };

        var porDia = SyncPlanner.PorDiaDeTreino(workouts, "TREINO A");

        Assert.Empty(porDia);
    }

    [Fact]
    public void EscolherCandidatos_IgnoraWorkoutsComTituloNaoReconhecidoSemRebentar()
    {
        var workouts = new List<LyftaWorkout>
        {
            Workout(1, "Cardio matinal", "2025-01-02"),
            Workout(2, "", "2025-01-03"),
            Workout(3, "TREINO C (LEGS)", "2025-01-04"),
        };

        var candidatos = SyncPlanner.EscolherCandidatos(workouts, EstadoVazio);

        Assert.Single(candidatos);
        Assert.True(candidatos.ContainsKey("TREINO C"));
        Assert.Equal(3, candidatos["TREINO C"].Id);
    }

    [Fact]
    public void EscolherCandidatos_IgnoraWorkoutsComDataInvalidaSemRebentar()
    {
        var workouts = new List<LyftaWorkout>
        {
            Workout(1, "TREINO A", "data-invalida"),
            Workout(2, "TREINO A", ""),
        };

        var candidatos = SyncPlanner.EscolherCandidatos(workouts, EstadoVazio);

        Assert.Empty(candidatos);
    }
}
