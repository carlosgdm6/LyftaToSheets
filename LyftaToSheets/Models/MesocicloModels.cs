namespace LyftaToSheets.Models;

/// <summary>Um exercício de um treino, tal como está na folha (linha real na sheet).</summary>
public sealed record Exercicio
{
    public required string Nome { get; init; }
    public required int Linha { get; init; }          // linha real na folha (1-based)
    public string? RepRange { get; init; }            // coluna G, ex: "1x6-8 + 1x10-12"
    public string? Observacoes { get; init; }         // coluna J
}

/// <summary>Um bloco de treino (A/B/C/D) com os seus exercícios.</summary>
public sealed record TreinoBlock
{
    public required string Nome { get; init; }        // "TREINO A"
    public required IReadOnlyList<Exercicio> Exercicios { get; init; }
}

/// <summary>Um treino lido da folha, com a próxima semana livre para importação.</summary>
public sealed record TreinoImport
{
    public required string Nome { get; init; }
    public required IReadOnlyList<Exercicio> Exercicios { get; init; }
    public required int ProximaSemana { get; init; }

    /// <summary>Se foi pedido para confirmar uma semana específica (a que o estado de sincronização
    /// guardado diz que já foi escrita), diz se essa célula ainda tem mesmo dados agora — null se
    /// não foi pedida nenhuma verificação. Permite à app não confiar cegamente na sua própria
    /// memória: se o utilizador apagar a célula à mão, a próxima sincronização volta a propô-la.</summary>
    public bool? SemanaVerificadaPreenchida { get; init; }
}

/// <summary>Uma linha de pré-visualização: o que vai ser escrito numa célula.</summary>
public sealed record LinhaPreview
{
    public required int Linha { get; init; }
    public required string ExercicioFolha { get; init; }   // nome na folha (posição)
    public required string ExercicioLyfta { get; init; }   // nome no Lyfta (posição)
    public required string Valor { get; init; }            // "1x11(50) + 1x8(50)"
}

/// <summary>Proposta de importação de um treino para uma semana concreta.</summary>
public sealed record TreinoPreview
{
    public required string Nome { get; init; }             // "TREINO A"
    public required int Semana { get; init; }
    public required string DataLyfta { get; init; }        // data do workout de origem
    public required long LyftaWorkoutId { get; init; }     // identidade do workout — usado para registar o estado de sincronização após gravar
    public required IReadOnlyList<LinhaPreview> Linhas { get; init; }
    public required IReadOnlyList<string> Avisos { get; init; }
}

/// <summary>Uma linha da visão geral (um treino) com o preenchimento de cada semana até
/// à semana atual — para a grelha treino × semana do modal "Visão geral".</summary>
public sealed record VisaoGeralTreino
{
    public required string Nome { get; init; }                     // "TREINO A"
    public required IReadOnlyList<bool> Preenchida { get; init; }   // índice 0 = semana 1
}

/// <summary>A folha inteira vista como grelha treino × semana, até à última semana com dados.</summary>
public sealed record VisaoGeralSheet
{
    public required int SemanaAtual { get; init; }  // = nº de colunas a mostrar (última semana com dados; 1 se nenhuma)
    public required IReadOnlyList<VisaoGeralTreino> Treinos { get; init; }
}
