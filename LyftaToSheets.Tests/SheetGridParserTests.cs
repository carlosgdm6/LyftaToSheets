using LyftaToSheets.Sheets;

namespace LyftaToSheets.Tests;

public class SheetGridParserTests
{
    // Constrói uma grelha mínima com 2 blocos de treino, cada um com uma linha de cabeçalho
    // ("EXERCÍCIO", a ignorar), dois exercícios, e terminando em "NOTAS".
    private static List<IList<object>> GrelhaComDoisBlocos()
    {
        var rows = new List<IList<object>>();
        // Linha 0 (linha 1 da folha): título do bloco A
        rows.Add(Linha("TREINO A"));
        // Linha 1: cabeçalho — deve ser ignorado
        rows.Add(Linha("EXERCÍCIO"));
        // Linha 2-3: exercícios do bloco A
        rows.Add(Linha("Supino", repRange: "8-10", observacoes: "Pegada média"));
        rows.Add(Linha("Remada"));
        // Linha 4: fim do bloco A
        rows.Add(Linha("NOTAS"));
        // Linha 5: título do bloco B
        rows.Add(Linha("TREINO B"));
        rows.Add(Linha("EXERCÍCIO"));
        rows.Add(Linha("Agachamento"));
        rows.Add(Linha("NOTAS"));
        return rows;

        static List<object> Linha(string exercicio, string? repRange = null, string? observacoes = null)
        {
            var linha = new List<object>();
            for (int i = 0; i <= SheetGridParser.ColObservacoes; i++) linha.Add("");
            linha[SheetGridParser.ColExercicio] = exercicio;
            if (repRange is not null) linha[SheetGridParser.ColRepRange] = repRange;
            if (observacoes is not null) linha[SheetGridParser.ColObservacoes] = observacoes;
            return linha;
        }
    }

    [Fact]
    public void ParseBlocos_IdentificaBlocosEExerciciosComLinhaCorreta()
    {
        var rows = GrelhaComDoisBlocos();

        var blocos = SheetGridParser.ParseBlocos(rows);

        Assert.Equal(2, blocos.Count);

        var blocoA = blocos[0];
        Assert.Equal("TREINO A", blocoA.Nome);
        Assert.Equal(2, blocoA.Exercicios.Count);
        Assert.Equal("Supino", blocoA.Exercicios[0].Nome);
        Assert.Equal(3, blocoA.Exercicios[0].Linha); // linha 0-based 2 -> 1-based 3
        Assert.Equal("8-10", blocoA.Exercicios[0].RepRange);
        Assert.Equal("Pegada média", blocoA.Exercicios[0].Observacoes);
        Assert.Equal("Remada", blocoA.Exercicios[1].Nome);
        Assert.Equal(4, blocoA.Exercicios[1].Linha);

        var blocoB = blocos[1];
        Assert.Equal("TREINO B", blocoB.Nome);
        Assert.Single(blocoB.Exercicios);
        Assert.Equal("Agachamento", blocoB.Exercicios[0].Nome);
        Assert.Equal(8, blocoB.Exercicios[0].Linha); // linha 0-based 7 -> 1-based 8
    }

    [Fact]
    public void ProximaSemanaVazia_SemNadaPreenchido_DevolveUm()
    {
        var rows = GrelhaComDoisBlocos();
        var blocos = SheetGridParser.ParseBlocos(rows);

        var proxima = SheetGridParser.ProximaSemanaVazia(rows, blocos[0].Exercicios);

        Assert.Equal(1, proxima);
    }

    [Fact]
    public void ProximaSemanaVazia_ComAlgumasSemanasPreenchidas_DevolveUltimaMaisUm()
    {
        var rows = GrelhaComDoisBlocos();
        var blocos = SheetGridParser.ParseBlocos(rows);
        var exercicios = blocos[0].Exercicios;

        // Preenche as semanas 1 e 2 para o primeiro exercício do bloco A.
        PreencherCelula(rows, exercicios[0].Linha, SheetGridParser.ColunaDaSemana(1), "3x10(30)");
        PreencherCelula(rows, exercicios[0].Linha, SheetGridParser.ColunaDaSemana(2), "3x10(32)");

        var proxima = SheetGridParser.ProximaSemanaVazia(rows, exercicios);

        Assert.Equal(3, proxima);
    }

    [Fact]
    public void ProximaSemanaVazia_ComTodasAsSemanasPreenchidas_FicaCapadaEmMaxSemanas()
    {
        var rows = GrelhaComDoisBlocos();
        var blocos = SheetGridParser.ParseBlocos(rows);
        var exercicios = blocos[0].Exercicios;

        for (int semana = 1; semana <= SheetGridParser.MaxSemanas; semana++)
            PreencherCelula(rows, exercicios[0].Linha, SheetGridParser.ColunaDaSemana(semana), "3x10(30)");

        var proxima = SheetGridParser.ProximaSemanaVazia(rows, exercicios);

        Assert.Equal(SheetGridParser.MaxSemanas, proxima);
    }

    [Fact]
    public void ObterValor_CelulaVaziaOuAusente_DevolveNull()
    {
        var rows = GrelhaComDoisBlocos();

        Assert.Null(SheetGridParser.ObterValor(rows, 1, SheetGridParser.ColunaDaSemana(1)));
        // coluna muito à frente, ausente da lista da linha
        Assert.Null(SheetGridParser.ObterValor(rows, 1, 500));
    }

    [Fact]
    public void ObterValor_LinhaForaDaGrelha_DevolveNull()
    {
        var rows = GrelhaComDoisBlocos();

        Assert.Null(SheetGridParser.ObterValor(rows, 0, 0));           // 1-based, 0 é inválido (idx -1)
        Assert.Null(SheetGridParser.ObterValor(rows, rows.Count + 10, 0));
    }

    [Fact]
    public void ObterValor_ValorPresente_DevolveTalQual()
    {
        var rows = GrelhaComDoisBlocos();
        var blocos = SheetGridParser.ParseBlocos(rows);
        var exercicio = blocos[0].Exercicios[0];
        PreencherCelula(rows, exercicio.Linha, SheetGridParser.ColunaDaSemana(1), "3x10(30)");

        var valor = SheetGridParser.ObterValor(rows, exercicio.Linha, SheetGridParser.ColunaDaSemana(1));

        Assert.Equal("3x10(30)", valor);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(13, "N")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    public void ColumnLetter_ConverteIndiceParaLetra(int indice, string esperado)
    {
        Assert.Equal(esperado, SheetGridParser.ColumnLetter(indice));
    }

    private static void PreencherCelula(List<IList<object>> rows, int linha1Based, int coluna0Based, string valor)
    {
        var idx = linha1Based - 1;
        var linha = rows[idx];
        while (linha.Count <= coluna0Based) linha.Add("");
        linha[coluna0Based] = valor;
    }
}
