using LyftaToSheets.Components;
using LyftaToSheets.Lyfta;
using LyftaToSheets.Services;
using LyftaToSheets.Sheets;
using LyftaToSheets.Sync;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configuração da spreadsheet (appsettings.json → secção "Sheets").
var sheetsConfig = builder.Configuration.GetSection("Sheets").Get<SheetsConfig>() ?? new SheetsConfig();
builder.Services.AddSingleton(sheetsConfig);
builder.Services.AddSingleton<ISheetsGateway, GoogleSheetsGateway>();
builder.Services.AddSingleton<MesocicloService>();

// Configuração e cliente do Lyfta.
var lyftaConfig = builder.Configuration.GetSection("Lyfta").Get<LyftaConfig>() ?? new LyftaConfig();
builder.Services.AddSingleton(lyftaConfig);
builder.Services.AddHttpClient<ILyftaClient, LyftaClient>();
builder.Services.AddSingleton<LyftaTokenService>();

// Estado de sincronização: por treino, qual foi o último workout do Lyfta já escrito na folha
// (identidade do workout, não comparação de texto — ver LyftaToSheets.Sync.SyncPlanner). Fica
// num ficheiro local fora do Git, tal como o token_store do Google (ver .gitignore).
var syncStatePath = Path.IsPathRooted(sheetsConfig.SyncStatePath)
    ? sheetsConfig.SyncStatePath
    : Path.Combine(builder.Environment.ContentRootPath, sheetsConfig.SyncStatePath);
builder.Services.AddSingleton<ISyncStateStore>(new JsonSyncStateStore(syncStatePath));

builder.Services.AddScoped<ImportService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Logger.LogInformation(
    "LyftaToSheets a arrancar. Ambiente={Ambiente} ContentRoot={ContentRoot} SpreadsheetId={SpreadsheetId}",
    app.Environment.EnvironmentName, app.Environment.ContentRootPath, sheetsConfig.SpreadsheetId);

app.Run();
