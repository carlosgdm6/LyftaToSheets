using System.Globalization;
using LyftaToSheets.Lyfta;
using LyftaToSheets.Services;

namespace LyftaToSheets.Sync;

/// <summary>Decide QUAIS workouts do Lyfta ainda não foram sincronizados, e a que posição cronológica
/// (dia de treino) corresponde um workout já sincronizado. Lógica pura, sem I/O — por isso totalmente
/// testável sem Google Sheets nem Lyfta reais.
///
/// Substitui duas heurísticas frágeis da versão anterior:
///  1) "semana ISO mais recente entre todos os treinos" — partia (e às vezes ignorava treinos
///     inteiros) sempre que os treinos de uma mesma semana de treino caíam em semanas de calendário
///     ISO diferentes (ex: TREINO A à quinta, TREINO D no domingo seguinte).
///  2) "já está gravado" por comparação de texto com a semana anterior da folha — dava falso
///     positivo sempre que repetias o mesmo peso/reps em duas semanas seguidas, e por isso saltava
///     a escrita de um treino que ainda não tinha sido escrito.
/// Esta versão usa a IDENTIDADE do workout (Id do Lyfta), guardada em <see cref="ISyncStateStore"/>,
/// e agrupa por DIA DE CALENDÁRIO em vez de semana ISO (nunca corta ao meio uma semana de treino).</summary>
public static class SyncPlanner
{
    /// <summary>Para cada treino (A/B/C/D) presente nos workouts, escolhe o mais recente; se já foi
    /// esse exato workout (mesmo Id) que ficou registado como sincronizado, não o inclui — não há
    /// nada de novo para esse treino.</summary>
    public static IReadOnlyDictionary<string, LyftaWorkout> EscolherCandidatos(
        IReadOnlyList<LyftaWorkout> workouts,
        IReadOnlyDictionary<string, TreinoSyncState> estadoSincronizado)
    {
        var comData = ComDataValida(workouts);

        var maisRecentePorTreino = comData
            .GroupBy(x => x.treino)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.data).First().w);

        var resultado = new Dictionary<string, LyftaWorkout>();
        foreach (var (treino, workout) in maisRecentePorTreino)
        {
            if (estadoSincronizado.TryGetValue(treino, out var estado) && estado.UltimoWorkoutId == workout.Id)
                continue; // este exato workout já está sincronizado — nada de novo para este treino

            resultado[treino] = workout;
        }
        return resultado;
    }

    /// <summary>Workouts de um treino agrupados por dia de calendário (um workout por dia — o mais
    /// recente desse dia, caso haja mais do que um lançamento), ordenados cronologicamente. A posição
    /// N (1-based) nesta lista corresponde à semana N da folha — usado para reenviar uma semana já
    /// escrita a partir da "Visão geral".</summary>
    public static IReadOnlyList<LyftaWorkout> PorDiaDeTreino(IReadOnlyList<LyftaWorkout> workouts, string treino)
    {
        return ComDataValida(workouts)
            .Where(x => x.treino == treino)
            .GroupBy(x => x.data.Date)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderByDescending(x => x.data).First().w)
            .ToList();
    }

    private static List<(string treino, DateTime data, LyftaWorkout w)> ComDataValida(IReadOnlyList<LyftaWorkout> workouts)
        => workouts
            .Select(w => (treino: WorkoutFormatter.NormalizarTreino(w.Title), data: ParseData(w.WorkoutPerformDate), w))
            .Where(x => x.treino is not null && x.data is not null)
            .Select(x => (x.treino!, x.data!.Value, x.w))
            .ToList();

    private static DateTime? ParseData(string s)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
