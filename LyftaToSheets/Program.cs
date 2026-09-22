using LyftaToSheets.Components;
using LyftaToSheets.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configuração da spreadsheet (appsettings.json → secção "Sheets").
var sheetsConfig = builder.Configuration.GetSection("Sheets").Get<SheetsConfig>() ?? new SheetsConfig();
builder.Services.AddSingleton(sheetsConfig);
builder.Services.AddSingleton<MesocicloService>();

// Configuração e cliente do Lyfta.
var lyftaConfig = builder.Configuration.GetSection("Lyfta").Get<LyftaConfig>() ?? new LyftaConfig();
builder.Services.AddSingleton(lyftaConfig);
builder.Services.AddHttpClient<LyftaClient>();
builder.Services.AddSingleton<LyftaTokenService>();
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
