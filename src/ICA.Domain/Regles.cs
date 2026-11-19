using ICA.Contracts;

namespace ICA.Domain;

public sealed record ErreurValidation(string Champ, string Message);

/// <summary>Règles métier d'un adhérent — partagées par tous les appareils et le serveur.</summary>
public static class RegleAdherent
{
    /// <summary>Met les champs au propre (espaces, e-mail, téléphones) avant validation.</summary>
    public static void Normaliser(Adherent a)
    {
        a.Nom = Texte.Nettoyer(a.Nom) ?? "";
        a.Prenom = Texte.Nettoyer(a.Prenom) ?? "";
        a.Adresse = Texte.Nettoyer(a.Adresse);
        a.ComplementAdresse = Texte.Nettoyer(a.ComplementAdresse);
        a.CodePostal = Texte.Nettoyer(a.CodePostal)?.Replace(" ", "");
        a.Ville = Texte.Nettoyer(a.Ville);
        a.Email = Email.Normaliser(a.Email);
        a.Notes = string.IsNullOrWhiteSpace(a.Notes) ? null : a.Notes.Trim();
        if (Telephone.TryNormaliser(a.Telephone1, out var t1)) a.Telephone1 = t1;
        if (Telephone.TryNormaliser(a.Telephone2, out var t2)) a.Telephone2 = t2;
        if (a.TypeAdherent == TypeAdherent.Adulte) a.ResponsableId = null;
    }

    public static IReadOnlyList<ErreurValidation> Valider(Adherent a, Func<Guid, Adherent?> trouver, DateOnly aujourdHui)
    {
        var erreurs = new List<ErreurValidation>();

        if (string.IsNullOrWhiteSpace(a.Nom))
            erreurs.Add(new("Nom", "Le nom est obligatoire."));
        if (string.IsNullOrWhiteSpace(a.Prenom))
            erreurs.Add(new("Prenom", "Le prénom est obligatoire."));

        if (!string.IsNullOrWhiteSpace(a.Email) && !Email.EstValide(a.Email))
            erreurs.Add(new("Email", "L'adresse e-mail ne semble pas correcte (exemple : prenom.nom@gmail.com)."));

        if (!Telephone.EstValide(a.Telephone1))
            erreurs.Add(new("Telephone1", "Le téléphone doit comporter 10 chiffres (exemple : 06 12 34 56 78)."));
        if (!Telephone.EstValide(a.Telephone2))
            erreurs.Add(new("Telephone2", "Le téléphone secondaire doit comporter 10 chiffres."));

        if (!string.IsNullOrWhiteSpace(a.CodePostal) &&
            (a.CodePostal.Length != 5 || Texte.Chiffres(a.CodePostal).Length != 5))
            erreurs.Add(new("CodePostal", "Le code postal doit comporter 5 chiffres."));

        if (a.DateNaissance is { } n)
        {
            if (n > aujourdHui)
                erreurs.Add(new("DateNaissance", "La date de naissance est dans le futur."));
            else if (n.Year < 1900)
                erreurs.Add(new("DateNaissance", "La date de naissance semble incorrecte."));
        }

        if (a.TypeAdherent == TypeAdherent.Enfant)
        {
            if (a.ResponsableId is not { } rid)
            {
                erreurs.Add(new("ResponsableId", "Choisissez l'adulte responsable de cet enfant."));
            }
            else if (rid == a.Id)
            {
                erreurs.Add(new("ResponsableId", "Un enfant ne peut pas être son propre responsable."));
            }
            else
            {
                var responsable = trouver(rid);
                if (responsable is null || responsable.EstSupprime)
                    erreurs.Add(new("ResponsableId", "Le responsable choisi n'existe plus."));
                else if (responsable.TypeAdherent != TypeAdherent.Adulte)
                    erreurs.Add(new("ResponsableId", "Le responsable doit être un adulte."));
            }
        }

        return erreurs;
    }

    /// <summary>Deux fiches qui désignent très probablement la même personne.</summary>
    public static bool SontProbablementIdentiques(Adherent a, Adherent b)
    {
        if (a.Id == b.Id) return false;
        if (Texte.PourRecherche(a.Nom) != Texte.PourRecherche(b.Nom)) return false;
        if (Texte.PourRecherche(a.Prenom) != Texte.PourRecherche(b.Prenom)) return false;

        if (a.DateNaissance is not null && b.DateNaissance is not null)
            return a.DateNaissance == b.DateNaissance;
        if (!string.IsNullOrEmpty(a.Email) && !string.IsNullOrEmpty(b.Email))
            return string.Equals(a.Email, b.Email, StringComparison.OrdinalIgnoreCase);

        // Même nom et prénom, rien pour les distinguer : on préfère signaler.
        return true;
    }
}

