using ICA.Client.Import;
using ICA.Contracts;

namespace ICA.Domain.Tests;

// Fichiers 100 % fictifs, créés pour le test.
public class ImportTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 10, 31);

    private static ResultatAnalyse Analyser(string contenuCsv)
    {
        var chemin = Path.Combine(Path.GetTempPath(), $"ica-test-{Guid.NewGuid():N}.csv");
        File.WriteAllText(chemin, contenuCsv);
        try { return AnalyseImport.Analyser(LecteurTableur.Lire(chemin), Aujourdhui); }
        finally { File.Delete(chemin); }
    }

    [Fact]
    public void Tableau_sans_en_tete_comme_celui_de_l_association()
    {
        var r = Analyser(
            "DUPONT Jean;12 rue des Fleurs 23000 gueret;jean.dupont@example.com;600000001;;14/03/1968;Cotis Payée\n" +
            "DUPONT Léa;12 rue des Fleurs 23000 gueret;;600000001;;02/06/2016;Cotis Payée\n" +
            "MARTIN paul;15 route de Limoges, 23300 LA SOUTERRAINE;;600000002;555000002;07/01/1959;\n");

        Assert.False(r.AvecEnTete);
        Assert.Equal(3, r.Detectes);
        var jean = r.Lignes[0].Adherent;
        Assert.Equal("DUPONT", jean.Nom);
        Assert.Equal("Jean", jean.Prenom);
        Assert.Equal("12 rue des Fleurs", jean.Adresse);
        Assert.Equal("23000", jean.CodePostal);
        Assert.Equal("Gueret", jean.Ville);
        Assert.Equal("+33600000001", jean.Telephone1); // le 0 perdu par Excel est remis
        Assert.True(r.Lignes[0].CotisationPayee);

        var lea = r.Lignes[1].Adherent;
        Assert.Equal(TypeAdherent.Enfant, lea.TypeAdherent);
        Assert.Equal(jean.Id, lea.ResponsableId);

        var paul = r.Lignes[2].Adherent;
        Assert.Equal("Paul", paul.Prenom);
        Assert.Equal("La Souterraine", paul.Ville);
        Assert.Equal("+33555000002", paul.Telephone2);
        Assert.False(r.Lignes[2].CotisationPayee);
    }

    [Fact]
    public void Tableau_avec_en_tete()
    {
        var r = Analyser("Nom;Prénom;Adresse;Code postal;Ville;Téléphone;E-mail\nROUX;camille;8 allée des Tilleuls;23000;Guéret;06 00 00 00 10;camille.roux@example.com\n");
        Assert.True(r.AvecEnTete);
        var a = Assert.Single(r.Lignes).Adherent;
        Assert.Equal("Camille", a.Prenom);
        Assert.Equal("camille.roux@example.com", a.Email);
    }

    [Fact]
    public void Ligne_sans_nom_rattachee_a_la_precedente()
    {
        var r = Analyser("BLANC Monique;11 rue du Château 23000 Guéret;;600000003;;;\n;;monique.blanc@example.com;;;;\n");
        var l = Assert.Single(r.Lignes);
        Assert.Equal("monique.blanc@example.com", l.Adherent.Email);
        Assert.True(l.AVerifier);
    }
}
