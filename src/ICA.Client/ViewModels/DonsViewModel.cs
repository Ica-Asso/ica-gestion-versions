using System.Collections.ObjectModel;
using System.Globalization;
using ICA.Client.Documents;
using ICA.Client.Mvvm;
using ICA.Domain;

namespace ICA.Client.ViewModels;

public sealed record LigneDonListe(
    string Entreprise, string Date, string Montant, string Type, string Statut, string Document,
    bool DocumentAttention, bool DocumentOk, bool EstAnnule,
    RelayCommand Modifier, RelayCommand Justifier, RelayCommand VoirDocument, bool AJustificatif, bool PeutJustifier);

public sealed record LigneEntrepriseListe(
    string Nom, string Siren, string Contact, string Dons, RelayCommand Modifier, RelayCommand NouveauDon, RelayCommand Retirer);

public sealed record LigneJustificatifListe(
    string Numero, string Type, string Entreprise, string Date, string Montant, string Statut,
    bool EstActif, bool EstEnvoye, bool EstInactif,
    CommandeAsync Voir, CommandeAsync Imprimer, CommandeAsync Telecharger, RelayCommand Envoyer, RelayCommand Remplacer, RelayCommand Annuler, RelayCommand Historique,
    RelayCommand RemisEnMain, bool PeutMarquerRemis);

public sealed record ElementATraiter(string Texte, RelayCommand Action, string Bouton, RelayCommand? ActionSecondaire = null, string? BoutonSecondaire = null)
{
    public bool ASecondaire => ActionSecondaire is not null;
}

