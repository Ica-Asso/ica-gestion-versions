using System.Globalization;
using System.Text.RegularExpressions;

namespace ICA.Domain;

/// <summary>Modèle de message (synchronisé entre appareils, CDC §34).</summary>
public sealed class ModeleMail : EntiteSynchronisee
{
    public string Nom { get; set; } = "";
    public string Objet { get; set; } = "";
    public string Corps { get; set; } = "";

    /// <summary>Modèle fourni par l'application (ne peut pas être supprimé).</summary>
    public bool EstSysteme { get; set; }

    /// <summary>Ordre d'affichage.</summary>
    public int Ordre { get; set; }
}

public static partial class ModelesParDefaut
{
    public const string Signature = "Bien cordialement,\nL'ICA — Partageons nos savoirs-faire";
    public const string NomRappelCotisation = "Rappel de cotisation";
    public const string NomInformation = "Information générale";
    public const string NomLibre = "Message libre";

    public static List<ModeleMail> Creer() =>
    [
        Modele(1, NomInformation, "L'ICA — Information",
            "Bonjour {{prenom}},\n\n[Votre message]\n\n" + Signature),

        Modele(2, "Assemblée générale", "L'ICA — Assemblée générale",
            "Bonjour {{prenom}},\n\n" +
            "Nous avons le plaisir de vous inviter à l'assemblée générale de L'ICA :\n\n" +
            "• Date : [jour et date]\n• Heure : [heure]\n• Lieu : [adresse]\n\n" +
            "Ordre du jour :\n• Rapport moral et d'activité\n• Rapport financier\n• Projets pour la saison à venir\n• Questions diverses\n\n" +
            "Si vous ne pouvez pas être présent(e), vous pouvez donner pouvoir à un autre adhérent.\n\n" +
            "Au plaisir de vous y retrouver,\nL'ICA — Partageons nos savoirs-faire"),

        Modele(3, "Nouvelle activité", "L'ICA — Nouvelle activité : [nom de l'activité]",
            "Bonjour {{prenom}},\n\n" +
            "Nous avons le plaisir de vous annoncer une nouvelle activité : [nom de l'activité].\n\n" +
            "• Quand : [jour et horaire]\n• Où : [lieu]\n• Pour qui : [public]\n\n" +
            "[Quelques mots pour présenter l'activité]\n\n" +
            "Pour vous inscrire ou poser une question, il suffit de répondre à ce message.\n\n" + Signature),

        Modele(4, "Annulation", "L'ICA — Annulation : [activité] du [date]",
            "Bonjour {{prenom}},\n\n" +
            "Nous sommes au regret de vous informer que [l'activité] prévue le [date] est annulée.\n\n" +
            "[Raison et éventuelle date de report]\n\n" +
            "Merci de votre compréhension.\n\n" + Signature),

        Modele(5, NomRappelCotisation, "L'ICA — Cotisation {{saison}}",
            "Bonjour {{prenom}},\n\n" +
            "Sauf erreur de notre part, votre cotisation {{saison}} à L'ICA n'a pas encore été réglée.\n" +
            "Pour rappel, la cotisation annuelle est de {{tarif_adulte}} pour un adulte et de {{tarif_enfant}} pour un enfant.\n" +
            "Vous pouvez la régler lors de votre prochaine venue (le mercredi de 14h à 17h), en espèces, par chèque ou par virement.\n\n" +
            "Si vous l'avez déjà réglée, merci de ne pas tenir compte de ce message.\n\n" +
            "Merci pour votre soutien,\nL'ICA — Partageons nos savoirs-faire"),

        Modele(6, "Événement", "L'ICA — [Nom de l'événement]",
            "Bonjour {{prenom}},\n\n" +
            "L'ICA organise [nom de l'événement] et nous serions ravis de vous y voir !\n\n" +
            "• Date : [jour et date]\n• Heure : [heure]\n• Lieu : [adresse]\n\n" +
            "[Détails : programme, ce qu'il faut apporter, participation…]\n\n" +
            "Merci de nous indiquer votre présence en répondant à ce message.\n\n" + Signature),

        Modele(7, NomLibre, "L'ICA — ",
            "Bonjour {{prenom}},\n\n\n\n" + Signature),
    ];

    private static ModeleMail Modele(int ordre, string nom, string objet, string corps) =>
        new() { Ordre = ordre, Nom = nom, Objet = objet, Corps = corps, EstSysteme = true };
}

/// <summary>Remplace les variables {{prenom}}, {{nom}}, {{saison}}, {{date}} (CDC §35).</summary>
public static partial class RenduModele
{
    public static string Appliquer(string texte, IReadOnlyDictionary<string, string?> valeurs)
    {
        var resultat = Variable().Replace(texte, m =>
            valeurs.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var v) ? v ?? "" : m.Value);
        // « Bonjour , » quand le prénom est vide (envoi groupé) → « Bonjour, »
        resultat = EspaceAvantPonctuation().Replace(resultat, "$1");
        return resultat;
    }

    public static Dictionary<string, string?> Valeurs(Adherent? destinataireUnique, string saison, DateOnly aujourdHui) => new()
    {
        ["prenom"] = destinataireUnique?.Prenom,
        ["nom"] = destinataireUnique?.Nom.ToUpper(CultureInfo.GetCultureInfo("fr-FR")),
        ["saison"] = saison,
        ["date"] = aujourdHui.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
    };

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex Variable();

    [GeneratedRegex(@"[ \t]+([,.])")]
    private static partial Regex EspaceAvantPonctuation();
}
