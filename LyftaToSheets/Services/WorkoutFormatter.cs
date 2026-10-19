using System.Globalization;
using System.Text.RegularExpressions;
using LyftaToSheets.Lyfta;

namespace LyftaToSheets.Services;

/// <summary>Converte um workout do Lyfta para o formato da folha do coach.</summary>
public static partial class WorkoutFormatter
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    /// <summary>"TREINO B (PUSH)" → "TREINO B". Devolve null se não reconhecer.</summary>
    public static string? NormalizarTreino(string title)
    {
        var m = TreinoRegex().Match(title ?? "");
        return m.Success ? $"TREINO {m.Groups[1].Value.ToUpperInvariant()}" : null;
    }

    /// <summary>Sets → "1x11(50) + 1x8(50)". Agrupa sets consecutivos iguais (2x10(30)).</summary>
    public static string FormatarSets(IEnumerable<LyftaSet> sets)
    {
        var partes = new List<string>();
        int count = 0;
        string? reps = null, peso = null;

        void Flush()
        {
            if (count > 0 && reps is not null)
                partes.Add($"{count}x{reps}({peso})");
        }

        foreach (var s in sets)
        {
            var r = s.Reps?.Trim() ?? "";
            var p = FormatarPeso(s.Weight);
            if (r == reps && p == peso) { count++; }
            else { Flush(); count = 1; reps = r; peso = p; }
        }
        Flush();
        return string.Join(" + ", partes);
    }

    /// <summary>"37.500" → "37,5"; "50.000" → "50"; "34.020" → "34,02".</summary>
    public static string FormatarPeso(string weight)
    {
        if (string.IsNullOrWhiteSpace(weight)) return "0";
        if (!double.TryParse(weight, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return weight.Trim();
        // Até 3 casas, sem zeros à direita, com vírgula (pt-PT).
        return v.ToString("0.###", Pt);
    }

    [GeneratedRegex(@"TREINO\s+([A-D])", RegexOptions.IgnoreCase)]
    private static partial Regex TreinoRegex();
}
