using System.Globalization;
using LyftaToSheets.Models;
using LyftaToSheets.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace LyftaToSheets.Components.Pages;

public partial class Registo
{
    private enum Fase { Hero, Carregando, Confirmacao, AGravar, Concluido, Dashboard, Erro }

    private Fase _fase = Fase.Hero;
    private bool _temTokenGuardado;
    private bool _ocupado;
    private string? _erro;
    private IReadOnlyList<TreinoPreview>? _previews;
    private readonly HashSet<string> _gravados = new();
    private string? _aGravarNome;

    private int _treinosSincronizadosSessao;
    private int _semanaMaisAlta;
    private DateTime? _ultimaSincronizacao;
    private int _gravadosNesteSync;
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
    private bool _resyncCarregando;
    private TreinoPreview? _resyncPreview;
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

    /// <summary>"2026-10-21 06:04:14" → "21/10/2026". Se não conseguir interpretar a data,
    /// mostra o texto tal como veio do Lyfta em vez de rebentar.</summary>
    private static string FormatarData(string dataLyfta)
        => DateTime.TryParse(dataLyfta, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : dataLyfta;

    private async Task IniciarAsync()
    {
        if (_ocupado) return;
        _ocupado = true;
        _erro = null;
        StateHasChanged();

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
                StateHasChanged();
                return;
            }
        }

