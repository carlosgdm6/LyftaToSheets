using LyftaToSheets.Models;
using LyftaToSheets.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace LyftaToSheets.Components.Pages;

public partial class Registo
{
    private enum Fase { Hero, Sync, Dashboard, Erro }

    private static readonly (string titulo, string detalhe)[] Passos =
    [
        ("A ligar ao Lyfta", "A ir buscar os teus treinos mais recentes."),
        ("A procurar os teus treinos", "A identificar os exercícios de cada treino."),
        ("A organizar os dados", "A encaixar os exercícios nos blocos da folha."),
        ("A procurar uma semana livre", "A folha nunca é sobrescrita — só a próxima semana vazia."),
        ("A enviar para o Google Sheets", "A escrever os valores, treino a treino."),
    ];

    private Fase _fase = Fase.Hero;
    private bool _temTokenGuardado;
    private bool _ocupado;
    private int _passo;
    private string? _erro;
    private IReadOnlyList<TreinoPreview>? _previews;
    private readonly HashSet<string> _gravados = new();
    private int _aGravarIndice = -1;
    private List<string> _chipsExercicios = new();

    private int _treinosSincronizadosSessao;
    private int _semanaMaisAlta;
    private DateTime? _ultimaSincronizacao;
    private IReadOnlyList<TreinoPreview>? _proximaPrevia;
    private bool _aCarregarProxima;
    private System.Threading.Timer? _relogio;

    private bool _definicoesAbertas;
    private bool _testandoGoogle;
    private bool? _googleTesteOk;
    private string _novoToken = "";
    private bool _renovandoToken;
    private bool? _tokenOk;

    private bool _visaoAberta;
    private bool _visaoCarregando;
    private string? _visaoErro;
    private VisaoGeralSheet? _visaoGeral;
    private string? _resyncTreino;
    private int? _resyncSemana;
    private string? _resyncErro;
    private bool _focarSemanaPendente;

