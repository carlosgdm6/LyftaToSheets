using LyftaToSheets.Lyfta;
using LyftaToSheets.Models;
using LyftaToSheets.Sync;

namespace LyftaToSheets.Services;

/// <summary>Cruza os treinos do Lyfta com a folha do coach e produz propostas de importação.
///
/// A filtragem de "o que ainda falta sincronizar" é feita pelo <see cref="SyncPlanner"/> (identidade
/// do workout via <see cref="ISyncStateStore"/>, agrupado por dia de calendário) — não há aqui nenhuma
/// heurística de "já está gravado" por comparação de texto nem de "semana ISO mais recente".</summary>
public sealed class ImportService
{
    private readonly ILyftaClient _lyfta;
    private readonly MesocicloService _mesociclo;
    private readonly ISyncStateStore _syncState;
    private readonly ILogger<ImportService> _log;

    public ImportService(ILyftaClient lyfta, MesocicloService mesociclo, ISyncStateStore syncState, ILogger<ImportService> log)
    {
        _lyfta = lyfta;
        _mesociclo = mesociclo;
        _syncState = syncState;
        _log = log;
    }

    /// <summary>Gera uma proposta por treino (A/B/C/D): o workout mais recente do Lyfta que ainda não
    /// foi sincronizado, colocado na próxima semana livre da folha, com mapeamento por posição.</summary>
    public async Task<IReadOnlyList<TreinoPreview>> GerarPreviewAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Import: a gerar preview...");
        var workouts = await _lyfta.GetWorkoutsAsync(ct);
        var estado = await _syncState.CarregarAsync(ct);
        var candidatos = SyncPlanner.EscolherCandidatos(workouts, estado);
        var treinosFolha = await _mesociclo.LerParaImportacaoAsync(ct);

        var previews = new List<TreinoPreview>();
        foreach (var tf in treinosFolha)
        {
            if (!candidatos.TryGetValue(tf.Nome, out var w))
            {
                _log.LogInformation("Import: {Treino} — sem workout novo para este treino, ignorado.", tf.Nome);
                continue;
            }

            var (linhas, avisos) = MapearPosicional(tf.Exercicios, w.Exercises);

            _log.LogInformation("Import: {Treino} — proposta para a semana {Semana} ({Linhas} linhas).",
                tf.Nome, tf.ProximaSemana, linhas.Count);

            previews.Add(new TreinoPreview
            {
                Nome = tf.Nome,
                Semana = tf.ProximaSemana,
                DataLyfta = w.WorkoutPerformDate,
                LyftaWorkoutId = w.Id,
                Linhas = linhas,
                Avisos = avisos
            });
        }
        _log.LogInformation("Import: {Count} propostas geradas.", previews.Count);
        return previews;
    }

    /// <summary>Grava uma proposta na folha (uma semana de um treino) e, SÓ DEPOIS de a escrita ter
    /// sucesso, regista o estado de sincronização — é o que faz o mecanismo de dedupe funcionar; sem
    /// isto a app voltaria a propor o mesmo treino para sempre. Se a escrita falhar, a exceção propaga
    /// e nada é registado.</summary>
    public async Task GravarAsync(TreinoPreview preview, CancellationToken ct = default)
    {
        _log.LogInformation("Import: a gravar {Treino} na semana {Semana}...", preview.Nome, preview.Semana);
        await _mesociclo.GravarLoteAsync(
            preview.Semana,
            preview.Linhas.Where(l => !string.IsNullOrWhiteSpace(l.Valor)).Select(l => (l.Linha, l.Valor)),
            ct);

        await _syncState.RegistarAsync(preview.Nome, preview.LyftaWorkoutId, preview.DataLyfta, ct);
    }

    /// <summary>Gera a proposta para REENVIAR um treino para uma semana já escrita (a partir da "visão
    /// geral"): a semana N da folha corresponde ao N-ésimo dia de calendário em que esse treino foi
    /// feito no Lyfta. Ao contrário do sync normal, não consulta o <see cref="ISyncStateStore"/> — um
    /// pedido de reenvio é sempre para reescrever.</summary>
    public async Task<TreinoPreview?> GerarPreviewParaSemanaAsync(string nomeTreino, int semana, CancellationToken ct = default)
    {
        _log.LogInformation("Import: a gerar proposta para reenviar {Treino} (semana {Semana})...", nomeTreino, semana);

        var tf = await _mesociclo.LerTreinoAsync(nomeTreino, semana, ct);
        if (tf is null)
        {
            _log.LogWarning("Import: {Treino} não existe na folha.", nomeTreino);
            return null;
        }

        var workouts = await _lyfta.GetWorkoutsAsync(ct);
        var porDia = SyncPlanner.PorDiaDeTreino(workouts, nomeTreino);

        if (semana < 1 || semana > porDia.Count)
        {
            _log.LogWarning("Import: sem workout do Lyfta correspondente à semana {Semana} de {Treino}.", semana, nomeTreino);
            return null;
        }
        var w = porDia[semana - 1];

        var (linhas, avisos) = MapearPosicional(tf.Exercicios, w.Exercises);

        return new TreinoPreview
        {
            Nome = tf.Nome,
            Semana = semana,
            DataLyfta = w.WorkoutPerformDate,
            LyftaWorkoutId = w.Id,
            Linhas = linhas,
            Avisos = avisos
        };
    }

    /// <summary>Mapeia os exercícios da folha com os exercícios do Lyfta POSICIONALMENTE (mesmo índice),
    /// até ao menor dos dois tamanhos; avisa se os totais diferirem — partilhado entre o fluxo de
    /// sincronização normal e o de reenvio manual.</summary>
    private static (List<LinhaPreview> linhas, List<string> avisos) MapearPosicional(
        IReadOnlyList<Exercicio> exerciciosFolha, List<LyftaExercise>? exerciciosLyfta)
    {
        var exLyfta = exerciciosLyfta ?? new List<LyftaExercise>();
        var avisos = new List<string>();
        if (exLyfta.Count != exerciciosFolha.Count)
            avisos.Add($"A folha tem {exerciciosFolha.Count} exercícios e o Lyfta tem {exLyfta.Count}. " +
                       "Confirma o alinhamento por posição antes de gravar.");

        var linhas = new List<LinhaPreview>();
        int n = Math.Min(exLyfta.Count, exerciciosFolha.Count);
        for (int i = 0; i < n; i++)
        {
            var alvo = exerciciosFolha[i];
            var origem = exLyfta[i];
            linhas.Add(new LinhaPreview
            {
                Linha = alvo.Linha,
                ExercicioFolha = alvo.Nome,
                ExercicioLyfta = origem.ExerciseName,
                Valor = WorkoutFormatter.FormatarSets(origem.Sets ?? new List<LyftaSet>())
            });
        }
        return (linhas, avisos);
    }
}
