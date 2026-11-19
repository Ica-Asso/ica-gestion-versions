using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ICA.Domain;

/// <summary>Normalisation de texte partagée Windows / mobile / serveur.</summary>
public static partial class Texte
{
    /// <summary>Minuscules, sans accents, espaces réduits : « Éloïse  Guéret » → « eloise gueret ».</summary>
    public static string PourRecherche(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return "";
        var decompose = texte.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decompose.Length);
        foreach (var c in decompose)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        var sansAccents = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        return Espaces().Replace(sansAccents, " ").Trim();
    }

    /// <summary>Supprime les espaces superflus ; renvoie null si vide.</summary>
    public static string? Nettoyer(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        return Espaces().Replace(texte.Trim(), " ");
    }

    /// <summary>Ne garde que les chiffres.</summary>
    public static string Chiffres(string? texte)
    {
        if (string.IsNullOrEmpty(texte)) return "";
        var sb = new StringBuilder(texte.Length);
        foreach (var c in texte)
            if (c is >= '0' and <= '9') sb.Append(c);
        return sb.ToString();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espaces();
}

public static partial class Email
{
    public static bool EstValide(string? email)
        => !string.IsNullOrWhiteSpace(email) && Format().IsMatch(email.Trim());

    public static string? Normaliser(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Format();
}

/// <summary>
/// Numéros de téléphone : stockés au format international (+33600000001),
/// affichés au format français (06 00 00 00 01).
/// </summary>
public static class Telephone
{
    public static bool TryNormaliser(string? saisie, out string? normalise)
    {
        normalise = null;
        if (string.IsNullOrWhiteSpace(saisie)) return true;

        var t = saisie.Trim();
        var international = t.StartsWith('+');
        var chiffres = Texte.Chiffres(t);

        if (international)
        {
            if (chiffres.StartsWith("33"))
            {
                var national = chiffres[2..];
                if (national.StartsWith('0')) national = national[1..];
                if (national.Length != 9) return false;
                normalise = "+33" + national;
                return true;
            }
            if (chiffres.Length is < 8 or > 15) return false;
            normalise = "+" + chiffres;
            return true;
        }

        if (chiffres.StartsWith("0033"))
        {
            var national = chiffres[4..];
            if (national.Length != 9) return false;
            normalise = "+33" + national;
            return true;
        }

        if (chiffres.Length == 10 && chiffres[0] == '0')
        {
            normalise = "+33" + chiffres[1..];
            return true;
        }

        return false;
    }

    public static bool EstValide(string? saisie) => TryNormaliser(saisie, out _);

    public static string Afficher(string? numero)
    {
        if (string.IsNullOrWhiteSpace(numero)) return "";
        if (numero.StartsWith("+33") && numero.Length == 12)
        {
            var n = "0" + numero[3..];
            return $"{n[..2]} {n[2..4]} {n[4..6]} {n[6..8]} {n[8..10]}";
        }
        return numero;
    }

    /// <summary>Formes chiffrées utilisées par la recherche (« 0600000001 » et « 33600000001 »).</summary>
    public static string PourRecherche(string? numero)
    {
        if (string.IsNullOrWhiteSpace(numero)) return "";
        var chiffres = Texte.Chiffres(numero);
        if (numero.StartsWith("+33") && chiffres.Length == 11)
            return chiffres + " 0" + chiffres[2..];
        return chiffres;
    }
}