    protected override void OnInitialized()
    {
        _temTokenGuardado = Mesociclo.TemTokenGuardado();
        _relogio = new System.Threading.Timer(_ => InvokeAsync(StateHasChanged), null, 20_000, 20_000);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focarSemanaPendente)
        {
            _focarSemanaPendente = false;
            await JS.InvokeVoidAsync("lyftaSyncFocar", "visao-semana-atual");
        }
    }

    public ValueTask DisposeAsync()
    {
        _relogio?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Escreve na consola do BROWSER (F12), não na consola do servidor — para se
    /// conseguir ver o que se passa a partir do próprio browser, ao lado do resto dos logs.</summary>
    private async Task ConsolaAsync(string mensagem, object? dados = null)
    {
        try
        {
            if (dados is null)
                await JS.InvokeVoidAsync("console.log", $"[LyftaSync] {mensagem}");
            else
                await JS.InvokeVoidAsync("console.log", $"[LyftaSync] {mensagem}", dados);
        }
        catch
        {
            // O circuito pode já não estar ligado (ex: página a fechar) — não é um erro a reportar.
        }
    }

    private async Task ConsolaErroAsync(string mensagem, Exception ex)
    {
        try { await JS.InvokeVoidAsync("console.error", $"[LyftaSync] {mensagem}", ex.ToString()); }
        catch { }
    }

    private string PassoEstado(int i) => i < _passo ? "done" : i == _passo ? "active" : "pending";

    private static IEnumerable<(int semana, bool alvo)> SemanaStrip(int alvo)
    {
        int inicio = Math.Max(1, alvo - 2);
        for (int s = inicio; s <= alvo; s++) yield return (s, s == alvo);
    }

    private string NodeClass(int idxNode)
    {
        if (_fase != Fase.Sync) return "";
        bool active = idxNode switch
        {
            0 => _passo is -1 or 0,
            1 => _passo is 1 or 2 or 3,
            2 => _passo == 4,
            _ => false
        };
        bool done = idxNode switch
        {
            0 => _passo >= 1,
            1 => _passo >= 4,
            2 => _passo >= 5,
            _ => false
        };
        return active ? "active" : done ? "done" : "";
    }

    private string NodeStatus(int idxNode)
    {
        if (_fase != Fase.Sync) return "";
        return idxNode switch
        {
            0 => _passo == -1 ? "" : _passo == 0 ? "a ligar…" : _passo >= 1 ? "treinos encontrados" : "",
            1 => _passo == -1 ? "a ligar ao Google…" : _passo is 1 or 2 or 3 ? "a processar…" : _passo >= 4 ? "processado" : "",
            2 => _passo == 4 ? "a escrever…" : _passo >= 5 ? "atualizado" : "",
            _ => ""
        };
    }

    private string ConnectorClass(int idx)
    {
        if (_fase != Fase.Sync) return "";
        bool active = idx == 0 ? _passo == 0 : _passo == 4;
        bool done = idx == 0 ? _passo >= 1 : _passo >= 5;
        return active ? "active" : done ? "done" : "";
    }

    private string TempoDesde()
    {
        if (_ultimaSincronizacao is null) return "agora mesmo";
        var delta = DateTime.Now - _ultimaSincronizacao.Value;
        if (delta.TotalSeconds < 45) return "agora mesmo";
        if (delta.TotalMinutes < 2) return "há 1 minuto";
        if (delta.TotalMinutes < 60) return $"há {(int)delta.TotalMinutes} minutos";
        if (delta.TotalHours < 2) return "há 1 hora";
        return $"há {(int)delta.TotalHours} horas";
    }

    private async Task IniciarAsync()
    {
        if (_ocupado) return;
        _ocupado = true;
        _erro = null;
        _fase = Fase.Sync;
        _passo = -1;
        StateHasChanged();
        await ConsolaAsync("A iniciar sincronização...");

        if (!_temTokenGuardado)
        {
            try
            {
                await Mesociclo.TestarLigacaoAsync();
                _temTokenGuardado = true;
                await ConsolaAsync("Ligação ao Google estabelecida.");
            }
            catch (Exception ex)
            {
                await ConsolaErroAsync("Falha ao ligar ao Google", ex);
                _erro = "Não foi possível ligar ao Google. " + ex.Message;
                _fase = Fase.Erro;
                _ocupado = false;
                return;
            }
        }

        await ExecutarSincronizacaoAsync();
        _ocupado = false;
    }

    private async Task SincronizarDeNovoAsync()
    {
        if (_ocupado) return;
        _ocupado = true;
        _fase = Fase.Sync;
        StateHasChanged();
        await ExecutarSincronizacaoAsync();
        _ocupado = false;
    }

    private async Task ExecutarSincronizacaoAsync()
    {
        _passo = 0;
        _previews = null;
        _chipsExercicios.Clear();
        _gravados.Clear();
        _aGravarIndice = -1;
        StateHasChanged();

        IReadOnlyList<TreinoPreview> previews;
        try
        {
            previews = await Import.GerarPreviewAsync();
        }
        catch (Exception ex)
        {
            await ConsolaErroAsync("Falha ao gerar a pré-visualização", ex);
            _erro = ex.Message;
            _fase = Fase.Erro;
            return;
        }
        _previews = previews;
        await ConsolaAsync($"{previews.Count} proposta(s) de treino geradas.");

        if (previews.Count == 0)
        {
            _passo = 5;
            StateHasChanged();
            await Task.Delay(500);
            await EntrarDashboardAsync(0);
            return;
        }

        _chipsExercicios = previews.SelectMany(p => p.Linhas.Select(l => l.ExercicioLyfta)).Distinct().Take(10).ToList();
        _passo = 1; StateHasChanged(); await Task.Delay(750);

        _passo = 2; StateHasChanged(); await Task.Delay(700);

        _passo = 3; StateHasChanged(); await Task.Delay(850);

        _passo = 4; StateHasChanged();
        int gravadosAgora = 0;
        for (int i = 0; i < previews.Count; i++)
        {
            var p = previews[i];
            _aGravarIndice = i;
            StateHasChanged();
            try
            {
                await Import.GravarAsync(p);
                _gravados.Add(p.Nome);
                _semanaMaisAlta = Math.Max(_semanaMaisAlta, p.Semana);
                gravadosAgora++;
                await ConsolaAsync($"Gravado: {p.Nome} (semana {p.Semana}).");
            }
            catch (Exception ex)
            {
                await ConsolaErroAsync($"Falha ao gravar {p.Nome}", ex);
                _aGravarIndice = -1;
                _erro = gravadosAgora > 0
                    ? $"{gravadosAgora} de {previews.Count} treinos foram gravados antes de um erro. {ex.Message}"
                    : ex.Message;
                _fase = Fase.Erro;
                return;
            }
            StateHasChanged();
            await Task.Delay(320);
        }
        _aGravarIndice = -1;

        _passo = 5;
        StateHasChanged();
        await ConsolaAsync($"Sincronização concluída: {gravadosAgora} treino(s) gravado(s).");
        await Task.Delay(1100);
        await EntrarDashboardAsync(gravadosAgora);
    }

    private async Task EntrarDashboardAsync(int gravadosAgora)
    {
        _treinosSincronizadosSessao += gravadosAgora;
        _ultimaSincronizacao = DateTime.Now;
        _fase = Fase.Dashboard;
        _aCarregarProxima = true;
        StateHasChanged();

        try { _proximaPrevia = await Import.GerarPreviewAsync(); }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Não foi possível carregar a próxima pré-visualização.");
            await ConsolaErroAsync("Não foi possível carregar a próxima pré-visualização", ex);
            _proximaPrevia = null;
        }
        _aCarregarProxima = false;
        StateHasChanged();
    }

    private void TentarNovamente()
    {
        _erro = null;
        _fase = _ultimaSincronizacao is not null ? Fase.Dashboard : Fase.Hero;
    }

    private async Task TestarGoogleAsync()
    {
        _testandoGoogle = true; _googleTesteOk = null;
        StateHasChanged();
        try
        {
            await Mesociclo.TestarLigacaoAsync();
            _googleTesteOk = true;
            _temTokenGuardado = true;
            await ConsolaAsync("Teste de ligação ao Google: OK.");
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Teste de ligação ao Google falhou.");
            await ConsolaErroAsync("Teste de ligação ao Google falhou", ex);
            _googleTesteOk = false;
        }
        finally { _testandoGoogle = false; }
    }

    private async Task RenovarTokenAsync()
    {
        _renovandoToken = true; _tokenOk = null;
        StateHasChanged();
        try
        {
            await LyftaToken.AtualizarAsync(_novoToken);
            _tokenOk = true;
            _novoToken = "";
            await ConsolaAsync("Token do Lyfta atualizado.");
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Atualização do token do Lyfta falhou.");
            await ConsolaErroAsync("Atualização do token do Lyfta falhou", ex);
            _tokenOk = false;
        }
        finally { _renovandoToken = false; }
    }

    private async Task AbrirVisaoGeralAsync()
    {
        _visaoAberta = true;
        _visaoCarregando = true;
        _visaoErro = null;
        _resyncErro = null;
        _visaoGeral = null;
        StateHasChanged();

        try
        {
            _visaoGeral = await Mesociclo.LerVisaoGeralAsync();
            _focarSemanaPendente = true;
            await ConsolaAsync($"Visão geral carregada: {_visaoGeral.Treinos.Count} treino(s), semana atual {_visaoGeral.SemanaAtual}.");
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Não foi possível carregar a visão geral.");
            await ConsolaErroAsync("Não foi possível carregar a visão geral", ex);
            _visaoErro = ex.Message;
        }
        finally
        {
            _visaoCarregando = false;
            StateHasChanged();
        }
    }

    private void FecharVisaoGeral() => _visaoAberta = false;

    private static string Letra(string nomeTreino)
    {
        const string prefixo = "TREINO ";
        return nomeTreino.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)
            ? nomeTreino[prefixo.Length..].Trim()
            : nomeTreino;
    }

    /// <summary>Reenvia um treino já preenchido, para a semana em que já está — chamado ao clicar
    /// numa célula verde na visão geral. Vai buscar outra vez o workout do Lyfta que correspondia
    /// a essa semana e reescreve a célula; não é preciso reabrir a visão geral, a própria grelha
    /// já mostra o resultado (verde continua verde — só o valor por trás é que é atualizado).</summary>
    private async Task ResincronizarAsync(string nomeTreino, int semana)
    {
        if (_ocupado) return;
        _ocupado = true;
        _resyncTreino = nomeTreino;
        _resyncSemana = semana;
        _resyncErro = null;
        StateHasChanged();
        await ConsolaAsync($"A ressincronizar {nomeTreino}, semana {semana}...");

        try
        {
            var preview = await Import.GerarPreviewParaSemanaAsync(nomeTreino, semana);
            if (preview is null)
            {
                _resyncErro = $"Não encontrei o treino do Lyfta correspondente a {nomeTreino}, semana {semana}.";
            }
            else
            {
                await Import.GravarAsync(preview);
                await ConsolaAsync($"{nomeTreino} (semana {semana}) ressincronizado.");
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Falha ao ressincronizar {Treino} (semana {Semana}).", nomeTreino, semana);
            await ConsolaErroAsync($"Falha ao ressincronizar {nomeTreino} (semana {semana})", ex);
            _resyncErro = ex.Message;
        }
        finally
        {
            _resyncTreino = null;
            _resyncSemana = null;
            _ocupado = false;
            StateHasChanged();
        }
    }
}
