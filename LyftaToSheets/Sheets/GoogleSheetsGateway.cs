using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace LyftaToSheets.Sheets;

/// <summary>
/// Implementação real de <see cref="ISheetsGateway"/>: autenticação OAuth contra a Google API e
/// leitura/escrita de células na spreadsheet configurada. Não sabe nada sobre treinos, exercícios
/// ou semanas — isso é responsabilidade do <see cref="SheetGridParser"/> e do MesocicloService.
/// </summary>
public sealed class GoogleSheetsGateway : ISheetsGateway
{
    private readonly SheetsConfig _config;
    private readonly ILogger<GoogleSheetsGateway> _log;
    private readonly IHostEnvironment _env;
    private SheetsService? _sheets;

    public GoogleSheetsGateway(SheetsConfig config, ILogger<GoogleSheetsGateway> log, IHostEnvironment env)
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

    /// <summary>Lê todas as linhas da folha configurada, tal como o Google Sheets as devolve.</summary>
    public async Task<IReadOnlyList<IList<object>>> LerLinhasAsync(CancellationToken ct = default)
    {
        var response = await ComRenovacaoDeTokenAsync(
            svc => svc.Spreadsheets.Values.Get(_config.SpreadsheetId, $"'{_config.SheetName}'").ExecuteAsync(ct), ct);
        return response.Values?.ToList() ?? new List<IList<object>>();
    }

    /// <summary>Escreve várias células de uma vez. Cada tuplo é (linha 1-based, coluna 0-based, valor).</summary>
    public async Task GravarLoteAsync(IEnumerable<(int linha, int coluna, string valor)> celulas, CancellationToken ct = default)
    {
        var data = celulas.Select(c => new ValueRange
        {
            Range = $"'{_config.SheetName}'!{SheetGridParser.ColumnLetter(c.coluna)}{c.linha}",
            Values = [[c.valor]]
        }).ToList();

        var body = new BatchUpdateValuesRequest
        {
            ValueInputOption = "USER_ENTERED",
            Data = data
        };
        await ComRenovacaoDeTokenAsync(svc => svc.Spreadsheets.Values.BatchUpdate(body, _config.SpreadsheetId).ExecuteAsync(ct), ct);
    }
}
