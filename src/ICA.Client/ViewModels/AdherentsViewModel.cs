using System.Collections.ObjectModel;
using ICA.Client.Mvvm;
using ICA.Client.Plateforme;
using ICA.Client.Services;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.ViewModels;

public enum FiltreAdherents { Tous, Adultes, Enfants, Payee, ARegler, ACompleter }

public sealed class ElementFiltre(FiltreAdherents filtre, string libelle, RelayCommand commande) : ObservableObject
{
    private int _nombre;
    private bool _estActif;
    public FiltreAdherents Filtre { get; } = filtre;
    public string Libelle { get; } = libelle;
    public RelayCommand Commande { get; } = commande;
    public bool EstAttention => Filtre is FiltreAdherents.ARegler or FiltreAdherents.ACompleter;
    public int Nombre { get => _nombre; set { if (SetProperty(ref _nombre, value)) OnPropertyChanged(nameof(Texte)); } }
    public string Texte => $"{Libelle} · {Nombre}";
    public bool EstActif { get => _estActif; set => SetProperty(ref _estActif, value); }
}

public sealed class ElementAdherent(Adherent adherent, string detail, bool estPaye, Action selectionChangee, string index,
    IReadOnlyList<string>? manques = null) : ObservableObject
{
    private bool _estCoche;
    public Adherent Adherent { get; } = adherent;
    public Guid Id => Adherent.Id;
    public string NomAffiche => Adherent.NomAffiche;
    public string Initiales => Adherent.Initiales;
    public string Detail { get; } = detail;
    public bool EstPaye { get; } = estPaye;
    public bool EstARegler => !EstPaye;
    public string Index { get; } = index;
    public IReadOnlyList<string> Manques { get; } = manques ?? [];
    public bool AManques => Manques.Count > 0;
    public string? TexteManques => InfosManquantes.Texte(Manques);
    public bool EstCoche
    {
        get => _estCoche;
        set { if (SetProperty(ref _estCoche, value)) selectionChangee(); }
    }
}

public sealed record LigneInfo(string Libelle, string Valeur);
public sealed record PastilleActivite(string Nom, string Couleur);

public sealed class EnfantFiche(string nomAffiche, string initiales, string info, RelayCommand ouvrir)
{
    public string NomAffiche { get; } = nomAffiche;
    public string Initiales { get; } = initiales;
    public string Info { get; } = info;
    public RelayCommand Ouvrir { get; } = ouvrir;
}

