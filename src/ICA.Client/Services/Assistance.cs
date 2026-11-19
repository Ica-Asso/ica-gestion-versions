using System.Globalization;
using System.Text;

namespace ICA.Client.Services;

/// <summary>Contenu d'un rapport « Un problème ? » (ARCHITECTURE.md §17 bis).</summary>
public sealed class RapportAssistance
{
    public required string Identifiant { get; init; }
    public required string Message { get; init; }
    public required IReadOnlyDictionary<string, string> Informations { get; init; }
    public required IReadOnlyList<string> Journal { get; init; }
    public bool ApresPlantage { get; init; }

    public static string NouvelIdentifiant(DateTimeOffset maintenant)
        => "SUP-" + maintenant.ToLocalTime().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);

    public string Objet => ApresPlantage
        ? $"[L'ICA Gestion] Fermeture inattendue — {Identifiant}"
        : $"[L'ICA Gestion] Signalement — {Identifiant}";

    /// <summary>Texte court, placé dans le corps du mail.</summary>
    public string Resume()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Rapport {Identifiant}");
        sb.AppendLine();
        sb.AppendLine("Message :");
        sb.AppendLine(string.IsNullOrWhiteSpace(Message) ? "(aucun message)" : Message.Trim());
        sb.AppendLine();
        foreach (var (cle, valeur) in Informations)
            sb.AppendLine($"{cle} : {valeur}");
        return sb.ToString();
    }

    /// <summary>Texte complet (résumé + journal technique), placé en pièce jointe.</summary>
    public string Complet()
    {
        var sb = new StringBuilder(Resume());
        sb.AppendLine();
        sb.AppendLine($"--- Journal technique ({Journal.Count} dernières lignes) ---");
        foreach (var ligne in Journal) sb.AppendLine(ligne);
        return sb.ToString();
    }
}
