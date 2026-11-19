using ICA.Client.MisesAJour;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Domain.Tests;

// Données exclusivement fictives.
public class MisesAJourTests
{
    private static string Release(string tag, string nom = "ICA-Gestion.exe", bool pre = false) => $$"""
        {"tag_name":"{{tag}}","draft":false,"prerelease":{{(pre ? "true" : "false")}},"body":"Corrections",
         "assets":[{"name":"{{nom}}","size":5000000,"digest":"sha256:abc123",
                    "browser_download_url":"https://github.com/Ica-Asso/ica-gestion-versions/releases/download/{{tag}}/{{nom}}"}]}
        """;

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.2", "0.2.0")]
    [InlineData("V1.0.3-beta", "1.0.3")]
    public void Lit_les_numeros_de_version(string tag, string attendu) =>
        Assert.Equal(Version.Parse(attendu), VerificateurMisesAJour.LireVersion(tag));

    [Fact]
    public void Propose_une_version_plus_recente()
    {
        var v = VerificateurMisesAJour.Lire(Release("v0.2.0"), new Version(0, 1, 0));
        Assert.NotNull(v);
        Assert.Equal("0.2.0", v!.Libelle);
        Assert.Equal("abc123", v.Sha256);
        Assert.Equal("Corrections", v.Notes);
    }

    [Fact]
    public void Ignore_la_meme_version_ou_plus_ancienne()
    {
        Assert.Null(VerificateurMisesAJour.Lire(Release("v0.1.0"), new Version(0, 1, 0)));
        Assert.Null(VerificateurMisesAJour.Lire(Release("v0.0.9"), new Version(0, 1, 0)));
    }

    [Fact]
    public void Ignore_les_preversions_et_les_releases_sans_programme()
    {
        Assert.Null(VerificateurMisesAJour.Lire(Release("v0.2.0", pre: true), new Version(0, 1, 0)));
        Assert.Null(VerificateurMisesAJour.Lire(Release("v0.2.0", nom: "autre.zip"), new Version(0, 1, 0)));
    }
}

public class InfosManquantesTests
{
    private static Adherent Complet() => new()
    {
        Nom = "Fictif", Prenom = "Alice", Email = "alice@exemple.test", Telephone1 = "+33600000001",
        Adresse = "1 rue de l'Exemple", CodePostal = "00000", Ville = "Testville", DateNaissance = new DateOnly(1980, 1, 1),
    };

    [Fact]
    public void Fiche_complete_rien_ne_manque() => Assert.Empty(InfosManquantes.De(Complet(), null));

    [Fact]
    public void Signale_ce_qui_manque()
    {
        var a = Complet();
        a.Email = null;
        a.Telephone1 = null;
        a.Ville = null;
        Assert.Equal("e-mail|téléphone|adresse", string.Join("|", InfosManquantes.De(a, null)));
        Assert.Equal("Manque : e-mail, téléphone, adresse", InfosManquantes.Texte(InfosManquantes.De(a, null)));
    }

    [Fact]
    public void Enfant_utilise_les_coordonnees_du_responsable()
    {
        var parent = Complet();
        var enfant = new Adherent { Nom = "Fictif", Prenom = "Léo", TypeAdherent = TypeAdherent.Enfant, ResponsableId = parent.Id };
        Assert.Equal("date de naissance", string.Join("|", InfosManquantes.De(enfant, parent)));
    }

    [Fact]
    public void Enfant_sans_responsable()
    {
        var enfant = new Adherent { Nom = "Fictif", Prenom = "Léo", TypeAdherent = TypeAdherent.Enfant, DateNaissance = new DateOnly(2018, 1, 1) };
        Assert.True(InfosManquantes.De(enfant, null).Contains("responsable"));
    }
}

public class HistoriqueMessagesTests
{
    [Fact]
    public void Les_messages_des_deux_ordinateurs_sont_reunis()
    {
        var local = new ICA.Client.Donnees.DonneesIca();
        var distant = new ICA.Client.Donnees.DonneesIca();
        local.Messages.Add(new MessageEnvoye { Objet = "Message A" });
        distant.Messages.Add(new MessageEnvoye { Objet = "Message B" });
        var bilan = ICA.Client.Donnees.Fusion.Fusionner(local, distant);
        Assert.Equal(2, local.Messages.Count);
        Assert.True(bilan.LocalModifie);
        Assert.True(bilan.DistantAMettreAJour);
    }
}
