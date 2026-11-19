using ICA.Client.Donnees;
using ICA.Domain;

namespace ICA.Domain.Tests;

// Données exclusivement fictives (numéros SIREN inventés, valides seulement au sens de la clé de contrôle).
public class SirenTests
{
    [Theory]
    [InlineData("123456709", true)]
    [InlineData("123 456 709", true)]
    [InlineData("123456789", false)]
    [InlineData("12345670", false)]
    [InlineData("12345670A", false)]
    public void Verifie_le_siren(string saisie, bool attendu) => Assert.Equal(attendu, Siren.EstValide(saisie));

    [Fact]
    public void Verifie_le_siret() => Assert.True(Siren.SiretValide("123 456 709 00005"));

    [Fact]
    public void Affiche_par_groupes() => Assert.Equal("123 456 709", Siren.Afficher("123456709"));

    [Fact]
    public void Detecte_les_doublons_par_siren_ou_par_nom()
    {
        var a = new DonateurEntreprise { RaisonSociale = "Boulangerie Exemple", Siren = "123456709" };
        Assert.True(RegleEntreprise.EstDoublon(a, new DonateurEntreprise { RaisonSociale = "Autre nom", Siren = "123456709" }));
        Assert.True(RegleEntreprise.EstDoublon(a, new DonateurEntreprise { RaisonSociale = "BOULANGERIE   exemple" }));
        Assert.False(RegleEntreprise.EstDoublon(a, new DonateurEntreprise { RaisonSociale = "Garage Exemple" }));
    }

    [Fact]
    public void Siret_doit_correspondre_au_siren()
    {
        var e = new DonateurEntreprise { RaisonSociale = "Exemple", Siren = "123456709", Siret = "12345670900005" };
        Assert.Empty(RegleEntreprise.Valider(e));
        e.Siren = "732829320";
        Assert.True(RegleEntreprise.Valider(e).Any(x => x.Champ == "Siret"));
    }
}

public class MontantEnLettresTests
{
    [Theory]
    [InlineData(1, "un euro")]
    [InlineData(21, "vingt et un euros")]
    [InlineData(71, "soixante et onze euros")]
    [InlineData(80, "quatre-vingts euros")]
    [InlineData(81, "quatre-vingt-un euros")]
    [InlineData(91, "quatre-vingt-onze euros")]
    [InlineData(100, "cent euros")]
    [InlineData(150, "cent cinquante euros")]
    [InlineData(200, "deux cents euros")]
    [InlineData(280, "deux cent quatre-vingts euros")]
    [InlineData(1000, "mille euros")]
    [InlineData(1200, "mille deux cents euros")]
    [InlineData(2080, "deux mille quatre-vingts euros")]
    [InlineData(80000, "quatre-vingt mille euros")]
    [InlineData(200000, "deux cent mille euros")]
    [InlineData(1000000, "un million d'euros")]
    public void Ecrit_les_montants(int montant, string attendu) => Assert.Equal(attendu, MontantEnLettres.Euros(montant));

    [Fact]
    public void Ecrit_les_centimes() => Assert.Equal("cent cinquante euros et cinquante centimes", MontantEnLettres.Euros(150.50m));
}

public class JustificatifsTests
{
    [Fact]
    public void Numero_suivant_sans_reutiliser_les_annules()
    {
        string[] existants = ["ICA-DON-2026-0001", "ICA-DON-2026-0002", "ICA-ATT-2026-0007", "ICA-DON-2025-0040"];
        Assert.Equal("ICA-DON-2026-0003", NumerosJustificatif.Suivant(TypeJustificatif.RecuFiscal, 2026, existants));
        Assert.Equal("ICA-ATT-2026-0008", NumerosJustificatif.Suivant(TypeJustificatif.Attestation, 2026, existants));
        Assert.Equal("ICA-DON-2027-0001", NumerosJustificatif.Suivant(TypeJustificatif.RecuFiscal, 2027, existants));
    }

    [Fact]
    public void Deux_ordinateurs_reservent_le_meme_numero_un_seul_le_garde()
    {
        var local = new DonneesIca();
        var distant = new DonneesIca();
        var a = new JustificatifDon { Numero = "ICA-ATT-2026-0001", Statut = StatutJustificatif.Reserve };
        var b = new JustificatifDon { Numero = "ICA-ATT-2026-0001", Statut = StatutJustificatif.Reserve };
        local.Justificatifs.Add(a);
        distant.Justificatifs.Add(b);
        Fusion.Fusionner(local, distant);
        Assert.Equal(1, local.Justificatifs.Count(j => j.Statut == StatutJustificatif.Reserve));
        var garde = local.Justificatifs.Single(j => j.Statut == StatutJustificatif.Reserve);
        Assert.Equal(new[] { a.Id, b.Id }.Min(), garde.Id);
    }

