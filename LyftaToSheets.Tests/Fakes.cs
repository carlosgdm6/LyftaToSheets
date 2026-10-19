using LyftaToSheets.Lyfta;
using LyftaToSheets.Sheets;
using LyftaToSheets.Sync;

namespace LyftaToSheets.Tests;

/// <summary>Dublê manual de <see cref="ILyftaClient"/> — devolve sempre a lista configurada no teste.</summary>
public sealed class FakeLyftaClient : ILyftaClient
{
    public List<LyftaWorkout> Workouts { get; set; } = new();

    public Task<IReadOnlyList<LyftaWorkout>> GetWorkoutsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LyftaWorkout>>(Workouts);
}

/// <summary>Dublê manual de <see cref="ISyncStateStore"/> — guarda o estado em memória e regista cada
/// chamada a <see cref="RegistarAsync"/> para os testes poderem confirmar o que foi gravado.</summary>
public sealed class FakeSyncStateStore : ISyncStateStore
{
    public Dictionary<string, TreinoSyncState> Estado { get; } = new();
    public List<(string treino, long workoutId, string workoutData)> Registos { get; } = new();

    public Task<IReadOnlyDictionary<string, TreinoSyncState>> CarregarAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, TreinoSyncState>>(Estado);

    public Task RegistarAsync(string treino, long workoutId, string workoutData, CancellationToken ct = default)
    {
        Registos.Add((treino, workoutId, workoutData));
        Estado[treino] = new TreinoSyncState { Treino = treino, UltimoWorkoutId = workoutId, UltimoWorkoutData = workoutData };
        return Task.CompletedTask;
    }
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
}
