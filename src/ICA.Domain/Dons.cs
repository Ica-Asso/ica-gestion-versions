using System.Globalization;
using System.Text;

namespace ICA.Domain;

// ============================================================
//  Dons & Mécénat — entités synchronisées entre les ordinateurs
// ============================================================

public enum TypeDon { Financier = 0, Nature = 1, Competences = 2 }

public enum StatutDon { EnAttente = 0, Recu = 1, Annule = 2 }

public enum ModeVersementDon { Especes = 0, Cheque = 1, Virement = 2, Carte = 3, Autre = 4 }

public enum TypeJustificatif { Attestation = 0, RecuFiscal = 1 }

/// <summary>Réservé : numéro attribué, PDF pas encore créé. Émis : remis/envoyable, contenu figé.</summary>
public enum StatutJustificatif { Reserve = 0, Emis = 1, Annule = 2, Remplace = 3 }

public enum EligibiliteFiscale { NonVerifiee = 0, Confirmee = 1, Desactivee = 2 }

/// <summary>Entreprise donatrice : annuaire séparé des adhérents.</summary>
public sealed class DonateurEntreprise : EntiteSynchronisee
{
    public string RaisonSociale { get; set; } = "";
    public string? FormeJuridique { get; set; }
    public string? Siren { get; set; }
    public string? Siret { get; set; }
    public string? Adresse { get; set; }
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
    public string? ContactNom { get; set; }
    public string? ContactPrenom { get; set; }
    public string? Email { get; set; }
    public string? Telephone { get; set; }
    public string? Notes { get; set; }