    [Fact]
    public void Un_numero_deja_emis_n_est_jamais_cede()
    {
        var local = new DonneesIca();
        var distant = new DonneesIca();
        var emis = new JustificatifDon { Numero = "ICA-ATT-2026-0001", Statut = StatutJustificatif.Emis };
        var reserve = new JustificatifDon { Numero = "ICA-ATT-2026-0001", Statut = StatutJustificatif.Reserve };
        local.Justificatifs.Add(reserve);
        distant.Justificatifs.Add(emis);
        Fusion.Fusionner(local, distant);
        Assert.Equal(StatutJustificatif.Emis, local.Justificatifs.Single(j => j.Id == emis.Id).Statut);
        Assert.Equal(StatutJustificatif.Annule, local.Justificatifs.Single(j => j.Id == reserve.Id).Statut);
    }

    [Fact]
    public void Validation_d_un_don()
    {
        var d = new Don { EntrepriseId = Guid.NewGuid(), Type = TypeDon.Financier, Montant = 150, Date = new DateOnly(2026, 11, 10) };
        Assert.Contains(RegleDon.Valider(d), e => e.Champ == "Mode");
        d.Mode = ModeVersementDon.Virement;
        Assert.Empty(RegleDon.Valider(d));
        var nature = new Don { EntrepriseId = Guid.NewGuid(), Type = TypeDon.Nature, Date = new DateOnly(2026, 11, 10) };
        Assert.NotEmpty(RegleDon.Valider(nature));
    }
}

public class DepotDonsTests
{
    private sealed class StockageMemoire : ICA.Client.Donnees.IStockage
    {
        public DonneesIca? Donnees { get; private set; }
        public string Emplacement => "mémoire";
        public DonneesIca? Charger() => Donnees;
        public void Enregistrer(DonneesIca donnees) => Donnees = donnees;
    }

    private sealed class JournalMuet : ICA.Client.Journal.IJournal
    {
        public string Dossier => "";
        public void Info(string message) { }
        public void Avertissement(string message) { }
        public void Erreur(string message, Exception? exception = null) { }
        public IReadOnlyList<string> DernieresLignes(int nombre) => [];
    }

    private static ICA.Client.Services.DepotIca Depot() =>
        new(new StockageMemoire(), new JournalMuet(), new ICA.Client.Configuration.ParametresApplication());

    private static (ICA.Client.Services.DepotIca, DonateurEntreprise, Don) AvecUnDon()
    {
        var depot = Depot();
        var e = new DonateurEntreprise { RaisonSociale = "Entreprise Exemple A", Siren = "123 456 709", Email = "contact@exemple.test" };
        Assert.True(depot.EnregistrerEntreprise(e).Reussi);
        var d = new Don { EntrepriseId = e.Id, Date = depot.AujourdHui, Type = TypeDon.Financier, Montant = 150, Mode = ModeVersementDon.Cheque };
        Assert.True(depot.EnregistrerDon(d).Reussi);
        return (depot, e, d);
    }

    [Fact]
    public void Refuse_un_deuxieme_siren_identique()
    {
        var (depot, _, _) = AvecUnDon();
        var r = depot.EnregistrerEntreprise(new DonateurEntreprise { RaisonSociale = "Autre", Siren = "123456709" });
        Assert.False(r.Reussi);
    }

    [Fact]
    public void Entreprise_avec_dons_ne_peut_pas_etre_retiree()
    {
        var (depot, e, _) = AvecUnDon();
        Assert.False(depot.SupprimerEntreprise(e.Id).Reussi);
    }

    [Fact]
    public void Recu_fiscal_bloque_tant_que_l_eligibilite_n_est_pas_confirmee()
    {
        var (depot, e, d) = AvecUnDon();
        Assert.Throws<InvalidOperationException>(() => depot.ReserverJustificatif(TypeJustificatif.RecuFiscal, e.Id, [d.Id], d.Date.Year));
    }