public sealed class FicheAdherentViewModel
{
    public FicheAdherentViewModel(Adherent a, DepotIca depot, Action<Guid> ouvrirFiche)
    {
        Adherent = a;
        var aujourdHui = depot.AujourdHui;
        var saison = depot.SaisonCourante;

        SousTitre = a.EstEnfant
            ? $"Enfant{(a.Age(aujourdHui) is { } age ? $" · {age} ans" : "")}"
            : $"Adulte{(a.AnneeAdhesion is { } an ? $" · adhérent depuis {an}" : "")}";

        var adresse = string.Join("\n", new[]
        {
            a.Adresse, a.ComplementAdresse,
            string.Join(' ', new[] { a.CodePostal, a.Ville?.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) }.Where(s => !string.IsNullOrEmpty(s))),
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var lignes = new List<LigneInfo>();
        if (adresse.Length > 0) lignes.Add(new("Adresse", adresse));
        if (a.Telephone1 is not null) lignes.Add(new("Téléphone", Telephone.Afficher(a.Telephone1)));
        if (a.Telephone2 is not null) lignes.Add(new("Autre tél.", Telephone.Afficher(a.Telephone2)));
        if (a.Email is not null) lignes.Add(new("E-mail", a.Email));
        if (a.DateNaissance is { } n) lignes.Add(new("Naissance", Libelles.DateCourte(n)));
        Lignes = lignes;
        TexteManques = InfosManquantes.Texte(InfosManquantes.De(a, a.ResponsableId is { } idResp ? depot.TrouverAdherent(idResp) : null));

        if (a.ResponsableId is { } rid && depot.TrouverAdherent(rid) is { } responsable)
        {
            Responsable = responsable.NomAffiche;
            ContactResponsable = string.Join(" · ", new[] { Telephone.Afficher(responsable.Telephone1), responsable.Email }
                .Where(s => !string.IsNullOrEmpty(s)));
            OuvrirResponsable = new RelayCommand(() => ouvrirFiche(responsable.Id));
        }

        Enfants = depot.EnfantsDe(a.Id)
            .Select(e => new EnfantFiche(e.NomAffiche, e.Initiales,
                $"Enfant{(e.Age(aujourdHui) is { } ageEnfant ? $" · {ageEnfant} ans" : "")}",
                new RelayCommand(() => ouvrirFiche(e.Id))))
            .ToList();

        var cotisation = depot.CotisationDe(a.Id, saison);
        TitreCotisation = $"Cotisation {saison}";
        CotisationPayee = cotisation?.Statut is StatutCotisation.Payee or StatutCotisation.Exoneree;
        CotisationTexte = cotisation?.Statut switch
        {
            StatutCotisation.Payee => $"✓ Payée{(cotisation.DatePaiement is { } d ? $" le {Libelles.DateCourte(d)}" : "")}",
            StatutCotisation.Exoneree => "✓ Exonérée",
            _ => "À régler",
        };
        CotisationDetail = string.Join(" · ", new[] { Libelles.Mode(cotisation?.ModePaiement), Libelles.Montant(cotisation?.Montant) }
            .Where(s => s.Length > 0));

        Activites = depot.ActivitesDe(a.Id).Select(x => new PastilleActivite(x.Nom, x.Couleur ?? "#9E1B32")).ToList();
    }

    public Adherent Adherent { get; }
    public string NomAffiche => Adherent.NomAffiche;
    public string Initiales => Adherent.Initiales;
    public string SousTitre { get; }
    public string? TexteManques { get; }
    public bool AManques => TexteManques is not null;
    public IReadOnlyList<LigneInfo> Lignes { get; }
    public string? Responsable { get; }
    public string? ContactResponsable { get; }
    public RelayCommand? OuvrirResponsable { get; }
    public bool AResponsable => Responsable is not null;
    public IReadOnlyList<EnfantFiche> Enfants { get; }
    public bool AEnfants => Enfants.Count > 0;
    public string TitreCotisation { get; }
    public bool CotisationPayee { get; }
    public bool CotisationARegler => !CotisationPayee;
    public string CotisationTexte { get; }
    public string CotisationDetail { get; }
    public IReadOnlyList<PastilleActivite> Activites { get; }
    public bool AActivites => Activites.Count > 0;
    public string? Notes => Adherent.Notes;
    public bool ANotes => !string.IsNullOrWhiteSpace(Adherent.Notes);
    public bool AEmailOuResponsable => Adherent.Email is not null || Responsable is not null;
}

public sealed class AdherentsViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private readonly PrincipalViewModel _principal;
    private readonly List<ElementAdherent> _tous = [];
    private string _recherche = "";
    private FiltreAdherents _filtre = FiltreAdherents.Tous;
    private string _tri = "Nom";
    private ElementAdherent? _selection;
    private FicheAdherentViewModel? _fiche;
    private bool _miseAJourEnCours;

    public static IReadOnlyList<string> OptionsTri { get; } =
        ["Nom", "Prénom", "Ville", "Code postal", "Date de naissance", "Cotisation", "Type"];

