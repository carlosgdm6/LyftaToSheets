using LyftaToSheets.Models;

namespace LyftaToSheets.Services;

/// <summary>Cruza os treinos do Lyfta com a folha do coach e produz propostas de importação.</summary>
public sealed class ImportService
{
    private readonly LyftaClient _lyfta;
    private readonly MesocicloService _mesociclo;
    private readonly ILogger<ImportService> _log;

    public ImportService(LyftaClient lyfta, MesocicloService mesociclo, ILogger<ImportService> log)
    {
        _lyfta = lyfta;
        _mesociclo = mesociclo;
        _log = log;
    }

    /// <summary>Gera uma proposta por treino (A/B/C/D): o workout mais recente do Lyfta
    /// colocado na próxima semana livre da folha, com mapeamento por posição.</summary>
    public async Task<IReadOnlyList<TreinoPreview>> GerarPreviewAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Import: a gerar preview...");
        var workouts = await _lyfta.GetWorkoutsAsync(ct);
        var treinosFolha = await _mesociclo.LerParaImportacaoAsync(ct);

        // Só interessam os treinos da SEMANA DE TREINO mais recente. Assim não voltamos a propor
        // treinos de semanas anteriores que já foram registados na folha.
        var comData = workouts
            .Select(w => (treino: WorkoutFormatter.NormalizarTreino(w.Title), data: ParseData(w.WorkoutPerformDate), w))
            .Where(x => x.treino is not null && x.data is not null)
            .ToList();

        int? semanaAlvo = comData.Count > 0 ? comData.Max(x => SemanaIso(x.data!.Value)) : null;

        // Workout mais recente por treino, apenas dentro da semana ISO mais recente.
        var maisRecentePorTreino = comData
            .Where(x => SemanaIso(x.data!.Value) == semanaAlvo)
            .GroupBy(x => x.treino!)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.data!.Value).First().w);

        var previews = new List<TreinoPreview>();
        foreach (var tf in treinosFolha)
        {
            if (!maisRecentePorTreino.TryGetValue(tf.Nome, out var w))
            {
                _log.LogInformation("Import: {Treino} — sem workout na semana de treino atual, ignorado.", tf.Nome);
                continue;
            }

            var exLyfta = w.Exercises ?? new List<LyftaExercise>();
            var avisos = new List<string>();
            if (exLyfta.Count != tf.Exercicios.Count)
                avisos.Add($"A folha tem {tf.Exercicios.Count} exercícios e o Lyfta tem {exLyfta.Count}. " +
                           "Confirma o alinhamento por posição antes de gravar.");

            var linhas = new List<LinhaPreview>();
            int n = Math.Min(exLyfta.Count, tf.Exercicios.Count);
            for (int i = 0; i < n; i++)
            {
                var alvo = tf.Exercicios[i];
                var origem = exLyfta[i];
                linhas.Add(new LinhaPreview
                {
                    Linha = alvo.Linha,
                    ExercicioFolha = alvo.Nome,
                    ExercicioLyfta = origem.ExerciseName,
                    Valor = WorkoutFormatter.FormatarSets(origem.Sets ?? new List<LyftaSet>())
                });
            }

            if (JaEstaGravado(tf.Exercicios, linhas))
            {
                _log.LogInformation("Import: {Treino} — já está gravado na folha (semana {Semana}), ignorado.", tf.Nome, tf.ProximaSemana);
                continue; // os valores já estão na folha (gravados pela app ou à mão) — não repetir
            }

            _log.LogInformation("Import: {Treino} — proposta para a semana {Semana} ({Linhas} linhas).",
                tf.Nome, tf.ProximaSemana, linhas.Count);

            previews.Add(new TreinoPreview
            {
                Nome = tf.Nome,
                Semana = tf.ProximaSemana,
                DataLyfta = w.WorkoutPerformDate,
                Linhas = linhas,
                Avisos = avisos
            });
        }
        _log.LogInformation("Import: {Count} propostas geradas.", previews.Count);
        return previews;
    }

    /// <summary>Verifica se os valores a propor já estão todos escritos na folha (na última semana
    /// preenchida de cada exercício), para não voltar a propor um treino já registado — pela app ou à mão.</summary>
    private static bool JaEstaGravado(IReadOnlyList<Exercicio> exercicios, IReadOnlyList<LinhaPreview> linhas)
    {
        var comValor = linhas.Where(l => !string.IsNullOrWhiteSpace(l.Valor)).ToList();
        if (comValor.Count == 0) return false;

        var atualPorLinha = exercicios.ToDictionary(e => e.Linha, e => e.ValorAtual);
        return comValor.All(l =>
            atualPorLinha.TryGetValue(l.Linha, out var atual) &&
            string.Equals(atual?.Trim(), l.Valor.Trim(), StringComparison.Ordinal));
    }

    private static DateTime? ParseData(string s)
        => DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Chave (ano, semana ISO) para agrupar treinos da mesma semana de treino.</summary>
    private static int SemanaIso(DateTime d)
        => System.Globalization.ISOWeek.GetYear(d) * 100 + System.Globalization.ISOWeek.GetWeekOfYear(d);

    /// <summary>Grava uma proposta na folha (uma semana de um treino).</summary>
    public async Task GravarAsync(TreinoPreview preview, CancellationToken ct = default)
    {
        _log.LogInformation("Import: a gravar {Treino} na semana {Semana}...", preview.Nome, preview.Semana);
        await _mesociclo.GravarLoteAsync(
            preview.Semana,
            preview.Linhas.Where(l => !string.IsNullOrWhiteSpace(l.Valor)).Select(l => (l.Linha, l.Valor)),
            ct);
    }
}
