using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ICA.Client.Donnees;
using ICA.Client.Journal;
using ICA.Client.Services;

namespace ICA.Client.Google;

public enum EtatSynchro { NonConnecte, EnCours, AJour, HorsConnexion, Erreur }

/// <summary>
/// Synchronisation entre ordinateurs par un fichier de données rangé dans le Google Drive de l'association.
/// Chaque appareil garde sa copie complète ; à chaque passage : télécharger → fusionner → renvoyer si besoin.
/// Une modification écrasée par un autre appareil est renvoyée au passage suivant : tout le monde converge.
/// </summary>
public sealed class SynchroDrive
{
    private const string NomFichier = "L'ICA Gestion - données (ne pas modifier).json";
    private const string ApiFichiers = "https://www.googleapis.com/drive/v3/files";
    private const string ApiEnvoi = "https://www.googleapis.com/upload/drive/v3/files";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly CompteGoogle _compte;
    private readonly DepotIca _depot;
    private readonly HttpClient _http;
    private readonly IJournal _journal;
    private readonly SemaphoreSlim _unSeul = new(1, 1);
    private EtatSynchro _etat;

    public SynchroDrive(CompteGoogle compte, DepotIca depot, HttpClient http, IJournal journal)
    {
        _compte = compte;
        _depot = depot;
        _http = http;
        _journal = journal;
        _etat = compte.EstConnecte ? EtatSynchro.EnCours : EtatSynchro.NonConnecte;
        compte.Change += (_, _) => { if (!compte.EstConnecte) Changer(EtatSynchro.NonConnecte, null); };
    }

    public event EventHandler? EtatChange;

    public EtatSynchro Etat => _etat;
    public string? Message { get; private set; }
    public DateTimeOffset? DerniereReussite { get; private set; }

    /// <summary>Vrai pendant que la synchronisation écrit elle-même dans le dépôt (pour ne pas se relancer).</summary>
    public bool EcritureEnCours { get; private set; }

