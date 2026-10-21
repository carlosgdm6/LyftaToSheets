using LyftaToSheets.Services;
using LyftaToSheets.Sheets;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyftaToSheets.Tests;

/// <summary>Reproduz, com uma grelha muito maior e mais realista do que a <see cref="FakeSheetsGateway.GrelhaPadrao"/>
/// mínima, o cenário exato que estava partido em produção: uma folha com ~30 semanas já preenchidas para
/// vários blocos de treino (A/B/C/D), cada um com vários exercícios — o ponto em que, com o limite fixo
/// antigo de <see cref="SheetGridParser.MaxSemanas"/> (29), a app ficava presa e nunca avançava para a
/// semana 31.</summary>
public class MesocicloServiceGrelhaRealTests
{
    private static readonly string[] SeteExercicios =
    {
        "Supino Reto", "Supino Inclinado", "Crucifixo", "Elevação Lateral",
        "Tríceps Corda", "Desenvolvimento", "Encolhimento"
    };

    private static MesocicloService CriarServico(FakeSheetsGateway gateway)
        => new(gateway, NullLogger<MesocicloService>.Instance);

    [Fact]
    public async Task LerParaImportacaoAsync_ComGrelhaRealDeQuatroBlocosE30SemanasPreenchidas_CalculaProximaSemana31ParaTodos()
    {
        // 4 blocos (A/B/C/D), 7 exercícios cada, 30 semanas já escritas — literalmente a folha do
        // utilizador em produção, que já ia muito além do antigo limite fixo de 29 semanas.
        var gateway = FakeSheetsGateway.GrelhaGrande(numBlocos: 4, nomesExercicios: SeteExercicios, semanasPreenchidas: 30);
        var servico = CriarServico(gateway);

        var treinos = await servico.LerParaImportacaoAsync();

        Assert.Equal(4, treinos.Count);
        Assert.Equal(new[] { "TREINO A", "TREINO B", "TREINO C", "TREINO D" }, treinos.Select(t => t.Nome));
        foreach (var treino in treinos)
        {
            Assert.Equal(7, treino.Exercicios.Count);
            Assert.Equal(31, treino.ProximaSemana); // nunca deve ficar preso em 29/30
        }
    }

    [Fact]
    public async Task LerParaImportacaoAsync_ComBlocosDeTamanhosDiferentes_CalculaProximaSemanaCorretaParaCadaUm()
    {
        // Blocos com números de exercícios diferentes (6 e 7, como na folha real) para confirmar que o
        // cálculo de "próxima semana livre" é por bloco, não global.
        var gateway = FakeSheetsGateway.GrelhaGrande(numBlocos: 2, nomesExercicios: SeteExercicios, semanasPreenchidas: 31);
        var servico = CriarServico(gateway);

        var treinos = await servico.LerParaImportacaoAsync();

        Assert.All(treinos, t => Assert.Equal(32, t.ProximaSemana));
    }

    [Fact]
    public async Task LerVisaoGeralAsync_ComGrelhaRealDe30SemanasPreenchidas_SemanaAtualChegaA30SemFicarCapada()
    {
        var gateway = FakeSheetsGateway.GrelhaGrande(numBlocos: 4, nomesExercicios: SeteExercicios, semanasPreenchidas: 30);
        var servico = CriarServico(gateway);

        var visao = await servico.LerVisaoGeralAsync();

        // Bug de produção: com o limite antigo de 29, isto nunca passava de 29 — aqui confirma-se que
        // chega mesmo a 30 (a última semana com dados reais na grelha).
        Assert.Equal(30, visao.SemanaAtual);
        Assert.Equal(4, visao.Treinos.Count);
        foreach (var treino in visao.Treinos)
        {
            Assert.Equal(30, treino.Preenchida.Count);
            Assert.All(treino.Preenchida, preenchida => Assert.True(preenchida));
        }
    }

    [Fact]
    public async Task LerVisaoGeralAsync_ComGrelhaRealDeMuitoAlemDoLimiteAntigo_AvancaPassadaSemana29()
    {
        // Regressão direta do bug: 35 semanas preenchidas (> antigo limite de 29).
        var gateway = FakeSheetsGateway.GrelhaGrande(numBlocos: 4, nomesExercicios: SeteExercicios, semanasPreenchidas: 35);
        var servico = CriarServico(gateway);

        var visao = await servico.LerVisaoGeralAsync();
        var treinos = await servico.LerParaImportacaoAsync();

        Assert.Equal(35, visao.SemanaAtual);
        Assert.All(treinos, t => Assert.Equal(36, t.ProximaSemana));
    }
}
