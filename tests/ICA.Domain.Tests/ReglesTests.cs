using ICA.Contracts;
using ICA.Domain;

namespace ICA.Domain.Tests;

// Données exclusivement fictives (CDC §71-6).
public class TelephoneTests
{
    [Theory]
    [InlineData("06 00 00 00 01", "+33600000001")]
    [InlineData("06.00.00.00.01", "+33600000001")]
    [InlineData("0600000001", "+33600000001")]
    [InlineData("+33 6 00 00 00 01", "+33600000001")]
    [InlineData("+33 (0)6 00 00 00 01", "+33600000001")]
    [InlineData("0033600000001", "+33600000001")]
    public void Normalise_les_formats_courants(string saisie, string attendu)
    {
        Assert.True(Telephone.TryNormaliser(saisie, out var n));
        Assert.Equal(attendu, n);
    }

    [Theory]
    [InlineData("06123")]
    [InlineData("12345678901")]
    [InlineData("abc")]
    public void Refuse_les_numeros_incomplets(string saisie) => Assert.False(Telephone.EstValide(saisie));

    [Fact]
    public void Vide_est_accepte() => Assert.True(Telephone.EstValide("  "));

    [Fact]
    public void Affiche_au_format_francais() => Assert.Equal("06 00 00 00 01", Telephone.Afficher("+33600000001"));
}

public class TexteTests
{
    [Fact]
    public void Recherche_sans_accents_ni_majuscules() => Assert.Equal("eloise gueret", Texte.PourRecherche("  Éloïse   GUÉRET "));
}

public class RegleAdherentTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 10, 31);

    private static Adherent Adulte(string prenom = "Jean", string nom = "Dupont") =>
        new() { Prenom = prenom, Nom = nom, TypeAdherent = TypeAdherent.Adulte };

    [Fact]
    public void Nom_et_prenom_obligatoires()
    {
        var erreurs = RegleAdherent.Valider(new Adherent(), _ => null, Aujourdhui);
        Assert.Contains(erreurs, e => e.Champ == "Nom");
        Assert.Contains(erreurs, e => e.Champ == "Prenom");
    }

    [Fact]
    public void Email_invalide_refuse()
    {
        var a = Adulte();
        a.Email = "jean.dupont@";
        Assert.Contains(RegleAdherent.Valider(a, _ => null, Aujourdhui), e => e.Champ == "Email");
    }

    [Fact]
    public void Enfant_sans_responsable_refuse()
    {
        var enfant = new Adherent { Prenom = "Léa", Nom = "Dupont", TypeAdherent = TypeAdherent.Enfant };
        Assert.Contains(RegleAdherent.Valider(enfant, _ => null, Aujourdhui), e => e.Champ == "ResponsableId");
    }

    [Fact]
    public void Enfant_avec_responsable_adulte_accepte()
    {
        var parent = Adulte();
        var enfant = new Adherent { Prenom = "Léa", Nom = "Dupont", TypeAdherent = TypeAdherent.Enfant, ResponsableId = parent.Id };
        Assert.Empty(RegleAdherent.Valider(enfant, id => id == parent.Id ? parent : null, Aujourdhui));
    }

    [Fact]
    public void Responsable_enfant_refuse()
    {
        var autreEnfant = new Adherent { Prenom = "Hugo", Nom = "Girard", TypeAdherent = TypeAdherent.Enfant };
        var enfant = new Adherent { Prenom = "Léa", Nom = "Dupont", TypeAdherent = TypeAdherent.Enfant, ResponsableId = autreEnfant.Id };
        Assert.Contains(RegleAdherent.Valider(enfant, _ => autreEnfant, Aujourdhui), e => e.Champ == "ResponsableId");
    }

    [Fact]
    public void Naissance_dans_le_futur_refusee()
    {
        var a = Adulte();
        a.DateNaissance = Aujourdhui.AddDays(1);
        Assert.Contains(RegleAdherent.Valider(a, _ => null, Aujourdhui), e => e.Champ == "DateNaissance");
    }

    [Fact]
    public void Normaliser_nettoie_les_champs()
    {
        var a = Adulte();
        a.Nom = "  Dupont  ";
        a.Email = " Jean.Dupont@Example.COM ";
        a.Telephone1 = "06 00 00 00 01";
        a.ResponsableId = Guid.NewGuid();
        RegleAdherent.Normaliser(a);
        Assert.Equal("Dupont", a.Nom);
        Assert.Equal("jean.dupont@example.com", a.Email);
        Assert.Equal("+33600000001", a.Telephone1);
        Assert.Null(a.ResponsableId); // un adulte n'a pas de responsable
    }

    [Fact]
    public void Doublon_detecte_sans_tenir_compte_des_accents()
    {
        var a = Adulte("Éric", "Lambert");
        var b = Adulte("eric", "LAMBERT");
        Assert.True(RegleAdherent.SontProbablementIdentiques(a, b));
    }

    [Fact]
    public void Homonymes_nes_a_des_dates_differentes_ne_sont_pas_des_doublons()
    {
        var a = Adulte();
        a.DateNaissance = new DateOnly(1968, 3, 14);
        var b = Adulte();
        b.DateNaissance = new DateOnly(1990, 1, 2);
        Assert.False(RegleAdherent.SontProbablementIdentiques(a, b));
    }
}