    public AdherentsViewModel(ContexteApplication ctx, PrincipalViewModel principal)
    {
        _ctx = ctx;
        _principal = principal;

        Filtres =
        [
            CreerFiltre(FiltreAdherents.Tous, "Tous"),
            CreerFiltre(FiltreAdherents.Adultes, "Adultes"),
            CreerFiltre(FiltreAdherents.Enfants, "Enfants"),
            CreerFiltre(FiltreAdherents.Payee, "Cotisation payée"),
            CreerFiltre(FiltreAdherents.ARegler, "À régler"),
            CreerFiltre(FiltreAdherents.ACompleter, "Infos manquantes"),
        ];
        Filtres[0].EstActif = true;

        AjouterCommand = new RelayCommand(_principal.AjouterAdherent);
        ModifierCommand = new RelayCommand(Modifier, () => Fiche is not null);
        SupprimerCommand = new RelayCommand(Supprimer, () => Fiche is not null);
        MailFicheCommand = new RelayCommand(() => { if (Fiche is not null) EcrireA([Fiche.Adherent]); });
        EncaisserFicheCommand = new RelayCommand(() => { if (Fiche is not null) _principal.AllerAuxCotisations(Fiche.Adherent.Id); });
        MailSelectionCommand = new RelayCommand(() => EcrireA(Coches.Select(e => e.Adherent).ToList()));
        ExporterSelectionCommand = new RelayCommand(() => Exporter(Coches.Select(e => e.Adherent).ToList(), "selection"));
        ExporterToutCommand = new RelayCommand(() => Exporter(Visibles.Select(e => e.Adherent).ToList(), "liste"));
        ImprimerSelectionCommand = new RelayCommand(() => Imprimer(Coches.Select(e => e.Adherent).ToList()));
        ImprimerToutCommand = new RelayCommand(() => Imprimer(Visibles.Select(e => e.Adherent).ToList()));
        DecocherToutCommand = new RelayCommand(() => ChangerCoches(false));
        CocherVisiblesCommand = new RelayCommand(() => ChangerCoches(true));
        EffacerRechercheCommand = new RelayCommand(() => Recherche = "");

        _ctx.Depot.Modifie += (_, _) => Recharger();
        Recharger();
    }

    public IReadOnlyList<ElementFiltre> Filtres { get; }
    public ObservableCollection<ElementAdherent> Visibles { get; } = [];

    public string Recherche
    {
        get => _recherche;
        set { if (SetProperty(ref _recherche, value)) { Appliquer(); OnPropertyChanged(nameof(ARecherche)); } }
    }
    public bool ARecherche => !string.IsNullOrEmpty(Recherche);

    public string Tri { get => _tri; set { if (SetProperty(ref _tri, value)) Appliquer(); } }

    public ElementAdherent? Selection
    {
        get => _selection;
        set
        {
            if (!SetProperty(ref _selection, value)) return;
            Fiche = value is null ? null : new FicheAdherentViewModel(value.Adherent, _ctx.Depot, OuvrirFiche);
        }
    }

    public FicheAdherentViewModel? Fiche
    {
        get => _fiche;
        private set
        {
            SetProperty(ref _fiche, value);
            OnPropertyChanged(nameof(AFiche));
            OnPropertyChanged(nameof(PasDeFiche));
            ModifierCommand.NotifierChangement();
            SupprimerCommand.NotifierChangement();
        }
    }
    public bool AFiche => Fiche is not null;
    public bool PasDeFiche => Fiche is null;

    public int NombreCoches => _tous.Count(e => e.EstCoche);
    public bool BarreSelectionVisible => NombreCoches > 0;
    public string TexteSelection => NombreCoches == 1 ? "1 adhérent sélectionné" : $"{NombreCoches} adhérents sélectionnés";
    public string TexteResultat => Visibles.Count switch
    {
        0 => "Aucun adhérent ne correspond.",
        1 => "1 adhérent",
        var n => $"{n} adhérents",
    };
    public bool AucunResultat => Visibles.Count == 0;

    public RelayCommand AjouterCommand { get; }
    public RelayCommand ModifierCommand { get; }
    public RelayCommand SupprimerCommand { get; }
    public RelayCommand MailFicheCommand { get; }
    public RelayCommand EncaisserFicheCommand { get; }
    public RelayCommand MailSelectionCommand { get; }
    public RelayCommand ExporterSelectionCommand { get; }
    public RelayCommand ExporterToutCommand { get; }
    public RelayCommand ImprimerSelectionCommand { get; }
    public RelayCommand ImprimerToutCommand { get; }
    public RelayCommand DecocherToutCommand { get; }
    public RelayCommand CocherVisiblesCommand { get; }
    public RelayCommand EffacerRechercheCommand { get; }

