using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ICA.Client.Google;

/// <summary>
/// Copies PDF des justificatifs dans un dossier du Google Drive de l'association
/// (« L'ICA Gestion - Justificatifs »). Droit drive.file : l'application ne voit que ce qu'elle a créé.
/// </summary>
public sealed class DocumentsDrive(CompteGoogle compte, HttpClient http)
{
    private const string NomDossier = "L'ICA Gestion - Justificatifs (ne pas modifier)";
    private const string ApiFichiers = "https://www.googleapis.com/drive/v3/files";
    private const string ApiEnvoi = "https://www.googleapis.com/upload/drive/v3/files";
    private string? _dossierId;

    public bool Disponible => compte.EstConnecte;

    public async Task<string> DeposerAsync(string nom, byte[] pdf, CancellationToken annulation = default)
    {
        var jeton = await compte.JetonAsync(annulation);
        var dossier = await DossierAsync(jeton, annulation);
        var meta = JsonSerializer.Serialize(new
        {
            name = nom,
            mimeType = "application/pdf",
            parents = new[] { dossier },
            appProperties = new Dictionary<string, string> { ["ica"] = "justificatif" },
        });
        var limite = "ica-" + Guid.NewGuid().ToString("N");
        using var corps = new MemoryStream();
        void Ecrire(string s) => corps.Write(Encoding.UTF8.GetBytes(s));
        Ecrire($"--{limite}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n{meta}\r\n--{limite}\r\nContent-Type: application/pdf\r\n\r\n");
        corps.Write(pdf);
        Ecrire($"\r\n--{limite}--");

        using var requete = new HttpRequestMessage(HttpMethod.Post, $"{ApiEnvoi}?uploadType=multipart&fields=id")
        {
            Content = new ByteArrayContent(corps.ToArray()),
        };
        requete.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/related; boundary={limite}");
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        using var reponse = await http.SendAsync(requete, annulation);
        var texte = await reponse.Content.ReadAsStringAsync(annulation);
        if (!reponse.IsSuccessStatusCode) throw new HttpRequestException($"Google Drive : dépôt du justificatif refusé ({(int)reponse.StatusCode})", null, reponse.StatusCode);
        using var doc = JsonDocument.Parse(texte);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public async Task<byte[]> TelechargerAsync(string id, CancellationToken annulation = default)
    {
        var jeton = await compte.JetonAsync(annulation);
        using var requete = new HttpRequestMessage(HttpMethod.Get, $"{ApiFichiers}/{Uri.EscapeDataString(id)}?alt=media");
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        using var reponse = await http.SendAsync(requete, annulation);
        if (!reponse.IsSuccessStatusCode) throw new HttpRequestException($"Google Drive : justificatif introuvable ({(int)reponse.StatusCode})", null, reponse.StatusCode);
        return await reponse.Content.ReadAsByteArrayAsync(annulation);
    }

    private async Task<string> DossierAsync(string jeton, CancellationToken annulation)
    {
        if (_dossierId is not null) return _dossierId;
        var q = Uri.EscapeDataString("appProperties has { key='ica' and value='justificatifs' } and mimeType='application/vnd.google-apps.folder' and trashed=false");
        using (var requete = new HttpRequestMessage(HttpMethod.Get, $"{ApiFichiers}?q={q}&spaces=drive&fields=files(id)"))
        {
            requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
            using var reponse = await http.SendAsync(requete, annulation);
            reponse.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync(annulation));
            var premier = doc.RootElement.GetProperty("files").EnumerateArray().FirstOrDefault();
            if (premier.ValueKind == JsonValueKind.Object) return _dossierId = premier.GetProperty("id").GetString()!;
        }

        var meta = JsonSerializer.Serialize(new
        {
            name = NomDossier,
            mimeType = "application/vnd.google-apps.folder",
            description = "Copies PDF des attestations et reçus de dons émis par L'ICA Gestion.",
            appProperties = new Dictionary<string, string> { ["ica"] = "justificatifs" },
        });
        using var creation = new HttpRequestMessage(HttpMethod.Post, $"{ApiFichiers}?fields=id")
        {
            Content = new StringContent(meta, Encoding.UTF8, "application/json"),
        };
        creation.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        using var rep = await http.SendAsync(creation, annulation);
        rep.EnsureSuccessStatusCode();
        using var d = JsonDocument.Parse(await rep.Content.ReadAsStringAsync(annulation));
        return _dossierId = d.RootElement.GetProperty("id").GetString()!;
    }
}
