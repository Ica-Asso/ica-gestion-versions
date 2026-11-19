using ICA.Client.Mvvm;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.ViewModels;

public sealed record OptionResponsable(Guid Id, string Nom);

public sealed class OptionInscription(Guid id, string nom, string couleur, bool estCoche) : ObservableObject
{
    private bool _estCoche = estCoche;
    public Guid Id { get; } = id;
    public string Nom { get; } = nom;
    public string Couleur { get; } = couleur;
    public bool EstCoche { get => _estCoche; set => SetProperty(ref _estCoche, value); }
}

/// <summary>Fenêtre « Nouvel adhérent » / « Modifier ».</summary>
public sealed class EditionAdherentViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private readonly Adherent _brouillon;
    private readonly bool _estNouveau;

    private string _nom, _prenom, _adresse, _complement, _codePostal, _ville, _email, _tel1, _tel2, _notes;
    private DateTime? _dateNaissance;
    private bool _estEnfant;
    private OptionResponsable? _responsable;
    private Dictionary<string, string> _erreurs = [];
    private string? _erreurGenerale;

    public EditionAdherentViewModel(ContexteApplication ctx, Adherent? existant)
    {
        _ctx = ctx;
        _estNouveau = existant is null;
        _brouillon = existant?.Copier() ?? new Adherent();

        _nom = _brouillon.Nom;
        _prenom = _brouillon.Prenom;
        _adresse = _brouillon.Adresse ?? "";
        _complement = _brouillon.ComplementAdresse ?? "";
        _codePostal = _brouillon.CodePostal ?? "";
        _ville = _brouillon.Ville ?? "";
        _email = _brouillon.Email ?? "";
        _tel1 = Telephone.Afficher(_brouillon.Telephone1);
        _tel2 = Telephone.Afficher(_brouillon.Telephone2);
        _notes = _brouillon.Notes ?? "";
        _dateNaissance = _brouillon.DateNaissance?.ToDateTime(TimeOnly.MinValue);
        _estEnfant = _brouillon.TypeAdherent == TypeAdherent.Enfant;

        Responsables = ctx.Depot.AdultesPourResponsable()
            .Where(a => a.Id != _brouillon.Id)
            .Select(a => new OptionResponsable(a.Id, a.NomAffiche))
            .ToList();
        _responsable = Responsables.FirstOrDefault(r => r.Id == _brouillon.ResponsableId);

        var inscrit = ctx.Depot.ActivitesDe(_brouillon.Id).Select(a => a.Id).ToHashSet();
        Inscriptions = ctx.Depot.Activites
            .Select(a => new OptionInscription(a.Id, a.Nom, a.Couleur ?? "#9E1B32", inscrit.Contains(a.Id))).ToList();

        EnregistrerCommand = new RelayCommand(Enregistrer);
        AnnulerCommand = new RelayCommand(() => Fermer?.Invoke(this, false));
    }

    /// <summary>Demande de fermeture de la fenêtre (vrai = enregistré).</summary>
    public event EventHandler<bool>? Fermer;

    public Guid? IdEnregistre { get; private set; }
    public string Titre => _estNouveau ? "Nouvel adhérent" : $"Modifier {_brouillon.NomAffiche}";
    public string TexteBouton => _estNouveau ? "Ajouter l'adhérent" : "Enregistrer";

    public string Nom { get => _nom; set => SetProperty(ref _nom, value); }
    public string Prenom { get => _prenom; set => SetProperty(ref _prenom, value); }
    public string Adresse { get => _adresse; set => SetProperty(ref _adresse, value); }
    public string ComplementAdresse { get => _complement; set => SetProperty(ref _complement, value); }
    public string CodePostal { get => _codePostal; set => SetProperty(ref _codePostal, value); }
    public string Ville { get => _ville; set => SetProperty(ref _ville, value); }
    public string Email { get => _email; set => SetProperty(ref _email, value); }
    public string Telephone1 { get => _tel1; set => SetProperty(ref _tel1, value); }
    public string Telephone2 { get => _tel2; set => SetProperty(ref _tel2, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public DateTime? DateNaissance { get => _dateNaissance; set => SetProperty(ref _dateNaissance, value); }

    public bool EstEnfant
    {
        get => _estEnfant;
        set
        {
            if (!SetProperty(ref _estEnfant, value)) return;
            OnPropertyChanged(nameof(EstAdulte));
        }
    }
    public bool EstAdulte { get => !_estEnfant; set { if (value) EstEnfant = false; } }

    public IReadOnlyList<OptionResponsable> Responsables { get; }
    public IReadOnlyList<OptionInscription> Inscriptions { get; }
    public bool AActivites => Inscriptions.Count > 0;

    public OptionResponsable? Responsable
    {
        get => _responsable;
        set
        {
            if (!SetProperty(ref _responsable, value) || value is null) return;
            // Un enfant habite généralement chez son responsable : on reprend l'adresse si elle est vide.
            if (string.IsNullOrWhiteSpace(Adresse) && _ctx.Depot.TrouverAdherent(value.Id) is { } r)
            {
                Adresse = r.Adresse ?? "";
                ComplementAdresse = r.ComplementAdresse ?? "";
                CodePostal = r.CodePostal ?? "";
                Ville = r.Ville ?? "";
            }
        }
    }

    public string? ErreurNom => Erreur("Nom");
    public string? ErreurPrenom => Erreur("Prenom");
    public string? ErreurEmail => Erreur("Email");
    public string? ErreurTelephone1 => Erreur("Telephone1");
    public string? ErreurTelephone2 => Erreur("Telephone2");
    public string? ErreurCodePostal => Erreur("CodePostal");
    public string? ErreurDateNaissance => Erreur("DateNaissance");
    public string? ErreurResponsable => Erreur("ResponsableId");
    public string? ErreurGenerale { get => _erreurGenerale; private set { SetProperty(ref _erreurGenerale, value); OnPropertyChanged(nameof(AErreurGenerale)); } }
    public bool AErreurGenerale => !string.IsNullOrEmpty(ErreurGenerale);

    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand AnnulerCommand { get; }

    private string? Erreur(string champ) => _erreurs.GetValueOrDefault(champ);

    private void Enregistrer()
    {
        var a = _brouillon.Copier();
        a.Nom = Nom;
        a.Prenom = Prenom;
        a.Adresse = Adresse;
        a.ComplementAdresse = ComplementAdresse;
        a.CodePostal = CodePostal;
        a.Ville = Ville;
        a.Email = Email;
        a.Telephone1 = Telephone1;
        a.Telephone2 = Telephone2;
        a.Notes = Notes;
        a.DateNaissance = DateNaissance is { } d ? DateOnly.FromDateTime(d) : null;
        a.TypeAdherent = EstEnfant ? TypeAdherent.Enfant : TypeAdherent.Adulte;
        a.ResponsableId = EstEnfant ? Responsable?.Id : null;

        if (_estNouveau)
        {
            var copiePourTest = a.Copier();
            RegleAdherent.Normaliser(copiePourTest);
            var doublons = _ctx.Depot.DoublonsPossibles(copiePourTest);
            if (doublons.Count > 0 && !_ctx.Plateforme.Confirmer(
                    "Cet adhérent existe peut-être déjà",
                    $"Une fiche « {doublons[0].NomAffiche} » existe déjà. Voulez-vous quand même créer une nouvelle fiche ?",
                    "Créer quand même"))
                return;
        }

        var resultat = _ctx.Depot.EnregistrerAdherent(a);
        _erreurs = resultat.Erreurs.GroupBy(e => e.Champ).ToDictionary(g => g.Key, g => g.First().Message);
        foreach (var champ in new[] { nameof(ErreurNom), nameof(ErreurPrenom), nameof(ErreurEmail), nameof(ErreurTelephone1),
                     nameof(ErreurTelephone2), nameof(ErreurCodePostal), nameof(ErreurDateNaissance), nameof(ErreurResponsable) })
            OnPropertyChanged(champ);

        var connus = new HashSet<string> { "Nom", "Prenom", "Email", "Telephone1", "Telephone2", "CodePostal", "DateNaissance", "ResponsableId" };
        var autres = resultat.Erreurs.Where(e => !connus.Contains(e.Champ)).Select(e => e.Message).ToList();
        ErreurGenerale = resultat.Reussi
            ? null
            : autres.Count > 0 ? string.Join("\n", autres) : "Vérifiez les champs signalés en rouge.";

        if (!resultat.Reussi) return;
        _ctx.Depot.DefinirActivites(a.Id, Inscriptions.Where(i => i.EstCoche).Select(i => i.Id).ToHashSet());
        IdEnregistre = a.Id;
        Fermer?.Invoke(this, true);
    }
}
