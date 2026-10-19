namespace LyftaToSheets.Sheets;

public sealed class SheetsConfig
{
    public string SpreadsheetId { get; set; } = "";
    public string SheetName { get; set; } = "MESOCICLO 1";
    public string CredentialsPath { get; set; } = "credentials.json";
    public string TokenFolder { get; set; } = "token_store";

    /// <summary>Ficheiro onde fica guardado, por treino, qual foi o último workout do Lyfta já
    /// sincronizado com a folha (ver <see cref="LyftaToSheets.Sync.ISyncStateStore"/>).</summary>
    public string SyncStatePath { get; set; } = "sync_state.json";
}