    private IEnumerable<ElementAdherent> Coches => _tous.Where(e => e.EstCoche);

    /// <summary>Appelé lors d'une navigation (recherche depuis l'accueil, fiche à ouvrir).</summary>
    public void Preparer(string? recherche, Guid? selection)
    {
        if (recherche is not null) Recherche = recherche.Trim();
        if (selection is { } id)
        {
            if (_tous.FirstOrDefault(e => e.Id == id) is { } cible && !Visibles.Contains(cible))
            {
                ChoisirFiltre(FiltreAdherents.Tous);
                Recherche = "";
            }
            Selection = Visibles.FirstOrDefault(e => e.Id == id);
        }
    }

    private ElementFiltre CreerFiltre(FiltreAdherents f, string libelle)
        => new(f, libelle, new RelayCommand(() => ChoisirFiltre(f)));

    private void ChoisirFiltre(FiltreAdherents f)
    {
        _filtre = f;
        foreach (var e in Filtres) e.EstActif = e.Filtre == f;
        Appliquer();
    }

    private void OuvrirFiche(Guid id) => Preparer(null, id);

    private void Recharger()
    {
        var depot = _ctx.Depot;
        var saison = depot.SaisonCourante;
        var coches = _tous.Where(e => e.EstCoche).Select(e => e.Id).ToHashSet();
        var selectionId = Selection?.Id;

        _tous.Clear();
        foreach (var a in depot.Adherents)
        {
            var responsable = a.ResponsableId is { } rid ? depot.TrouverAdherent(rid) : null;
            var detail = a.EstEnfant
                ? $"Enfant · responsable : {responsable?.NomAffiche ?? "?"}"
                : string.Join(" · ", new[] { "Adulte", a.Ville, Telephone.Afficher(a.Telephone1) }.Where(s => !string.IsNullOrEmpty(s)));
            var paye = depot.StatutDe(a.Id, saison) is StatutCotisation.Payee or StatutCotisation.Exoneree;
            var element = new ElementAdherent(a, detail, paye, SelectionChangee, RechercheAdherent.IndexDe(a, responsable?.NomAffiche),
                InfosManquantes.De(a, responsable));
            element.EstCoche = coches.Contains(a.Id);
            _tous.Add(element);
        }

        foreach (var f in Filtres) f.Nombre = _tous.Count(e => Passe(e, f.Filtre));

        Appliquer();
        Selection = selectionId is { } id ? Visibles.FirstOrDefault(e => e.Id == id) : null;
        SelectionChangee();
    }

    private static bool Passe(ElementAdherent e, FiltreAdherents f) => f switch
    {
        FiltreAdherents.Adultes => !e.Adherent.EstEnfant,
        FiltreAdherents.Enfants => e.Adherent.EstEnfant,
        FiltreAdherents.Payee => e.EstPaye,
        FiltreAdherents.ARegler => !e.EstPaye,
        FiltreAdherents.ACompleter => e.AManques,
        _ => true,
    };

    private void Appliquer()
    {
        IEnumerable<ElementAdherent> r = _tous.Where(e => Passe(e, _filtre) && RechercheAdherent.Correspond(e.Index, Recherche));
        r = Tri switch
        {
            "Prénom" => r.OrderBy(e => Texte.PourRecherche(e.Adherent.Prenom)).ThenBy(e => Texte.PourRecherche(e.Adherent.Nom)),
            "Ville" => r.OrderBy(e => Texte.PourRecherche(e.Adherent.Ville)).ThenBy(e => Texte.PourRecherche(e.Adherent.Nom)),
            "Code postal" => r.OrderBy(e => e.Adherent.CodePostal ?? "").ThenBy(e => Texte.PourRecherche(e.Adherent.Nom)),
            "Date de naissance" => r.OrderBy(e => e.Adherent.DateNaissance ?? DateOnly.MaxValue),
            "Cotisation" => r.OrderBy(e => e.EstPaye).ThenBy(e => Texte.PourRecherche(e.Adherent.Nom)),
            "Type" => r.OrderBy(e => e.Adherent.TypeAdherent).ThenBy(e => Texte.PourRecherche(e.Adherent.Nom)),
            _ => r.OrderBy(e => Texte.PourRecherche(e.Adherent.Nom)).ThenBy(e => Texte.PourRecherche(e.Adherent.Prenom)),
        };

        var selection = Selection;
        _miseAJourEnCours = true;
        Visibles.Clear();
        foreach (var e in r) Visibles.Add(e);
        _miseAJourEnCours = false;
        if (selection is not null && Visibles.Contains(selection)) Selection = selection;
        OnPropertyChanged(nameof(TexteResultat));
        OnPropertyChanged(nameof(AucunResultat));
    }

