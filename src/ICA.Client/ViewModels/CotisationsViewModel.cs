using System.Globalization;
using ICA.Client.Mvvm;
using ICA.Client.Services;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.ViewModels;

public sealed class LigneCotisation(Guid id, string nomAffiche, string initiales, string detail, RelayCommand action) : ObservableObject
{
    private bool _estCible;
    public Guid Id { get; } = id;
    public string NomAffiche { get; } = nomAffiche;
    public string Initiales { get; } = initiales;
    public string Detail { get; } = detail;
    public RelayCommand Action { get; } = action;
    public bool EstCible { get => _estCible; set => SetProperty(ref _estCible, value); }
}

public sealed class CotisationsViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private readonly PrincipalViewModel _principal;
    private string _saison;
    private IReadOnlyList<string> _saisons = [];
    private Statistiques _stats = new(0, 0, 0, 0, 0);
    private IReadOnlyList<LigneCotisation> _aRegler = [];
    private IReadOnlyList<LigneCotisation> _paiements = [];
    private LigneCotisation? _cible;
    private string _montant = "";
    private ModePaiement _mode = ModePaiement.Cheque;
    private DateTime _datePaiement = DateTime.Today;
    private string? _erreurMontant;

    public CotisationsViewModel(ContexteApplication ctx, PrincipalViewModel principal)
    {
        _ctx = ctx;
        _principal = principal;
        _saison = ctx.Depot.SaisonCourante;
        ConfirmerCommand = new RelayCommand(Confirmer);
        FermerEncaissementCommand = new RelayCommand(() => Cible = null);
        RappelCommand = new RelayCommand(EnvoyerRappel, () => _aRegler.Count > 0);
        ChoisirModeCommand = new RelayCommand(p => { if (p is string s && Enum.TryParse<ModePaiement>(s, out var m)) Mode = m; });
        _ctx.Depot.Modifie += (_, _) => Rafraichir();
        Rafraichir();
    }

    public IReadOnlyList<string> SaisonsDisponibles { get => _saisons; private set => SetProperty(ref _saisons, value); }

    public string Saison
    {
        get => _saison;
        set { if (value is not null && SetProperty(ref _saison, value)) { Cible = null; Rafraichir(); } }
    }

    public Statistiques Stats { get => _stats; private set => SetProperty(ref _stats, value); }
    private BilanFinancier _bilan = new(0, 0, 0, 0, 0, 0);
    public BilanFinancier Bilan { get => _bilan; private set => SetProperty(ref _bilan, value); }
    public string Encaisse => Libelles.Montant(Bilan.Encaisse);
    public string ResteAEncaisser => Libelles.Montant(Bilan.ResteAEncaisser);
    public string Prevu => Libelles.Montant(Bilan.Prevu);
    public string DetailModes => string.Join("  ·  ", new[]
    {
        Bilan.Especes > 0 ? $"Espèces {Libelles.Montant(Bilan.Especes)}" : null,
        Bilan.Cheques > 0 ? $"Chèques {Libelles.Montant(Bilan.Cheques)}" : null,
        Bilan.Virements > 0 ? $"Virements {Libelles.Montant(Bilan.Virements)}" : null,
        Bilan.Autres > 0 ? $"Mode non précisé {Libelles.Montant(Bilan.Autres)}" : null,
    }.Where(s => s is not null));
    public string TitreBilanFinancier => $"Argent de la saison {Saison}";

    public double Pourcentage => Stats.Total == 0 ? 0 : 100.0 * Stats.Payees / Stats.Total;
    public string TexteBilan => Stats.Total == 0
        ? "Aucun adhérent pour l'instant."
        : $"{Stats.Payees} adhérent{(Stats.Payees > 1 ? "s" : "")} sur {Stats.Total} {(Stats.Payees > 1 ? "ont" : "a")} réglé leur cotisation";

    public IReadOnlyList<LigneCotisation> ARegler { get => _aRegler; private set { SetProperty(ref _aRegler, value); OnPropertyChanged(nameof(ToutEstRegle)); OnPropertyChanged(nameof(TexteRappel)); RappelCommand.NotifierChangement(); } }
    public IReadOnlyList<LigneCotisation> DerniersPaiements { get => _paiements; private set { SetProperty(ref _paiements, value); OnPropertyChanged(nameof(AucunPaiement)); } }
    public bool ToutEstRegle => ARegler.Count == 0;
    public bool AucunPaiement => DerniersPaiements.Count == 0;
    public string TexteRappel => ARegler.Count <= 1 ? "Envoyer un rappel" : $"Envoyer un rappel aux {ARegler.Count}";

    // ---------- Encaissement ----------

    public LigneCotisation? Cible
    {
        get => _cible;
        private set
        {
            if (_cible is not null) _cible.EstCible = false;
            SetProperty(ref _cible, value);
            if (_cible is not null) _cible.EstCible = true;
            OnPropertyChanged(nameof(EncaissementVisible));
            OnPropertyChanged(nameof(EncaissementMasque));
            OnPropertyChanged(nameof(SousTitreEncaissement));
            ErreurMontant = null;
        }
    }
    public bool EncaissementVisible => Cible is not null;
    public bool EncaissementMasque => Cible is null;
    public string SousTitreEncaissement => $"Saison {Saison}";

    public string Montant { get => _montant; set => SetProperty(ref _montant, value); }
    public string? ErreurMontant { get => _erreurMontant; private set => SetProperty(ref _erreurMontant, value); }

    public ModePaiement Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value)) return;
            OnPropertyChanged(nameof(ModeEspeces));
            OnPropertyChanged(nameof(ModeCheque));
            OnPropertyChanged(nameof(ModeVirement));
        }
    }
    // Liées aux trois boutons « Espèces / Chèque / Virement » (un seul coché à la fois).
    public bool ModeEspeces { get => Mode == ModePaiement.Especes; set { if (value) Mode = ModePaiement.Especes; } }
    public bool ModeCheque { get => Mode == ModePaiement.Cheque; set { if (value) Mode = ModePaiement.Cheque; } }
    public bool ModeVirement { get => Mode == ModePaiement.Virement; set { if (value) Mode = ModePaiement.Virement; } }

    public DateTime DatePaiement { get => _datePaiement; set => SetProperty(ref _datePaiement, value); }

    public RelayCommand ConfirmerCommand { get; }
    public RelayCommand FermerEncaissementCommand { get; }
    public RelayCommand RappelCommand { get; }
    public RelayCommand ChoisirModeCommand { get; }

    /// <summary>Appelé lors d'une navigation : ouvre directement l'encaissement d'un adhérent.</summary>
    public void Preparer(Guid? adherentId)
    {
        if (adherentId is null) return;
        if (Saison != _ctx.Depot.SaisonCourante) Saison = _ctx.Depot.SaisonCourante;
        var ligne = ARegler.FirstOrDefault(l => l.Id == adherentId);
        if (ligne is not null) OuvrirEncaissement(ligne);
    }

    private void Rafraichir()
    {
        var depot = _ctx.Depot;
        var saisons = depot.Saisons();
        if (!saisons.SequenceEqual(SaisonsDisponibles))
        {
            SaisonsDisponibles = saisons;
            OnPropertyChanged(nameof(Saison)); // la liste déroulante se resynchronise
        }
        Stats = depot.StatistiquesDe(Saison);
        OnPropertyChanged(nameof(Pourcentage));
        OnPropertyChanged(nameof(TexteBilan));
        Bilan = depot.BilanDe(Saison);
        foreach (var n in new[] { nameof(Encaisse), nameof(ResteAEncaisser), nameof(Prevu), nameof(DetailModes), nameof(TitreBilanFinancier) })
            OnPropertyChanged(n);
        OnPropertyChanged(nameof(SousTitreEncaissement));

        var cibleId = Cible?.Id;
        var aRegler = new List<LigneCotisation>();
        foreach (var a in depot.Adherents
                     .Where(a => depot.StatutDe(a.Id, Saison) == StatutCotisation.APayer)
                     .OrderBy(a => Texte.PourRecherche(a.Nom)).ThenBy(a => Texte.PourRecherche(a.Prenom)))
        {
            LigneCotisation? ligne = null;
            ligne = new LigneCotisation(a.Id, a.NomAffiche, a.Initiales, Detail(a),
                new RelayCommand(() => OuvrirEncaissement(ligne!)));
            aRegler.Add(ligne);
        }
        ARegler = aRegler;

        DerniersPaiements = depot.Adherents
            .Select(a => (a, c: depot.CotisationDe(a.Id, Saison)))
            .Where(x => x.c?.Statut == StatutCotisation.Payee)
            .OrderByDescending(x => x.c!.DatePaiement).ThenByDescending(x => x.c!.UpdatedAt)
            .Take(8)
            .Select(x => new LigneCotisation(x.a.Id, x.a.NomAffiche, x.a.Initiales,
                string.Join(" · ", new[]
                {
                    Libelles.Mode(x.c!.ModePaiement),
                    x.c.DatePaiement is { } d ? Libelles.DateCourte(d) : "",
                    Libelles.Montant(x.c.Montant),
                }.Where(s => s.Length > 0)),
                new RelayCommand(() => AnnulerPaiement(x.a))))
            .ToList();

        Cible = cibleId is { } id ? ARegler.FirstOrDefault(l => l.Id == id) : null;
    }

    private string Detail(Adherent a)
    {
        if (a.EstEnfant && a.ResponsableId is { } rid && _ctx.Depot.TrouverAdherent(rid) is { } r)
            return $"Enfant · responsable : {r.NomAffiche}";
        return a.Ville is null ? "Adulte" : $"Adulte · {a.Ville}";
    }

    private void OuvrirEncaissement(LigneCotisation ligne)
    {
        Cible = ligne;
        var existante = _ctx.Depot.CotisationDe(ligne.Id, Saison);
        var type = _ctx.Depot.TrouverAdherent(ligne.Id)?.TypeAdherent ?? ICA.Contracts.TypeAdherent.Adulte;
        var montant = existante?.Montant ?? _ctx.Parametres.Cotisations.TarifPour(type);
        Montant = montant?.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")) ?? "";
        Mode = ModePaiement.Cheque;
        DatePaiement = DateTime.Today;
    }

    private void Confirmer()
    {
        if (Cible is null) return;

        decimal? montant = null;
        var saisie = Montant.Replace("€", "").Replace(" ", "").Trim();
        if (saisie.Length > 0)
        {
            if (!decimal.TryParse(saisie.Replace('.', ','), NumberStyles.Number, CultureInfo.GetCultureInfo("fr-FR"), out var m) || m < 0)
            {
                ErreurMontant = "Montant incorrect (exemple : 15 ou 12,50).";
                return;
            }
            montant = m;
        }

        _ctx.Depot.MarquerCotisationPayee(Cible.Id, Saison, montant, Mode, DateOnly.FromDateTime(DatePaiement));
        Cible = null;
    }

    private void AnnulerPaiement(Adherent a)
    {
        if (!_ctx.Plateforme.Confirmer("Annuler ce paiement ?",
                $"La cotisation {Saison} de {a.NomAffiche} repassera « À régler ».", "Annuler le paiement"))
            return;
        _ctx.Depot.AnnulerPaiement(a.Id, Saison);
    }

    private void EnvoyerRappel()
    {
        var destinataires = ARegler.Select(l => _ctx.Depot.TrouverAdherent(l.Id)).OfType<Adherent>().ToList();
        _principal.EcrireA(destinataires, ModelesParDefaut.NomRappelCotisation, Saison);
    }
}
