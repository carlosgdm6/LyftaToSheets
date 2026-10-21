using Bunit;
using LyftaToSheets.Components.Pages;
using LyftaToSheets.Lyfta;
using LyftaToSheets.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyftaToSheets.Tests;

/// <summary>Testes de componente (bUnit) para <see cref="Registo"/> - a prova de que o fluxo de
/// confirmacao em 3 fases (Hero -> Carregando -> Confirmacao -> so depois de confirmar escreve-se)
/// funciona de verdade, e nao so "parece certo" ao ler o codigo.
///
/// <see cref="ImportService"/> e <see cref="MesocicloService"/> sao classes seladas concretas, nao
/// interfaces - por isso sao construidas aqui com os MESMOS dubles de <see cref="ILyftaClient"/>,
/// <see cref="LyftaToSheets.Sheets.ISheetsGateway"/> e <see cref="LyftaToSheets.Sync.ISyncStateStore"/>
/// ja estabelecidos em <see cref="ImportServiceTests"/> (ver Fakes.cs), e sao as instancias reais
/// resultantes que ficam registadas no DI de teste do bUnit - exatamente como o Registo as injeta
/// (@inject ImportService / @inject MesocicloService).</summary>
public class RegistoComponentTests
{
    private static readonly string[] DoisExercicios = { "Supino", "Remada" };

    /// <summary>Monta um BunitContext com os dois servicos concretos do Registo construidos a partir
    /// dos MESMOS dubles (para que, por exemplo, uma escrita feita via ImportService.GravarAsync
    /// fique visivel se o componente voltar a ler pelo MesocicloService injetado a parte), mais
    /// LyftaTokenService e ILogger&lt;Registo&gt;. O IJSRuntime e tratado pelo proprio bUnit
    /// (JSInterop em modo "Loose" - invocacoes nao configuradas devolvem o valor por omissao em vez
    /// de rebentar), o que cobre as chamadas feitas pelo componente a "console.log"/"console.error"
    /// e, se chegar a acontecer, a "lyftaSyncFocar".</summary>
    private static (BunitContext ctx, FakeSheetsGateway gateway, FakeLyftaClient lyfta, FakeSyncStateStore syncState) CriarContexto(
        FakeSheetsGateway gateway, FakeLyftaClient? lyfta = null, FakeSyncStateStore? syncState = null)
    {
        lyfta ??= new FakeLyftaClient();
        syncState ??= new FakeSyncStateStore();

        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var mesociclo = new MesocicloService(gateway, NullLogger<MesocicloService>.Instance);
        var import = new ImportService(lyfta, mesociclo, syncState, NullLogger<ImportService>.Instance);
        var tokenService = new LyftaTokenService(new FakeHostEnvironment(), new LyftaConfig(), NullLogger<LyftaTokenService>.Instance);

        ctx.Services.AddSingleton(mesociclo);
        ctx.Services.AddSingleton(import);
        ctx.Services.AddSingleton(tokenService);
        ctx.Services.AddSingleton<ILogger<Registo>>(NullLogger<Registo>.Instance);

        return (ctx, gateway, lyfta, syncState);
    }

    private static LyftaWorkout WorkoutTreinoA(long id = 1, string data = "2025-01-02") => new()
    {
        Id = id,
        Title = "TREINO A (PUSH)",
        WorkoutPerformDate = data,
        Exercises = new List<LyftaExercise>
        {
            new() { ExerciseName = "Supino Reto", Sets = new List<LyftaSet> { new() { Reps = "10", Weight = "30" } } },
            new() { ExerciseName = "Remada Curvada", Sets = new List<LyftaSet> { new() { Reps = "8", Weight = "40" } } },
        }
    };

    [Fact]
    public void PrimeiroCarregamento_MostraHeroSemEscreverNadaNemChamarGravacao()
    {
        var (ctx, gateway, _, syncState) = CriarContexto(FakeSheetsGateway.GrelhaPadrao());
        using (ctx)
        {
            var cut = ctx.Render<Registo>();

            Assert.Contains("hero", cut.Markup);
            // "LyftaSync" fica dividido em dois elementos no markup (<h1>...Lyfta<span>Sync</span></h1>) -
            // por isso compara-se o texto visivel do titulo, nao uma substring contigua do HTML.
            Assert.Contains("LyftaSync", cut.Find("h1.hero-title").TextContent);
            Assert.NotNull(cut.Find("button.cta"));

            // So carregar a pagina nao deve ter escrito nada na folha nem registado sincronizacao.
            Assert.Empty(gateway.CelulasGravadas);
            Assert.Empty(syncState.Registos);
        }
    }

