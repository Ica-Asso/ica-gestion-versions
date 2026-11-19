using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ICA.Client.Google;

public sealed record PieceJointe(string Nom, string TypeMime, byte[] Contenu);

/// <summary>Le compte Google connecté n'a pas de boîte Gmail (compte créé avec une adresse Hotmail, Orange…).</summary>
public sealed class SansBoiteGmailException() : Exception(
    "Ce compte Google n'a pas de boîte Gmail : l'envoi direct n'est pas possible. Le message va s'ouvrir dans votre messagerie.");

/// <summary>Envoi direct depuis le Gmail de l'association (droit gmail.send : envoyer, jamais lire).</summary>
public sealed class EnvoiGmail
{
    private readonly CompteGoogle compte;
    private readonly HttpClient http;
    private bool _sansBoite;

    public EnvoiGmail(CompteGoogle compte, HttpClient http)
    {
        this.compte = compte;
        this.http = http;
        compte.Change += (_, _) => _sansBoite = false; // autre compte : on réessaiera
    }

    public bool Disponible => compte.EstConnecte && !_sansBoite;
    public string? Expediteur => compte.Email;

    /// <summary>
    /// Envoie un message. Plusieurs destinataires : ils sont mis en copie cachée (personne ne voit les adresses des autres),
    /// et le message est adressé à l'association elle-même, qui en garde ainsi une copie.
    /// </summary>
    public async Task EnvoyerAsync(IReadOnlyList<string> destinataires, string objet, string corps,
        PieceJointe? pieceJointe = null, CancellationToken annulation = default, IReadOnlyList<PieceJointe>? autresPieces = null)
    {
        var expediteur = compte.Email ?? throw new GoogleDeconnecteException("Compte Google non connecté.");
        var a = destinataires.Count == 1 ? destinataires : [expediteur];
        var cci = destinataires.Count == 1 ? [] : destinataires;
        var pieces = new List<PieceJointe>();
        if (pieceJointe is not null) pieces.Add(pieceJointe);
        if (autresPieces is not null) pieces.AddRange(autresPieces);
        var brut = ConstruireMime(expediteur, a, cci, objet, corps, pieces);

        var jeton = await compte.JetonAsync(annulation);
        using var requete = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { raw = CompteGoogle.Base64Url(brut) }), Encoding.UTF8, "application/json"),
        };
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        using var reponse = await http.SendAsync(requete, annulation);
        if (!reponse.IsSuccessStatusCode)
        {
            var texte = await reponse.Content.ReadAsStringAsync(annulation);
            if (texte.Contains("Mail service not enabled", StringComparison.OrdinalIgnoreCase))
            {
                _sansBoite = true;
                throw new SansBoiteGmailException();
            }
            throw new HttpRequestException($"Gmail a refusé l'envoi : {(int)reponse.StatusCode} {(texte.Length > 300 ? texte[..300] : texte)}", null, reponse.StatusCode);
        }
    }

    internal static byte[] ConstruireMime(string de, IEnumerable<string> a, IEnumerable<string> cci, string objet, string corps, IReadOnlyList<PieceJointe> pieces)
    {
        static string EnTete(string texte) => "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(texte)) + "?=";
        static string Base64Lignes(byte[] octets) =>
            string.Join("\r\n", Convert.ToBase64String(octets).Chunk(76).Select(c => new string(c)));

        var sb = new StringBuilder();
        sb.Append("From: ").Append(EnTete("L'ICA")).Append(" <").Append(de).Append(">\r\n");
        sb.Append("To: ").Append(string.Join(", ", a)).Append("\r\n");
        var copies = cci.ToList();
        if (copies.Count > 0) sb.Append("Bcc: ").Append(string.Join(", ", copies)).Append("\r\n");
        sb.Append("Subject: ").Append(EnTete(objet)).Append("\r\n");
        sb.Append("MIME-Version: 1.0\r\n");

        var texte = Base64Lignes(Encoding.UTF8.GetBytes(corps.Replace("\r\n", "\n").Replace("\n", "\r\n")));
        if (pieces.Count == 0)
        {
            sb.Append("Content-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n").Append(texte).Append("\r\n");
        }
        else
        {
            var limite = "ica-" + Guid.NewGuid().ToString("N");
            sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(limite).Append("\"\r\n\r\n");
            sb.Append("--").Append(limite).Append("\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n").Append(texte).Append("\r\n");
            foreach (var pj in pieces)
            {
                sb.Append("--").Append(limite).Append("\r\nContent-Type: ").Append(pj.TypeMime).Append("; name=\"").Append(pj.Nom).Append("\"\r\n")
                  .Append("Content-Disposition: attachment; filename=\"").Append(pj.Nom).Append("\"\r\nContent-Transfer-Encoding: base64\r\n\r\n")
                  .Append(Base64Lignes(pj.Contenu)).Append("\r\n");
            }
            sb.Append("--").Append(limite).Append("--\r\n");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
