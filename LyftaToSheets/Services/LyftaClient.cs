using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LyftaToSheets.Services;

/// <summary>Cliente da API do Lyfta. Obtém os treinos (workouts) do utilizador.</summary>
public sealed class LyftaClient
{
    private readonly HttpClient _http;
    private readonly LyftaConfig _config;
    private readonly ILogger<LyftaClient> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public LyftaClient(HttpClient http, LyftaConfig config, ILogger<LyftaClient> log)
    {
        _http = http;
        _config = config;
        _log = log;
        _http.BaseAddress = new Uri(_config.BaseUrl);
    }

    /// <summary>Puxa TODAS as páginas de workouts e devolve a lista completa.</summary>
    public async Task<IReadOnlyList<LyftaWorkout>> GetWorkoutsAsync(CancellationToken ct = default)
    {
        // Lido do config a cada chamada (não só no construtor) para que "Renovar token" tenha efeito
        // imediato, mesmo em instâncias de LyftaClient já criadas antes da atualização.
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(_config.AuthScheme, _config.Token);

        _log.LogInformation("Lyfta: a pedir treinos a {BaseUrl}...", _config.BaseUrl);

        var todos = new List<LyftaWorkout>();
        int pagina = 1, totalPaginas = 1;

        do
        {
            var resp = await _http.GetAsync($"/api/v1/workouts?page={pagina}", ct);

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                _log.LogWarning("Lyfta: 401 Unauthorized — o token da API expirou ou é inválido.");
                throw new InvalidOperationException(
                    "O token do Lyfta expirou ou é inválido. Usa o botão \"Renovar token do Lyfta\" para o atualizares.");
            }
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var page = await JsonSerializer.DeserializeAsync<LyftaWorkoutsResponse>(stream, JsonOpts, ct);
            if (page?.Workouts is { Count: > 0 }) todos.AddRange(page.Workouts);
            totalPaginas = page?.TotalPages ?? 1;
            _log.LogInformation("Lyfta: página {Pagina}/{Total} — {Count} treinos nesta página.",
                pagina, totalPaginas, page?.Workouts?.Count ?? 0);
            pagina++;
        }
        while (pagina <= totalPaginas);

        _log.LogInformation("Lyfta: {Count} treinos no total.", todos.Count);
        return todos;
    }
}

public sealed class LyftaConfig
{
    public string BaseUrl { get; set; } = "https://my.lyfta.app";
    public string Token { get; set; } = "";
    public string AuthScheme { get; set; } = "Bearer";
}

// ---- Modelos da resposta (só os campos que usamos) ----

public sealed class LyftaWorkoutsResponse
{
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("current_page")] public int CurrentPage { get; set; }
    public List<LyftaWorkout>? Workouts { get; set; }
}

public sealed class LyftaWorkout
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    [JsonPropertyName("workout_perform_date")] public string WorkoutPerformDate { get; set; } = "";
    public List<LyftaExercise>? Exercises { get; set; }
}

public sealed class LyftaExercise
{
    [JsonPropertyName("excercise_name")] public string ExerciseName { get; set; } = "";
    public List<LyftaSet>? Sets { get; set; }
}

public sealed class LyftaSet
{
    public string Weight { get; set; } = "";
    public string Reps { get; set; } = "";
}
