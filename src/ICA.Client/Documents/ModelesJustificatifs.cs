using System.Globalization;
using ICA.Domain;

namespace ICA.Client.Documents;

/// <summary>Mise en page des justificatifs de dons et du bilan annuel.</summary>
public static class ModelesJustificatifs
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private const float Marge = 50f;
    private const float Largeur = DocumentPdf.LargeurPage - 2 * Marge;

    public const string MentionAttestation =
        "Cette attestation confirme la réception d'un don. Elle ne constitue pas un reçu fiscal ouvrant droit à une réduction d'impôt.";

    public static string Euros(decimal m) => m.ToString("N2", Fr) + " €";
    private static string Date(DateOnly d) => d.ToString("d MMMM yyyy", Fr);

    /// <summary>Fige le contenu à partir des fiches actuelles.</summary>
    public static ContenuJustificatif Construire(IdentiteAssociation association, DonateurEntreprise e, IReadOnlyList<Don> dons, string? numeroRemplace)
    {
        var tries = dons.OrderBy(d => d.Date).ToList();
        var nature = tries.Where(d => d.Type != TypeDon.Financier).ToList();
        return new ContenuJustificatif
        {
            Association = association.Copie(),
            EntrepriseNom = e.RaisonSociale,
            EntrepriseForme = e.FormeJuridique,
            EntrepriseSiren = e.Siren,
            EntrepriseAdresse = e.Adresse,
            EntrepriseVille = e.LigneVille,
            TotalVersements = tries.Where(d => d.Type == TypeDon.Financier).Sum(d => d.Montant ?? 0),
            TotalNature = nature.Sum(d => d.Montant ?? 0),
            Modes = tries.Where(d => d.Mode is not null).Select(d => d.Mode!.Value).Distinct().OrderBy(m => m).ToList(),
            DescriptionNature = string.Join("\n", nature.Select(d => $"• {d.Date:dd/MM/yyyy} : {RegleDon.Description(d)}")),
            Salaries = string.Join("\n", nature.Where(d => !string.IsNullOrWhiteSpace(d.SalariesMisADisposition)).Select(d => $"• {d.SalariesMisADisposition!.Trim()}")),
            DateDebut = tries.First().Date,
            DateFin = tries.Last().Date,
            Lignes = tries.Select(d => new LigneDonJustificatif
            {
                Date = d.Date, Type = d.Type, Montant = d.Montant ?? 0,
                Libelle = d.Type == TypeDon.Financier
                    ? $"Versement{(d.Mode is { } m ? " — " + RegleDon.Libelle(m).ToLowerInvariant() : "")}{(string.IsNullOrWhiteSpace(d.Description) ? "" : " — " + d.Description)}"
                    : RegleDon.Description(d),
            }).ToList(),
            NumeroRemplace = numeroRemplace,
        };
    }

    public static byte[] Generer(JustificatifDon j, byte[]? logo, byte[]? signature = null) =>
        j.Type == TypeJustificatif.RecuFiscal ? RecuFiscal(j, signature) : Attestation(j, logo, signature);

    private static string Periode(ContenuJustificatif c) =>
        c.DateDebut == c.DateFin ? $"le {Date(c.DateDebut)}" : $"du {Date(c.DateDebut)} au {Date(c.DateFin)}";

    private static string IdentifiantAsso(IdentiteAssociation a) =>
        string.Join(" — ", new[]
        {
            string.IsNullOrWhiteSpace(a.Siren) ? null : "SIREN " + Siren.Afficher(a.Siren),
            string.IsNullOrWhiteSpace(a.Rna) ? null : "RNA " + a.Rna,
        }.Where(s => s is not null));

    private static string AdresseAsso(IdentiteAssociation a) =>
        string.Join(", ", new[] { a.Adresse, string.Join(" ", new[] { a.CodePostal, a.Ville?.ToUpperInvariant() }.Where(s => !string.IsNullOrWhiteSpace(s))) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    // =====================================================================
    //  Attestation simple
    // =====================================================================

    public static byte[] Attestation(JustificatifDon j, byte[]? logo, byte[]? signature = null)
    {
        var c = j.Contenu ?? throw new InvalidOperationException("Contenu manquant.");
        var a = c.Association;
        var pdf = new DocumentPdf { Titre = $"Attestation de don {j.Numero}" };
        pdf.NouvellePage();

        var y = EnTeteLettre(pdf, a, logo);

        // Titre
        pdf.Texte(DocumentPdf.LargeurPage / 2, 168, "ATTESTATION DE DON", 20, true, alignement: Alignement.Centre);
        pdf.Texte(DocumentPdf.LargeurPage / 2, 188, $"N° {j.Numero}", 12, true, Couleur.Bordeaux, Alignement.Centre);
        y = 206;
        if (c.NumeroRemplace is { } ancien)
        {
            pdf.Texte(DocumentPdf.LargeurPage / 2, y, $"Annule et remplace l'attestation n° {ancien}", 10, couleur: Couleur.Gris, alignement: Alignement.Centre);
            y += 16;
        }

        // Donateur
        y += 10;
        y = Bloc(pdf, y, "Entreprise donatrice",
        [
            c.EntrepriseNom + (string.IsNullOrWhiteSpace(c.EntrepriseForme) ? "" : $" ({c.EntrepriseForme})"),
            string.IsNullOrWhiteSpace(c.EntrepriseSiren) ? "" : "SIREN " + Siren.Afficher(c.EntrepriseSiren),
            c.EntrepriseAdresse ?? "",
            c.EntrepriseVille ?? "",
        ]);

        // Corps
        y += 18;
        y = pdf.Paragraphe(Marge, y, Largeur,
            $"L'association {a.Denomination} atteste avoir reçu de l'entreprise ci-dessus, {Periode(c)}, le ou les dons suivants :", 11);
        y = Tableau(pdf, y + 6, c);

        y += 10;
        if (c.TotalVersements > 0) { pdf.Texte(Marge, y, "Total des versements :", 11, true); pdf.Texte(Marge + Largeur, y, Euros(c.TotalVersements), 11, true, alignement: Alignement.Droite); y += 16; }
        if (c.TotalNature > 0)
        {
            pdf.Texte(Marge, y, "Valeur des dons en nature ou de compétences* :", 11, true);
            pdf.Texte(Marge + Largeur, y, Euros(c.TotalNature), 11, true, alignement: Alignement.Droite);
            y += 14;
            pdf.Texte(Marge, y, "* valeur communiquée par l'entreprise donatrice.", 8.5f, couleur: Couleur.Gris);
            y += 14;
        }
        if (!string.IsNullOrWhiteSpace(c.Salaries))
            y = pdf.Paragraphe(Marge, y + 4, Largeur, "Salariés mis à disposition :\n" + c.Salaries, 10);

        // Mention obligatoire
        y += 14;
        var lignesMention = DocumentPdf.Couper(MentionAttestation, Largeur - 24, 10.5f, true);
        var hMention = lignesMention.Count * 14 + 16;
        pdf.Cadre(Marge, y, Largeur, hMention, Couleur.BordeauxClair, Couleur.Bordeaux, 0.8f);
        var ym = y + 20;
        foreach (var l in lignesMention) { pdf.Texte(Marge + 12, ym, l, 10.5f, true, Couleur.Bordeaux); ym += 14; }
        y += hMention;

        Signature(pdf, Math.Max(y + 30, 640), a, j.DateEmission ?? DateOnly.FromDateTime(DateTime.Today), "Pour l'association", signature);

        PiedDePage(pdf, $"Attestation n° {j.Numero} — remerciements de {a.Denomination} pour votre soutien.");
        return pdf.Octets();
    }

    // =====================================================================
    //  Reçu fiscal — Cerfa 16216*03, formulaire 2041-MEC-SD (article 238 bis du CGI)
    //  Rubriques et libellés dans l'ordre du formulaire officiel ; aucune n'est supprimée.
    // =====================================================================

    public static byte[] RecuFiscal(JustificatifDon j, byte[]? signature, bool specimen = false)
    {
        var c = j.Contenu ?? throw new InvalidOperationException("Contenu manquant.");
        var a = c.Association;
        var pdf = new DocumentPdf { Titre = $"Reçu {j.Numero}", Filigrane = specimen ? "SPÉCIMEN — NON VALABLE" : null };
        pdf.NouvellePage();

        pdf.Texte(Marge, 44, $"N° {CategoriesFiscales.ModeleEnVigueur}", 9, true);
        pdf.Texte(Marge, 56, "2041-MEC-SD", 9, true);
        pdf.Texte(Marge + Largeur, 44, "Numéro d'ordre du reçu", 9, couleur: Couleur.Gris, alignement: Alignement.Droite);
        pdf.Texte(Marge + Largeur, 58, j.Numero, 11, true, alignement: Alignement.Droite);
        var y = pdf.Paragraphe(Marge + 90, 46, Largeur - 220,
            "Reçu des dons et versements effectués par les entreprises au titre de l'article 238 bis du code général des impôts", 12, true, interligne: 1.2f);
        y = Math.Max(y, 70);
        if (c.NumeroRemplace is { } ancien)
        {
            pdf.Texte(Marge, y + 4, $"Annule et remplace le reçu n° {ancien}", 9.5f, couleur: Couleur.Gris);
            y += 14;
        }

        // ---- Organisme bénéficiaire ----
        y = Section(pdf, y + 8, "Organisme bénéficiaire des dons et versements");
        y = Champ(pdf, y, "Dénomination de l'organisme", a.Denomination);
        y = Champ(pdf, y, "Numéro SIREN ou RNA (1)", IdentifiantAsso(a));
        y = Champ(pdf, y, "N° et rue", a.Adresse ?? "");
        y = Champ(pdf, y, "Code postal — Commune", string.Join(" ", new[] { a.CodePostal, a.Ville?.ToUpperInvariant() }.Where(s => !string.IsNullOrWhiteSpace(s))));
        y = Champ(pdf, y, "Pays", "France");
        y = Champ(pdf, y, "Objet (2)", a.Objet);

        var choisie = CategoriesFiscales.Trouver(a.Categorie);
        pdf.Texte(Marge, y, "Cochez la case concernée (2) :", 9, true);
        y += 13;
        void Ligne(string libelle, bool coche, float retrait)
        {
            var lignes = DocumentPdf.Couper(libelle, Largeur - retrait - 16, 8);
            y = SautSiBesoin(pdf, y, lignes.Count * 10 + 4);
            pdf.Case(Marge + retrait, y - 7.5f, coche);
            foreach (var l in lignes) { pdf.Texte(Marge + retrait + 14, y, l, 8); y += 10; }
            y += 1.5f;
        }
        Ligne(CategoriesFiscales.LibelleOeuvreInteretGeneral, choisie?.SousCategorie == true, 0);
        foreach (var cat in CategoriesFiscales.Toutes)
        {
            var coche = choisie?.Code == cat.Code;
            var libelle = cat.AvecPrecision ? $"Autres (précisez : {(coche ? a.CategoriePrecision : "")})" : cat.Libelle;
            Ligne(libelle, coche, cat.SousCategorie ? 16 : 0);
        }

        // ---- Entreprise donatrice ----
        y = Section(pdf, y + 6, "Entreprise donatrice");
        y = Champ(pdf, y, "Dénomination de l'entreprise", c.EntrepriseNom);
        y = Champ(pdf, y, "Forme juridique", c.EntrepriseForme ?? "");
        y = Champ(pdf, y, "Numéro SIREN", Siren.Afficher(c.EntrepriseSiren));
        y = Champ(pdf, y, "N° et rue", c.EntrepriseAdresse ?? "");
        y = Champ(pdf, y, "Code postal — Commune", c.EntrepriseVille ?? "");

        // ---- Dons et versements ----
        y = Section(pdf, y + 6, "Dons et versements effectués par l'entreprise");
        y = Phrase(pdf, y, "L'organisme bénéficiaire reconnaît avoir reçu, au titre de la réduction d'impôt prévue à l'article 238 bis du code général des impôts, " +
                           "des dons en nature pour une valeur en euros égale à (5) (6) :", Euros(c.TotalNature));
        y = Champ(pdf, y, "Indiquez la valeur totale des dons en nature en toutes lettres", MontantEnLettres.Euros(c.TotalNature));
        y = Champ(pdf, y, "Description exhaustive des biens et prestations reçus et acceptés (nature et quantité) et détail des salariés mis à disposition (7)",
            string.Join("\n", new[] { c.DescriptionNature, string.IsNullOrWhiteSpace(c.Salaries) ? null : "Salariés mis à disposition :\n" + c.Salaries }
                .Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } desc ? desc : "Néant");

        y = Phrase(pdf, y + 4, "L'organisme bénéficiaire reconnaît avoir reçu, au titre de la réduction d'impôt prévue à l'article 238 bis du code général des impôts, " +
                               "des versements pour une valeur totale égale à :", Euros(c.TotalVersements));
        y = Champ(pdf, y, "Indiquez le total des versements en toutes lettres", MontantEnLettres.Euros(c.TotalVersements));

        y = SautSiBesoin(pdf, y, 40);
        pdf.Texte(Marge, y + 4, "Forme des versements (8) :", 9, true);
        var x = Marge + 125;
        foreach (var (libelle, modes) in new (string, ModeVersementDon[])[]
                 {
                     ("Remise d'espèces", [ModeVersementDon.Especes]),
                     ("Chèque", [ModeVersementDon.Cheque]),
                     ("Virement, prélèvement ou carte bancaire", [ModeVersementDon.Virement, ModeVersementDon.Carte]),
                     ("Autre", [ModeVersementDon.Autre]),
                 })
        {
            pdf.Case(x, y - 3.5f, c.Modes.Any(modes.Contains));
            pdf.Texte(x + 13, y + 4, libelle, 8);
            x += DocumentPdf.Largeur(libelle, 8) + 24;
        }
        y += 22;

        y = SautSiBesoin(pdf, y, 60);
        pdf.Cadre(Marge, y, Largeur, 38, Couleur.GrisClair);
        pdf.Texte(Marge + 10, y + 15, "Montant total des dons et versements reçus par l'organisme :", 10, true);
        pdf.Texte(Marge + Largeur - 10, y + 15, Euros(c.Total), 11, true, alignement: Alignement.Droite);
        pdf.Texte(Marge + 10, y + 30, "Soit en toutes lettres : " + MontantEnLettres.Euros(c.Total), 9);
        y += 52;
        y = Champ(pdf, y, "Date ou période au cours de laquelle les dons et versements ont été effectués (9)", c.DateDebut == c.DateFin
            ? c.DateDebut.ToString("dd/MM/yyyy", Fr)
            : $"du {c.DateDebut:dd/MM/yyyy} au {c.DateFin:dd/MM/yyyy}");

        // ---- Date et signature ----
        var date = j.DateEmission ?? DateOnly.FromDateTime(DateTime.Today);
        y = SautSiBesoin(pdf, y, 150);
        pdf.Texte(Marge, y + 6, "Date et signature", 10, true);
        pdf.Texte(Marge, y + 22, $"Le {date:dd / MM / yyyy}", 10.5f);
        Signature(pdf, y, a, date, "Le représentant de l'organisme bénéficiaire", signature, afficherFaitA: false);
        y += 130;

        // ---- Notes du formulaire ----
        y = SautSiBesoin(pdf, y, 90);
        foreach (var note in NotesRecu)
            y = pdf.Paragraphe(Marge, y, Largeur, note, 7.5f, couleur: Couleur.Gris, interligne: 1.25f);

        // Détail des dons (annexe datée et signée, cf. note 7 ; ne remplace aucune rubrique)
        if (c.Lignes.Count > 1)
        {
            pdf.NouvellePage();
            var ya = Section(pdf, 60, $"Annexe — détail des dons et versements du reçu n° {j.Numero}");
            ya = Tableau(pdf, ya + 4, c);
            Signature(pdf, ya + 20, a, date, "Le représentant de l'organisme bénéficiaire", signature);
        }
        PiedDePage(pdf, $"Reçu n° {j.Numero} — {a.Denomination} — Cerfa n° {CategoriesFiscales.ModeleEnVigueur}");
        return pdf.Octets();
    }

    /// <summary>Notes du formulaire, reprises en substance (numéros identiques au Cerfa).</summary>
    private static readonly string[] NotesRecu =
    [
        "(1) Pour les associations d'Alsace-Moselle, indiquer le numéro d'inscription au registre du tribunal judiciaire.",
        "(2) Cocher la catégorie de l'organisme et préciser l'objet si nécessaire.",
        "(5) L'organisme bénéficiaire reporte la valeur déclarée par l'entreprise donatrice.",
        "(6) Les dons en nature refusés par l'organisme n'ouvrent pas droit à la réduction d'impôt.",
        "(7) La description peut figurer sur une annexe datée et signée jointe au reçu.",
        "(8) L'organisme bénéficiaire peut cocher plusieurs cases.",
        "(9) Un reçu peut regrouper plusieurs dons ou versements effectués au cours d'une période qui ne peut pas chevaucher deux exercices de l'entreprise donatrice.",
    ];

    /// <summary>
    /// Reçu fictif, filigrané « SPÉCIMEN — NON VALABLE », non numéroté et non enregistré :
    /// sert à vérifier le rendu rubrique par rubrique contre le Cerfa officiel.
    /// </summary>
    public static byte[] Specimen(IdentiteAssociation association, byte[]? signature, DateOnly aujourdhui)
    {
        var entreprise = new DonateurEntreprise
        {
            RaisonSociale = "ENTREPRISE EXEMPLE (fictive)", FormeJuridique = "SARL", Siren = "123456709",
            Adresse = "1 rue de l'Exemple", CodePostal = "00000", Ville = "Exempleville",
        };
        var dons = new List<Don>
        {
            new() { EntrepriseId = entreprise.Id, Date = aujourdhui, Type = TypeDon.Financier, Montant = 150, Mode = ModeVersementDon.Virement, Description = "Soutien aux ateliers" },
            new() { EntrepriseId = entreprise.Id, Date = aujourdhui, Type = TypeDon.Nature, Montant = 280.50m, Description = "Chaises pliantes", Quantites = "10 chaises" },
        };
        var j = new JustificatifDon
        {
            Numero = $"ICA-DON-{aujourdhui.Year}-SPECIMEN", Type = TypeJustificatif.RecuFiscal, Annee = aujourdhui.Year, DateEmission = aujourdhui,
            Contenu = Construire(association, entreprise, dons, null),
        };
        return RecuFiscal(j, signature, specimen: true);
    }

    /// <summary>
    /// Lettre de remerciement sur papier à en-tête de L'ICA, jointe à l'envoi d'un reçu fiscal.
    /// Elle n'est pas un justificatif et ne remplace pas le reçu officiel.
    /// </summary>
    public static byte[] LettreAccompagnement(JustificatifDon j, byte[]? logo, byte[]? signature)
    {
        var c = j.Contenu ?? throw new InvalidOperationException("Contenu manquant.");
        var a = c.Association;
        var pdf = new DocumentPdf { Titre = $"Lettre de remerciement — {j.Numero}" };
        pdf.NouvellePage();
        var y = EnTeteLettre(pdf, a, logo);

        var xDest = Marge + Largeur / 2 + 20;
        var yd = y + 30;
        foreach (var l in new[] { c.EntrepriseNom, c.EntrepriseAdresse, c.EntrepriseVille }.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            pdf.Texte(xDest, yd, l!, 11, l == c.EntrepriseNom);
            yd += 15;
        }
        var date = j.DateEmission ?? DateOnly.FromDateTime(DateTime.Today);
        pdf.Texte(xDest, yd + 20, $"{(string.IsNullOrWhiteSpace(a.LieuSignature) ? a.Ville : a.LieuSignature)}, le {date.ToString("d MMMM yyyy", Fr)}", 10.5f);

        y = yd + 70;
        pdf.Texte(Marge, y, $"Objet : reçu fiscal n° {j.Numero}", 11, true);
        y += 30;
        y = pdf.Paragraphe(Marge, y, Largeur,
            "Madame, Monsieur,\n\n" +
            $"Au nom de l'association {a.Denomination}, nous vous remercions chaleureusement pour votre soutien.\n\n" +
            $"Vous trouverez ci-joint le reçu des dons et versements effectués par les entreprises (formulaire 2041-MEC-SD, Cerfa n° {CategoriesFiscales.ModeleEnVigueur}), " +
            $"n° {j.Numero}, pour un montant total de {Euros(c.Total)}, au titre de la période {(c.DateDebut == c.DateFin ? $"du {c.DateDebut:dd/MM/yyyy}" : $"du {c.DateDebut:dd/MM/yyyy} au {c.DateFin:dd/MM/yyyy}")}.\n\n" +
            "Grâce à votre contribution, nous pouvons poursuivre nos actions et activités associatives.\n\n" +
            "Nous vous prions d'agréer, Madame, Monsieur, l'expression de nos salutations distinguées.", 11, interligne: 1.4f);

        Signature(pdf, y + 20, a, date, "Pour l'association", signature, afficherFaitA: false);
        PiedDePage(pdf, $"{a.Denomination} — {AdresseAsso(a)}");
        return pdf.Octets();
    }

    /// <summary>En-tête du papier de l'association : logo, nom, coordonnées. Renvoie le y sous le filet.</summary>
    private static float EnTeteLettre(DocumentPdf pdf, IdentiteAssociation a, byte[]? logo)
    {
        var xTexte = Marge;
        if (logo is not null)
        {
            try { pdf.Image(logo, Marge, 40, 72, 72); xTexte = Marge + 88; }
            catch (InvalidDataException) { /* logo illisible : on continue sans */ }
        }
        pdf.Texte(xTexte, 58, a.Denomination, 20, true, Couleur.Bordeaux);
        var y = 74f;
        foreach (var ligne in new[] { AdresseAsso(a), IdentifiantAsso(a), string.Join(" — ", new[] { a.Email, a.Telephone }.Where(s => !string.IsNullOrWhiteSpace(s))) })
        {
            if (string.IsNullOrWhiteSpace(ligne)) continue;
            pdf.Texte(xTexte, y, ligne, 9.5f, couleur: Couleur.Gris);
            y += 13;
        }
        pdf.Trait(Marge, 128, Marge + Largeur, 128, 1.2f, Couleur.Bordeaux);
        return 128;
    }

    /// <summary>Phrase de certification suivie du montant en gras.</summary>
    private static float Phrase(DocumentPdf pdf, float y, string texte, string montant)
    {
        y = SautSiBesoin(pdf, y, 50);
        y = pdf.Paragraphe(Marge, y, Largeur - 110, texte, 9.5f, interligne: 1.3f);
        pdf.Texte(Marge + Largeur, y - 12, montant, 11, true, alignement: Alignement.Droite);
        pdf.Trait(Marge, y - 4, Marge + Largeur, y - 4, 0.3f, Couleur.GrisClair);
        return y + 8;
    }

    // =====================================================================
    //  Bilan annuel
    // =====================================================================

    public sealed record LigneBilan(string Entreprise, DateOnly Date, string Type, decimal Montant, string Document);

    public static byte[] BilanAnnuel(ICA.Client.Services.BilanDons b, IReadOnlyList<LigneBilan> lignes, IdentiteAssociation a, byte[]? logo, DateOnly aujourdhui)
    {
        var pdf = new DocumentPdf { Titre = $"Bilan des dons {b.Annee}" };
        pdf.NouvellePage();
        if (logo is not null) { try { pdf.Image(logo, Marge, 36, 54, 54); } catch (InvalidDataException) { } }
        pdf.Texte(Marge + 70, 60, $"Bilan des dons {b.Annee}", 20, true, Couleur.Bordeaux);
        pdf.Texte(Marge + 70, 78, $"{a.Denomination} — édité le {aujourdhui:dd/MM/yyyy}", 10, couleur: Couleur.Gris);
        pdf.Trait(Marge, 102, Marge + Largeur, 102, 1, Couleur.Bordeaux);

        var y = 128f;
        foreach (var (libelle, valeur) in new[]
                 {
                     ("Total des dons financiers reçus", Euros(b.TotalFinancier)),
                     ("Total déclaré des dons en nature", Euros(b.TotalNature)),
                     ("Total déclaré du mécénat de compétences", Euros(b.TotalCompetences)),
                     ("Nombre d'entreprises donatrices", b.NombreEntreprises.ToString(Fr)),
                     ("Nombre de dons enregistrés", b.NombreDons.ToString(Fr)),
                     ("Attestations simples émises", b.AttestationsEmises.ToString(Fr)),
                     ("Reçus fiscaux émis", b.RecusFiscauxEmis.ToString(Fr)),
                     ("Montant cumulé des dons ayant donné lieu à reçu fiscal", Euros(b.MontantRecusFiscaux)),
                 })
        {
            pdf.Texte(Marge, y, libelle, 11);
            pdf.Texte(Marge + Largeur, y, valeur, 11, true, alignement: Alignement.Droite);
            pdf.Trait(Marge, y + 6, Marge + Largeur, y + 6, 0.3f, Couleur.GrisClair);
            y += 20;
        }

        y = Section(pdf, y + 10, "Déclaration annuelle des dons (article 222 bis du CGI)");
        y = pdf.Paragraphe(Marge, y, Largeur,
            $"Montant total des dons ayant donné lieu à reçu fiscal en {b.Annee} : {Euros(b.MontantRecusFiscaux)} — nombre de reçus : {b.RecusFiscauxEmis}.\n" +
            "Ces chiffres préparent la déclaration ; elle reste à effectuer par l'association (formulaire 2065-bis-SD case K si l'association dépose " +
            "une déclaration de résultat, sinon sur demarches-simplifiees.fr), dans les trois mois suivant la clôture de l'exercice " +
            "(clôture au 31 décembre : au plus tard le deuxième jour ouvré suivant le 1er mai). Rien n'est à déclarer si aucun reçu fiscal n'a été émis.",
            9.5f, couleur: Couleur.Gris);

        y = Section(pdf, y + 10, "Liste des dons");
        y = EnTeteListe(pdf, y);
        foreach (var l in lignes)
        {
            if (y > DocumentPdf.HauteurPage - 70) { pdf.NouvellePage(); y = EnTeteListe(pdf, 60); }
            pdf.Texte(Marge, y, Tronquer(l.Entreprise, 145, 9), 9);
            pdf.Texte(Marge + 150, y, l.Date.ToString("dd/MM/yyyy", Fr), 9);
            pdf.Texte(Marge + 205, y, l.Type, 9);
            pdf.Texte(Marge + 330, y, Euros(l.Montant), 9, alignement: Alignement.Droite);
            pdf.Texte(Marge + 340, y, Tronquer(l.Document, Largeur - 340, 9), 9);
            y += 15;
        }
        PiedDePage(pdf, $"Bilan des dons {b.Annee} — {a.Denomination}");
        return pdf.Octets();
    }

    private static float EnTeteListe(DocumentPdf pdf, float y)
    {
        pdf.Cadre(Marge, y - 11, Largeur, 16, Couleur.GrisClair);
        foreach (var (x, t, droite) in new[] { (0f, "Entreprise", false), (150f, "Date", false), (205f, "Type", false), (330f, "Montant", true), (340f, "Document", false) })
            pdf.Texte(Marge + x, y, t, 9, true, alignement: droite ? Alignement.Droite : Alignement.Gauche);
        return y + 18;
    }

    // =====================================================================
    //  Éléments communs
    // =====================================================================

    private static float Section(DocumentPdf pdf, float y, string titre)
    {
        y = SautSiBesoin(pdf, y, 60);
        pdf.Cadre(Marge, y, Largeur, 18, Couleur.Bordeaux);
        pdf.Texte(Marge + 8, y + 12.5f, titre, 10.5f, true, new Couleur(1, 1, 1));
        return y + 32;
    }

    /// <summary>Libellé à gauche, valeur à droite (multi-lignes).</summary>
    private static float Champ(DocumentPdf pdf, float y, string libelle, string valeur)
    {
        const float colonne = 170;
        var lignesLibelle = DocumentPdf.Couper(libelle, colonne - 10, 9, true);
        var lignesValeur = DocumentPdf.Couper(string.IsNullOrWhiteSpace(valeur) ? "—" : valeur, Largeur - colonne, 10);
        var hauteur = Math.Max(lignesLibelle.Count * 11.5f, lignesValeur.Count * 13f);
        y = SautSiBesoin(pdf, y, hauteur + 8);
        var yl = y;
        foreach (var l in lignesLibelle) { pdf.Texte(Marge, yl, l, 9, true, Couleur.Gris); yl += 11.5f; }
        var yv = y;
        foreach (var l in lignesValeur) { pdf.Texte(Marge + colonne, yv, l, 10); yv += 13; }
        y += hauteur + 2;
        pdf.Trait(Marge, y - 6, Marge + Largeur, y - 6, 0.3f, Couleur.GrisClair);
        return y + 6;
    }

    private static float Bloc(DocumentPdf pdf, float y, string titre, IReadOnlyList<string> lignes)
    {
        var utiles = lignes.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var h = 30 + utiles.Count * 14;
        pdf.Cadre(Marge, y, Largeur, h, Couleur.GrisClair);
        pdf.Texte(Marge + 12, y + 18, titre.ToUpperInvariant(), 8.5f, true, Couleur.Gris);
        var yl = y + 34;
        for (var i = 0; i < utiles.Count; i++) { pdf.Texte(Marge + 12, yl, utiles[i], i == 0 ? 12 : 10.5f, i == 0); yl += 14; }
        return y + h;
    }

    private static float Tableau(DocumentPdf pdf, float y, ContenuJustificatif c)
    {
        pdf.Cadre(Marge, y, Largeur, 18, Couleur.GrisClair);
        pdf.Texte(Marge + 8, y + 12.5f, "Date", 9, true);
        pdf.Texte(Marge + 80, y + 12.5f, "Nature", 9, true);
        pdf.Texte(Marge + 165, y + 12.5f, "Description", 9, true);
        pdf.Texte(Marge + Largeur - 8, y + 12.5f, "Montant / valeur", 9, true, alignement: Alignement.Droite);
        y += 32;
        foreach (var l in c.Lignes)
        {
            var lignes = DocumentPdf.Couper(l.Libelle, Largeur - 165 - 100, 9.5f);
            y = SautSiBesoin(pdf, y, lignes.Count * 12 + 10);
            pdf.Texte(Marge + 8, y, l.Date.ToString("dd/MM/yyyy", Fr), 9.5f);
            pdf.Texte(Marge + 80, y, RegleDon.Libelle(l.Type), 9.5f);
            pdf.Texte(Marge + Largeur - 8, y, Euros(l.Montant), 9.5f, true, alignement: Alignement.Droite);
            var yl = y;
            foreach (var t in lignes) { pdf.Texte(Marge + 165, yl, t, 9.5f); yl += 12; }
            y = yl + 4;
            pdf.Trait(Marge, y - 9, Marge + Largeur, y - 9, 0.3f, Couleur.GrisClair);
        }
        return y;
    }

    private static void Signature(DocumentPdf pdf, float y, IdentiteAssociation a, DateOnly date, string qualiteParDefaut,
        byte[]? image = null, bool afficherFaitA = true)
    {
        y = SautSiBesoin(pdf, y, 120);
        var x = Marge + Largeur / 2 + 20;
        var l = Largeur / 2 - 20;
        if (afficherFaitA)
            pdf.Texte(x, y, $"Fait à {(string.IsNullOrWhiteSpace(a.LieuSignature) ? a.Ville ?? "…………" : a.LieuSignature)}, le {date.ToString("d MMMM yyyy", Fr)}", 10.5f);
        pdf.Texte(x, y + 18, string.IsNullOrWhiteSpace(a.SignataireNom) ? qualiteParDefaut : a.SignataireNom!, 10.5f, true);
        if (!string.IsNullOrWhiteSpace(a.SignataireQualite)) pdf.Texte(x, y + 32, a.SignataireQualite!, 10, couleur: Couleur.Gris);
        pdf.Texte(x, y + 52, "Signature :", 9, couleur: Couleur.Gris);
        pdf.Cadre(x, y + 58, l, 60, trait: Couleur.GrisClair);
        if (image is null) return;
        try
        {
            var (li, hi, _) = DocumentPdf.LireJpeg(image);
            var echelle = Math.Min((l - 12) / li, 52f / hi);
            var w = li * echelle;
            var h = hi * echelle;
            pdf.Image(image, x + (l - w) / 2, y + 62 + (52 - h) / 2, w, h);
        }
        catch (InvalidDataException) { /* image illisible : cadre laissé vide pour signer à la main */ }
    }

    private static void PiedDePage(DocumentPdf pdf, string texte) => pdf.PiedDePage = texte;

    private static float SautSiBesoin(DocumentPdf pdf, float y, float besoin)
    {
        if (y + besoin <= DocumentPdf.HauteurPage - 60) return y;
        pdf.NouvellePage();
        return 60;
    }

    private static string Tronquer(string s, float largeur, float taille)
    {
        if (DocumentPdf.Largeur(s, taille) <= largeur) return s;
        while (s.Length > 1 && DocumentPdf.Largeur(s + "…", taille) > largeur) s = s[..^1];
        return s + "…";
    }
}
