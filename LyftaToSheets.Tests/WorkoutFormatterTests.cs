using LyftaToSheets.Lyfta;
using LyftaToSheets.Services;

namespace LyftaToSheets.Tests;

public class WorkoutFormatterTests
{
    [Theory]
    [InlineData("TREINO B (PUSH)", "TREINO B")]
    [InlineData("treino a", "TREINO A")]
    [InlineData("Treino C - Pernas", "TREINO C")]
    public void NormalizarTreino_ReconheceTreinoValido(string titulo, string esperado)
    {
        Assert.Equal(esperado, WorkoutFormatter.NormalizarTreino(titulo));
    }

    [Theory]
    [InlineData("Cardio")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizarTreino_DevolveNullQuandoNaoReconhece(string? titulo)
    {
        Assert.Null(WorkoutFormatter.NormalizarTreino(titulo!));
    }

    [Fact]
    public void FormatarSets_AgrupaSetsConsecutivosIguais()
    {
        var sets = new List<LyftaSet>
        {
            new() { Reps = "10", Weight = "30" },
            new() { Reps = "10", Weight = "30" },
            new() { Reps = "10", Weight = "30" },
        };

        var resultado = WorkoutFormatter.FormatarSets(sets);

        Assert.Equal("3x10(30)", resultado);
    }

    [Fact]
    public void FormatarSets_NaoAgrupaSetsDiferentes()
    {
        var sets = new List<LyftaSet>
        {
            new() { Reps = "11", Weight = "50" },
            new() { Reps = "8", Weight = "50" },
        };

        var resultado = WorkoutFormatter.FormatarSets(sets);

        Assert.Equal("1x11(50) + 1x8(50)", resultado);
    }

    [Fact]
    public void FormatarSets_ListaVazia_DevolveStringVazia()
    {
        var resultado = WorkoutFormatter.FormatarSets(new List<LyftaSet>());

        Assert.Equal("", resultado);
    }

    [Theory]
    [InlineData("37.500", "37,5")]
    [InlineData("50.000", "50")]
    [InlineData("34.020", "34,02")]
    public void FormatarPeso_FormataCasasDecimaisCorretamente(string entrada, string esperado)
    {
        Assert.Equal(esperado, WorkoutFormatter.FormatarPeso(entrada));
    }

    [Fact]
    public void FormatarPeso_VazioDevolveZero()
    {
        // Confirmado no código-fonte: string.IsNullOrWhiteSpace(weight) => "0".
        Assert.Equal("0", WorkoutFormatter.FormatarPeso(""));
        Assert.Equal("0", WorkoutFormatter.FormatarPeso("   "));
    }

    [Fact]
    public void FormatarPeso_InvalidoDevolveTextoOriginalTrimado()
    {
        // Confirmado no código-fonte: quando double.TryParse falha, devolve weight.Trim().
        Assert.Equal("abc", WorkoutFormatter.FormatarPeso("  abc  "));
    }
}