    [Fact]
    public void ClicarBotaoPrincipal_AvancaParaConfirmacaoEMostraTreinoESemanaSemEscreverAindaNaFolha()
    {
        // TREINO A com 30 semanas ja preenchidas -> proxima semana livre = 31 (o cenario que estava
        // partido em producao, em que a app nunca passava da semana 29/30).
        var gateway = FakeSheetsGateway.GrelhaGrande(numBlocos: 1, nomesExercicios: DoisExercicios, semanasPreenchidas: 30);
        var lyfta = new FakeLyftaClient { Workouts = { WorkoutTreinoA() } };
        var (ctx, _, _, syncState) = CriarContexto(gateway, lyfta);
        using (ctx)
        {
            var cut = ctx.Render<Registo>();

            cut.Find("button.cta").Click();

            Assert.Contains("TREINO A", cut.Markup);
            Assert.Contains("Semana 31", cut.Markup);
            Assert.Contains("Pronto para sincronizar", cut.Markup);

            // A pre-visualizacao nao deve, por si so, escrever nada nem registar sincronizacao.
            Assert.Empty(gateway.CelulasGravadas);
            Assert.Empty(syncState.Registos);
        }
    }

    [Fact]
    public void ClicarConfirmarEEscrever_SoEntaoChamaAGravacaoNaFolhaERegistaOEstado()
    {
        var gateway = FakeSheetsGateway.GrelhaGrande(numBlocos: 1, nomesExercicios: DoisExercicios, semanasPreenchidas: 30);
        var lyfta = new FakeLyftaClient { Workouts = { WorkoutTreinoA() } };
        var (ctx, _, _, syncState) = CriarContexto(gateway, lyfta);
        using (ctx)
        {
            var cut = ctx.Render<Registo>();
            cut.Find("button.cta").Click();

            // Antes de confirmar: nada escrito ainda (reconfirma o teste anterior, mesmo em cima do clique decisivo).
            Assert.Empty(gateway.CelulasGravadas);

            cut.Find("button.btn-primario").Click(); // "Confirmar e escrever ..."

            Assert.NotEmpty(gateway.CelulasGravadas);
            var registo = Assert.Single(syncState.Registos);
            Assert.Equal("TREINO A", registo.treino);
            Assert.Equal(1, registo.workoutId);
            Assert.Contains("Está feito.", cut.Markup);
        }
    }

    [Fact]
    public void ZeroPropostas_MostraTudoEmDiaEmVezDoEcraDeConfirmacao()
    {
        // Nenhum workout do Lyfta -> SyncPlanner nao escolhe candidatos para nenhum treino -> 0 propostas.
        var gateway = FakeSheetsGateway.GrelhaGrande(numBlocos: 1, nomesExercicios: DoisExercicios, semanasPreenchidas: 30);
        var (ctx, _, _, _) = CriarContexto(gateway);
        using (ctx)
        {
            var cut = ctx.Render<Registo>();

            cut.Find("button.cta").Click();

            Assert.Contains("Tudo em dia", cut.Markup);
            Assert.DoesNotContain("Pronto para sincronizar", cut.Markup);
        }
    }

    [Fact]
    public void ExcecaoAoGerarPreview_MostraEcraDeErroComAMensagemSemRebentar()
    {
        var gateway = FakeSheetsGateway.GrelhaPadrao();
        var lyfta = new FakeLyftaClient { ExcecaoAoObterWorkouts = new InvalidOperationException("Falha simulada a obter treinos do Lyfta.") };
        var (ctx, _, _, _) = CriarContexto(gateway, lyfta);
        using (ctx)
        {
            var cut = ctx.Render<Registo>();

            cut.Find("button.cta").Click();

            Assert.Contains("Não foi possível sincronizar", cut.Markup);
            Assert.Contains("Falha simulada a obter treinos do Lyfta.", cut.Markup);
        }
    }
}
