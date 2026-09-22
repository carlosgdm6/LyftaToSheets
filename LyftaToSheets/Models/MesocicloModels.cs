namespace LyftaToSheets.Models;

/// <summary>Um exercício de um treino, tal como está na folha (linha real na sheet).</summary>
public sealed record Exercicio
{
    public required string Nome { get; init; }
    public required int Linha { get; init; }          // linha real na folha (1-based)
    public string? RepRange { get; init; }            // coluna G, ex: "1x6-8 + 1x10-12"
    public string? Observacoes { get; init; }         // coluna J
    public string? ValorAtual { get; init; }          // o que já está na célula da semana escolhida
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
    public required IReadOnlyList<LinhaPreview> Linhas { get; init; }
    public required IReadOnlyList<string> Avisos { get; init; }
}
