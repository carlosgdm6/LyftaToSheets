using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using LyftaToSheets.Models;

namespace LyftaToSheets.Services;

/// <summary>
/// Lê e escreve na folha "MESOCICLO 1" da spreadsheet do coach.
/// Estratégia: lê SEMPRE a folha antes de escrever, para descobrir a linha real
/// de cada exercício. Assim, se o coach reorganizar exercícios, não escrevemos no sítio errado.
/// </summary>
public sealed class MesocicloService
{
    private readonly SheetsConfig _config;
    private readonly ILogger<MesocicloService> _log;
    private readonly IHostEnvironment _env;
    private SheetsService? _sheets;

    // Nomes das colunas fixas (0-based) na folha MESOCICLO 1.
    private const int ColExercicio = 1;   // B
    private const int ColRepRange = 6;    // G
    private const int ColObservacoes = 9; // J
    private const int ColSemana1 = 13;    // N (semana 1); cada semana seguinte salta +2 colunas.

    public MesocicloService(SheetsConfig config, ILogger<MesocicloService> log, IHostEnvironment env)
    {
        _config = config;
        _log = log;
        _env = env;
    }

    /// <summary>Indicação rápida e sem rede: já existe um token Google guardado localmente?
    /// Usado só para decidir o estado inicial da UI (mostrar "Sincronizar" ou "Ligar ao Google");
    /// a autenticação real só acontece quando algo efetivamente chama a API.</summary>
    public bool TemTokenGuardado()
        => Directory.Exists(_config.TokenFolder) && Directory.EnumerateFiles(_config.TokenFolder).Any();

    /// <summary>Autentica via OAuth e guarda o token localmente. Fora de Development (ex: publicado
    /// no IIS) NUNCA deve tentar o fluxo interativo (abrir um browser) sem já ter um token guardado —
    /// no servidor não há sessão de ambiente de trabalho nem browser para completar esse consentimento,
    /// por isso o AuthorizeAsync ficaria pendurado para sempre à espera de um redirect que nunca chega
    /// (foi exatamente isto que aconteceu em produção: o token expirou, o código tentou reautenticar
    /// sozinho, e ficou preso ali). Falhar já, com uma mensagem clara, é sempre melhor que ficar pendurado.</summary>
    private async Task<SheetsService> GetServiceAsync(CancellationToken ct)
    {
        if (_sheets is not null) return _sheets;

        if (!_env.IsDevelopment() && !TemTokenGuardado())
        {
            throw new InvalidOperationException(
                "Não há nenhum token do Google guardado e este ambiente (produção) não consegue abrir " +
                "um browser para autenticar interativamente. Autentica localmente (dotnet run), completa " +
                "o login que abre no browser, e copia a pasta 'token_store' resultante para este servidor.");
        }

        _log.LogInformation("Google: a autenticar (credentials={Credentials}, tokens={TokenFolder})...",
            _config.CredentialsPath, _config.TokenFolder);

        await using var stream = File.OpenRead(_config.CredentialsPath);
        var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets;

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            [SheetsService.Scope.Spreadsheets],
            "user",
            ct,
            new FileDataStore(_config.TokenFolder, fullPath: true));

