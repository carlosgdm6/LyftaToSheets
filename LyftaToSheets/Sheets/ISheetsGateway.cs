namespace LyftaToSheets.Sheets;

/// <summary>Acesso de baixo nível à folha de cálculo: só lê/escreve células, não sabe nada sobre
/// treinos, exercícios ou semanas — isso é responsabilidade do <see cref="SheetGridParser"/> e do
/// MesocicloService. Mantém a lógica de autenticação/credenciais do Google isolada e substituível
/// por um dublê nos testes.</summary>
public interface ISheetsGateway
{
    /// <summary>Indicação rápida e sem rede: já existe um token Google guardado localmente?</summary>
    bool TemTokenGuardado();

    /// <summary>Autentica (se necessário) e confirma acesso à spreadsheet.</summary>
    Task TestarLigacaoAsync(CancellationToken ct = default);

    /// <summary>Lê todas as linhas da folha configurada, tal como o Google Sheets as devolve
    /// (0-based, uma lista de células por linha; células vazias podem estar ausentes).</summary>
    Task<IReadOnlyList<IList<object>>> LerLinhasAsync(CancellationToken ct = default);

    /// <summary>Escreve várias células de uma vez. Cada tuplo é (linha 1-based, coluna 0-based, valor).</summary>
    Task GravarLoteAsync(IEnumerable<(int linha, int coluna, string valor)> celulas, CancellationToken ct = default);
}