public class RechercheTests
{
    private static readonly Adherent Jean = new()
    {
        Prenom = "Jean", Nom = "Dupont", Adresse = "12 rue des Fleurs", CodePostal = "23000", Ville = "Guéret",
        Email = "jean.dupont@example.com", Telephone1 = "+33600000001",
    };

    [Theory]
    [InlineData("dupont")]
    [InlineData("JEAN dup")]
    [InlineData("gueret")]
    [InlineData("fleurs")]
    [InlineData("23000")]
    [InlineData("06 00 00 00 01")]
    [InlineData("0600000001")]
    [InlineData("example.com")]
    public void Trouve(string recherche) => Assert.True(RechercheAdherent.Correspond(RechercheAdherent.IndexDe(Jean), recherche));

    [Theory]
    [InlineData("martin")]
    [InlineData("06 00 00 00 02")]
    public void Ne_trouve_pas(string recherche) => Assert.False(RechercheAdherent.Correspond(RechercheAdherent.IndexDe(Jean), recherche));
}

public class SaisonsEtAnniversairesTests
{
    [Theory]
    [InlineData(2026, 10, ModeSaison.Civile, "2026")]
    [InlineData(2026, 10, ModeSaison.Scolaire, "2026-2027")]
    [InlineData(2026, 3, ModeSaison.Scolaire, "2025-2026")]
    public void Saison_courante(int annee, int mois, ModeSaison mode, string attendu)
        => Assert.Equal(attendu, Saisons.Courante(new DateOnly(annee, mois, 15), mode));

    [Fact]
    public void Anniversaire_29_fevrier_une_annee_non_bissextile()
        => Assert.Equal(new DateOnly(2027, 2, 28), Anniversaires.Prochain(new DateOnly(2000, 2, 29), new DateOnly(2026, 10, 31)));

    [Fact]
    public void Anniversaire_aujourdhui()
        => Assert.Equal(new DateOnly(2026, 10, 31), Anniversaires.Prochain(new DateOnly(1980, 10, 31), new DateOnly(2026, 10, 31)));
}

public class ModelesTests
{
    [Fact]
    public void Sept_modeles_par_defaut() => Assert.Equal(7, ModelesParDefaut.Creer().Count);

    [Fact]
    public void Prenom_remplace_pour_un_destinataire()
    {
        var jean = new Adherent { Prenom = "Jean", Nom = "Dupont" };
        var v = RenduModele.Valeurs(jean, "2026", new DateOnly(2026, 10, 31));
        Assert.Equal("Bonjour Jean, cotisation 2026", RenduModele.Appliquer("Bonjour {{prenom}}, cotisation {{saison}}", v));
    }

    [Fact]
    public void Envoi_groupe_sans_prenom()
    {
        var v = RenduModele.Valeurs(null, "2026", new DateOnly(2026, 10, 31));
        Assert.Equal("Bonjour,", RenduModele.Appliquer("Bonjour {{prenom}},", v));
    }

    [Fact]
    public void Variable_inconnue_conservee()
        => Assert.Equal("{{activite}}", RenduModele.Appliquer("{{activite}}", new Dictionary<string, string?>()));
}

public class PlanningTests
{
    [Theory]
    [InlineData("14h00", 14, 0)]
    [InlineData("14h", 14, 0)]
    [InlineData("9h30", 9, 30)]
    [InlineData("09:30", 9, 30)]
    [InlineData("18", 18, 0)]
    public void Lit_les_heures(string saisie, int h, int m)
    {
        Assert.True(RegleSeance.TryLireHeure(saisie, out var heure));
        Assert.Equal(new TimeOnly(h, m), heure);
    }

    [Fact]
    public void Heure_invalide_refusee() => Assert.False(RegleSeance.TryLireHeure("25h", out _));

    [Fact]
    public void Repetition_hebdomadaire()
    {
        var dates = RegleSeance.ChaqueSemaine(new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 24));
        Assert.Equal(4, dates.Count);
        Assert.Equal(new DateOnly(2026, 11, 24), dates[^1]);
    }

    [Fact]
    public void Fin_avant_debut_refusee()
    {
        var s = new Seance { Titre = "AG", HeureDebut = new TimeOnly(18, 0), HeureFin = new TimeOnly(17, 0) };
        Assert.Contains(RegleSeance.Valider(s), e => e.Champ == "HeureFin");
    }
}
