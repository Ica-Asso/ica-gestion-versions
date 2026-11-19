using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ICA.Client.Donnees;

namespace ICA.Client.Services;

/// <summary>
/// Sauvegardes locales (CDC §40) tant qu'il n'y a pas de serveur :
/// copie automatique quotidienne + sauvegarde manuelle (clé USB…) + restauration.
/// </summary>
public static class Sauvegardes
{
    public const int CopiesQuotidiennesGardees = 30;
    private const string NomDansArchive = "donnees-ica.json";

    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Une copie par jour dans le dossier des sauvegardes ; les plus anciennes sont supprimées.</summary>
    public static void CopieQuotidienne(string fichierDonnees, string dossier)
    {
        if (!File.Exists(fichierDonnees)) return;
        Directory.CreateDirectory(dossier);
        var cible = Path.Combine(dossier, $"auto-{DateTime.Now:yyyy-MM-dd}.json");
        if (!File.Exists(cible)) File.Copy(fichierDonnees, cible);

        foreach (var ancien in Directory.GetFiles(dossier, "auto-*.json").OrderDescending().Skip(CopiesQuotidiennesGardees))
            File.Delete(ancien);
    }

    public static DateTime? DerniereCopie(string dossier) =>
        Directory.Exists(dossier)
            ? Directory.GetFiles(dossier, "auto-*.json").Select(File.GetLastWriteTime).DefaultIfEmpty().Max() is var d && d != default ? d : null
            : null;

    /// <summary>Crée un fichier .zip contenant toutes les données.</summary>
    public static void Exporter(string fichierDonnees, string cheminZip, int nombreAdherents, string? dossierJustificatifs = null)
    {
        if (File.Exists(cheminZip)) File.Delete(cheminZip);
        using var zip = ZipFile.Open(cheminZip, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(fichierDonnees, NomDansArchive);
        // Copies PDF des justificatifs de dons effectivement remis.
        if (dossierJustificatifs is not null && Directory.Exists(dossierJustificatifs))
            foreach (var pdf in Directory.EnumerateFiles(dossierJustificatifs, "*.pdf", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(pdf, "justificatifs/" + Path.GetRelativePath(dossierJustificatifs, pdf).Replace('\\', '/'));
        var info = zip.CreateEntry("LISEZ-MOI.txt");
        using var w = new StreamWriter(info.Open(), new UTF8Encoding(true));
        w.WriteLine("Sauvegarde de L'ICA Gestion");
        w.WriteLine($"Date : {DateTime.Now:dd/MM/yyyy HH:mm}");
        w.WriteLine($"Adhérents : {nombreAdherents}");
        w.WriteLine();
        w.WriteLine("Pour la restaurer : L'ICA Gestion > Paramètres > Restaurer une sauvegarde.");
        w.WriteLine("Ce fichier contient des données personnelles : conservez-le en lieu sûr.");
    }

    /// <summary>Vérifie qu'une sauvegarde est lisible et renvoie son contenu (sans rien modifier).</summary>
    public static DonneesIca Lire(string cheminSauvegarde)
    {
        string json;
        if (cheminSauvegarde.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(cheminSauvegarde);
            var entree = zip.GetEntry(NomDansArchive) ?? throw new InvalidDataException("Ce fichier n'est pas une sauvegarde de L'ICA Gestion.");
            using var r = new StreamReader(entree.Open(), Encoding.UTF8);
            json = r.ReadToEnd();
        }
        else
        {
            json = File.ReadAllText(cheminSauvegarde);
        }

        try
        {
            return JsonSerializer.Deserialize<DonneesIca>(json, Options)
                   ?? throw new InvalidDataException("Sauvegarde vide.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Cette sauvegarde est abîmée ou n'est pas une sauvegarde de L'ICA Gestion.");
        }
    }

    /// <summary>Remplace les données par la sauvegarde, après avoir mis de côté l'état actuel.</summary>
    public static void Restaurer(string cheminSauvegarde, string fichierDonnees, string dossierSauvegardes, string? dossierJustificatifs = null)
    {
        var donnees = Lire(cheminSauvegarde); // lève une exception si illisible : rien n'est touché
        if (dossierJustificatifs is not null && cheminSauvegarde.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // Les PDF déjà présents ne sont jamais écrasés (ce sont les documents remis).
            using var zip = ZipFile.OpenRead(cheminSauvegarde);
            var racine = Path.GetFullPath(dossierJustificatifs);
            foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("justificatifs/", StringComparison.Ordinal) && e.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            {
                var cible = Path.GetFullPath(Path.Combine(racine, e.FullName["justificatifs/".Length..]));
                if (!cible.StartsWith(racine, StringComparison.OrdinalIgnoreCase) || File.Exists(cible)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
                e.ExtractToFile(cible);
            }
        }
        Directory.CreateDirectory(dossierSauvegardes);
        if (File.Exists(fichierDonnees))
            File.Copy(fichierDonnees, Path.Combine(dossierSauvegardes, $"avant-restauration-{DateTime.Now:yyyy-MM-dd-HHmmss}.json"));
        File.WriteAllText(fichierDonnees, JsonSerializer.Serialize(donnees, new JsonSerializerOptions(Options) { WriteIndented = true }));
    }
}
