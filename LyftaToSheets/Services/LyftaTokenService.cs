using System.Text.Json;
using System.Text.Json.Nodes;

namespace LyftaToSheets.Services;

/// <summary>Atualiza o token da API do Lyfta: grava-o no appsettings.json (para sobreviver a um
/// reinício) e aplica-o de imediato à configuração em memória, para não ser preciso reiniciar
/// o processo/serviço — o próximo pedido ao Lyfta já usa o token novo.</summary>
public sealed class LyftaTokenService
{
    private readonly string _appsettingsPath;
    private readonly LyftaConfig _config;
    private readonly ILogger<LyftaTokenService> _log;

    public LyftaTokenService(IHostEnvironment env, LyftaConfig config, ILogger<LyftaTokenService> log)
    {
        _appsettingsPath = Path.Combine(env.ContentRootPath, "appsettings.json");
        _config = config;
        _log = log;
    }

    public async Task AtualizarAsync(string novoToken, CancellationToken ct = default)
    {
        novoToken = novoToken.Trim();
        if (novoToken.Length == 0)
            throw new ArgumentException("O token não pode estar vazio.");

        _log.LogInformation("A gravar o novo token do Lyfta em {Path}...", _appsettingsPath);

        var json = await File.ReadAllTextAsync(_appsettingsPath, ct);
        var raiz = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        var lyfta = raiz["Lyfta"]?.AsObject() ?? new JsonObject();
        lyfta["Token"] = novoToken;
        raiz["Lyfta"] = lyfta;

        await File.WriteAllTextAsync(_appsettingsPath, raiz.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);

        _config.Token = novoToken; // efeito imediato, sem reiniciar o processo
        _log.LogInformation("Token do Lyfta atualizado com sucesso.");
    }
}
