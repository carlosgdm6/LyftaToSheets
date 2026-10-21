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
    /// foi sincronizado, colocado na próxima semana livre da folha, com mapeamento por posição.
    ///
    /// A memória local (<see cref="ISyncStateStore"/>) nunca é a última palavra: para cada treino que
    /// ela diz "já sincronizado", a folha é consultada para confirmar se essa célula ainda tem mesmo
    /// dados. Se o utilizador a apagar à mão (ex: ao corrigir formatação, ou por engano), deixa de
    /// contar como sincronizado e o treino volta a ser proposto.</summary>
    public async Task<IReadOnlyList<TreinoPreview>> GerarPreviewAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Import: a gerar preview...");
        var workouts = await _lyfta.GetWorkoutsAsync(ct);
        var historico = await _syncState.CarregarAsync(ct);

        // O histórico guarda uma entrada por (treino, semana) — para decidir "já está sincronizado?"
        // só interessa a semana mais recente de cada treino.
        var estadoMaisRecente = historico.Values
            .GroupBy(r => r.Treino)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Semana).First());

        var semanasAVerificar = estadoMaisRecente
            .Where(kv => kv.Value.Semana > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Semana);
        var treinosFolha = await _mesociclo.LerParaImportacaoAsync(semanasAVerificar, ct);

        var estadoEfetivo = new Dictionary<string, TreinoSyncState>(estadoMaisRecente);
        foreach (var tf in treinosFolha)
        {
            if (tf.SemanaVerificadaPreenchida == false && estadoEfetivo.ContainsKey(tf.Nome))
            {
                _log.LogInformation(
                    "Import: {Treino} estava marcado como sincronizado (semana {Semana}) mas a célula já não tem dados — a propor de novo.",
                    tf.Nome, estadoMaisRecente[tf.Nome].Semana);
                estadoEfetivo.Remove(tf.Nome);
            }
        }

        var candidatos = SyncPlanner.EscolherCandidatos(workouts, estadoEfetivo);

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

        await _syncState.RegistarAsync(preview.Nome, preview.LyftaWorkoutId, preview.DataLyfta, preview.Semana, ct);
    }

    /// <summary>Gera a proposta para REENVIAR um treino para uma semana já escrita (a partir da "visão
    /// geral"). Ao contrário do sync normal, não filtra por já-sincronizado — um pedido de reenvio é
    /// sempre para reescrever — mas tenta o máximo possível descobrir o workout CERTO:
    ///
    /// 1) Se esta app já escreveu essa semana (está no histórico de <see cref="ISyncStateStore"/>),
    ///    usa a identidade exata desse workout — nunca desalinha, mesmo que a folha tenha semanas sem
    ///    workout correspondente direto no Lyfta (semanas puladas, preenchidas à mão, etc.).
    /// 2) Caso contrário (semana escrita antes de este histórico existir, ou nunca sincronizada por
    ///    esta app), recorre ao melhor esforço de sempre: a semana N corresponde ao N-ésimo dia de
    ///    calendário em que esse treino foi feito no Lyfta.</summary>
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

        LyftaWorkout? w = null;
        var historico = await _syncState.CarregarAsync(ct);
        if (historico.TryGetValue(TreinoSyncState.Chave(nomeTreino, semana), out var registo))
        {
            w = workouts.FirstOrDefault(x => x.Id == registo.UltimoWorkoutId);
            if (w is not null)
                _log.LogInformation("Import: {Treino} semana {Semana} — workout identificado pelo histórico (id {Id}).", nomeTreino, semana, w.Id);
        }

        if (w is null)
        {
            var porDia = SyncPlanner.PorDiaDeTreino(workouts, nomeTreino);
            if (semana >= 1 && semana <= porDia.Count)
                w = porDia[semana - 1];
        }

        if (w is null)
        {
            _log.LogWarning("Import: sem workout do Lyfta correspondente à semana {Semana} de {Treino}.", semana, nomeTreino);
            return null;
        }

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
