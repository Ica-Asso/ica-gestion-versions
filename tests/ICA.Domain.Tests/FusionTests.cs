using ICA.Client.Donnees;
using ICA.Contracts;

namespace ICA.Domain.Tests;

public class FusionTests
{
    private static DonneesIca Copie(DonneesIca d)
    {
        var o = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        return System.Text.Json.JsonSerializer.Deserialize<DonneesIca>(System.Text.Json.JsonSerializer.Serialize(d, o), o)!;
    }

    private static DonneesIca Base()
    {
        var d = new DonneesIca { EstDemo = false };
        d.Adherents.Add(new Adherent { Prenom = "Jean", Nom = "Dupont", UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) });
        d.Adherents.Add(new Adherent { Prenom = "Marie", Nom = "Leroy", UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) });
        return d;
    }

    [Fact]
    public void Deux_ordinateurs_finissent_identiques()
    {
        var pcBureau = Base();
        var pcPortable = Copie(pcBureau);
        var maintenant = DateTimeOffset.UtcNow;

        // Bureau : modifie Jean et ajoute Paul. Portable : modifie Marie et supprime Jean… plus tard.
        pcBureau.Adherents[0].Ville = "Guéret"; pcBureau.Adherents[0].MarquerModifie(maintenant);
        pcBureau.Adherents.Add(new Adherent { Prenom = "Paul", Nom = "Martin", UpdatedAt = maintenant });
        pcPortable.Adherents[1].Ville = "Ajain"; pcPortable.Adherents[1].MarquerModifie(maintenant);

        // Le bureau envoie sur le Drive, le portable fusionne puis renvoie, le bureau fusionne.
        var drive = Copie(pcBureau);
        var bilanPortable = Fusion.Fusionner(pcPortable, drive);
        Assert.True(bilanPortable.LocalModifie);
        Assert.True(bilanPortable.DistantAMettreAJour);
        drive = Copie(pcPortable);
        Fusion.Fusionner(pcBureau, drive);

        Assert.Equal(3, pcBureau.Adherents.Count);
        Assert.Equal(3, pcPortable.Adherents.Count);
        foreach (var a in pcBureau.Adherents)
        {
            var b = pcPortable.Adherents.Single(x => x.Id == a.Id);
            Assert.Equal(a.Ville, b.Ville);
        }
        Assert.Equal("Guéret", pcPortable.Adherents.Single(a => a.Prenom == "Jean").Ville);
        Assert.Equal("Ajain", pcBureau.Adherents.Single(a => a.Prenom == "Marie").Ville);
    }

    [Fact]
    public void Suppression_propagee()
    {
        var a = Base();
        var b = Copie(a);
        a.Adherents[0].Supprimer(DateTimeOffset.UtcNow);
        Fusion.Fusionner(b, Copie(a));
        Assert.True(b.Adherents[0].EstSupprime);
    }

    [Fact]
    public void Cotisation_en_double_dedoublonnee_en_gardant_la_payee()
    {
        var a = Base();
        var b = Copie(a);
        var id = a.Adherents[0].Id;
        a.Cotisations.Add(new Cotisation { AdherentId = id, Saison = "2026" });
        var payee = new Cotisation { AdherentId = id, Saison = "2026" };
        payee.MarquerPayee(25m, ModePaiement.Especes, new DateOnly(2026, 10, 31), DateTimeOffset.UtcNow);
        b.Cotisations.Add(payee);

        Fusion.Fusionner(a, Copie(b));
        var actives = a.Cotisations.Where(c => !c.EstSupprime).ToList();
        Assert.Single(actives);
        Assert.Equal(StatutCotisation.Payee, actives[0].Statut);
    }
}
