using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using ICA.Client.Journal;

namespace ICA.Client.MisesAJour;

/// <summary>Une version publiée sur GitHub (Releases du dépôt public de l'association).</summary>
public sealed record VersionDisponible(Version Numero, string Libelle, string Notes, string UrlTelechargement, long Taille, string? Sha256);

/// <summary>
/// Cherche et télécharge les nouvelles versions publiées dans les « Releases » GitHub.
/// Seul le programme (ICA-Gestion.exe) est remplacé : données, réglages et connexion Google restent en place.
/// </summary>
public sealed class VerificateurMisesAJour(string depot, string versionActuelle, string dossierTelechargement, HttpClient http, IJournal journal)
{
    public const string NomFichier = "ICA-Gestion.exe";

    public Version? VersionActuelle { get; } = LireVersion(versionActuelle);

    public async Task<VersionDisponible?> ChercherAsync(CancellationToken annulation = default)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{depot}/releases/latest");
        requete.Headers.UserAgent.Add(new ProductInfoHeaderValue("ICA-Gestion", versionActuelle));
        requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var reponse = await http.SendAsync(requete, annulation);
        if (reponse.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // aucune version publiée
        reponse.EnsureSuccessStatusCode();
        return Lire(await reponse.Content.ReadAsStringAsync(annulation), VersionActuelle);
    }

    /// <summary>Télécharge et vérifie le nouveau programme ; renvoie son chemin.</summary>
    public async Task<string> TelechargerAsync(VersionDisponible v, IProgress<int>? progression = null, CancellationToken annulation = default)
    {
        Directory.CreateDirectory(dossierTelechargement);
        var cible = Path.Combine(dossierTelechargement, NomFichier);
        var temporaire = cible + ".partiel";

        using var requete = new HttpRequestMessage(HttpMethod.Get, v.UrlTelechargement);
        requete.Headers.UserAgent.Add(new ProductInfoHeaderValue("ICA-Gestion", versionActuelle));
        requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        // Téléchargement long : pas de délai global, on surveille juste l'annulation.
        using var reponse = await http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, annulation);
        reponse.EnsureSuccessStatusCode();
        var total = reponse.Content.Headers.ContentLength ?? v.Taille;

        using (var sha = SHA256.Create())
        {
            await using (var source = await reponse.Content.ReadAsStreamAsync(annulation))
            await using (var fichier = File.Create(temporaire))
            {
                var tampon = new byte[81920];
                long lu = 0;
                var dernier = -1;
                int n;
                while ((n = await source.ReadAsync(tampon, annulation)) > 0)
                {
                    await fichier.WriteAsync(tampon.AsMemory(0, n), annulation);
                    sha.TransformBlock(tampon, 0, n, null, 0);
                    lu += n;
                    var pourcent = total > 0 ? (int)(lu * 100 / total) : 0;
                    if (pourcent != dernier) { dernier = pourcent; progression?.Report(pourcent); }
                }
                sha.TransformFinalBlock([], 0, 0);
            }

            var empreinte = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            if (v.Sha256 is { } attendu && !string.Equals(attendu, empreinte, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaire);
                throw new InvalidDataException("Le fichier téléchargé est abîmé (empreinte différente). Réessayez plus tard.");
            }
        }

        // Un programme Windows commence toujours par « MZ ».
        await using (var verif = File.OpenRead(temporaire))
        {
            var entete = new byte[2];
            if (verif.Length < 1_000_000 || await verif.ReadAsync(entete, annulation) != 2 || entete[0] != 'M' || entete[1] != 'Z')
            {
                verif.Close();
                File.Delete(temporaire);
                throw new InvalidDataException("Le fichier téléchargé n'est pas un programme valide.");
            }
        }

        File.Move(temporaire, cible, overwrite: true);
        journal.Info($"Mise à jour {v.Libelle} téléchargée");
        return cible;
    }

    /// <summary>Lit la réponse de l'API GitHub ; null si pas plus récent ou pas de programme joint.</summary>
    public static VersionDisponible? Lire(string json, Version? actuelle)
    {
        using var doc = JsonDocument.Parse(json);
        var racine = doc.RootElement;
        if (racine.TryGetProperty("draft", out var brouillon) && brouillon.GetBoolean()) return null;
        if (racine.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var tag = racine.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (LireVersion(tag) is not { } numero) return null;
        if (actuelle is not null && numero <= actuelle) return null;

        if (!racine.TryGetProperty("assets", out var fichiers)) return null;
        foreach (var f in fichiers.EnumerateArray())
        {
            if (!string.Equals(f.GetProperty("name").GetString(), NomFichier, StringComparison.OrdinalIgnoreCase)) continue;
            var url = f.GetProperty("browser_download_url").GetString();
            if (url is null || !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) return null;
            var taille = f.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            string? sha = null;
            if (f.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha = digest["sha256:".Length..];
            var notes = racine.TryGetProperty("body", out var b) ? (b.GetString() ?? "").Trim() : "";
            if (notes.Length > 600) notes = notes[..600] + "…";
            return new VersionDisponible(numero, Afficher(numero), notes, url, taille, sha);
        }
        return null;
    }

    /// <summary>« v0.2.0 », « 0.2 », « 0.2.0-beta » → 0.2.0.</summary>
    public static Version? LireVersion(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        var t = texte.Trim().TrimStart('v', 'V');
        var fin = 0;
        while (fin < t.Length && (char.IsDigit(t[fin]) || t[fin] == '.')) fin++;
        var parties = t[..fin].Split('.', StringSplitOptions.RemoveEmptyEntries).Take(4).Select(int.Parse).ToList();
        if (parties.Count == 0) return null;
        while (parties.Count < 3) parties.Add(0);
        return parties.Count == 3 ? new Version(parties[0], parties[1], parties[2]) : new Version(parties[0], parties[1], parties[2], parties[3]);
    }

    public static string Afficher(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);
}