    private void SelectionChangee()
    {
        if (_miseAJourEnCours) return;
        OnPropertyChanged(nameof(NombreCoches));
        OnPropertyChanged(nameof(BarreSelectionVisible));
        OnPropertyChanged(nameof(TexteSelection));
    }

    private void ChangerCoches(bool valeur)
    {
        var cibles = valeur ? Visibles.ToList() : _tous;
        _miseAJourEnCours = true;
        foreach (var e in cibles) e.EstCoche = valeur;
        _miseAJourEnCours = false;
        SelectionChangee();
    }

    private void Modifier()
    {
        if (Fiche is null) return;
        var vm = new EditionAdherentViewModel(_ctx, Fiche.Adherent);
        _ctx.Plateforme.EditerAdherent(vm);
    }

    private void Supprimer()
    {
        if (Fiche is null) return;
        var a = Fiche.Adherent;
        if (!_ctx.Plateforme.Confirmer("Supprimer cet adhérent ?",
                $"{a.NomAffiche} et ses cotisations seront supprimés.\nCette action ne peut pas être annulée depuis l'application.",
                "Supprimer", danger: true))
            return;

        var resultat = _ctx.Depot.SupprimerAdherent(a.Id);
        if (!resultat.Reussi)
            _ctx.Plateforme.Informer("Suppression impossible", resultat.Erreurs[0].Message);
    }

    private void EcrireA(IReadOnlyList<Adherent> adherents) => _principal.EcrireA(adherents);

    private void Exporter(IReadOnlyList<Adherent> adherents, string suffixe)
    {
        if (adherents.Count == 0) return;
        var chemin = _ctx.Plateforme.ChoisirFichierExport($"Adherents-ICA-{suffixe}-{_ctx.Depot.AujourdHui:yyyy-MM-dd}.csv");
        if (chemin is null) return;
        try
        {
            ExportCsv.Ecrire(chemin, adherents, _ctx.Depot, _ctx.Depot.SaisonCourante);
            _ctx.Plateforme.Informer("Export terminé", $"{adherents.Count} adhérent(s) exporté(s). Le fichier s'ouvre avec Excel.");
        }
        catch (IOException ex)
        {
            _ctx.Journal.Erreur("Échec de l'export CSV", ex);
            _ctx.Plateforme.Informer("Export impossible", "Le fichier est peut-être ouvert dans Excel. Fermez-le puis réessayez.");
        }
    }

    private void Imprimer(IReadOnlyList<Adherent> adherents)
    {
        if (adherents.Count == 0) return;
        var saison = _ctx.Depot.SaisonCourante;
        var lignes = adherents.Select(a => new LigneImpression(
            a.NomAffiche,
            a.EstEnfant ? "Enfant" : "Adulte",
            string.Join(", ", new[] { a.Adresse, string.Join(' ', new[] { a.CodePostal, a.Ville?.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) }.Where(s => !string.IsNullOrEmpty(s))) }
                .Where(s => !string.IsNullOrWhiteSpace(s))),
            Telephone.Afficher(a.Telephone1),
            a.Email ?? "",
            Libelles.Statut(_ctx.Depot.StatutDe(a.Id, saison)))).ToList();
        _ctx.Plateforme.Imprimer($"Adhérents de L'ICA — {Libelles.DateCourte(_ctx.Depot.AujourdHui)}", lignes);
    }
}