    [Fact]
    public async Task Emission_hors_ligne_numerote_fige_et_conserve_le_pdf()
    {
        var (depot, e, d) = AvecUnDon();
        var dossier = Path.Combine(Path.GetTempPath(), "ica-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ICA.Client.Services.ServiceJustificatifs(depot, dossier, null, new JournalMuet());
            var j = await service.EmettreAsync(TypeJustificatif.Attestation, e, [d]);
            Assert.Equal($"ICA-ATT-{d.Date.Year}-0001", j.Numero);
            Assert.Equal(StatutJustificatif.Emis, j.Statut);
            Assert.Equal(150m, j.Contenu!.TotalVersements);
            var chemin = service.Chemin(j);
            Assert.True(File.Exists(chemin));
            Assert.Equal(j.Empreinte, ICA.Client.Services.ServiceJustificatifs.Empreinte(File.ReadAllBytes(chemin)));
            Assert.Equal(j.Id, depot.JustificatifDe(d.Id)!.Id);

            // Correction : le don change, l'ancien document reste figé, le remplaçant a un nouveau numéro.
            d.Montant = 200;
            Assert.True(depot.EnregistrerDon(d).Reussi);
            Assert.Equal(150m, depot.TrouverJustificatif(j.Id)!.Contenu!.TotalVersements);
            var nouveau = await service.EmettreAsync(TypeJustificatif.Attestation, e, [depot.TrouverDon(d.Id)!], j);
            Assert.Equal($"ICA-ATT-{d.Date.Year}-0002", nouveau.Numero);
            Assert.Equal(StatutJustificatif.Remplace, depot.TrouverJustificatif(j.Id)!.Statut);
            Assert.Equal(200m, nouveau.Contenu!.TotalVersements);

            // Bilan de l'année
            var b = depot.BilanDonsDe(d.Date.Year);
            Assert.Equal(200m, b.TotalFinancier);
            Assert.Equal(1, b.AttestationsEmises);
            Assert.Equal(0, b.RecusFiscauxEmis);
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, true);
        }
    }

    [Fact]
    public void Le_pdf_est_valide()
    {
        var c = new ContenuJustificatif
        {
            EntrepriseNom = "Entreprise Exemple « B »", TotalVersements = 300, DateDebut = new DateOnly(2026, 11, 15), DateFin = new DateOnly(2026, 11, 15),
            Lignes = [new LigneDonJustificatif { Date = new DateOnly(2026, 11, 15), Type = TypeDon.Financier, Montant = 300, Libelle = "Versement — virement" }],
            Modes = [ModeVersementDon.Virement],
        };
        foreach (var type in new[] { TypeJustificatif.Attestation, TypeJustificatif.RecuFiscal })
        {
            var pdf = ICA.Client.Documents.ModelesJustificatifs.Generer(new JustificatifDon { Numero = "ICA-DON-2026-0001", Type = type, Contenu = c, DateEmission = new DateOnly(2026, 11, 19) }, null);
            var texte = System.Text.Encoding.Latin1.GetString(pdf);
            Assert.StartsWith("%PDF-1.4", texte);
            Assert.EndsWith("%%EOF\n", texte);
        }
    }
}

public class RecuFiscalConformiteTests
{
    private static ParametresAssociation Complet() => new()
    {
        Eligibilite = EligibiliteFiscale.Confirmee,
        ReferenceRescrit = "Rescrit fictif n° 0000",
        SignatureJpeg = "AAAA",
        ModeleVerifie = CategoriesFiscales.ModeleEnVigueur,
        Identite = new IdentiteAssociation
        {
            Denomination = "Association Exemple", Rna = "W000000000", Adresse = "1 rue de l'Exemple", CodePostal = "00000", Ville = "Exempleville",
            SignataireNom = "Marie EXEMPLE", SignataireQualite = "Présidente",
        },
    };

    [Fact]
    public void Tout_est_complet() => Assert.Empty(ConditionsRecuFiscal.Association(Complet()));

    [Fact]
    public void Non_verifie_par_defaut_et_bloquant()
    {
        Assert.Equal(EligibiliteFiscale.NonVerifiee, new ParametresAssociation().Eligibilite);
        Assert.Contains(ConditionsRecuFiscal.Association(new ParametresAssociation()), m => m.Contains("éligibilité"));
    }

    [Fact]
    public void Chaque_information_obligatoire_bloque()
    {
        var p = Complet(); p.SignatureJpeg = null;
        Assert.Contains(ConditionsRecuFiscal.Association(p), m => m.Contains("signature"));
        p = Complet(); p.Identite.SignataireQualite = null;
        Assert.Contains(ConditionsRecuFiscal.Association(p), m => m.Contains("signataire"));
        p = Complet(); p.Identite.Rna = null;
        Assert.Contains(ConditionsRecuFiscal.Association(p), m => m.Contains("SIREN ou RNA"));
        p = Complet(); p.Identite.Ville = null;
        Assert.Contains(ConditionsRecuFiscal.Association(p), m => m.Contains("adresse"));
        p = Complet(); p.ModeleVerifie = "16216*01";
        Assert.Contains(ConditionsRecuFiscal.Association(p), m => m.Contains("spécimen"));
        p = Complet(); p.Identite.Categorie = "OIG-AUTRE";
        Assert.Contains(ConditionsRecuFiscal.Association(p), m => m.Contains("Autres"));
    }

    [Fact]
    public void Entreprise_incomplete_bloque()
    {
        var e = new DonateurEntreprise { RaisonSociale = "Exemple" };
        var manques = ConditionsRecuFiscal.Entreprise(e);
        Assert.Contains(manques, m => m.Contains("SIREN"));
        Assert.Contains(manques, m => m.Contains("forme juridique"));
    }

    [Fact]
    public void Anciennes_categories_reconnues()
    {
        Assert.Equal("OIG-ASSO", CategoriesFiscales.Trouver("Œuvre ou organisme d'intérêt général — Association loi 1901")!.Code);
        Assert.Equal("OIG-ASSO", CategoriesFiscales.Trouver(new IdentiteAssociation().Categorie)!.Code);
    }

    [Fact]
    public void Specimen_filigrane()
    {
        var pdf = ICA.Client.Documents.ModelesJustificatifs.Specimen(Complet().Identite, null, new DateOnly(2026, 11, 19));
        var texte = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.Contains("SP\\311CIMEN", texte);
        Assert.Contains("16216*03", texte);
    }
}
