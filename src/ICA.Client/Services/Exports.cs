using System.Globalization;
using System.Text;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.Services;

/// <summary>Export CSV lisible directement par Excel en français (séparateur « ; », UTF-8 avec BOM).</summary>
public static class ExportCsv
{
    public static void Ecrire(string chemin, IEnumerable<Adherent> adherents, DepotIca depot, string saison)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', "Nom", "Prénom", "Type", "Adresse", "Complément", "Code postal", "Ville",
            "E-mail", "Téléphone", "Téléphone secondaire", "Date de naissance", "Responsable", $"Cotisation {saison}"));

        foreach (var a in adherents)
        {
            var responsable = a.ResponsableId is { } rid ? depot.TrouverAdherent(rid)?.NomAffiche : null;
            sb.AppendLine(string.Join(';',
                Champ(a.Nom.ToUpper(CultureInfo.GetCultureInfo("fr-FR"))), Champ(a.Prenom),
                Champ(a.TypeAdherent == TypeAdherent.Enfant ? "Enfant" : "Adulte"),
                Champ(a.Adresse), Champ(a.ComplementAdresse), Champ(a.CodePostal), Champ(a.Ville),
                Champ(a.Email), Champ(Telephone.Afficher(a.Telephone1)), Champ(Telephone.Afficher(a.Telephone2)),
                Champ(a.DateNaissance?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                Champ(responsable),
                Champ(Libelles.Statut(depot.StatutDe(a.Id, saison)))));
        }

        File.WriteAllText(chemin, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Champ(string? valeur)
    {
        if (string.IsNullOrEmpty(valeur)) return "";
        // Empêche l'interprétation de formules par Excel (=, +, -, @).
        if (valeur[0] is '=' or '+' or '-' or '@') valeur = "'" + valeur;
        return valeur.Contains(';') || valeur.Contains('"') || valeur.Contains('\n')
            ? "\"" + valeur.Replace("\"", "\"\"") + "\""
            : valeur;
    }
}

public static class Libelles
{
    public static string Statut(StatutCotisation s) => s switch
    {
        StatutCotisation.Payee => "Payée",
        StatutCotisation.Exoneree => "Exonérée",
        _ => "À régler",
    };

    public static string Mode(ModePaiement? m) => m switch
    {
        ModePaiement.Especes => "Espèces",
        ModePaiement.Cheque => "Chèque",
        ModePaiement.Virement => "Virement",
        ModePaiement.Autre => "Autre",
        _ => "",
    };

    public static string Montant(decimal? m) =>
        m is { } v ? v.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")) + " €" : "";

    public static string DateCourte(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string DateLongue(DateOnly d) =>
        d.ToString("dddd d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR")) is var s && s.Length > 0
            ? char.ToUpper(s[0], CultureInfo.GetCultureInfo("fr-FR")) + s[1..]
            : s;

    public static string Jour(DateOnly d) => d.ToString("dddd d MMM", CultureInfo.GetCultureInfo("fr-FR"));
}