/// <summary>Recherche instantanée : nom, prénom, adresse, commune, e-mail, téléphone.</summary>
/// <summary>Ce qu'il manque sur une fiche, pour que la gérante sache quoi compléter.</summary>
public static class InfosManquantes
{
    /// <summary>
    /// Pour un enfant, e-mail, téléphone et adresse peuvent venir de son responsable :
    /// ils ne manquent que s'ils manquent aussi chez lui.
    /// </summary>
    public static IReadOnlyList<string> De(Adherent a, Adherent? responsable)
    {
        static bool Vide(string? s) => string.IsNullOrWhiteSpace(s);
        static bool SansTel(Adherent x) => Vide(x.Telephone1) && Vide(x.Telephone2);
        static bool SansAdresse(Adherent x) => Vide(x.Adresse) || Vide(x.CodePostal) || Vide(x.Ville);
        var r = responsable is not null && a.EstEnfant ? responsable : null;

        var manques = new List<string>();
        if (a.EstEnfant && a.ResponsableId is null) manques.Add("responsable");
        if (Vide(a.Email) && (r is null || Vide(r.Email))) manques.Add("e-mail");
        if (SansTel(a) && (r is null || SansTel(r))) manques.Add("téléphone");
        if (SansAdresse(a) && (r is null || SansAdresse(r))) manques.Add("adresse");
        if (a.DateNaissance is null) manques.Add("date de naissance");
        return manques;
    }

    public static string? Texte(IReadOnlyList<string> manques) =>
        manques.Count == 0 ? null : "Manque : " + string.Join(", ", manques);
}

public static class RechercheAdherent
{
    public static string IndexDe(Adherent a, string? nomResponsable = null)
    {
        var texte = string.Join(' ',
            a.Nom, a.Prenom, a.Adresse, a.ComplementAdresse, a.CodePostal, a.Ville, a.Email, nomResponsable);
        return Texte.PourRecherche(texte) + " " +
               Telephone.PourRecherche(a.Telephone1) + " " +
               Telephone.PourRecherche(a.Telephone2);
    }

    /// <summary>Chaque mot de la recherche doit apparaître quelque part (ordre libre).</summary>
    public static bool Correspond(string index, string? recherche)
    {
        if (string.IsNullOrWhiteSpace(recherche)) return true;

        // Numéro tapé avec des espaces ou des points (« 06 12 34 56 78 ») : on le cherche d'un bloc.
        var seulementNumero = recherche.All(c => char.IsDigit(c) || c is ' ' or '.' or '-' or '+');
        var chiffresRecherche = Texte.Chiffres(recherche);
        if (seulementNumero && chiffresRecherche.Length >= 4)
            return index.Replace(" ", "").Contains(chiffresRecherche, StringComparison.Ordinal);

        foreach (var mot in Texte.PourRecherche(recherche).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var chiffres = Texte.Chiffres(mot);
            var cherche = chiffres.Length >= 2 && chiffres.Length == mot.Length ? chiffres : mot;
            if (!index.Contains(cherche, StringComparison.Ordinal))
            {
                // « 06 12 34 » tapé avec espaces : on compare aussi les chiffres collés.
                if (chiffres.Length < 2 || !index.Replace(" ", "").Contains(chiffres, StringComparison.Ordinal))
                    return false;
            }
        }
        return true;
    }
}

public static class Saisons
{
    public static string Courante(DateOnly aujourdHui, ModeSaison mode) => mode switch
    {
        ModeSaison.Scolaire => aujourdHui.Month >= 9
            ? $"{aujourdHui.Year}-{aujourdHui.Year + 1}"
            : $"{aujourdHui.Year - 1}-{aujourdHui.Year}",
        _ => aujourdHui.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

public static class Anniversaires
{
    /// <summary>Prochaine date d'anniversaire (aujourd'hui compris). 29 février → 28 février les années non bissextiles.</summary>
    public static DateOnly Prochain(DateOnly naissance, DateOnly aujourdHui)
    {
        var cetteAnnee = DateAnniversaire(naissance, aujourdHui.Year);
        return cetteAnnee >= aujourdHui ? cetteAnnee : DateAnniversaire(naissance, aujourdHui.Year + 1);
    }

    private static DateOnly DateAnniversaire(DateOnly naissance, int annee)
    {
        var jour = Math.Min(naissance.Day, DateTime.DaysInMonth(annee, naissance.Month));
        return new DateOnly(annee, naissance.Month, jour);
    }
}