        await CarregarPreviewAsync();
        _ocupado = false;
    }

    /// <summary>Vai buscar a pré-visualização (o que seria escrito) sem escrever nada — o
    /// utilizador só vê a confirmação na fase seguinte e decide se avança.</summary>
    private async Task CarregarPreviewAsync()
    {
        _fase = Fase.Carregando;
        _gravados.Clear();
        _aGravarNome = null;
        StateHasChanged();

        try
        {
            _previews = await Import.GerarPreviewAsync();
            await ConsolaAsync($"{_previews.Count} proposta(s) de treino geradas.");
            _fase = Fase.Confirmacao;
        }
        catch (Exception ex)
        {
            await ConsolaErroAsync("Falha ao gerar a pré-visualização", ex);
            _erro = ex.Message;
            _fase = Fase.Erro;
        }
        StateHasChanged();
    }

    private void Voltar()
    {
        _previews = null;
        _fase = FaseDeRetorno();
    }

    /// <summary>Depois de cancelar ou de um erro, volta ao dashboard se já houve alguma
    /// sincronização nesta sessão, ou ao ecrã inicial caso contrário.</summary>
    private Fase FaseDeRetorno() => _ultimaSincronizacao is not null ? Fase.Dashboard : Fase.Hero;

    /// <summary>Reutiliza a pré-visualização que já está carregada no cartão "Próxima
    /// sincronização" em vez de voltar a pedir a mesma coisa à Lyfta e à Google Sheet.</summary>
    private async Task VerProximaSincronizacaoAsync()
    {
        if (_proximaPrevia is not null)
        {
            _previews = _proximaPrevia;
            _gravados.Clear();
            _aGravarNome = null;
            _fase = Fase.Confirmacao;
            StateHasChanged();
            return;
        }
        await CarregarPreviewAsync();
    }

    /// <summary>Escreve, um treino de cada vez, só depois de o utilizador ter confirmado a
    /// pré-visualização. Sem atrasos artificiais — cada passo demora exatamente o tempo real
    /// da escrita no Google Sheets.</summary>
    private async Task ConfirmarEGravarAsync()
    {
        if (_ocupado || _previews is null) return;
        _ocupado = true;
        _fase = Fase.AGravar;
        StateHasChanged();

        int gravadosAgora = 0;
        _gravadosNesteSync = 0;
        foreach (var p in _previews)
        {
            _aGravarNome = p.Nome;
            StateHasChanged();
            try
            {
                await Import.GravarAsync(p);
                _gravados.Add(p.Nome);
                _semanaMaisAlta = Math.Max(_semanaMaisAlta, p.Semana);
                gravadosAgora++;
                _gravadosNesteSync = gravadosAgora;
                await ConsolaAsync($"Gravado: {p.Nome} (semana {p.Semana}).");
            }
            catch (Exception ex)
            {
                await ConsolaErroAsync($"Falha ao gravar {p.Nome}", ex);
                _aGravarNome = null;
                _erro = gravadosAgora > 0
                    ? $"{gravadosAgora} de {_previews.Count} treinos foram gravados antes de um erro. {ex.Message}"
                    : ex.Message;
                _fase = Fase.Erro;
                _ocupado = false;
                StateHasChanged();
                return;
            }
            StateHasChanged();
        }
        _aGravarNome = null;

        await ConsolaAsync($"Sincronização concluída: {gravadosAgora} treino(s) gravado(s).");
        _fase = Fase.Concluido;
        _ocupado = false;
        StateHasChanged();
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
        _fase = FaseDeRetorno();
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
        CancelarResync();
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

    private void FecharVisaoGeral()
    {
        _visaoAberta = false;
        CancelarResync();
    }

    private static string Letra(string nomeTreino)
    {
        const string prefixo = "TREINO ";
        return nomeTreino.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)
            ? nomeTreino[prefixo.Length..].Trim()
            : nomeTreino;
    }

    /// <summary>Clicar numa célula já preenchida da Visão Geral só pede a pré-visualização do
    /// reenvio — não escreve nada ainda. A escrita só acontece se o utilizador confirmar a seguir
    /// em <see cref="ConfirmarResyncAsync"/>.</summary>
    private async Task PedirResyncAsync(string nomeTreino, int semana)
    {
        // _ocupado (não só _resyncCarregando) é o que a grelha usa para desativar as células —
        // sem isto, clicar numa segunda célula antes da primeira pré-visualização chegar fazia as
        // duas pedirem em paralelo e podia deixar o cartão de confirmação a mostrar o treino/semana
        // errado (de quem respondesse por último), diferente do que o utilizador clicou mais recente.
        if (_ocupado) return;
        _ocupado = true;
        _resyncTreino = nomeTreino;
        _resyncSemana = semana;
        _resyncCarregando = true;
        _resyncErro = null;
        _resyncPreview = null;
        StateHasChanged();

        try
        {
            var preview = await Import.GerarPreviewParaSemanaAsync(nomeTreino, semana);
            if (preview is null)
                _resyncErro = $"Não encontrei o treino do Lyfta correspondente a {nomeTreino}, semana {semana}.";
            else
                _resyncPreview = preview;
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Falha ao preparar o reenvio de {Treino} (semana {Semana}).", nomeTreino, semana);
            await ConsolaErroAsync($"Falha ao preparar o reenvio de {nomeTreino} (semana {semana})", ex);
            _resyncErro = ex.Message;
        }
        finally
        {
            _resyncCarregando = false;
            _ocupado = false;
            StateHasChanged();
        }
    }

    private void CancelarResync()
    {
        _resyncPreview = null;
        _resyncTreino = null;
        _resyncSemana = null;
        _resyncErro = null;
    }

    /// <summary>Escreve o reenvio que o utilizador acabou de confirmar e atualiza a grelha da
    /// Visão Geral para refletir o resultado.</summary>
    private async Task ConfirmarResyncAsync()
    {
        if (_ocupado || _resyncPreview is null) return;
        _ocupado = true;
        StateHasChanged();

        var preview = _resyncPreview;
        try
        {
            await Import.GravarAsync(preview);
            await ConsolaAsync($"{preview.Nome} (semana {preview.Semana}) ressincronizado.");
            _visaoGeral = await Mesociclo.LerVisaoGeralAsync();
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Falha ao ressincronizar {Treino} (semana {Semana}).", preview.Nome, preview.Semana);
            await ConsolaErroAsync($"Falha ao ressincronizar {preview.Nome} (semana {preview.Semana})", ex);
            _resyncErro = ex.Message;
        }
        finally
        {
            _resyncPreview = null;
            _resyncTreino = null;
            _resyncSemana = null;
            _ocupado = false;
            StateHasChanged();
        }
    }
}
