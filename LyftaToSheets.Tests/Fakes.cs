using LyftaToSheets.Lyfta;
using LyftaToSheets.Sheets;
using LyftaToSheets.Sync;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace LyftaToSheets.Tests;

/// <summary>Dublê manual de <see cref="ILyftaClient"/> — devolve sempre a lista configurada no teste.</summary>
public sealed class FakeLyftaClient : ILyftaClient
{
    public List<LyftaWorkout> Workouts { get; set; } = new();

    /// <summary>Quando definida, <see cref="GetWorkoutsAsync"/> lança esta exceção em vez de devolver
    /// <see cref="Workouts"/> — usado para simular uma falha a meio da geração de uma pré-visualização
    /// (ex: Lyfta em baixo, token inválido) sem precisar de um dublê de rede real.</summary>
    public Exception? ExcecaoAoObterWorkouts { get; set; }

    public Task<IReadOnlyList<LyftaWorkout>> GetWorkoutsAsync(CancellationToken ct = default)
    {
        if (ExcecaoAoObterWorkouts is not null) throw ExcecaoAoObterWorkouts;
        return Task.FromResult<IReadOnlyList<LyftaWorkout>>(Workouts);
    }
}

/// <summary>Dublê manual de <see cref="IHostEnvironment"/> — só o suficiente para construir um
/// <see cref="LyftaToSheets.Services.LyftaTokenService"/> em testes (que só usa
/// <see cref="ContentRootPath"/> no construtor); nenhum teste de componente precisa de ler/escrever
/// o appsettings.json real.</summary>
public sealed class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "LyftaToSheets.Tests";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>Dublê manual de <see cref="ISyncStateStore"/> — guarda o histórico em memória, indexado
/// como no ficheiro real por <see cref="TreinoSyncState.Chave"/>, e regista cada chamada a
/// <see cref="RegistarAsync"/> para os testes poderem confirmar o que foi gravado.</summary>
public sealed class FakeSyncStateStore : ISyncStateStore
{
    public Dictionary<string, TreinoSyncState> Estado { get; } = new();
    public List<(string treino, long workoutId, string workoutData, int semana)> Registos { get; } = new();

    public Task<IReadOnlyDictionary<string, TreinoSyncState>> CarregarAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, TreinoSyncState>>(Estado);

    public Task RegistarAsync(string treino, long workoutId, string workoutData, int semana, CancellationToken ct = default)
    {
        Registos.Add((treino, workoutId, workoutData, semana));
        Estado[TreinoSyncState.Chave(treino, semana)] = new TreinoSyncState { Treino = treino, UltimoWorkoutId = workoutId, UltimoWorkoutData = workoutData, Semana = semana };
        return Task.CompletedTask;
    }

    /// <summary>Atalho para os testes montarem o estado inicial sem teclar a chave composta à mão.</summary>
    public void Definir(string treino, int semana, long workoutId, string workoutData)
        => Estado[TreinoSyncState.Chave(treino, semana)] = new TreinoSyncState { Treino = treino, Semana = semana, UltimoWorkoutId = workoutId, UltimoWorkoutData = workoutData };
}

/// <summary>Dublê manual de <see cref="ISheetsGateway"/> — mantém uma grelha em memória que simula a
/// folha "MESOCICLO 1" (sem rede nem Google), para os testes poderem confirmar que a célula certa
/// ficou com o valor esperado depois de um GravarLoteAsync.</summary>
public sealed class FakeSheetsGateway : ISheetsGateway
{
    public List<List<object>> Rows { get; }
    public bool LancarExcecaoAoGravar { get; set; }
    public List<(int linha, int coluna, string valor)> CelulasGravadas { get; } = new();

    public FakeSheetsGateway(List<List<object>> rows)
    {
        Rows = rows;
    }

    public bool TemTokenGuardado() => true;