    /// <param name="attendre">Vrai : si une synchronisation est déjà en cours, attendre sa fin puis en refaire une complète.</param>
    public async Task SynchroniserAsync(bool attendre = false, CancellationToken annulation = default)
    {
        if (!_compte.EstConnecte) { Changer(EtatSynchro.NonConnecte, null); return; }
        if (!await _unSeul.WaitAsync(attendre ? Timeout.Infinite : 0, annulation)) return; // déjà en cours
        try
        {
            Changer(EtatSynchro.EnCours, null);
            var jeton = await _compte.JetonAsync(annulation);
            var fichier = await TrouverFichierAsync(jeton, annulation);

            if (fichier is null)
            {
                if (_depot.EstDemo)
                {
                    Changer(EtatSynchro.AJour, "Rien à synchroniser tant que la démonstration est affichée : importez vos adhérents.");
                    return;
                }
                await CreerFichierAsync(jeton, _depot.Instantane(), annulation);
                _journal.Info("Synchro : fichier créé dans Google Drive");
            }
            else
            {
                var distant = await TelechargerAsync(jeton, fichier, annulation);
                EcritureEnCours = true;
                bool aEnvoyer;
                try
                {
                    if (_depot.EstDemo)
                    {
                        // Nouvel ordinateur : on récupère simplement les données de l'association.
                        _depot.RemplacerPar(distant);
                        aEnvoyer = false;
                        _journal.Info("Synchro : données récupérées depuis Google Drive (nouvel appareil)");
                    }
                    else
                    {
                        aEnvoyer = _depot.FusionnerAvec(distant).DistantAMettreAJour;
                    }
                }
                finally
                {
                    EcritureEnCours = false;
                }
                if (aEnvoyer)
                {
                    await EnvoyerAsync(jeton, fichier, _depot.Instantane(), annulation);
                    _journal.Info("Synchro : modifications envoyées");
                }
            }

            DerniereReussite = DateTimeOffset.Now;
            Changer(EtatSynchro.AJour, null);
        }
        catch (GoogleDeconnecteException ex)
        {
            Changer(EtatSynchro.NonConnecte, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException { StatusCode: null } or TaskCanceledException or IOException)
        {
            Changer(EtatSynchro.HorsConnexion, "Pas de connexion Internet : vos modifications seront envoyées plus tard.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            _journal.Erreur("Synchronisation Google Drive impossible", ex);
            Changer(EtatSynchro.Erreur, "La synchronisation a échoué. Elle sera retentée automatiquement.");
        }
        finally
        {
            _unSeul.Release();
        }
    }

    private void Changer(EtatSynchro etat, string? message)
    {
        _etat = etat;
        Message = message;
        EtatChange?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- Appels à l'API Google Drive ----------------

    private sealed record Fichier(string Id);

    private async Task<Fichier?> TrouverFichierAsync(string jeton, CancellationToken annulation)
    {
        var q = Uri.EscapeDataString("appProperties has { key='ica' and value='donnees' } and trashed=false");
        using var requete = new HttpRequestMessage(HttpMethod.Get,
            $"{ApiFichiers}?q={q}&spaces=drive&orderBy=modifiedTime%20desc&fields=files(id,modifiedTime)");
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        using var reponse = await _http.SendAsync(requete, annulation);
        var json = await Lire(reponse, "recherche du fichier");
        using var doc = JsonDocument.Parse(json);
        var premier = doc.RootElement.GetProperty("files").EnumerateArray().FirstOrDefault();
        return premier.ValueKind == JsonValueKind.Object ? new Fichier(premier.GetProperty("id").GetString()!) : null;
    }

    private async Task<DonneesIca> TelechargerAsync(string jeton, Fichier fichier, CancellationToken annulation)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Get, $"{ApiFichiers}/{fichier.Id}?alt=media");
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        using var reponse = await _http.SendAsync(requete, annulation);
        var json = await Lire(reponse, "téléchargement");
        return JsonSerializer.Deserialize<DonneesIca>(json, Options) ?? throw new JsonException("Fichier Drive vide.");
    }

    private async Task CreerFichierAsync(string jeton, DonneesIca donnees, CancellationToken annulation)
    {
        var meta = JsonSerializer.Serialize(new
        {
            name = NomFichier,
            mimeType = "application/json",
            description = "Données de L'ICA Gestion. Ne pas modifier ni supprimer : synchronisation entre les ordinateurs.",
            appProperties = new Dictionary<string, string> { ["ica"] = "donnees" },
        });
        var limite = "ica-" + Guid.NewGuid().ToString("N");
        var corps = new StringBuilder()
            .Append("--").Append(limite).Append("\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n").Append(meta).Append("\r\n")
            .Append("--").Append(limite).Append("\r\nContent-Type: application/json\r\n\r\n").Append(JsonSerializer.Serialize(donnees, Options)).Append("\r\n")
            .Append("--").Append(limite).Append("--").ToString();

        using var requete = new HttpRequestMessage(HttpMethod.Post, $"{ApiEnvoi}?uploadType=multipart&fields=id")
        {
            Content = new StringContent(corps, Encoding.UTF8),
        };
        requete.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/related; boundary={limite}");
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        using var reponse = await _http.SendAsync(requete, annulation);
        await Lire(reponse, "création du fichier");
    }

    private async Task EnvoyerAsync(string jeton, Fichier fichier, DonneesIca donnees, CancellationToken annulation)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Patch, $"{ApiEnvoi}/{fichier.Id}?uploadType=media&fields=id")
        {
            Content = new StringContent(JsonSerializer.Serialize(donnees, Options), Encoding.UTF8, "application/json"),
        };
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        using var reponse = await _http.SendAsync(requete, annulation);
        await Lire(reponse, "envoi des modifications");
    }

    private static async Task<string> Lire(HttpResponseMessage reponse, string etape)
    {
        var texte = await reponse.Content.ReadAsStringAsync();
        if (reponse.StatusCode == HttpStatusCode.Unauthorized)
            throw new HttpRequestException($"Google Drive : accès refusé ({etape})", null, reponse.StatusCode);
        if (!reponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Google Drive : erreur {(int)reponse.StatusCode} ({etape}) {(texte.Length > 300 ? texte[..300] : texte)}", null, reponse.StatusCode);
        return texte;
    }
}