/// <summary>Page « Dons & Mécénat ».</summary>
public sealed class DonsViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly ContexteApplication _ctx;
    private int _annee;
    private int _onglet;
    private string _recherche = "";
    private Option<TypeDon?> _filtreType;
    private Option<StatutDon?> _filtreStatut;
    private Option<string> _filtreDocument;
    private Services.BilanDons _bilan;
    private IReadOnlyList<int> _annees = [];

    public DonsViewModel(ContexteApplication ctx)
    {
        _ctx = ctx;
        _annee = ctx.Depot.AujourdHui.Year;
        _filtreType = FiltresType[0];
        _filtreStatut = FiltresStatut[0];
        _filtreDocument = FiltresDocument[0];
        _bilan = ctx.Depot.BilanDonsDe(_annee);

        NouveauDonCommand = new RelayCommand(() => NouveauDon(null));
        NouvelleEntrepriseCommand = new RelayCommand(() => Ouvrir(new EditionEntrepriseViewModel(_ctx, null)));
        BilanPdfCommand = new CommandeAsync(BilanPdfAsync);
        BilanExcelCommand = new CommandeAsync(BilanExcelAsync);
        AssociationCommand = new RelayCommand(() => Ouvrir(new EditionAssociationViewModel(_ctx)));
        _ctx.Depot.Modifie += (_, _) => Recharger();
        Recharger();
    }

    // ---------- En-tête et chiffres ----------

    public IReadOnlyList<int> Annees { get => _annees; private set => SetProperty(ref _annees, value); }
    public int Annee { get => _annee; set { if (SetProperty(ref _annee, value)) Recharger(); } }

    public string TotalFinancier => ModelesJustificatifs.Euros(_bilan.TotalFinancier);
    public string TotalNature => ModelesJustificatifs.Euros(_bilan.TotalNatureEtCompetences);
    public int NombreEntreprises => _bilan.NombreEntreprises;
    public int NombreDons => _bilan.NombreDons;
    public int NombreJustificatifs => _bilan.AttestationsEmises + _bilan.RecusFiscauxEmis;
    public string DetailJustificatifs => $"{_bilan.AttestationsEmises} attestation(s), {_bilan.RecusFiscauxEmis} reçu(s) fiscal(aux)";

    public string StatutFiscal => _ctx.Depot.Association.Eligibilite switch
    {
        EligibiliteFiscale.Confirmee when RecusFiscauxActifs => "Reçus fiscaux : activés (éligibilité confirmée par l'association, spécimen vérifié)",
        EligibiliteFiscale.Confirmee => "Reçus fiscaux bloqués — il manque : " + string.Join(", ", _ctx.Depot.ManquesRecuFiscal),
        EligibiliteFiscale.Desactivee => "Reçus fiscaux : désactivés — attestations simples uniquement",
        _ => "Reçus fiscaux : éligibilité non vérifiée — attestations simples uniquement",
    };
    public bool RecusFiscauxActifs => _ctx.Depot.RecusFiscauxAutorises;
    public bool RecusFiscauxInactifs => !RecusFiscauxActifs;

    public ObservableCollection<ElementATraiter> ATraiter { get; } = [];
    public bool RienATraiter => ATraiter.Count == 0;

    public RelayCommand NouveauDonCommand { get; }
    public RelayCommand NouvelleEntrepriseCommand { get; }
    public CommandeAsync BilanPdfCommand { get; }
    public CommandeAsync BilanExcelCommand { get; }
    public RelayCommand AssociationCommand { get; }

    // ---------- Onglets ----------

    public bool OngletDons { get => _onglet == 0; set { if (value) ChangerOnglet(0); } }
    public bool OngletEntreprises { get => _onglet == 1; set { if (value) ChangerOnglet(1); } }
    public bool OngletJustificatifs { get => _onglet == 2; set { if (value) ChangerOnglet(2); } }

    private void ChangerOnglet(int o)
    {
        _onglet = o;
        OnPropertyChanged(nameof(OngletDons));
        OnPropertyChanged(nameof(OngletEntreprises));
        OnPropertyChanged(nameof(OngletJustificatifs));
    }

    // ---------- Recherche et filtres ----------

    public string Recherche { get => _recherche; set { if (SetProperty(ref _recherche, value)) Recharger(); } }

    public IReadOnlyList<Option<TypeDon?>> FiltresType { get; } =
    [
        new(null, "Tous les types"), new(TypeDon.Financier, "Financier"), new(TypeDon.Nature, "En nature"), new(TypeDon.Competences, "Compétences"),
    ];
    public Option<TypeDon?> FiltreType { get => _filtreType; set { if (SetProperty(ref _filtreType, value)) Recharger(); } }

    public IReadOnlyList<Option<StatutDon?>> FiltresStatut { get; } =
    [
        new(null, "Tous les statuts"), new(StatutDon.EnAttente, "En attente"), new(StatutDon.Recu, "Reçu et vérifié"), new(StatutDon.Annule, "Annulé"),
    ];
    public Option<StatutDon?> FiltreStatut { get => _filtreStatut; set { if (SetProperty(ref _filtreStatut, value)) Recharger(); } }

    public IReadOnlyList<Option<string>> FiltresDocument { get; } =
    [
        new("", "Tous les documents"), new("agenerer", "À générer"), new("attestation", "Attestation simple"),
        new("recu", "Reçu fiscal"), new("nonenvoye", "Non envoyé"), new("envoye", "Envoyé"),
    ];
    public Option<string> FiltreDocument { get => _filtreDocument; set { if (SetProperty(ref _filtreDocument, value)) Recharger(); } }

    public ObservableCollection<LigneDonListe> LignesDons { get; } = [];
    public ObservableCollection<LigneEntrepriseListe> LignesEntreprises { get; } = [];
    public ObservableCollection<LigneJustificatifListe> LignesJustificatifs { get; } = [];
    public bool AucunDon => LignesDons.Count == 0;
    public bool AucuneEntreprise => LignesEntreprises.Count == 0;
    public bool AucunJustificatif => LignesJustificatifs.Count == 0;

    // ---------- Chargement ----------

    private bool Correspond(params string?[] champs)
    {
        if (string.IsNullOrWhiteSpace(_recherche)) return true;
        var mots = _recherche.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var texte = string.Join(" ", champs.Where(c => c is not null));
        return mots.All(m => texte.Contains(m, StringComparison.CurrentCultureIgnoreCase)
                             || texte.Replace(" ", "").Contains(m.Replace(",", "."), StringComparison.CurrentCultureIgnoreCase));
    }

    private void Recharger()
    {
        var depot = _ctx.Depot;
        // Remplacer la liste seulement si elle change (sinon la liste déroulante perd l'année choisie).
        var annees = depot.AnneesDons;
        if (!annees.SequenceEqual(_annees)) Annees = annees;
        _bilan = depot.BilanDonsDe(_annee);
        foreach (var n in new[] { nameof(TotalFinancier), nameof(TotalNature), nameof(NombreEntreprises), nameof(NombreDons), nameof(NombreJustificatifs),
                     nameof(DetailJustificatifs), nameof(StatutFiscal), nameof(RecusFiscauxActifs), nameof(RecusFiscauxInactifs) })
            OnPropertyChanged(n);

        string NomEntreprise(Guid id) => depot.TrouverEntreprise(id)?.RaisonSociale ?? "(entreprise inconnue)";

        // Dons de l'année
        LignesDons.Clear();
        foreach (var d in depot.Dons.Where(d => d.Date.Year == _annee))
        {
            var j = depot.JustificatifDe(d.Id);
            if (_filtreType.Valeur is { } t && d.Type != t) continue;
            if (_filtreStatut.Valeur is { } s && d.Statut != s) continue;
            var ok = _filtreDocument.Valeur switch
            {
                "agenerer" => j is null && d.Statut == StatutDon.Recu,
                "attestation" => j?.Type == TypeJustificatif.Attestation,
                "recu" => j?.Type == TypeJustificatif.RecuFiscal,
                "nonenvoye" => j is { DateEnvoi: null },
                "envoye" => j is { DateEnvoi: not null },
                _ => true,
            };
            var entreprise = NomEntreprise(d.EntrepriseId);
            var montant = d.Montant is { } m ? ModelesJustificatifs.Euros(m) : "—";
            if (!ok || !Correspond(entreprise, montant, d.Montant?.ToString(Fr), d.Date.ToString("dd/MM/yyyy", Fr), d.Description, d.Quantites, j?.Numero)) continue;

            var document = d.Statut switch
            {
                StatutDon.Annule => "Don annulé",
                StatutDon.EnAttente => "En attente de réception",
                _ when j is null => "À générer",
                _ => $"{j.Numero} — {(j.DateEnvoi is null ? "à envoyer" : j.EnvoyeA == "remis en main propre" ? "remis" : "envoyé")}",
            };
            var id = d.Id;
            LignesDons.Add(new LigneDonListe(entreprise, d.Date.ToString("dd/MM/yyyy", Fr), montant, RegleDon.Libelle(d.Type), RegleDon.Libelle(d.Statut), document,
                DocumentAttention: d.Statut == StatutDon.Recu && (j is null || j.DateEnvoi is null),
                DocumentOk: j is { DateEnvoi: not null },
                EstAnnule: d.Statut == StatutDon.Annule,
                Modifier: new RelayCommand(() => ModifierDon(id)),
                Justifier: new RelayCommand(() => Justifier(id)),
                VoirDocument: new RelayCommand(() => { if (depot.JustificatifDe(id) is { } jj) _ = VoirAsync(jj); }),
                AJustificatif: j is not null,
                PeutJustifier: j is null && d.Statut == StatutDon.Recu));
        }

        // Annuaire
        LignesEntreprises.Clear();
        foreach (var e in depot.Entreprises)
        {
            if (!Correspond(e.RaisonSociale, e.Siren, e.Contact, e.Ville, e.Email)) continue;
            var dons = depot.DonsDe(e.Id).Where(d => d.Statut != StatutDon.Annule).ToList();
            var id = e.Id;
            LignesEntreprises.Add(new LigneEntrepriseListe(e.RaisonSociale, Siren.Afficher(e.Siren),
                string.Join(" — ", new[] { e.Contact, e.Email, e.Telephone is { } tel ? Telephone.Afficher(tel) : null }.Where(s => !string.IsNullOrWhiteSpace(s))),
                dons.Count == 0 ? "Aucun don" : $"{dons.Count} don(s) — {ModelesJustificatifs.Euros(dons.Sum(d => d.Montant ?? 0))}",
                new RelayCommand(() => Ouvrir(new EditionEntrepriseViewModel(_ctx, depot.TrouverEntreprise(id)))),
                new RelayCommand(() => NouveauDon(id)),
                new RelayCommand(() => RetirerEntreprise(id))));
        }

        // Justificatifs de l'année
        LignesJustificatifs.Clear();
        foreach (var j in depot.Justificatifs.Where(j => j.Annee == _annee))
        {
            var entreprise = NomEntreprise(j.EntrepriseId);
            if (!Correspond(j.Numero, entreprise, j.Contenu?.Total.ToString(Fr))) continue;
            if (_filtreDocument.Valeur == "attestation" && j.Type != TypeJustificatif.Attestation) continue;
            if (_filtreDocument.Valeur == "recu" && j.Type != TypeJustificatif.RecuFiscal) continue;
            var statut = j.Statut switch
            {
                StatutJustificatif.Annule => $"Annulé — {j.MotifAnnulation}",
                StatutJustificatif.Remplace => $"Remplacé par {depot.TrouverJustificatif(j.RemplacePar ?? Guid.Empty)?.Numero}",
                _ when j.DateEnvoi is { } env && j.EnvoyeA == "remis en main propre" => $"Remis en main propre le {env.ToLocalTime():dd/MM/yyyy}",
                _ when j.DateEnvoi is { } env => $"Envoyé le {env.ToLocalTime():dd/MM/yyyy} à {j.EnvoyeA}",
                _ => "Émis — à envoyer",
            };
            var jid = j.Id;
            LignesJustificatifs.Add(new LigneJustificatifListe(j.Numero,
                j.Type == TypeJustificatif.RecuFiscal ? "Reçu fiscal" : "Attestation", entreprise,
                j.DateEmission?.ToString("dd/MM/yyyy", Fr) ?? "", j.Contenu is { } c ? ModelesJustificatifs.Euros(c.Total) : "", statut,
                EstActif: j.EstActif, EstEnvoye: j.EstActif && j.DateEnvoi is not null, EstInactif: !j.EstActif,
                Voir: new CommandeAsync(() => VoirAsync(depot.TrouverJustificatif(jid)!)),
                Imprimer: new CommandeAsync(() => ImprimerAsync(depot.TrouverJustificatif(jid)!)),
                Telecharger: new CommandeAsync(() => TelechargerAsync(depot.TrouverJustificatif(jid)!)),
                Envoyer: new RelayCommand(() => Envoyer(jid)),
                Remplacer: new RelayCommand(() => Remplacer(jid)),
                Annuler: new RelayCommand(() => Annuler(jid)),
                Historique: new RelayCommand(() => Historique(jid)),
                RemisEnMain: new RelayCommand(() => MarquerRemis(jid)),
                PeutMarquerRemis: j.EstActif && j.DateEnvoi is null));
        }

        // À traiter
        ATraiter.Clear();
        foreach (var d in depot.Dons.Where(d => d.Statut == StatutDon.Recu && depot.JustificatifDe(d.Id) is null).Take(6))
        {
            var id = d.Id;
            ATraiter.Add(new ElementATraiter($"{NomEntreprise(d.EntrepriseId)} — don du {d.Date:dd/MM/yyyy} : justificatif à générer",
                new RelayCommand(() => Justifier(id)), "Générer"));
        }
        foreach (var j in depot.Justificatifs.Where(j => j.EstActif && j.DateEnvoi is null).Take(6))
        {
            var id = j.Id;
            ATraiter.Add(new ElementATraiter($"{j.Numero} ({NomEntreprise(j.EntrepriseId)}) : à envoyer ou à remettre",
                new RelayCommand(() => Envoyer(id)), "Envoyer", new RelayCommand(() => MarquerRemis(id)), "Remis en main propre"));
        }
        foreach (var d in depot.Dons.Where(d => d.Statut == StatutDon.EnAttente).Take(4))
        {
            var id = d.Id;
            ATraiter.Add(new ElementATraiter($"{NomEntreprise(d.EntrepriseId)} — don du {d.Date:dd/MM/yyyy} en attente de réception",
                new RelayCommand(() => ModifierDon(id)), "Mettre à jour"));
        }

        foreach (var n in new[] { nameof(AucunDon), nameof(AucuneEntreprise), nameof(AucunJustificatif), nameof(RienATraiter) })
            OnPropertyChanged(n);
    }

    // ---------- Actions ----------

    private bool Ouvrir(FormulaireViewModel vm) => _ctx.Plateforme.OuvrirFormulaire(vm);

    private void NouveauDon(Guid? entrepriseId)
    {
        var vm = new EditionDonViewModel(_ctx, null, entrepriseId);
        if (!Ouvrir(vm) || vm.IdEnregistre is not { } id || _ctx.Depot.TrouverDon(id) is not { Statut: StatutDon.Recu } don) return;
        ChangerOnglet(0);
        if (_ctx.Plateforme.Confirmer("Don enregistré", "Voulez-vous générer le justificatif maintenant ?", "Générer le justificatif"))
            Justifier(don.Id);
    }

    private void ModifierDon(Guid id)
    {
        if (_ctx.Depot.TrouverDon(id) is { } d) Ouvrir(new EditionDonViewModel(_ctx, d));
    }

    private void Justifier(Guid donId)
    {
        if (_ctx.Depot.TrouverDon(donId) is not { } d) return;
        if (d.Statut != StatutDon.Recu)
        {
            _ctx.Plateforme.Informer("Don pas encore reçu", "Passez d'abord le don au statut « Reçu et vérifié ».");
            return;
        }
        var vm = new GenerationJustificatifViewModel(_ctx, d);
        if (Ouvrir(vm) && vm.Resultat is { } j) ApresEmission(j);
    }

    private void ApresEmission(JustificatifDon j)
    {
        _ = VoirAsync(j);
        if (_ctx.Plateforme.Confirmer($"Justificatif {j.Numero} émis",
                "Le PDF s'est ouvert pour vérification. Voulez-vous l'envoyer à l'entreprise maintenant ?", "Envoyer par e-mail"))
            Envoyer(j.Id);
    }

    private void Envoyer(Guid id)
    {
        if (_ctx.Depot.TrouverJustificatif(id) is not { EstActif: true } j) return;
        try { _ctx.Justificatifs?.VerifierEnvoiPossible(j); }
        catch (InvalidOperationException ex) { _ctx.Plateforme.Informer("Envoi impossible", ex.Message); return; }
        Ouvrir(new EnvoiJustificatifViewModel(_ctx, j));
    }

    /// <summary>Justificatif imprimé et donné à l'entreprise : il sort de « À traiter ».</summary>
    private void MarquerRemis(Guid id)
    {
        if (_ctx.Depot.TrouverJustificatif(id) is not { EstActif: true } j) return;
        if (!_ctx.Plateforme.Confirmer($"{j.Numero} remis en main propre ?",
                "Le document est noté comme remis à l'entreprise (imprimé et donné en main propre) et disparaît de la liste « À traiter ».",
                "Oui, il est remis"))
            return;
        _ctx.Depot.MarquerJustificatifEnvoye(id, "remis en main propre");
    }

    private void Remplacer(Guid id)
    {
        if (_ctx.Depot.TrouverJustificatif(id) is not { Statut: StatutJustificatif.Emis or StatutJustificatif.Annule } j) return;
        if (j.DonIds.Select(_ctx.Depot.TrouverDon).FirstOrDefault(d => d is { Statut: StatutDon.Recu }) is not { } premier)
        {
            _ctx.Plateforme.Informer("Remplacement impossible", "Aucun des dons de ce justificatif n'est encore « reçu et vérifié ».");
            return;
        }
        var vm = new GenerationJustificatifViewModel(_ctx, premier, j);
        if (Ouvrir(vm) && vm.Resultat is { } nouveau) ApresEmission(nouveau);
    }

    private void Annuler(Guid id)
    {
        if (_ctx.Depot.TrouverJustificatif(id) is not { EstActif: true } j) return;
        if (!_ctx.Plateforme.Confirmer($"Annuler {j.Numero} ?",
                "Le document reste dans l'historique avec la mention « annulé » ; son numéro ne sera jamais réutilisé.\n" +
                (j.DateEnvoi is not null ? "Il a déjà été envoyé : pensez à prévenir l'entreprise.\n" : "") +
                "Pour corriger une erreur, préférez « Remplacer », qui émet directement le document corrigé.", "Annuler le document", danger: true))
            return;
        _ctx.Depot.AnnulerJustificatif(id, "Annulé par l'association");
    }

    private void Historique(Guid id)
    {
        if (_ctx.Depot.TrouverJustificatif(id) is not { } j) return;
        var lignes = _ctx.Depot.HistoriqueDe(id).Concat(j.DonIds.SelectMany(_ctx.Depot.HistoriqueDe)).OrderByDescending(h => h.Quand)
            .Select(h => $"{h.Quand.ToLocalTime():dd/MM/yyyy HH:mm} — {h.Action}{(string.IsNullOrWhiteSpace(h.Detail) ? "" : " : " + h.Detail)}{(h.Poste is null ? "" : $" ({h.Poste})")}");
        _ctx.Plateforme.Informer($"Historique de {j.Numero}", string.Join("\n", lignes.Take(25)));
    }

    private void RetirerEntreprise(Guid id)
    {
        if (_ctx.Depot.TrouverEntreprise(id) is not { } e) return;
        if (!_ctx.Plateforme.Confirmer($"Retirer « {e.RaisonSociale} » ?", "L'entreprise sera retirée de l'annuaire.", "Retirer", danger: true)) return;
        var r = _ctx.Depot.SupprimerEntreprise(id);
        if (!r.Reussi) _ctx.Plateforme.Informer("Impossible de retirer l'entreprise", r.Erreurs[0].Message);
    }

    private async Task<string?> FichierAsync(JustificatifDon j)
    {
        if (_ctx.Justificatifs is not { } s) return null;
        try { return await s.FichierAsync(j); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _ctx.Journal.Erreur("Ouverture du justificatif impossible", ex);
            _ctx.Plateforme.Informer("Document indisponible", "Le PDF n'a pas pu être ouvert : " + ex.Message);
            return null;
        }
    }

    private async Task VoirAsync(JustificatifDon j)
    {
        if (await FichierAsync(j) is { } chemin && !_ctx.Plateforme.OuvrirLien(chemin))
            _ctx.Plateforme.Informer("Aucun lecteur PDF", "Aucune application ne sait ouvrir les PDF sur cet ordinateur (Microsoft Edge le fait).");
    }

    private async Task ImprimerAsync(JustificatifDon j)
    {
        if (await FichierAsync(j) is { } chemin) _ctx.Plateforme.ImprimerFichier(chemin);
    }

    private async Task TelechargerAsync(JustificatifDon j)
    {
        if (await FichierAsync(j) is not { } chemin) return;
        var cible = _ctx.Plateforme.ChoisirFichierAEnregistrer("Enregistrer le justificatif", j.NomFichier, "Document PDF (*.pdf)|*.pdf");
        if (cible is null) return;
        File.Copy(chemin, cible, overwrite: true);
    }

    // ---------- Bilan ----------

    private IReadOnlyList<ModelesJustificatifs.LigneBilan> LignesBilan()
    {
        var depot = _ctx.Depot;
        return depot.Dons.Where(d => d.Date.Year == _annee && d.Statut != StatutDon.Annule).OrderBy(d => d.Date)
            .Select(d =>
            {
                var j = depot.JustificatifDe(d.Id);
                return new ModelesJustificatifs.LigneBilan(depot.TrouverEntreprise(d.EntrepriseId)?.RaisonSociale ?? "", d.Date, RegleDon.Libelle(d.Type),
                    d.Montant ?? 0, d.Statut == StatutDon.EnAttente ? "En attente" : j is null ? "À générer" : $"{j.Numero}{(j.DateEnvoi is null ? "" : " — envoyé")}");
            }).ToList();
    }

    private async Task BilanPdfAsync()
    {
        var cible = _ctx.Plateforme.ChoisirFichierAEnregistrer("Enregistrer le bilan des dons", $"Bilan des dons {_annee}.pdf", "Document PDF (*.pdf)|*.pdf");
        if (cible is null) return;
        var pdf = ModelesJustificatifs.BilanAnnuel(_bilan, LignesBilan(), _ctx.Depot.Association.Identite, _ctx.LogoJpeg, _ctx.Depot.AujourdHui);
        await File.WriteAllBytesAsync(cible, pdf);
        _ctx.Plateforme.OuvrirLien(cible);
    }

    private async Task BilanExcelAsync()
    {
        var cible = _ctx.Plateforme.ChoisirFichierAEnregistrer("Exporter les dons vers Excel", $"Dons {_annee}.xlsx", "Classeur Excel (*.xlsx)|*.xlsx");
        if (cible is null) return;
        var depot = _ctx.Depot;
        var b = _bilan;
        var classeur = new ClasseurXlsx();
        classeur.AjouterFeuille($"Bilan {_annee}",
        [
            ["Indicateur", "Valeur"],
            ["Total des dons financiers reçus", b.TotalFinancier],
            ["Total déclaré des dons en nature", b.TotalNature],
            ["Total déclaré du mécénat de compétences", b.TotalCompetences],
            ["Nombre d'entreprises donatrices", b.NombreEntreprises],
            ["Nombre de dons enregistrés", b.NombreDons],
            ["Attestations simples émises", b.AttestationsEmises],
            ["Reçus fiscaux émis (déclaration art. 222 bis CGI)", b.RecusFiscauxEmis],
            ["Montant des dons ayant donné lieu à reçu fiscal (art. 222 bis CGI)", b.MontantRecusFiscaux],
        ]);
        classeur.AjouterFeuille("Dons",
            new object?[][] { ["Entreprise", "SIREN", "Date", "Type", "Montant / valeur", "Mode", "Statut", "Description", "Justificatif", "Envoyé le"] }
                .Concat(depot.Dons.Where(d => d.Date.Year == _annee).OrderBy(d => d.Date).Select(d =>
                {
                    var e = depot.TrouverEntreprise(d.EntrepriseId);
                    var j = depot.JustificatifDe(d.Id);
                    return new object?[]
                    {
                        e?.RaisonSociale, Siren.Afficher(e?.Siren), d.Date, RegleDon.Libelle(d.Type), d.Montant, d.Mode is { } m ? RegleDon.Libelle(m) : null,
                        RegleDon.Libelle(d.Statut), RegleDon.Description(d), j?.Numero, j?.DateEnvoi?.ToLocalTime().ToString("dd/MM/yyyy", Fr),
                    };
                })));
        classeur.AjouterFeuille("Justificatifs",
            new object?[][] { ["Numéro", "Type", "Entreprise", "Émis le", "Total", "Statut", "Envoyé à", "Empreinte SHA-256"] }
                .Concat(depot.Justificatifs.Where(j => j.Annee == _annee).OrderBy(j => j.Numero, StringComparer.Ordinal).Select(j => new object?[]
                {
                    j.Numero, j.Type == TypeJustificatif.RecuFiscal ? "Reçu fiscal" : "Attestation", depot.TrouverEntreprise(j.EntrepriseId)?.RaisonSociale,
                    j.DateEmission, j.Contenu?.Total, j.Statut.ToString(), j.EnvoyeA, j.Empreinte,
                })));
        await File.WriteAllBytesAsync(cible, classeur.Octets());
        _ctx.Plateforme.OuvrirLien(cible);
    }
}