    public string Contact => string.Join(" ", new[] { ContactPrenom, ContactNom }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string LigneVille => string.Join(" ", new[] { CodePostal, Ville?.ToUpperInvariant() }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed class Don : EntiteSynchronisee
{
    public Guid EntrepriseId { get; set; }
    public DateOnly Date { get; set; }
    public TypeDon Type { get; set; }

    /// <summary>Don financier : somme versée. Don en nature / compétences : valorisation communiquée par l'entreprise.</summary>
    public decimal? Montant { get; set; }
    public ModeVersementDon? Mode { get; set; }
    public string? Description { get; set; }

    /// <summary>Don en nature : nature et quantités (« 10 chaises pliantes »…).</summary>
    public string? Quantites { get; set; }

    /// <summary>Mécénat de compétences : salariés mis à disposition (nombre, heures, qualification).</summary>
    public string? SalariesMisADisposition { get; set; }

    /// <summary>Références des pièces fournies par l'entreprise (facture, attestation de valorisation…).</summary>
    public string? PiecesJustificatives { get; set; }
    public StatutDon Statut { get; set; } = StatutDon.Recu;

    public bool EstFinancier => Type == TypeDon.Financier;
}

/// <summary>
/// Attestation simple ou reçu fiscal (modèle 2041-MEC-SD). Une fois émis, son contenu ne change plus :
/// une correction = annulation + nouveau document qui le remplace. Un numéro n'est jamais réutilisé.
/// </summary>
public sealed class JustificatifDon : EntiteSynchronisee
{
    public string Numero { get; set; } = "";
    public TypeJustificatif Type { get; set; }
    public int Annee { get; set; }
    public Guid EntrepriseId { get; set; }
    public List<Guid> DonIds { get; set; } = [];
    public StatutJustificatif Statut { get; set; } = StatutJustificatif.Reserve;
    public DateOnly? DateEmission { get; set; }

    /// <summary>Contenu figé au moment de l'émission (ce qui figure sur le PDF).</summary>
    public ContenuJustificatif? Contenu { get; set; }

    public string? Empreinte { get; set; }
    public string? DriveFichierId { get; set; }

    public Guid? RemplaceId { get; set; }
    public Guid? RemplacePar { get; set; }
    public string? MotifAnnulation { get; set; }

    public DateTimeOffset? DateEnvoi { get; set; }
    public string? EnvoyeA { get; set; }

    public bool EstActif => Statut == StatutJustificatif.Emis;
    public string NomFichier => $"{Numero}.pdf";
}

/// <summary>Instantané de tout ce qui est imprimé sur le justificatif.</summary>
public sealed class ContenuJustificatif
{
    public IdentiteAssociation Association { get; set; } = new();
    public string EntrepriseNom { get; set; } = "";
    public string? EntrepriseForme { get; set; }
    public string? EntrepriseSiren { get; set; }
    public string? EntrepriseAdresse { get; set; }
    public string? EntrepriseVille { get; set; }
    public decimal TotalVersements { get; set; }
    public decimal TotalNature { get; set; }
    public List<ModeVersementDon> Modes { get; set; } = [];
    public string DescriptionNature { get; set; } = "";
    public string? Salaries { get; set; }
    public DateOnly DateDebut { get; set; }
    public DateOnly DateFin { get; set; }
    public List<LigneDonJustificatif> Lignes { get; set; } = [];
    public string? NumeroRemplace { get; set; }

    public decimal Total => TotalVersements + TotalNature;
}

public sealed class LigneDonJustificatif
{
    public DateOnly Date { get; set; }
    public TypeDon Type { get; set; }
    public decimal Montant { get; set; }
    public string Libelle { get; set; } = "";
}

/// <summary>Identité de L'ICA telle qu'elle figure sur les justificatifs.</summary>
public sealed class IdentiteAssociation
{
    public string Denomination { get; set; } = "L'ICA";
    public string? Siren { get; set; }
    public string? Rna { get; set; }
    public string? Adresse { get; set; }
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
    public string? Email { get; set; }
    public string? Telephone { get; set; }
    public string Objet { get; set; } = "Partageons nos savoirs-faire : ateliers et activités de partage de connaissances.";

    /// <summary>Code de la catégorie cochée sur le reçu (voir CategoriesFiscales ; anciennes valeurs reconnues).</summary>
    public string Categorie { get; set; } = CategoriesFiscales.Association1901;

    /// <summary>Précision demandée par la case « Autres (précisez) ».</summary>
    public string? CategoriePrecision { get; set; }
    public string? SignataireNom { get; set; }
    public string? SignataireQualite { get; set; } = "Présidente";
    public string? LieuSignature { get; set; }

    public IdentiteAssociation Copie() => (IdentiteAssociation)MemberwiseClone();
}

/// <summary>Réglages partagés de l'association (une seule fiche, synchronisée).</summary>
public sealed class ParametresAssociation : EntiteSynchronisee
{
    public IdentiteAssociation Identite { get; set; } = new();
    public EligibiliteFiscale Eligibilite { get; set; } = EligibiliteFiscale.NonVerifiee;
    public string? ReferenceRescrit { get; set; }
    public DateOnly? DateRescrit { get; set; }

    /// <summary>Pièces justificatives de l'éligibilité (références de courriers, statuts, fichiers…).</summary>
    public string? PiecesRescrit { get; set; }
    public string? JustificationEligibilite { get; set; }
    public DateOnly? DateDecisionEligibilite { get; set; }

    /// <summary>Signature du représentant habilité (image JPEG, base64), apposée sur les reçus fiscaux.</summary>
    public string? SignatureJpeg { get; set; }

    /// <summary>Modèle officiel contre lequel un spécimen a été vérifié (ex. « 16216*03 »), et quand.</summary>
    public string? ModeleVerifie { get; set; }
    public DateOnly? DateVerificationModele { get; set; }
}

/// <summary>Catégories d'organismes du formulaire 2041-MEC-SD (Cerfa 16216*03).</summary>
public sealed record CategorieFiscale(string Code, string Libelle, bool SousCategorie, bool AvecPrecision = false);

public static class CategoriesFiscales
{
    public const string ModeleEnVigueur = "16216*03";
    public const string Association1901 = "OIG-ASSO";
    public const string CodeOeuvreInteretGeneral = "OIG";

    public const string LibelleOeuvreInteretGeneral =
        "Œuvre ou organisme d'intérêt général ayant un caractère philanthropique, éducatif, scientifique, social, humanitaire, sportif, " +
        "familial, culturel ou concourant à l'égalité entre les femmes et les hommes…";

    /// <summary>
    /// Dans l'ordre du formulaire. Les libellés repris du Cerfa 16216*03 ; les plus longs sont abrégés (…) :
    /// le texte complet figure sur le formulaire officiel.
    /// </summary>
    public static readonly IReadOnlyList<CategorieFiscale> Toutes =
    [
        new("OIG-ASSO", "Association loi 1901", true),
        new("OIG-RUP", "Association ou fondation reconnue d'utilité publique par décret", true),
        new("OIG-FONDUNIV", "Fondation universitaire ou fondation partenariale mentionnées respectivement aux articles L.719-12 et L.719-13 du code de l'éducation", true),
        new("OIG-FONDENT", "Fondation d'entreprise", true),
        new("OIG-MUSEE", "Musée de France", true),
        new("OIG-AIDE", "Organismes sans but lucratif fournissant gratuitement une aide alimentaire, des soins médicaux ou des produits de première nécessité à des personnes en difficulté ou favorisant leur logement", true),
        new("OIG-AUTRE", "Autres (précisez)", true, AvecPrecision: true),
        new("OIG-CULTE", "Association cultuelle ou établissement public des cultes reconnus d'Alsace-Moselle", true),
        new("ENS-SUP", "Établissement d'enseignement supérieur ou d'enseignement artistique public ou privé, d'intérêt général, à but non lucratif", false),
        new("ENS-CONSULAIRE", "Établissement d'enseignement supérieur consulaire mentionné à l'article L. 711-17 du code de commerce", false),
        new("RECHERCHE", "Société ou organisme public ou privé agréé par le ministre chargé du budget en vertu de l'article 4 de l'ordonnance n° 58-882 du 25 septembre 1958 relative à la fiscalité en matière de recherche scientifique et technique", false),
        new("SPECTACLE", "Organisme public ou privé dont la gestion est désintéressée et qui a pour activité principale la présentation au public d'œuvres dramatiques, lyriques, musicales, chorégraphiques, cinématographiques…", false),
        new("THESE", "Projet de thèse proposé au mécénat de doctorat par une école doctorale", false),
        new("EXPO", "Société, dont l'État est l'actionnaire unique, qui a pour activité la représentation de la France aux expositions universelles", false),
        new("SNP-44", "Société nationale de programme mentionnée à l'article 44 de la loi n° 86-1067 du 30 septembre 1986 relative à la liberté de communication…", false),
        new("SNP-44-III", "Société nationale de programme mentionnée au III de l'article 44 de la loi n° 86-1067 du 30 septembre 1986…", false),
        new("PATRIMOINE", "Fondation du patrimoine ou fondation ou association reconnue d'utilité publique qui subventionnent des travaux sur des monuments historiques…", false),
        new("FONDS-DOTATION", "Fonds de dotation", false),
        new("PME", "Organisme agréé ayant pour objet exclusif d'accorder des aides financières ou de fournir des prestations d'accompagnement à des petites et moyennes entreprises (4 de l'article 238 bis du CGI)", false),
        new("FEDERATION", "Fédération ou union d'organismes ayant pour objet exclusif de fédérer, d'organiser, de représenter et de promouvoir les organismes agréés…", false),
        new("BIENS-CULTURELS", "Organismes ayant pour objet la sauvegarde, contre les effets d'un conflit armé, des biens culturels mentionnés à l'article 1er de la Convention du 14 mai 1954…", false),
        new("UE", "Organisme établi dans un État membre de l'Union européenne autre que la France poursuivant des objectifs et présentant des caractéristiques similaires…", false),
    ];

    /// <summary>Retrouve une catégorie par son code ou par l'ancien libellé enregistré (versions précédentes).</summary>
    public static CategorieFiscale? Trouver(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return null;
        if (Toutes.FirstOrDefault(c => c.Code == valeur || c.Libelle == valeur) is { } c) return c;
        return valeur switch
        {
            "Œuvre ou organisme d'intérêt général — Association loi 1901" => Toutes[0],
            "Œuvre ou organisme d'intérêt général — Association ou fondation reconnue d'utilité publique" => Toutes[1],
            "Œuvre ou organisme d'intérêt général — Fondation universitaire ou partenariale" => Toutes[2],
            "Œuvre ou organisme d'intérêt général — Fondation d'entreprise" => Toutes[3],
            "Œuvre ou organisme d'intérêt général — Musée de France" => Toutes[4],
            "Fonds de dotation" => Toutes.First(x => x.Code == "FONDS-DOTATION"),
            _ => null,
        };
    }
}

/// <summary>Ce qui manque pour pouvoir émettre (ou envoyer) un reçu fiscal.</summary>
public static class ConditionsRecuFiscal
{
    public static IReadOnlyList<string> Association(ParametresAssociation p)
    {
        var i = p.Identite;
        var manques = new List<string>();
        if (p.Eligibilite != EligibiliteFiscale.Confirmee) manques.Add("éligibilité au mécénat confirmée");
        if (string.IsNullOrWhiteSpace(i.Denomination)) manques.Add("nom officiel de l'association");
        if (string.IsNullOrWhiteSpace(i.Siren) && string.IsNullOrWhiteSpace(i.Rna)) manques.Add("numéro SIREN ou RNA");
        if (string.IsNullOrWhiteSpace(i.Adresse) || string.IsNullOrWhiteSpace(i.CodePostal) || string.IsNullOrWhiteSpace(i.Ville)) manques.Add("adresse complète");
        if (string.IsNullOrWhiteSpace(i.Objet)) manques.Add("objet de l'association");
        var categorie = CategoriesFiscales.Trouver(i.Categorie);
        if (categorie is null) manques.Add("catégorie fiscale");
        else if (categorie.AvecPrecision && string.IsNullOrWhiteSpace(i.CategoriePrecision)) manques.Add("précision de la catégorie « Autres »");
        if (string.IsNullOrWhiteSpace(i.SignataireNom) || string.IsNullOrWhiteSpace(i.SignataireQualite)) manques.Add("nom et fonction du signataire");
        if (string.IsNullOrWhiteSpace(p.SignatureJpeg)) manques.Add("signature du représentant habilité");
        if (p.ModeleVerifie != CategoriesFiscales.ModeleEnVigueur) manques.Add($"vérification du spécimen contre le Cerfa {CategoriesFiscales.ModeleEnVigueur}");
        return manques;
    }

    public static IReadOnlyList<string> Entreprise(DonateurEntreprise e)
    {
        var manques = new List<string>();
        if (string.IsNullOrWhiteSpace(e.RaisonSociale)) manques.Add("dénomination de l'entreprise");
        if (string.IsNullOrWhiteSpace(e.FormeJuridique)) manques.Add("forme juridique de l'entreprise");
        if (!Siren.EstValide(e.Siren)) manques.Add("numéro SIREN de l'entreprise");
        if (string.IsNullOrWhiteSpace(e.Adresse) || string.IsNullOrWhiteSpace(e.CodePostal) || string.IsNullOrWhiteSpace(e.Ville)) manques.Add("adresse complète de l'entreprise");
        return manques;
    }
}

/// <summary>Traçabilité : création, correction, annulation, émission, envoi.</summary>
public sealed class HistoriqueDon : EntiteSynchronisee
{
    public DateTimeOffset Quand { get; set; }
    public string Action { get; set; } = "";
    public Guid CibleId { get; set; }
    public string Detail { get; set; } = "";
    public string? Poste { get; set; }
}

// ============================================================
//  Règles
// ============================================================

public static class Siren
{
    /// <summary>9 chiffres, clé de Luhn (SIRET : 14 chiffres, même clé, sauf La Poste).</summary>
    public static bool EstValide(string? siren) => Chiffres(siren) is { Length: 9 } c && Luhn(c);

    public static bool SiretValide(string? siret)
    {
        if (Chiffres(siret) is not { Length: 14 } c) return false;
        if (c.StartsWith("356000000")) return c.Sum(ch => ch - '0') % 5 == 0; // La Poste
        return Luhn(c);
    }

    public static string? Normaliser(string? s) => Chiffres(s) is { Length: > 0 } c ? c : null;

    public static string Afficher(string? s) =>
        Chiffres(s) is { Length: 9 } c ? $"{c[..3]} {c[3..6]} {c[6..]}" :
        Chiffres(s) is { Length: 14 } d ? $"{d[..3]} {d[3..6]} {d[6..9]} {d[9..]}" : s ?? "";

    private static string? Chiffres(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : new string(s.Where(char.IsAsciiDigit).ToArray()) is var c && c.Length == s.Count(ch => !char.IsWhiteSpace(ch) && ch != '.') ? c : null;

    private static bool Luhn(string chiffres)
    {
        var somme = 0;
        for (var i = 0; i < chiffres.Length; i++)
        {
            var n = chiffres[chiffres.Length - 1 - i] - '0';
            if (i % 2 == 1) { n *= 2; if (n > 9) n -= 9; }
            somme += n;
        }
        return somme % 10 == 0;
    }
}

public static class RegleEntreprise
{
    public static IReadOnlyList<ErreurValidation> Valider(DonateurEntreprise e)
    {
        var erreurs = new List<ErreurValidation>();
        if (string.IsNullOrWhiteSpace(e.RaisonSociale)) erreurs.Add(new("RaisonSociale", "La raison sociale est obligatoire."));
        if (!string.IsNullOrWhiteSpace(e.Siren) && !Siren.EstValide(e.Siren))
            erreurs.Add(new("Siren", "Le numéro SIREN doit comporter 9 chiffres valides (vérifiez la saisie)."));
        if (!string.IsNullOrWhiteSpace(e.Siret))
        {
            if (!Siren.SiretValide(e.Siret)) erreurs.Add(new("Siret", "Le numéro SIRET doit comporter 14 chiffres valides."));
            else if (Siren.Normaliser(e.Siren) is { } s && !Siren.Normaliser(e.Siret)!.StartsWith(s))
                erreurs.Add(new("Siret", "Le SIRET doit commencer par le SIREN de l'entreprise."));
        }
        if (!string.IsNullOrWhiteSpace(e.Email) && !Email.EstValide(e.Email)) erreurs.Add(new("Email", "L'adresse e-mail n'est pas valide."));
        if (!string.IsNullOrWhiteSpace(e.Telephone) && !Telephone.EstValide(e.Telephone)) erreurs.Add(new("Telephone", "Le numéro de téléphone n'est pas valide."));
        return erreurs;
    }

    /// <summary>Même SIREN, ou même raison sociale (sans accents ni ponctuation).</summary>
    public static bool EstDoublon(DonateurEntreprise a, DonateurEntreprise b)
    {
        if (a.Id == b.Id || a.EstSupprime || b.EstSupprime) return false;
        if (Siren.Normaliser(a.Siren) is { } sa && sa == Siren.Normaliser(b.Siren)) return true;
        return Cle(a.RaisonSociale) is { Length: > 0 } ca && ca == Cle(b.RaisonSociale);
    }

    private static string Cle(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}

public static class RegleDon
{
    public static IReadOnlyList<ErreurValidation> Valider(Don d)
    {
        var erreurs = new List<ErreurValidation>();
        if (d.EntrepriseId == Guid.Empty) erreurs.Add(new("EntrepriseId", "Choisissez l'entreprise donatrice."));
        if (d.Montant is < 0) erreurs.Add(new("Montant", "Le montant ne peut pas être négatif."));
        switch (d.Type)
        {
            case TypeDon.Financier:
                if (d.Montant is null or 0) erreurs.Add(new("Montant", "Indiquez le montant versé."));
                if (d.Mode is null) erreurs.Add(new("Mode", "Indiquez le mode de versement."));
                break;
            case TypeDon.Nature:
                if (string.IsNullOrWhiteSpace(d.Description) && string.IsNullOrWhiteSpace(d.Quantites))
                    erreurs.Add(new("Description", "Décrivez les biens reçus (nature et quantités)."));
                break;
            case TypeDon.Competences:
                if (string.IsNullOrWhiteSpace(d.Description) && string.IsNullOrWhiteSpace(d.SalariesMisADisposition))
                    erreurs.Add(new("Description", "Décrivez la prestation et les salariés mis à disposition."));
                break;
        }
        return erreurs;
    }

    public static string Libelle(TypeDon t) => t switch
    {
        TypeDon.Financier => "Financier",
        TypeDon.Nature => "En nature",
        _ => "Compétences",
    };

    public static string Libelle(ModeVersementDon m) => m switch
    {
        ModeVersementDon.Especes => "Espèces",
        ModeVersementDon.Cheque => "Chèque",
        ModeVersementDon.Virement => "Virement",
        ModeVersementDon.Carte => "Carte bancaire",
        _ => "Autre",
    };

    public static string Libelle(StatutDon s) => s switch
    {
        StatutDon.EnAttente => "En attente",
        StatutDon.Recu => "Reçu et vérifié",
        _ => "Annulé",
    };

    /// <summary>Texte d'une ligne de don sur un justificatif.</summary>
    public static string Description(Don d)
    {
        var morceaux = new[] { d.Description, d.Quantites }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim());
        var texte = string.Join(" — ", morceaux);
        return texte.Length > 0 ? texte : Libelle(d.Type);
    }
}

public static class NumerosJustificatif
{
    public static string Prefixe(TypeJustificatif t) => t == TypeJustificatif.RecuFiscal ? "ICA-DON" : "ICA-ATT";

    public static string Format(TypeJustificatif t, int annee, int rang) => $"{Prefixe(t)}-{annee}-{rang:0000}";

    /// <summary>Prochain numéro : on compte tous les numéros déjà attribués, même annulés (jamais réutilisés).</summary>
    public static string Suivant(TypeJustificatif t, int annee, IEnumerable<string> existants)
    {
        var debut = $"{Prefixe(t)}-{annee}-";
        var max = existants.Where(n => n.StartsWith(debut, StringComparison.Ordinal))
            .Select(n => int.TryParse(n[debut.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : 0)
            .DefaultIfEmpty(0).Max();
        return Format(t, annee, max + 1);
    }
}

/// <summary>Montants en toutes lettres (orthographe traditionnelle), pour les reçus.</summary>
public static class MontantEnLettres
{
    private static readonly string[] Unites =
    [
        "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
        "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf",
    ];

    private static readonly string[] Dizaines = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt", "quatre-vingt"];

    public static string Euros(decimal montant)
    {
        if (montant < 0) throw new ArgumentOutOfRangeException(nameof(montant));
        montant = Math.Round(montant, 2, MidpointRounding.AwayFromZero);
        var euros = (long)Math.Truncate(montant);
        var centimes = (int)((montant - euros) * 100);
        var texte = Nombre(euros) + (euros >= 1_000_000 && euros % 1_000_000 == 0 ? " d'euros" : euros > 1 ? " euros" : " euro");
        if (centimes > 0) texte += " et " + Nombre(centimes) + (centimes > 1 ? " centimes" : " centime");
        return texte;
    }

    public static string Nombre(long n)
    {
        if (n < 0) return "moins " + Nombre(-n);
        if (n == 0) return "zéro";
        var parties = new List<string>();
        var milliards = n / 1_000_000_000; n %= 1_000_000_000;
        var millions = n / 1_000_000; n %= 1_000_000;
        var milliers = n / 1000; var reste = n % 1000;
        if (milliards > 0) parties.Add(MoinsDeMille((int)milliards, false) + (milliards > 1 ? " milliards" : " milliard"));
        if (millions > 0) parties.Add(MoinsDeMille((int)millions, false) + (millions > 1 ? " millions" : " million"));
        if (milliers > 0) parties.Add(milliers == 1 ? "mille" : MoinsDeMille((int)milliers, false) + " mille");
        if (reste > 0) parties.Add(MoinsDeMille((int)reste, true));
        return string.Join(" ", parties);
    }

    /// <param name="final">« quatre-vingts » et « deux cents » ne prennent le s qu'en fin de nombre.</param>
    private static string MoinsDeMille(int n, bool final)
    {
        var centaines = n / 100;
        var reste = n % 100;
        var sb = new List<string>();
        if (centaines > 0)
            sb.Add(centaines == 1 ? "cent" : Unites[centaines] + (reste == 0 && final ? " cents" : " cent"));
        if (reste > 0) sb.Add(MoinsDeCent(reste, final));
        return string.Join(" ", sb);
    }

    private static string MoinsDeCent(int n, bool final)
    {
        if (n < 20) return Unites[n];
        var d = n / 10;
        var u = n % 10;
        if (d is 7 or 9) u += 10;
        var dizaine = Dizaines[d];
        if (u == 0) return d == 8 && final ? "quatre-vingts" : dizaine;
        if (u is 1 or 11 && d is not (8 or 9)) return $"{dizaine} et {Unites[u]}";
        return $"{dizaine}-{Unites[u]}";
    }
}