        _sheets = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "LyftaToSheets"
        });
        _log.LogInformation("Google: autenticado.");
        return _sheets;
    }

    /// <summary>Corre uma chamada à API do Sheets, e se o token guardado tiver expirado/sido
    /// revogado (invalid_grant — o erro mais comum, e que antes ficava permanentemente por
    /// resolver até o utilizador carregar manualmente em "Testar ligação"), limpa-o e reautentica
    /// automaticamente antes de repetir a chamada uma única vez. Assim qualquer operação (não só o
    /// teste de ligação) se autorrecupera de um token inválido.</summary>
    private async Task<T> ComRenovacaoDeTokenAsync<T>(Func<SheetsService, Task<T>> acao, CancellationToken ct)
    {
        var svc = await GetServiceAsync(ct);
        try
        {
            return await acao(svc);
        }
        catch (Exception ex) when (EhTokenInvalido(ex))
        {
            _log.LogWarning(ex, "Google: token inválido/expirado (invalid_grant). A limpar e a autenticar de novo...");
            _sheets = null;
            if (Directory.Exists(_config.TokenFolder))
                Directory.Delete(_config.TokenFolder, recursive: true);

            svc = await GetServiceAsync(ct);
            var resultado = await acao(svc);
            _log.LogInformation("Google: recuperado com sucesso depois de reautenticar.");
            return resultado;
        }
    }

    private static bool EhTokenInvalido(Exception ex)
        => ex is Google.Apis.Auth.OAuth2.Responses.TokenResponseException
           || ex.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase);

    /// <summary>Testa a ligação ao Google (autentica e confirma acesso à spreadsheet).</summary>
    public async Task TestarLigacaoAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Google: a testar ligação...");
        await ComRenovacaoDeTokenAsync(svc => svc.Spreadsheets.Get(_config.SpreadsheetId).ExecuteAsync(ct), ct);
        _log.LogInformation("Google: ligação OK.");
    }

    /// <summary>Lê toda a folha e devolve os blocos de treino (A/B/C/D) com os exercícios,
    /// já com o valor atual da semana escolhida.</summary>
    public async Task<IReadOnlyList<TreinoBlock>> LerTreinosAsync(int semana, CancellationToken ct = default)
    {
        var range = $"'{_config.SheetName}'";
        var response = await ComRenovacaoDeTokenAsync(svc => svc.Spreadsheets.Values.Get(_config.SpreadsheetId, range).ExecuteAsync(ct), ct);
        var rows = response.Values ?? new List<IList<object>>();

        int colSemana = ColSemana1 + (semana - 1) * 2;

        var blocos = new List<TreinoBlock>();
        for (int i = 0; i < rows.Count; i++)
        {
            var nomeB = Cell(rows[i], ColExercicio);
            if (nomeB.StartsWith("TREINO ", StringComparison.OrdinalIgnoreCase))
            {
                var (nome, exercicios) = LerBloco(rows, i, colSemana);
                blocos.Add(new TreinoBlock { Nome = nome, Exercicios = exercicios });
            }
        }
        return blocos;
    }

    /// <summary>Lê a folha uma vez e devolve, por treino, os exercícios e a próxima semana a preencher
    /// (= última semana com dados + 1). Não escreve nada.
    /// O <see cref="Exercicio.ValorAtual"/> de cada exercício vem da última semana já preenchida
    /// (não da semana 1), para o ImportService poder comparar com o que vai propor e detetar
    /// treinos já registados na folha — pela app ou à mão — sem precisar de guardar estado à parte.</summary>
    public async Task<IReadOnlyList<TreinoImport>> LerParaImportacaoAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Sheets: a ler a folha '{Sheet}'...", _config.SheetName);
        var response = await ComRenovacaoDeTokenAsync(svc => svc.Spreadsheets.Values.Get(_config.SpreadsheetId, $"'{_config.SheetName}'").ExecuteAsync(ct), ct);
        var rows = response.Values ?? new List<IList<object>>();
        _log.LogInformation("Sheets: {Count} linhas lidas.", rows.Count);

        var resultado = new List<TreinoImport>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (!Cell(rows[i], ColExercicio).StartsWith("TREINO ", StringComparison.OrdinalIgnoreCase)) continue;

            var (nome, exerciciosBase) = LerBloco(rows, i, ColSemana1);
            int proxima = ProximaSemanaVazia(rows, exerciciosBase);

            var exercicios = exerciciosBase;
            if (proxima > 1)
            {
                int colAnterior = ColSemana1 + (proxima - 2) * 2;
                (_, exercicios) = LerBloco(rows, i, colAnterior);
            }

            resultado.Add(new TreinoImport { Nome = nome, Exercicios = exercicios, ProximaSemana = proxima });
        }
        _log.LogInformation("Sheets: {Count} blocos de treino encontrados.", resultado.Count);
        return resultado;
    }

    /// <summary>Última semana (1..29) com algum dado no bloco, +1. Se nada preenchido → 1.</summary>
    private static int ProximaSemanaVazia(IList<IList<object>> rows, IReadOnlyList<Exercicio> exercicios)
    {
        int ultimaComDados = 0;
        for (int semana = 1; semana <= 29; semana++)
        {
            int col = ColSemana1 + (semana - 1) * 2;
            bool temDados = exercicios.Any(ex =>
            {
                int idx = ex.Linha - 1; // volta a 0-based
                return idx < rows.Count && Cell(rows[idx], col).Trim().Length > 0;
            });
            if (temDados) ultimaComDados = semana;
        }
        return Math.Min(ultimaComDados + 1, 29);
    }

    private (string nome, List<Exercicio> exercicios) LerBloco(IList<IList<object>> rows, int headerIdx, int colSemana)
    {
        var nomeTreino = Cell(rows[headerIdx], ColExercicio).Trim();
        var exercicios = new List<Exercicio>();

        // Percorre a partir da linha do título; salta a linha de cabeçalho ("EXERCÍCIO").
        for (int i = headerIdx + 1; i < rows.Count; i++)
        {
            var b = Cell(rows[i], ColExercicio).Trim();
            if (b.Length == 0) continue;
            if (b.StartsWith("TREINO ", StringComparison.OrdinalIgnoreCase)) break;      // próximo treino
            if (b.Equals("NOTAS", StringComparison.OrdinalIgnoreCase)) break;            // fim do bloco
            if (b.Equals("EXERCÍCIO", StringComparison.OrdinalIgnoreCase)) continue;     // cabeçalho

            exercicios.Add(new Exercicio
            {
                Nome = b,
                Linha = i + 1, // Values.Get é 0-based; a folha é 1-based.
                RepRange = NullIfEmpty(Cell(rows[i], ColRepRange)),
                Observacoes = NullIfEmpty(Cell(rows[i], ColObservacoes)),
                ValorAtual = NullIfEmpty(Cell(rows[i], colSemana))
            });
        }
        return (nomeTreino, exercicios);
    }

    /// <summary>Escreve o valor das séries (ex: "1x12(30) + 1x10(35)") na célula do exercício/semana.</summary>
    public async Task GravarAsync(int linha, int semana, string valor, CancellationToken ct = default)
    {
        int colSemana = ColSemana1 + (semana - 1) * 2;
        string a1 = $"'{_config.SheetName}'!{ColumnLetter(colSemana)}{linha}";
        var body = new ValueRange { Values = [[valor]] };

        await ComRenovacaoDeTokenAsync(svc =>
        {
            var req = svc.Spreadsheets.Values.Update(body, _config.SpreadsheetId, a1);
            req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            return req.ExecuteAsync(ct);
        }, ct);
    }

    /// <summary>Escreve várias células de uma vez (uma semana de um treino). Cada tuplo = (linha, valor).</summary>
    public async Task GravarLoteAsync(int semana, IEnumerable<(int linha, string valor)> celulas, CancellationToken ct = default)
    {
        int col = ColSemana1 + (semana - 1) * 2;

        var data = celulas.Select(c => new ValueRange
        {
            Range = $"'{_config.SheetName}'!{ColumnLetter(col)}{c.linha}",
            Values = [[c.valor]]
        }).ToList();

        _log.LogInformation("Sheets: a gravar {Count} células na semana {Semana}...", data.Count, semana);

        var body = new BatchUpdateValuesRequest
        {
            ValueInputOption = "USER_ENTERED",
            Data = data
        };
        await ComRenovacaoDeTokenAsync(svc => svc.Spreadsheets.Values.BatchUpdate(body, _config.SpreadsheetId).ExecuteAsync(ct), ct);

        _log.LogInformation("Sheets: gravado com sucesso.");
    }

    private static string Cell(IList<object> row, int index)
        => index < row.Count && row[index] is not null ? row[index].ToString() ?? "" : "";

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Converte índice de coluna 0-based em letras A1 (0→A, 13→N, 26→AA...).</summary>
    private static string ColumnLetter(int zeroBased)
    {
        int n = zeroBased + 1;
        var s = "";
        while (n > 0)
        {
            int rem = (n - 1) % 26;
            s = (char)('A' + rem) + s;
            n = (n - 1) / 26;
        }
        return s;
    }
}

public sealed class SheetsConfig
{
    public string SpreadsheetId { get; set; } = "";
    public string SheetName { get; set; } = "MESOCICLO 1";
    public string CredentialsPath { get; set; } = "credentials.json";
    public string TokenFolder { get; set; } = "token_store";
}
