using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICA.Client.Journal;

namespace ICA.Client.Google;

/// <summary>Identifiants du projet Google Cloud (fichier google-client.json téléchargé depuis la console).</summary>
public sealed record IdentifiantsClient(string ClientId, string ClientSecret)
{
    public static IdentifiantsClient? Charger(string chemin)
    {
        try
        {
            if (!File.Exists(chemin)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(chemin));
            var racine = doc.RootElement;
            // Format Google : { "installed": { "client_id": …, "client_secret": … } }
            foreach (var nom in new[] { "installed", "web" })
                if (racine.TryGetProperty(nom, out var bloc))
                    return new IdentifiantsClient(bloc.GetProperty("client_id").GetString()!, bloc.GetProperty("client_secret").GetString()!);
            return new IdentifiantsClient(racine.GetProperty("client_id").GetString()!, racine.GetProperty("client_secret").GetString()!);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

/// <summary>Coffre chiffré propre à l'utilisateur Windows (DPAPI) ou au téléphone (SecureStorage).</summary>
public interface ICoffreSecret
{
    string? Lire(string nom);
    void Ecrire(string nom, string valeur);
    void Effacer(string nom);
}

public sealed class GoogleDeconnecteException(string message) : Exception(message);

/// <summary>
/// Connexion au compte Google de l'association (OAuth 2.0 pour application de bureau, PKCE + boucle locale).
/// Droits demandés : uniquement les fichiers créés par l'application (drive.file) et l'envoi de mails (gmail.send).
/// </summary>
public sealed class CompteGoogle
{
    public const string Droits = "openid email https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/gmail.send";
    private const string CleJeton = "google.refresh_token";
    private const string CleEmail = "google.email";

    private readonly IdentifiantsClient? _ids;
    private readonly ICoffreSecret _coffre;
    private readonly IJournal _journal;
    private readonly HttpClient _http;
    private readonly Func<string, bool> _ouvrirNavigateur;
    private readonly SemaphoreSlim _verrou = new(1, 1);
    private string? _jetonAcces;
    private DateTimeOffset _expiration;

    public CompteGoogle(IdentifiantsClient? ids, ICoffreSecret coffre, IJournal journal, HttpClient http, Func<string, bool> ouvrirNavigateur)
    {
        _ids = ids;
        _coffre = coffre;
        _journal = journal;
        _http = http;
        _ouvrirNavigateur = ouvrirNavigateur;
    }

    public event EventHandler? Change;

    public bool EstConfigure => _ids is not null;
    public bool EstConnecte => EstConfigure && !string.IsNullOrEmpty(_coffre.Lire(CleJeton));
    public string? Email => _coffre.Lire(CleEmail);

    /// <summary>Ouvre le navigateur sur la page Google, attend le retour, enregistre l'accès.</summary>
    public async Task ConnecterAsync(CancellationToken annulation)
    {
        if (_ids is null) throw new InvalidOperationException("La connexion Google n'est pas configurée (fichier google-client.json absent).");

        var verificateur = Base64Url(RandomNumberGenerator.GetBytes(32));
        var defi = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificateur)));
        var etat = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var ecoute = new TcpListener(IPAddress.Loopback, 0);
        ecoute.Start();
        var port = ((IPEndPoint)ecoute.LocalEndpoint).Port;
        var retour = $"http://127.0.0.1:{port}/";

        var url = "https://accounts.google.com/o/oauth2/v2/auth?" + Formulaire(new()
        {
            ["client_id"] = _ids.ClientId,
            ["redirect_uri"] = retour,
            ["response_type"] = "code",
            ["scope"] = Droits,
            ["code_challenge"] = defi,
            ["code_challenge_method"] = "S256",
            ["state"] = etat,
            ["access_type"] = "offline",
            ["prompt"] = "consent select_account",
        });
        if (!_ouvrirNavigateur(url)) throw new InvalidOperationException("Le navigateur n'a pas pu s'ouvrir.");

        using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
        delai.CancelAfter(TimeSpan.FromMinutes(5));
        var parametres = await AttendreRetourAsync(ecoute, delai.Token);

        if (parametres.TryGetValue("error", out var erreur))
            throw new OperationCanceledException(erreur == "access_denied" ? "Connexion refusée." : $"Erreur Google : {erreur}");
        if (!parametres.TryGetValue("state", out var etatRecu) || etatRecu != etat || !parametres.TryGetValue("code", out var code))
            throw new InvalidOperationException("Réponse de Google inattendue.");

        using var reponse = await _http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _ids.ClientId,
            ["client_secret"] = _ids.ClientSecret,
            ["redirect_uri"] = retour,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verificateur,
        }), annulation);
        var json = await LireJsonAsync(reponse, "obtention du jeton");

        var refresh = json.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("Google n'a pas fourni d'accès durable. Réessayez la connexion.");
        MemoriserAcces(json);
        _coffre.Ecrire(CleJeton, refresh);

        // Adresse du compte (affichage + expéditeur des mails)
        using var requete = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        requete.Headers.Authorization = new("Bearer", _jetonAcces);
        using var info = await _http.SendAsync(requete, annulation);
        var profil = await LireJsonAsync(info, "lecture du profil");
        if (profil.TryGetProperty("email", out var email) && email.GetString() is { } adresse) _coffre.Ecrire(CleEmail, adresse);

        _journal.Info("Compte Google connecté");
        Change?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Jeton d'accès valide (renouvelé automatiquement).</summary>
    public async Task<string> JetonAsync(CancellationToken annulation)
    {
        if (_ids is null || _coffre.Lire(CleJeton) is not { } refresh) throw new GoogleDeconnecteException("Compte Google non connecté.");
        await _verrou.WaitAsync(annulation);
        try
        {
            if (_jetonAcces is not null && DateTimeOffset.UtcNow < _expiration) return _jetonAcces;

            using var reponse = await _http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _ids.ClientId,
                ["client_secret"] = _ids.ClientSecret,
                ["refresh_token"] = refresh,
                ["grant_type"] = "refresh_token",
            }), annulation);

            if (reponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                var texte = await reponse.Content.ReadAsStringAsync(annulation);
                if (texte.Contains("invalid_grant", StringComparison.Ordinal))
                {
                    // Accès retiré depuis le compte Google, ou mot de passe changé.
                    _journal.Avertissement("Accès Google révoqué : déconnexion");
                    Oublier();
                    throw new GoogleDeconnecteException("L'accès au compte Google a été retiré. Reconnectez-vous dans Paramètres.");
                }
            }
            MemoriserAcces(await LireJsonAsync(reponse, "renouvellement du jeton"));
            return _jetonAcces!;
        }
        finally
        {
            _verrou.Release();
        }
    }

    public async Task DeconnecterAsync()
    {
        var refresh = _coffre.Lire(CleJeton);
        Oublier();
        if (refresh is null) return;
        try
        {
            using var _ = await _http.PostAsync("https://oauth2.googleapis.com/revoke",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh }));
        }
        catch (HttpRequestException) { /* hors connexion : l'accès local est oublié de toute façon */ }
        _journal.Info("Compte Google déconnecté");
    }

    private void Oublier()
    {
        _coffre.Effacer(CleJeton);
        _coffre.Effacer(CleEmail);
        _jetonAcces = null;
        Change?.Invoke(this, EventArgs.Empty);
    }

    private void MemoriserAcces(JsonElement json)
    {
        _jetonAcces = json.GetProperty("access_token").GetString();
        var duree = json.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        _expiration = DateTimeOffset.UtcNow.AddSeconds(duree - 120);
    }

    private static async Task<JsonElement> LireJsonAsync(HttpResponseMessage reponse, string etape)
    {
        var texte = await reponse.Content.ReadAsStringAsync();
        if (!reponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Google a refusé ({etape}) : {(int)reponse.StatusCode} {Tronquer(texte)}", null, reponse.StatusCode);
        return JsonDocument.Parse(texte).RootElement.Clone();
    }

    /// <summary>Petit serveur local qui reçoit la redirection de Google dans le navigateur.</summary>
    private static async Task<Dictionary<string, string>> AttendreRetourAsync(TcpListener ecoute, CancellationToken annulation)
    {
        while (true)
        {
            using var client = await ecoute.AcceptTcpClientAsync(annulation);
            await using var flux = client.GetStream();
            using var lecteur = new StreamReader(flux, Encoding.ASCII, leaveOpen: true);
            var ligne = await lecteur.ReadLineAsync(annulation) ?? "";
            // « GET /?code=…&state=… HTTP/1.1 »
            var morceaux = ligne.Split(' ');
            var chemin = morceaux.Length > 1 ? morceaux[1] : "/";
            var requete = chemin.Contains('?') ? chemin[(chemin.IndexOf('?') + 1)..] : "";
            var parametres = requete.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "");

            var ok = parametres.ContainsKey("code");
            var page = $$"""
                <!doctype html><html lang="fr"><head><meta charset="utf-8"><title>L'ICA Gestion</title></head>
                <body style="font-family:Segoe UI,sans-serif;background:#F6F4F1;color:#1F1D1B;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
                <div style="background:#fff;border:1px solid #E8E3DE;border-radius:16px;padding:40px 48px;text-align:center;max-width:520px">
                <h1 style="color:#9E1B32;margin:0 0 12px">{{(ok ? "C'est fait !" : "Connexion interrompue")}}</h1>
                <p style="font-size:18px">{{(ok ? "Votre compte Google est relié à L'ICA Gestion.<br>Vous pouvez fermer cet onglet et revenir à l'application." : "Vous pouvez fermer cet onglet et réessayer depuis L'ICA Gestion.")}}</p>
                </div></body></html>
                """;
            var corps = Encoding.UTF8.GetBytes(page);
            var entete = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {corps.Length}\r\nConnection: close\r\n\r\n");
            await flux.WriteAsync(entete, annulation);
            await flux.WriteAsync(corps, annulation);

            // Le navigateur peut demander /favicon.ico : on ignore tout ce qui n'est pas la réponse de Google.
            if (parametres.ContainsKey("code") || parametres.ContainsKey("error")) return parametres;
        }
    }

    internal static string Formulaire(Dictionary<string, string> valeurs) =>
        string.Join('&', valeurs.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    internal static string Base64Url(byte[] octets) =>
        Convert.ToBase64String(octets).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Tronquer(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
