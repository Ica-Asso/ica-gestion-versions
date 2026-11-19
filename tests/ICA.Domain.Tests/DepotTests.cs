using ICA.Client.Configuration;
using ICA.Client.Donnees;
using ICA.Client.Journal;
using ICA.Client.Services;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Domain.Tests;

public class DepotTests
{
    private sealed class StockageMemoire : IStockage
    {
        public DonneesIca? Donnees { get; private set; }
        public int Enregistrements { get; private set; }
        public string Emplacement => "mémoire";
        public DonneesIca? Charger() => Donnees;
        public void Enregistrer(DonneesIca donnees) { Donnees = donnees; Enregistrements++; }
    }

    private sealed class JournalMuet : IJournal
    {
        public string Dossier => "";
        public void Info(string message) { }
        public void Avertissement(string message) { }
        public void Erreur(string message, Exception? exception = null) { }
        public IReadOnlyList<string> DernieresLignes(int nombre) => [];
    }

    private static DepotIca NouveauDepot() => new(new StockageMemoire(), new JournalMuet(), new ParametresApplication());

    [Fact]
    public void Premier_lancement_cree_des_donnees_de_demo()
    {
        var depot = NouveauDepot();
        Assert.True(depot.Adherents.Count > 10);
        Assert.Contains(depot.Adherents, a => a.EstEnfant && a.ResponsableId is not null);
    }

    [Fact]
    public void Ajout_cree_une_cotisation_a_regler()
    {
        var depot = NouveauDepot();
        var a = new Adherent { Prenom = "Zoé", Nom = "Testeur" };
        Assert.True(depot.EnregistrerAdherent(a).Reussi);
        Assert.Equal(StatutCotisation.APayer, depot.StatutDe(a.Id, depot.SaisonCourante));
    }

    [Fact]
    public void Modification_incremente_la_version()
    {
        var depot = NouveauDepot();
        var a = depot.Adherents.First(x => !x.EstEnfant);
        var version = a.Version;
        var copie = a.Copier();
        copie.Ville = "Aubusson";
        depot.EnregistrerAdherent(copie);
        Assert.Equal(version + 1, depot.TrouverAdherent(a.Id)!.Version);
        Assert.Equal("Aubusson", depot.TrouverAdherent(a.Id)!.Ville);
    }

    [Fact]
    public void Suppression_refusee_si_responsable_d_enfants()
    {
        var depot = NouveauDepot();
        var parent = depot.Adherents.First(a => depot.EnfantsDe(a.Id).Count > 0);
        Assert.False(depot.SupprimerAdherent(parent.Id).Reussi);
        Assert.NotNull(depot.TrouverAdherent(parent.Id));
    }

    [Fact]
    public void Suppression_logique_avec_ses_cotisations()
    {
        var depot = NouveauDepot();
        var a = depot.Adherents.First(x => !x.EstEnfant && depot.EnfantsDe(x.Id).Count == 0);
        Assert.True(depot.SupprimerAdherent(a.Id).Reussi);
        Assert.Null(depot.TrouverAdherent(a.Id));
        Assert.Null(depot.CotisationDe(a.Id, depot.SaisonCourante));
    }

    [Fact]
    public void Encaissement_puis_annulation()
    {
        var depot = NouveauDepot();
        var a = depot.Adherents.First(x => depot.StatutDe(x.Id, depot.SaisonCourante) == StatutCotisation.APayer);
        var avant = depot.StatistiquesDe(depot.SaisonCourante);

        depot.MarquerCotisationPayee(a.Id, depot.SaisonCourante, 15m, ModePaiement.Especes, depot.AujourdHui);
        Assert.Equal(avant.Payees + 1, depot.StatistiquesDe(depot.SaisonCourante).Payees);

        depot.AnnulerPaiement(a.Id, depot.SaisonCourante);
        Assert.Equal(avant.Payees, depot.StatistiquesDe(depot.SaisonCourante).Payees);
    }

    [Fact]
    public void Bilan_financier_selon_les_tarifs()
    {
        var depot = NouveauDepot();
        var saison = depot.SaisonCourante;
        var attendu = depot.Adherents.Sum(a => a.EstEnfant ? 15m : 25m);
        var avant = depot.BilanDe(saison);
        Assert.Equal(attendu, avant.Prevu);

        var a = depot.Adherents.First(x => !x.EstEnfant && depot.StatutDe(x.Id, saison) == StatutCotisation.APayer);
        depot.MarquerCotisationPayee(a.Id, saison, 25m, ModePaiement.Especes, depot.AujourdHui);
        var apres = depot.BilanDe(saison);
        Assert.Equal(avant.Encaisse + 25m, apres.Encaisse);
        Assert.Equal(avant.ResteAEncaisser - 25m, apres.ResteAEncaisser);
        Assert.Equal(avant.Especes + 25m, apres.Especes);
    }
}
