using System.Globalization;
using ICA.Client.Mvvm;
using ICA.Domain;

namespace ICA.Client.ViewModels;

public sealed record OptionActivite(Guid? Id, string Nom, string Couleur);

/// <summary>Fenêtre « Planifier une activité » / « Modifier la séance ».</summary>
public sealed class PlanificationViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private readonly Seance? _existante;
    private OptionActivite? _activite;
    private string _titre;
    private DateTime _date;
    private string _heureDebut;
    private string _heureFin;
    private string _lieu;
    private string _notes;
    private bool _repeter;
    private DateTime _repeterJusquau;
    private string? _erreur;

    public PlanificationViewModel(ContexteApplication ctx, Seance? existante, DateOnly date, Guid? activiteId)
    {
        _ctx = ctx;
        _existante = existante;

        Options = ctx.Depot.Activites.Select(a => new OptionActivite(a.Id, a.Nom, a.Couleur ?? "#9E1B32"))
            .Append(new OptionActivite(null, "Autre : événement ponctuel (AG, sortie, repas…)", "#9E1B32"))
            .ToList();
        _activite = activiteId is { } aid ? Options.FirstOrDefault(o => o.Id == aid) : null;
        _activite ??= existante is not null ? Options[^1] : Options[0];

        _titre = existante?.Titre ?? "";
        _date = (existante?.Date ?? date).ToDateTime(TimeOnly.MinValue);
        _heureDebut = RegleSeance.AfficherHeure(existante?.HeureDebut);
        _heureFin = RegleSeance.AfficherHeure(existante?.HeureFin);
        _lieu = existante?.Lieu ?? "";
        _notes = existante?.Notes ?? "";
        _repeterJusquau = ICA.Client.Donnees.DonneesDemo.FinDeSaison(DateOnly.FromDateTime(_date)).ToDateTime(TimeOnly.MinValue);
        if (existante is null && _heureDebut.Length == 0) { _heureDebut = "14h"; _heureFin = "17h"; }

        // Nouvelle séance d'une activité : on reprend horaires et lieu de sa dernière séance.
        if (existante is null) ReprendreHabitudes();

        EnregistrerCommand = new RelayCommand(Enregistrer);
        AnnulerCommand = new RelayCommand(() => Fermer?.Invoke(this, false));
    }

    public event EventHandler<bool>? Fermer;

    public DateOnly? PremiereDate { get; private set; }
    public bool EstNouvelle => _existante is null;
    public string TitreFenetre => EstNouvelle ? "Planifier une activité" : "Modifier la séance";
    public string TexteBouton => EstNouvelle ? "Ajouter au calendrier" : "Enregistrer";

    public IReadOnlyList<OptionActivite> Options { get; }

    public OptionActivite? Activite
    {
        get => _activite;
        set
        {
            if (!SetProperty(ref _activite, value)) return;
            OnPropertyChanged(nameof(EstEvenement));
            OnPropertyChanged(nameof(LibelleTitre));
            if (EstNouvelle) ReprendreHabitudes();
        }
    }

    public bool EstEvenement => Activite?.Id is null;
    public string LibelleTitre => EstEvenement ? "Nom de l'événement *" : "Précision (facultatif)";
    public string IndicationTitre => "Exemple : Assemblée générale, Spécial Noël…";

    public string Titre { get => _titre; set => SetProperty(ref _titre, value); }
    public DateTime Date { get => _date; set { if (SetProperty(ref _date, value)) OnPropertyChanged(nameof(ResumeRepetition)); } }
    public string HeureDebut { get => _heureDebut; set => SetProperty(ref _heureDebut, value); }
    public string HeureFin { get => _heureFin; set => SetProperty(ref _heureFin, value); }
    public string Lieu { get => _lieu; set => SetProperty(ref _lieu, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public bool Repeter { get => _repeter; set { if (SetProperty(ref _repeter, value)) OnPropertyChanged(nameof(ResumeRepetition)); } }
    public DateTime RepeterJusquau { get => _repeterJusquau; set { if (SetProperty(ref _repeterJusquau, value)) OnPropertyChanged(nameof(ResumeRepetition)); } }

    public string ResumeRepetition
    {
        get
        {
            var n = RegleSeance.ChaqueSemaine(DateOnly.FromDateTime(Date), DateOnly.FromDateTime(RepeterJusquau)).Count;
            var jour = Date.ToString("dddd", CultureInfo.GetCultureInfo("fr-FR"));
            return n <= 1 ? "Une seule date." : $"Tous les {jour}s : {n} séances seront ajoutées.";
        }
    }

    public string? Erreur { get => _erreur; private set { SetProperty(ref _erreur, value); OnPropertyChanged(nameof(AErreur)); } }
    public bool AErreur => !string.IsNullOrEmpty(Erreur);

    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand AnnulerCommand { get; }

    private void ReprendreHabitudes()
    {
        if (Activite?.Id is not { } id) return;
        var derniere = _ctx.Depot.SeancesEntre(DateOnly.MinValue, DateOnly.MaxValue).LastOrDefault(s => s.ActiviteId == id);
        if (derniere is null) return;
        HeureDebut = RegleSeance.AfficherHeure(derniere.HeureDebut);
        HeureFin = RegleSeance.AfficherHeure(derniere.HeureFin);
        Lieu = derniere.Lieu ?? "";
    }

    private void Enregistrer()
    {
        Erreur = null;
        if (!RegleSeance.TryLireHeure(HeureDebut, out var debut)) { Erreur = "Heure de début incorrecte (exemple : 14h00)."; return; }
        if (!RegleSeance.TryLireHeure(HeureFin, out var fin)) { Erreur = "Heure de fin incorrecte (exemple : 17h00)."; return; }
        if (EstEvenement && string.IsNullOrWhiteSpace(Titre)) { Erreur = "Donnez un nom à l'événement."; return; }

        var premiere = DateOnly.FromDateTime(Date);
        List<Seance> seances;
        if (_existante is not null)
        {
            var s = _existante.Copier();
            Remplir(s, premiere, debut, fin);
            seances = [s];
        }
        else
        {
            var dates = Repeter ? RegleSeance.ChaqueSemaine(premiere, DateOnly.FromDateTime(RepeterJusquau)) : [premiere];
            if (dates.Count == 0) { Erreur = "La date de fin de répétition est avant la première date."; return; }
            var serie = dates.Count > 1 ? Guid.CreateVersion7() : (Guid?)null;
            seances = dates.Select(d =>
            {
                var s = new Seance { SerieId = serie };
                Remplir(s, d, debut, fin);
                return s;
            }).ToList();
        }

        var resultat = _ctx.Depot.EnregistrerSeances(seances);
        if (!resultat.Reussi) { Erreur = resultat.Erreurs[0].Message; return; }
        PremiereDate = premiere;
        Fermer?.Invoke(this, true);
    }

    private void Remplir(Seance s, DateOnly date, TimeOnly? debut, TimeOnly? fin)
    {
        s.ActiviteId = Activite?.Id;
        s.Titre = Titre;
        s.Date = date;
        s.HeureDebut = debut;
        s.HeureFin = fin;
        s.Lieu = Lieu;
        s.Notes = Notes;
    }
}

public sealed class OptionCouleur(string hex, RelayCommand choisir) : ObservableObject
{
    private bool _estChoisie;
    public string Hex { get; } = hex;
    public RelayCommand Choisir { get; } = choisir;
    public bool EstChoisie { get => _estChoisie; set => SetProperty(ref _estChoisie, value); }
}

/// <summary>Fenêtre « Nouvelle activité » / « Modifier l'activité ».</summary>
public sealed class EditionActiviteViewModel : ObservableObject
{
    public static readonly string[] Palette = ["#E2457A", "#2B9ED8", "#F29F05", "#3FAE5A", "#8E5BD9", "#E8613C", "#1AA39A", "#9E1B32"];

    private readonly ContexteApplication _ctx;
    private readonly Activite _brouillon;
    private string _nom;
    private string _description;
    private string? _erreur;

    public EditionActiviteViewModel(ContexteApplication ctx, Activite? existante)
    {
        _ctx = ctx;
        EstNouvelle = existante is null;
        _brouillon = existante is null
            ? new Activite { Couleur = Palette[ctx.Depot.Activites.Count % Palette.Length] }
            : new Activite { Id = existante.Id, Nom = existante.Nom, Description = existante.Description, Couleur = existante.Couleur, Archivee = existante.Archivee };
        _nom = _brouillon.Nom;
        _description = _brouillon.Description ?? "";

        Couleurs = Palette.Select(h =>
        {
            OptionCouleur? o = null;
            o = new OptionCouleur(h, new RelayCommand(() => ChoisirCouleur(o!)));
            return o;
        }).ToList();
        (Couleurs.FirstOrDefault(c => string.Equals(c.Hex, _brouillon.Couleur, StringComparison.OrdinalIgnoreCase)) ?? Couleurs[0]).EstChoisie = true;

        EnregistrerCommand = new RelayCommand(Enregistrer);
        AnnulerCommand = new RelayCommand(() => Fermer?.Invoke(this, false));
        ArchiverCommand = new RelayCommand(Archiver, () => !EstNouvelle);
    }

    public event EventHandler<bool>? Fermer;
    public Guid? IdEnregistre { get; private set; }
    public bool EstNouvelle { get; }
    public bool PeutArchiver => !EstNouvelle;
    public string TitreFenetre => EstNouvelle ? "Nouvelle activité" : "Modifier l'activité";

    public string Nom { get => _nom; set => SetProperty(ref _nom, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public IReadOnlyList<OptionCouleur> Couleurs { get; }
    public string? Erreur { get => _erreur; private set { SetProperty(ref _erreur, value); OnPropertyChanged(nameof(AErreur)); } }
    public bool AErreur => !string.IsNullOrEmpty(Erreur);

    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand AnnulerCommand { get; }
    public RelayCommand ArchiverCommand { get; }

    private void ChoisirCouleur(OptionCouleur o)
    {
        foreach (var c in Couleurs) c.EstChoisie = c == o;
        _brouillon.Couleur = o.Hex;
    }

    private void Enregistrer()
    {
        _brouillon.Nom = Nom;
        _brouillon.Description = Description;
        var r = _ctx.Depot.EnregistrerActivite(_brouillon);
        if (!r.Reussi) { Erreur = r.Erreurs[0].Message; return; }
        IdEnregistre = _brouillon.Id;
        Fermer?.Invoke(this, true);
    }

    private void Archiver()
    {
        if (!_ctx.Plateforme.Confirmer("Archiver cette activité ?",
                "Elle n'apparaîtra plus dans les listes, mais son historique est conservé.", "Archiver", danger: true))
            return;
        _brouillon.Nom = Nom;
        _brouillon.Archivee = true;
        _ctx.Depot.EnregistrerActivite(_brouillon);
        Fermer?.Invoke(this, true);
    }
}
