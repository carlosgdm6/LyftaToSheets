using LyftaToSheets.Models;
using LyftaToSheets.Sheets;

namespace LyftaToSheets.Services;

/// <summary>
/// Lê e escreve na folha "MESOCICLO 1" da spreadsheet do coach, através do <see cref="ISheetsGateway"/>
/// (acesso de baixo nível / autenticação) e do <see cref="SheetGridParser"/> (interpretação pura da
/// grelha). Estratégia: lê SEMPRE a folha antes de escrever, para descobrir a linha real de cada
/// exercício. Assim, se o coach reorganizar exercícios, não escrevemos no sítio errado.
/// </summary>
public sealed class MesocicloService
{
    private readonly ISheetsGateway _gateway;
    private readonly ILogger<MesocicloService> _log;

    public MesocicloService(ISheetsGateway gateway, ILogger<MesocicloService> log)
    {
        _gateway = gateway;
        _log = log;
    }

    /// <summary>Indicação rápida e sem rede: já existe um token Google guardado localmente?
    /// Usado só para decidir o estado inicial da UI (mostrar "Sincronizar" ou "Ligar ao Google");
    /// a autenticação real só acontece quando algo efetivamente chama a API.</summary>
    public bool TemTokenGuardado() => _gateway.TemTokenGuardado();

    /// <summary>Testa a ligação ao Google (autentica e confirma acesso à spreadsheet).</summary>
    public async Task TestarLigacaoAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Google: a testar ligação...");
        await _gateway.TestarLigacaoAsync(ct);
        _log.LogInformation("Google: ligação OK.");
    }

    /// <summary>Lê toda a folha e devolve os blocos de treino (A/B/C/D) com os seus exercícios.</summary>
    public async Task<IReadOnlyList<TreinoBlock>> LerTreinosAsync(int semana, CancellationToken ct = default)
    {
        var rows = await _gateway.LerLinhasAsync(ct);
        var blocos = SheetGridParser.ParseBlocos(rows);

        return blocos
            .Select(b => new TreinoBlock { Nome = b.Nome, Exercicios = MapExercicios(b.Exercicios) })
            .ToList();
    }

    /// <summary>Lê a folha uma vez e devolve, por treino, os exercícios e a próxima semana a preencher
    /// (= última semana com dados + 1). Não escreve nada.</summary>
    public async Task<IReadOnlyList<TreinoImport>> LerParaImportacaoAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Sheets: a ler a folha...");
        var rows = await _gateway.LerLinhasAsync(ct);
        var blocos = SheetGridParser.ParseBlocos(rows);
        _log.LogInformation("Sheets: {Count} linhas lidas, {Blocos} blocos de treino encontrados.", rows.Count, blocos.Count);

        var resultado = blocos
            .Select(b => new TreinoImport
            {
                Nome = b.Nome,
                Exercicios = MapExercicios(b.Exercicios),
                ProximaSemana = SheetGridParser.ProximaSemanaVazia(rows, b.Exercicios)
            })
            .ToList();

        return resultado;
    }

    /// <summary>Lê a folha inteira e monta a grelha treino × semana (preenchida ou não) para o
    /// modal "Visão geral". Mostra só até à última semana com algum dado — não as 29 possíveis.</summary>
    public async Task<VisaoGeralSheet> LerVisaoGeralAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Sheets: a ler visão geral da folha...");
        var rows = await _gateway.LerLinhasAsync(ct);
        var blocos = SheetGridParser.ParseBlocos(rows);

        var brutos = new List<(string nome, bool[] preenchida)>();
        int semanaAtual = 0;

        foreach (var bloco in blocos)
        {
            var preenchida = new bool[SheetGridParser.MaxSemanas];
            for (int semana = 1; semana <= SheetGridParser.MaxSemanas; semana++)
            {
                int col = SheetGridParser.ColunaDaSemana(semana);
                bool temDados = bloco.Exercicios.Any(ex => SheetGridParser.ObterValor(rows, ex.Linha, col) is not null);
                preenchida[semana - 1] = temDados;
                if (temDados) semanaAtual = Math.Max(semanaAtual, semana);
            }
            brutos.Add((bloco.Nome, preenchida));
        }

        int colunas = Math.Max(semanaAtual, 1);
        var treinos = brutos
            .Select(b => new VisaoGeralTreino { Nome = b.nome, Preenchida = b.preenchida.Take(colunas).ToList() })
            .ToList();

        _log.LogInformation("Sheets: visão geral — {Count} treinos, semana atual {Semana}.", treinos.Count, colunas);
        return new VisaoGeralSheet { SemanaAtual = colunas, Treinos = treinos };
    }

    /// <summary>Lê um treino específico (exercícios/linhas) para reenviar uma semana JÁ escrita —
    /// ao contrário de <see cref="LerParaImportacaoAsync"/>, não calcula "próxima semana vazia":
    /// a semana vem definida por quem chama (o utilizador escolheu-a na visão geral).</summary>
    public async Task<TreinoImport?> LerTreinoAsync(string nomeTreino, int semana, CancellationToken ct = default)
    {
        var rows = await _gateway.LerLinhasAsync(ct);
        var blocos = SheetGridParser.ParseBlocos(rows);

        var bloco = blocos.FirstOrDefault(b => string.Equals(b.Nome, nomeTreino, StringComparison.OrdinalIgnoreCase));
        if (bloco is null) return null;

        return new TreinoImport { Nome = bloco.Nome, Exercicios = MapExercicios(bloco.Exercicios), ProximaSemana = semana };
    }

    /// <summary>Escreve o valor das séries (ex: "1x12(30) + 1x10(35)") na célula do exercício/semana.</summary>
    public Task GravarAsync(int linha, int semana, string valor, CancellationToken ct = default)
        => _gateway.GravarLoteAsync([(linha, SheetGridParser.ColunaDaSemana(semana), valor)], ct);

    /// <summary>Escreve várias células de uma vez (uma semana de um treino). Cada tuplo = (linha, valor).</summary>
    public async Task GravarLoteAsync(int semana, IEnumerable<(int linha, string valor)> celulas, CancellationToken ct = default)
    {
        int col = SheetGridParser.ColunaDaSemana(semana);
        var lista = celulas.Select(c => (c.linha, coluna: col, c.valor)).ToList();

        _log.LogInformation("Sheets: a gravar {Count} células na semana {Semana}...", lista.Count, semana);
        await _gateway.GravarLoteAsync(lista, ct);
        _log.LogInformation("Sheets: gravado com sucesso.");
    }

    private static List<Exercicio> MapExercicios(IReadOnlyList<ExercicioBruto> exercicios)
        => exercicios
            .Select(ex => new Exercicio
            {
                Nome = ex.Nome,
                Linha = ex.Linha,
                RepRange = ex.RepRange,
                Observacoes = ex.Observacoes
            })
            .ToList();
}
