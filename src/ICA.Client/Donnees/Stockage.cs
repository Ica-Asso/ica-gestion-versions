using System.Text.Json;
using System.Text.Json.Serialization;
using ICA.Client.Journal;
using ICA.Domain;

namespace ICA.Client.Donnees;

/// <summary>Ensemble des données d'un appareil.</summary>
public sealed class DonneesIca
{
    public int VersionSchema { get; set; } = 3;

    /// <summary>Vrai tant que ce sont les adhérents fictifs de la démonstration.</summary>
    public bool EstDemo { get; set; } = true;
    public List<Adherent> Adherents { get; set; } = [];
    public List<Cotisation> Cotisations { get; set; } = [];
    public List<Activite> Activites { get; set; } = [];
    public List<AdherentActivite> AdherentActivites { get; set; } = [];
    public List<ModeleMail> ModelesMail { get; set; } = [];
    public List<Seance> Seances { get; set; } = [];
    public List<MessageEnvoye> Messages { get; set; } = [];

    // Dons & Mécénat
    public List<DonateurEntreprise> Entreprises { get; set; } = [];
    public List<Don> Dons { get; set; } = [];
    public List<JustificatifDon> Justificatifs { get; set; } = [];
    public List<HistoriqueDon> HistoriqueDons { get; set; } = [];
    public List<ParametresAssociation> Association { get; set; } = [];
}

/// <summary>
/// Stockage local. v0.1 (démo) : un fichier JSON.
/// v0.4 : remplacé par le cache SQLite + file d'attente de synchronisation, sans toucher aux écrans.
/// </summary>
public interface IStockage
{
    DonneesIca? Charger();
    void Enregistrer(DonneesIca donnees);
    string Emplacement { get; }
}

public sealed class StockageJson(string chemin, IJournal journal) : IStockage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Emplacement => chemin;

    public DonneesIca? Charger()
    {
        if (!File.Exists(chemin)) return null;
        try
        {
            return JsonSerializer.Deserialize<DonneesIca>(File.ReadAllText(chemin), Options);
        }
        catch (JsonException ex)
        {
            // On ne perd jamais un fichier illisible : il est mis de côté, pas écrasé.
            var deCote = $"{chemin}.illisible-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(chemin, deCote);
            journal.Erreur($"Fichier de données illisible, mis de côté : {Path.GetFileName(deCote)}", ex);
            return null;
        }
    }

    public void Enregistrer(DonneesIca donnees)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        var temporaire = chemin + ".tmp";
        File.WriteAllText(temporaire, JsonSerializer.Serialize(donnees, Options));
        if (File.Exists(chemin))
            File.Replace(temporaire, chemin, chemin + ".bak");
        else
            File.Move(temporaire, chemin);
    }
}
