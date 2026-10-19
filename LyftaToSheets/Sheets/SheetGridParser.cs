namespace LyftaToSheets.Sheets;

public sealed record ExercicioBruto
{
    public required string Nome { get; init; }
    public required int Linha { get; init; } // 1-based, linha real na folha
    public string? RepRange { get; init; }
    public string? Observacoes { get; init; }
}

public sealed record BlocoBruto
{
    public required string Nome { get; init; }          // "TREINO A"
    public required int HeaderIndex { get; init; }       // 0-based, linha do título na grelha
    public required IReadOnlyList<ExercicioBruto> Exercicios { get; init; }
}

/// <summary>Interpreta a grelha bruta devolvida pelo Google Sheets (linhas × células) de acordo com
/// o esquema fixo da folha "MESOCICLO 1". Não faz nenhuma chamada de rede — por isso é totalmente
/// testável com uma grelha construída à mão, ao contrário do resto do acesso à sheet.</summary>
public static class SheetGridParser
{
    public const int ColExercicio = 1;   // B
    public const int ColRepRange = 6;    // G
    public const int ColObservacoes = 9; // J
    public const int ColSemana1 = 13;    // N (semana 1); cada semana seguinte salta +2 colunas.
    public const int MaxSemanas = 29;

    public static int ColunaDaSemana(int semana) => ColSemana1 + (semana - 1) * 2;

    public static string ObterCelula(IList<object> linha, int indice)
        => indice < linha.Count && linha[indice] is not null ? linha[indice].ToString() ?? "" : "";

    /// <summary>Valor na célula (linha 1-based da folha, coluna 0-based), ou null se vazia/fora da grelha.</summary>
    public static string? ObterValor(IReadOnlyList<IList<object>> rows, int linha1Based, int coluna0Based)
    {
        int idx = linha1Based - 1;
        if (idx < 0 || idx >= rows.Count) return null;
        var v = ObterCelula(rows[idx], coluna0Based);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>Identifica todos os blocos "TREINO X" da folha e os seus exercícios (linha real,
    /// rep range, observações) — sem valores de nenhuma semana; esses lêem-se à parte com <see cref="ObterValor"/>.</summary>
    public static IReadOnlyList<BlocoBruto> ParseBlocos(IReadOnlyList<IList<object>> rows)
    {
        var blocos = new List<BlocoBruto>();
        for (int i = 0; i < rows.Count; i++)
        {
            var nomeB = ObterCelula(rows[i], ColExercicio);
            if (!nomeB.StartsWith("TREINO ", StringComparison.OrdinalIgnoreCase)) continue;

            var nomeTreino = nomeB.Trim();
            var exercicios = new List<ExercicioBruto>();

            // Percorre a partir da linha a seguir ao título; salta a linha de cabeçalho ("EXERCÍCIO").
            for (int j = i + 1; j < rows.Count; j++)
            {
                var b = ObterCelula(rows[j], ColExercicio).Trim();
                if (b.Length == 0) continue;
                if (b.StartsWith("TREINO ", StringComparison.OrdinalIgnoreCase)) break;   // próximo treino
                if (b.Equals("NOTAS", StringComparison.OrdinalIgnoreCase)) break;          // fim do bloco
                if (b.Equals("EXERCÍCIO", StringComparison.OrdinalIgnoreCase)) continue;   // cabeçalho

                exercicios.Add(new ExercicioBruto
                {
                    Nome = b,
                    Linha = j + 1, // Values.Get é 0-based; a folha é 1-based.
                    RepRange = NullIfEmpty(ObterCelula(rows[j], ColRepRange)),
                    Observacoes = NullIfEmpty(ObterCelula(rows[j], ColObservacoes))
                });
            }

            blocos.Add(new BlocoBruto { Nome = nomeTreino, HeaderIndex = i, Exercicios = exercicios });
        }
        return blocos;
    }

    /// <summary>Última semana (1..MaxSemanas) com algum dado no bloco, +1. Se nada preenchido → 1.
    /// É a única fonte de verdade sobre "qual é a próxima coluna livre" — nunca sobrescreve uma
    /// célula já preenchida (pela app ou à mão pelo coach).</summary>
    public static int ProximaSemanaVazia(IReadOnlyList<IList<object>> rows, IReadOnlyList<ExercicioBruto> exercicios)
    {
        int ultimaComDados = 0;
        for (int semana = 1; semana <= MaxSemanas; semana++)
        {
            int col = ColunaDaSemana(semana);
            bool temDados = exercicios.Any(ex => ObterValor(rows, ex.Linha, col) is not null);
            if (temDados) ultimaComDados = semana;
        }
        return Math.Min(ultimaComDados + 1, MaxSemanas);
    }

    /// <summary>Converte índice de coluna 0-based em letras A1 (0→A, 13→N, 26→AA...).</summary>
    public static string ColumnLetter(int zeroBasedCol)
    {
        int n = zeroBasedCol + 1;
        var s = "";
        while (n > 0)
        {
            int rem = (n - 1) % 26;
            s = (char)('A' + rem) + s;
            n = (n - 1) / 26;
        }
        return s;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