    public Task TestarLigacaoAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<IList<object>>> LerLinhasAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IList<object>>>(Rows.Select(r => (IList<object>)r).ToList());

    public Task GravarLoteAsync(IEnumerable<(int linha, int coluna, string valor)> celulas, CancellationToken ct = default)
    {
        if (LancarExcecaoAoGravar)
            throw new InvalidOperationException("Falha simulada ao gravar na folha.");

        foreach (var (linha, coluna, valor) in celulas)
        {
            CelulasGravadas.Add((linha, coluna, valor));
            var idx = linha - 1;
            while (Rows.Count <= idx) Rows.Add(new List<object>());
            var row = Rows[idx];
            while (row.Count <= coluna) row.Add("");
            row[coluna] = valor;
        }

        return Task.CompletedTask;
    }

    /// <summary>Grelha mínima "MESOCICLO 1": 2 treinos (A com 2 exercícios, B com 1), TREINO A já tem a
    /// semana 1 preenchida para o primeiro exercício (ProximaSemana = 2), TREINO B está totalmente vazio
    /// (ProximaSemana = 1).</summary>
    public static FakeSheetsGateway GrelhaPadrao()
    {
        List<object> Linha(string exercicio, string? valorSemana1 = null)
        {
            var linha = new List<object>();
            for (int i = 0; i <= SheetGridParser.ColSemana1; i++) linha.Add("");
            linha[SheetGridParser.ColExercicio] = exercicio;
            if (valorSemana1 is not null) linha[SheetGridParser.ColSemana1] = valorSemana1;
            return linha;
        }

        var rows = new List<List<object>>
        {
            Linha("TREINO A"),
            Linha("EXERCÍCIO"),
            Linha("Supino", valorSemana1: "3x10(30)"),
            Linha("Remada"),
            Linha("NOTAS"),
            Linha("TREINO B"),
            Linha("EXERCÍCIO"),
            Linha("Agachamento"),
            Linha("NOTAS"),
        };
        return new FakeSheetsGateway(rows);
    }

    /// <summary>Grelha muito maior e mais realista que <see cref="GrelhaPadrao"/> — simula a folha real
    /// de produção: <paramref name="numBlocos"/> treinos ("TREINO A", "TREINO B", ...), cada um com os
    /// exercícios de <paramref name="nomesExercicios"/>, e <paramref name="semanasPreenchidas"/> semanas
    /// já escritas (todas as células de exercício com um valor) — o cenário exato que estava partido em
    /// produção, em que um limite fixo de semanas fazia a app ficar presa sem nunca avançar.</summary>
    public static FakeSheetsGateway GrelhaGrande(int numBlocos, IReadOnlyList<string> nomesExercicios, int semanasPreenchidas)
    {
        int colFinal = SheetGridParser.ColunaDaSemana(Math.Max(semanasPreenchidas, 1));

        List<object> NovaLinha(string exercicio)
        {
            var linha = new List<object>();
            for (int i = 0; i <= colFinal; i++) linha.Add("");
            linha[SheetGridParser.ColExercicio] = exercicio;
            return linha;
        }

        var rows = new List<List<object>>();
        var linhasExercicios = new List<List<object>>();
        for (int b = 0; b < numBlocos; b++)
        {
            char letra = (char)('A' + b);
            rows.Add(NovaLinha($"TREINO {letra}"));
            rows.Add(NovaLinha("EXERCÍCIO"));
            foreach (var nome in nomesExercicios)
            {
                var linha = NovaLinha(nome);
                rows.Add(linha);
                linhasExercicios.Add(linha);
            }
            rows.Add(NovaLinha("NOTAS"));
        }

        for (int semana = 1; semana <= semanasPreenchidas; semana++)
        {
            int col = SheetGridParser.ColunaDaSemana(semana);
            foreach (var linha in linhasExercicios)
                linha[col] = $"3x10({30 + semana})";
        }

        return new FakeSheetsGateway(rows);
    }
}
