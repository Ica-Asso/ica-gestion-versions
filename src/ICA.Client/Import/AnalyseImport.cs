using System.Globalization;
using System.Text.RegularExpressions;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.Import;

public enum RoleColonne
{
    Ignorer, NomComplet, Nom, Prenom, Adresse, CodePostal, Ville, Email, Telephone, Telephone2, DateNaissance, Cotisation, Notes,
}

public sealed class LigneImportee
{
    public required int NumeroLigne { get; init; }
    public required Adherent Adherent { get; init; }
    public bool CotisationPayee { get; set; }
    public List<string> Remarques { get; } = [];
    public bool AVerifier => Remarques.Count > 0;
}

public sealed class ResultatAnalyse
{
    public List<LigneImportee> Lignes { get; } = [];
    public Dictionary<int, RoleColonne> Colonnes { get; } = [];
    public bool AvecEnTete { get; set; }
    public int Detectes => Lignes.Count;
    public int AVerifier => Lignes.Count(l => l.AVerifier);
    public int Prets => Detectes - AVerifier;
    public int Enfants => Lignes.Count(l => l.Adherent.EstEnfant);
    public int CotisationsPayees => Lignes.Count(l => l.CotisationPayee);
}

/// <summary>
/// Transforme le tableau existant de la gérante en fiches adhérents (CDC §23).
/// Reconnaît les colonnes par leur en-tête, ou à défaut par leur contenu.
/// </summary>
public static partial class AnalyseImport
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static ResultatAnalyse Analyser(IReadOnlyList<object?[]> lignes, DateOnly aujourdHui)
    {
        var resultat = new ResultatAnalyse();
        var nonVides = lignes.Select((l, i) => (l, numero: i + 1)).Where(x => x.l.Any(c => c is not null)).ToList();
        if (nonVides.Count == 0) return resultat;

        // 1. Colonnes : par en-tête si la première ligne en a un, sinon d'après le contenu.
        var enTete = ColonnesParEnTete(nonVides[0].l);
        IEnumerable<(object?[] l, int numero)> donnees = nonVides;
        if (enTete.Count >= 2)
        {
            resultat.AvecEnTete = true;
            foreach (var (k, v) in enTete) resultat.Colonnes[k] = v;
            donnees = nonVides.Skip(1);
        }
        else
        {
            foreach (var (k, v) in ColonnesParContenu(nonVides.Select(x => x.l).ToList())) resultat.Colonnes[k] = v;
        }

        // 2. Lignes
        LigneImportee? precedente = null;
        foreach (var (cellules, numero) in donnees)
        {
            object? Val(RoleColonne role) =>
                resultat.Colonnes.Where(c => c.Value == role).Select(c => c.Key < cellules.Length ? cellules[c.Key] : null).FirstOrDefault(v => v is not null);

            var nomComplet = Texte_(Val(RoleColonne.NomComplet));
            var nom = Texte_(Val(RoleColonne.Nom));
            var prenom = Texte_(Val(RoleColonne.Prenom));

            if (nomComplet is null && nom is null && prenom is null)
            {
                // Ligne sans nom : en général la suite de la ligne précédente (2e adresse, e-mail…).
                if (precedente is not null) Rattacher(precedente, numero, Val, aujourdHui);
                continue;
            }

            var a = new Adherent();
            var ligne = new LigneImportee { NumeroLigne = numero, Adherent = a };

            if (nomComplet is not null) DecouperNom(nomComplet, a, ligne);
            else
            {
                a.Nom = nom ?? "";
                a.Prenom = PrenomPropre(prenom ?? "");
                if (a.Nom.Length == 0) ligne.Remarques.Add("Nom manquant");
                if (a.Prenom.Length == 0) ligne.Remarques.Add("Prénom manquant");
            }

            Remplir(ligne, Val, aujourdHui, ecraser: true);
            resultat.Lignes.Add(ligne);
            precedente = ligne;
        }

        // 3. Enfants : rattachement à l'adulte du même nom vivant à la même adresse (ou mêmes coordonnées).
        var adultes = resultat.Lignes.Where(l => !l.Adherent.EstEnfant).ToList();
        foreach (var enfant in resultat.Lignes.Where(l => l.Adherent.EstEnfant))
        {
            var e = enfant.Adherent;
            var candidats = adultes.Where(p => Texte.PourRecherche(p.Adherent.Nom) == Texte.PourRecherche(e.Nom)).ToList();
            var responsable = candidats.FirstOrDefault(p => MemeContact(p.Adherent, e)) ?? (candidats.Count == 1 ? candidats[0] : null);
            if (responsable is not null) e.ResponsableId = responsable.Adherent.Id;
            else enfant.Remarques.Add("Enfant : adulte responsable à choisir");
        }

        // 4. Doublons dans le fichier
        foreach (var groupe in resultat.Lignes.GroupBy(l => Texte.PourRecherche(l.Adherent.Nom + " " + l.Adherent.Prenom)).Where(g => g.Count() > 1))
            foreach (var l in groupe)
                l.Remarques.Add($"Doublon possible (lignes {string.Join(", ", groupe.Select(x => x.NumeroLigne))})");

        return resultat;
    }

    private static bool MemeContact(Adherent a, Adherent b) =>
        (a.Adresse is not null && Texte.PourRecherche(a.Adresse) == Texte.PourRecherche(b.Adresse)) ||
        (a.Email is not null && a.Email == b.Email) ||
        (a.Telephone1 is not null && a.Telephone1 == b.Telephone1);

    private static void Remplir(LigneImportee ligne, Func<RoleColonne, object?> val, DateOnly aujourdHui, bool ecraser)
    {
        var a = ligne.Adherent;
        void Poser(Func<string?> lire, Action<string> ecrire, string? actuel)
        {
            var v = lire();
            if (v is null) return;
            if (actuel is null || ecraser) ecrire(v);
        }

        // Adresse : « 12 rue des Fleurs 23000 Guéret » → rue / code postal / ville
        if (Texte_(val(RoleColonne.Adresse)) is { } adresse && (a.Adresse is null || ecraser))
        {
            var m = AdresseAvecCp().Match(adresse);
            if (m.Success && m.Groups["rue"].Value.Trim(' ', ',', '-').Length > 0)
            {
                a.Adresse = Majuscule(m.Groups["rue"].Value.Trim(' ', ',', ';', '-'));
                a.CodePostal = m.Groups["cp"].Value;
                a.Ville = VillePropre(m.Groups["ville"].Value);
            }
            else if (m.Success)
            {
                a.CodePostal = m.Groups["cp"].Value;
                a.Ville = VillePropre(m.Groups["ville"].Value);
            }
            else
            {
                a.Adresse = Majuscule(adresse);
                if (val(RoleColonne.CodePostal) is null) ligne.Remarques.Add("Code postal et ville à compléter");
            }
        }
        Poser(() => Texte_(val(RoleColonne.CodePostal)) is { } cp ? Texte.Chiffres(cp).PadLeft(5, '0') : null, v => a.CodePostal = v, a.CodePostal);
        Poser(() => Texte_(val(RoleColonne.Ville)) is { } ville ? VillePropre(ville) : null, v => a.Ville = v, a.Ville);

        if (Texte_(val(RoleColonne.Email)) is { } email && (a.Email is null || ecraser))
        {
            if (Email.EstValide(email)) a.Email = Email.Normaliser(email);
            else { ligne.Remarques.Add("E-mail illisible (recopié dans les notes)"); AjouterNote(a, "E-mail d'origine : " + email); }
        }

        foreach (var (role, lire, ecrire) in new (RoleColonne, Func<string?>, Action<string>)[]
                 {
                     (RoleColonne.Telephone, () => a.Telephone1, v => a.Telephone1 = v),
                     (RoleColonne.Telephone2, () => a.Telephone2, v => a.Telephone2 = v),
                 })
        {
            if (val(role) is not { } brut) continue;
            var numero = NumeroDepuis(brut);
            if (numero is not null && Telephone.TryNormaliser(numero, out var n) && n is not null)
            {
                if (lire() is null || ecraser) ecrire(n);
                else if (a.Telephone2 is null && n != a.Telephone1) a.Telephone2 = n;
            }
            else { ligne.Remarques.Add("Téléphone illisible (recopié dans les notes)"); AjouterNote(a, "Téléphone d'origine : " + brut); }
        }

        if (val(RoleColonne.DateNaissance) is { } date && (a.DateNaissance is null || ecraser))
        {
            var d = DateDepuis(date);
            if (d is null) { ligne.Remarques.Add("Date de naissance illisible (recopiée dans les notes)"); AjouterNote(a, "Date d'origine : " + date); }
            else if (d > aujourdHui || d.Value.Year < 1900) ligne.Remarques.Add("Date de naissance incorrecte, non reprise");
            else
            {
                a.DateNaissance = d;
                a.TypeAdherent = a.Age(aujourdHui) < 18 ? TypeAdherent.Enfant : TypeAdherent.Adulte;
            }
        }

        if (Texte_(val(RoleColonne.Cotisation)) is { } cotis)
        {
            var t = Texte.PourRecherche(cotis);
            ligne.CotisationPayee = t.Contains("pay") || t.Contains("regl") || t.Contains("ok") || t is "oui" or "x" or "vrai" ||
                                    (decimal.TryParse(cotis.Replace("€", "").Trim(), NumberStyles.Number, Fr, out var montant) && montant > 0);
        }

        if (Texte_(val(RoleColonne.Notes)) is { } notes) AjouterNote(a, notes);
    }

    private static void Rattacher(LigneImportee precedente, int numero, Func<RoleColonne, object?> val, DateOnly aujourdHui)
    {
        var a = precedente.Adherent;
        // Ce qui manque est complété ; ce qui existe déjà part dans les notes.
        foreach (var role in new[] { RoleColonne.Adresse, RoleColonne.Email, RoleColonne.Telephone, RoleColonne.Telephone2, RoleColonne.DateNaissance })
        {
            if (val(role) is not { } v) continue;
            var dejaRempli = role switch
            {
                RoleColonne.Adresse => a.Adresse is not null || a.CodePostal is not null,
                RoleColonne.Email => a.Email is not null,
                RoleColonne.Telephone => a.Telephone1 is not null && a.Telephone2 is not null,
                RoleColonne.DateNaissance => a.DateNaissance is not null,
                _ => false,
            };
            if (dejaRempli) AjouterNote(a, $"Autre information (ligne {numero}) : {v}");
            else Remplir(precedente, r => r == role ? v : null, aujourdHui, ecraser: false);
        }
        precedente.Remarques.Add($"Ligne {numero} sans nom rattachée à cette fiche");
    }

    /// <summary>« DUPONT Jean-Pierre » → Nom DUPONT, Prénom Jean-Pierre.</summary>
    private static void DecouperNom(string complet, Adherent a, LigneImportee ligne)
    {
        var mots = complet.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static bool EnMajuscules(string m) => m.Any(char.IsLetter) && m == m.ToUpper(Fr);
        var nbNom = mots.TakeWhile(EnMajuscules).Count();

        if (mots.Length == 1)
        {
            a.Nom = mots[0];
            ligne.Remarques.Add("Prénom manquant");
        }
        else if (nbNom == 0 || nbNom == mots.Length)
        {
            // Pas de distinction NOM / Prénom : on suppose « NOM Prénom » et on signale.
            a.Nom = string.Join(' ', mots[..^1]);
            a.Prenom = PrenomPropre(mots[^1]);
            ligne.Remarques.Add("Nom et prénom à vérifier");
        }
        else
        {
            a.Nom = string.Join(' ', mots[..nbNom]);
            a.Prenom = PrenomPropre(string.Join(' ', mots[nbNom..]));
        }
    }

    // ---------------- Reconnaissance des colonnes ----------------

    private static Dictionary<int, RoleColonne> ColonnesParEnTete(object?[] ligne)
    {
        var roles = new Dictionary<int, RoleColonne>();
        // Une vraie ligne d'en-tête ne contient ni e-mail, ni nombre, ni code postal, ni long texte.
        if (ligne.Any(c => c is double || c is string s && (s.Contains('@') || s.Length > 40 || Texte.Chiffres(s).Length >= 4)))
            return roles;
        for (var i = 0; i < ligne.Length; i++)
        {
            if (ligne[i] is not string s) continue;
            var t = Texte.PourRecherche(s);
            RoleColonne? role =
                t.Contains("mail") || t.Contains("courriel") ? RoleColonne.Email :
                t.Contains("naiss") ? RoleColonne.DateNaissance :
                (t.Contains("tel") || t.Contains("portable") || t.Contains("mobile")) && (t.Contains('2') || t.Contains("second") || t.Contains("autre")) ? RoleColonne.Telephone2 :
                t.Contains("tel") || t.Contains("portable") || t.Contains("mobile") ? RoleColonne.Telephone :
                t.Contains("cotis") || t.Contains("paye") || t.Contains("regle") ? RoleColonne.Cotisation :
                t.Contains("prenom") && t.Contains("nom") && !t.StartsWith("prenom") ? RoleColonne.NomComplet :
                t.Contains("prenom") ? RoleColonne.Prenom :
                t.Contains("code postal") || t is "cp" || t.Contains("postal") ? RoleColonne.CodePostal :
                t.Contains("ville") || t.Contains("commune") || t.Contains("localite") ? RoleColonne.Ville :
                t.Contains("adresse") || t.Contains("rue") ? RoleColonne.Adresse :
                t.Contains("note") || t.Contains("observ") || t.Contains("remarque") || t.Contains("commentaire") ? RoleColonne.Notes :
                t is "nom" || t.StartsWith("nom ") || t.Contains("adherent") ? RoleColonne.Nom :
                null;
            if (role is { } r && !roles.ContainsValue(r)) roles[i] = r;
            else if (role == RoleColonne.Telephone && !roles.ContainsValue(RoleColonne.Telephone2)) roles[i] = RoleColonne.Telephone2;
        }
        return roles;
    }

    private static Dictionary<int, RoleColonne> ColonnesParContenu(List<object?[]> lignes)
    {
        var nbCol = lignes.Max(l => l.Length);
        var roles = new Dictionary<int, RoleColonne>();
        double Part(int col, Func<object, bool> test)
        {
            var remplies = lignes.Select(l => col < l.Length ? l[col] : null).Where(v => v is not null).ToList();
            return remplies.Count == 0 ? 0 : remplies.Count(v => test(v!)) / (double)remplies.Count;
        }
        int Remplies(int col) => lignes.Count(l => col < l.Length && l[col] is not null);

        bool EstEmail(object v) => v is string s && s.Contains('@');
        bool EstTelephone(object v) => NumeroDepuis(v) is { } n && Telephone.EstValide(n);
        bool EstDate(object v) => v is double d ? d is > 1 and < 80000 : v is string s && DateDepuis(s) is not null;
        bool EstAdresse(object v) => v is string s && AdresseAvecCp().IsMatch(s);
        bool EstCotisation(object v) => v is string s && Texte.PourRecherche(s) is var t && (t.Contains("pay") || t.Contains("cotis") || t.Contains("regl"));
        bool EstNom(object v) => v is string s && s.Length > 1 && !s.Any(char.IsDigit) && !s.Contains('@') && s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5;

        var restantes = Enumerable.Range(0, nbCol).Where(c => Remplies(c) > 0).ToList();
        void Prendre(RoleColonne role, Func<object, bool> test, double seuil = 0.6)
        {
            var col = restantes.Select(c => (c, p: Part(c, test))).Where(x => x.p >= seuil).OrderByDescending(x => x.p).ThenBy(x => x.c).Select(x => (int?)x.c).FirstOrDefault();
            if (col is { } c) { roles[c] = role; restantes.Remove(c); }
        }

        Prendre(RoleColonne.Email, EstEmail);
        Prendre(RoleColonne.Cotisation, EstCotisation);
        Prendre(RoleColonne.DateNaissance, EstDate);
        Prendre(RoleColonne.Telephone, EstTelephone);
        Prendre(RoleColonne.Telephone2, EstTelephone);
        Prendre(RoleColonne.Adresse, EstAdresse, 0.4);
        // Le nom : la première colonne restante qui ressemble à des noms.
        if (restantes.FirstOrDefault(c => Part(c, EstNom) >= 0.8) is var nomCol && restantes.Contains(nomCol))
        {
            roles[nomCol] = RoleColonne.NomComplet;
            restantes.Remove(nomCol);
        }
        // Adresse sans code postal : la colonne de texte restante la plus remplie.
        if (!roles.ContainsValue(RoleColonne.Adresse) && restantes.OrderByDescending(Remplies).FirstOrDefault(c => Part(c, v => v is string) > 0.6) is var ad && restantes.Contains(ad))
            roles[ad] = RoleColonne.Adresse;
        return roles;
    }

    // ---------------- Outils ----------------

    private static string? Texte_(object? v) => v switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : Regex.Replace(s.Trim(), @"\s+", " "),
        double d => d.ToString("0.##########", CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    /// <summary>Excel enlève le 0 des numéros (606060606) : on le remet.</summary>
    private static string? NumeroDepuis(object v)
    {
        var chiffres = v switch
        {
            double d when d >= 1e7 && d < 1e12 => ((long)d).ToString(CultureInfo.InvariantCulture),
            string s => s.Trim().StartsWith('+') ? s : Texte.Chiffres(s),
            _ => null,
        };
        if (string.IsNullOrEmpty(chiffres) || (chiffres.StartsWith('+') ? Texte.Chiffres(chiffres).Length < 8 : chiffres.Length < 8)) return null;
        if (chiffres.StartsWith('+')) return chiffres;
        return chiffres.Length == 9 && chiffres[0] != '0' ? "0" + chiffres : chiffres;
    }

    private static DateOnly? DateDepuis(object v)
    {
        if (v is double d && d is > 1 and < 80000)
            return DateOnly.FromDateTime(DateTime.FromOADate(d));
        if (v is string s && DateTime.TryParseExact(s.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy"],
                Fr, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    private static string PrenomPropre(string prenom)
    {
        if (prenom.Length == 0) return prenom;
        var bas = prenom.ToLower(Fr);
        return Regex.Replace(bas, @"(^|[\s\-'])(\p{L})", m => m.Groups[1].Value + m.Groups[2].Value.ToUpper(Fr));
    }

    private static string VillePropre(string ville)
    {
        var v = Regex.Replace(ville.Trim(' ', ',', '-', ';'), @"\s+", " ");
        return v == v.ToLower(Fr) || v == v.ToUpper(Fr) ? PrenomPropre(v) : v;
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Fr) + s[1..];

    private static void AjouterNote(Adherent a, string note) =>
        a.Notes = string.IsNullOrEmpty(a.Notes) ? note : a.Notes + "\n" + note;

    [GeneratedRegex(@"^(?<rue>.*?)[\s,;\-]*(?<cp>\b\d{5}\b)\s*(?<ville>[^\d].*)$")]
    private static partial Regex AdresseAvecCp();
}
