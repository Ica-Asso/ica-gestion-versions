using ICA.Client.Configuration;
using ICA.Client.Journal;
using ICA.Client.Mvvm;
using ICA.Client.Plateforme;
using ICA.Client.Services;

namespace ICA.Client.ViewModels;

public static class Pages
{
    public const string Accueil = "accueil";
    public const string Adherents = "adherents";
    public const string Messagerie = "messagerie";
    public const string Cotisations = "cotisations";
    public const string Activites = "activites";
    public const string Dons = "dons";
    public const string Parametres = "parametres";
}

public sealed class ElementNavigation(string id, string libelle) : ObservableObject
{
    private bool _estActif;
    public string Id { get; } = id;
    public string Libelle { get; } = libelle;
    public bool EstActif { get => _estActif; set => SetProperty(ref _estActif, value); }
}

/// <summary>Contexte partagé par tous les écrans.</summary>
public sealed class ContexteApplication(DepotIca depot, IServicesPlateforme plateforme, ParametresApplication parametres, IJournal journal)
{
    public DepotIca Depot { get; } = depot;
    public IServicesPlateforme Plateforme { get; } = plateforme;
    public ParametresApplication Parametres { get; } = parametres;
    public IJournal Journal { get; } = journal;

    /// <summary>Réglages propres à cet ordinateur (messagerie…).</summary>
    public PreferencesLocales Preferences { get; init; } = new();

    /// <summary>%LOCALAPPDATA%\\L-ICA\\backups</summary>
    public string DossierSauvegardes { get; init; } = "";

    /// <summary>%LOCALAPPDATA%\\L-ICA\\justificatifs : copies PDF des documents remis.</summary>
    public string? DossierJustificatifs { get; init; }

    /// <summary>Logo de L'ICA (JPEG) pour les documents PDF.</summary>
    public byte[]? LogoJpeg { get; init; }

    /// <summary>Justificatifs de dons (numérotation, PDF, envoi).</summary>
    public ServiceJustificatifs? Justificatifs { get; init; }

    /// <summary>Compte Google de l'association (null si non configuré).</summary>
    public ICA.Client.Google.CompteGoogle? Google { get; init; }
    public ICA.Client.Google.SynchroDrive? Synchro { get; init; }
    public ICA.Client.Google.EnvoiGmail? Gmail { get; init; }

    /// <summary>Mises à jour automatiques (null : version de développement).</summary>
    public ICA.Client.MisesAJour.VerificateurMisesAJour? MisesAJour { get; init; }
}

public sealed class PrincipalViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private object _pageCourante;

    public PrincipalViewModel(ContexteApplication ctx)
    {
        _ctx = ctx;
        Navigation =
        [
            new(Pages.Accueil, "Accueil"),
            new(Pages.Adherents, "Adhérents"),
            new(Pages.Messagerie, "Messagerie"),
            new(Pages.Cotisations, "Cotisations"),
            new(Pages.Activites, "Activités"),
            new(Pages.Dons, "Dons & Mécénat"),
            new(Pages.Parametres, "Paramètres"),
        ];

        Accueil = new AccueilViewModel(ctx, this);
        Adherents = new AdherentsViewModel(ctx, this);
        Cotisations = new CotisationsViewModel(ctx, this);
        Parametres = new ParametresViewModel(ctx, this);
        Messagerie = new MessagerieViewModel(ctx, this);
        Activites = new ActivitesViewModel(ctx, this);
        Dons = new DonsViewModel(ctx);

        NaviguerCommand = new RelayCommand(p => AllerA(p as string ?? Pages.Accueil));
        MettreAJourCommand = new CommandeAsync(MettreAJourAsync, () => _miseAJour is not null);
        AssistanceCommand = new RelayCommand(() => _ctx.Plateforme.OuvrirAssistance());

        _ctx.Depot.Modifie += (_, _) => RafraichirEtat();
        if (_ctx.Synchro is not null) _ctx.Synchro.EtatChange += (_, _) => RafraichirEtat();

        _pageCourante = Accueil;
        Navigation[0].EstActif = true;
    }

    public IReadOnlyList<ElementNavigation> Navigation { get; }
    public AccueilViewModel Accueil { get; }
    public AdherentsViewModel Adherents { get; }
    public CotisationsViewModel Cotisations { get; }
    public ParametresViewModel Parametres { get; }
    public MessagerieViewModel Messagerie { get; }
    public ActivitesViewModel Activites { get; }
    public DonsViewModel Dons { get; }

    public object PageCourante { get => _pageCourante; private set => SetProperty(ref _pageCourante, value); }

    public bool EstDemo => _ctx.Depot.EstDemo;
    public bool EstReel => !EstDemo;

    private ICA.Client.Google.EtatSynchro EtatSync => _ctx.Synchro?.Etat ?? ICA.Client.Google.EtatSynchro.NonConnecte;

    public string EtatTexte => EstDemo && EtatSync == ICA.Client.Google.EtatSynchro.NonConnecte ? "Mode démonstration" : EtatSync switch
    {
        ICA.Client.Google.EtatSynchro.AJour => "Synchronisé",
        ICA.Client.Google.EtatSynchro.EnCours => "Synchronisation…",
        ICA.Client.Google.EtatSynchro.HorsConnexion => "Hors connexion",
        ICA.Client.Google.EtatSynchro.Erreur => "Synchro à réessayer",
        _ => "Sur cet ordinateur",
    };

    public string EtatDetail => EstDemo && EtatSync == ICA.Client.Google.EtatSynchro.NonConnecte
        ? "Adhérents fictifs. Paramètres > Importer mes adhérents pour commencer."
        : EtatSync switch
        {
            ICA.Client.Google.EtatSynchro.NonConnecte => "Données sur cet ordinateur uniquement. Paramètres > Se connecter avec Google pour synchroniser vos ordinateurs.",
            ICA.Client.Google.EtatSynchro.AJour => $"Vos ordinateurs sont à jour (dernière synchronisation à {_ctx.Synchro?.DerniereReussite:HH:mm}).",
            _ => _ctx.Synchro?.Message ?? "",
        };

    /// <summary>Couleur du voyant : vert (tout va bien), orange (démo, en cours), rouge (hors connexion, erreur).</summary>
    public bool VoyantVert => !VoyantOrange && !VoyantRouge;
    public bool VoyantOrange => (EstDemo && EtatSync == ICA.Client.Google.EtatSynchro.NonConnecte) || EtatSync == ICA.Client.Google.EtatSynchro.EnCours;
    public bool VoyantRouge => EtatSync is ICA.Client.Google.EtatSynchro.HorsConnexion or ICA.Client.Google.EtatSynchro.Erreur;

    private void RafraichirEtat()
    {
        foreach (var n in new[] { nameof(EstDemo), nameof(EstReel), nameof(EtatTexte), nameof(EtatDetail), nameof(VoyantVert), nameof(VoyantOrange), nameof(VoyantRouge) })
            OnPropertyChanged(n);
    }

    // ---------- Mises à jour ----------

    private ICA.Client.MisesAJour.VersionDisponible? _miseAJour;
    private string? _progressionMiseAJour;

    public bool MiseAJourDisponible => _miseAJour is not null;
    public string TitreMiseAJour => _miseAJour is null ? "" : $"Nouvelle version {_miseAJour.Libelle}";
    public string TexteBoutonMiseAJour => _progressionMiseAJour ?? "Mettre à jour";
    public CommandeAsync MettreAJourCommand { get; }

    /// <summary>Cherche une nouvelle version. manuel = demandé depuis les Paramètres (on répond dans tous les cas).</summary>
    public async Task VerifierMiseAJourAsync(bool manuel)
    {
        if (_ctx.MisesAJour is not { } maj)
        {
            if (manuel) _ctx.Plateforme.Informer("Mises à jour", "Les mises à jour automatiques ne sont pas actives sur cette version (version de développement).");
            return;
        }
        try
        {
            var v = await maj.ChercherAsync();
            if (v is not null)
            {
                _miseAJour = v;
                _ctx.Journal.Info($"Mise à jour disponible : {v.Libelle}");
                foreach (var n in new[] { nameof(MiseAJourDisponible), nameof(TitreMiseAJour), nameof(TexteBoutonMiseAJour) }) OnPropertyChanged(n);
                MettreAJourCommand.NotifierChangement();
                if (manuel) await MettreAJourAsync();
            }
            else if (manuel)
                _ctx.Plateforme.Informer("Mises à jour", $"Vous avez la dernière version ({_ctx.Plateforme.VersionApplication}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or FormatException or OverflowException)
        {
            _ctx.Journal.Avertissement($"Recherche de mise à jour impossible : {ex.GetType().Name}");
            if (manuel) _ctx.Plateforme.Informer("Mises à jour", "Impossible de vérifier pour le moment. Vérifiez la connexion Internet et réessayez.");
        }
    }

    private async Task MettreAJourAsync()
    {
        if (_miseAJour is not { } v || _ctx.MisesAJour is not { } maj) return;
        var message = $"La version {v.Libelle} de L'ICA Gestion est disponible (vous avez la {_ctx.Plateforme.VersionApplication})."
                      + (v.Notes.Length > 0 ? $"\n\nNouveautés :\n{v.Notes}" : "")
                      + "\n\nL'application va se fermer quelques secondes puis se rouvrir. Vos adhérents ne sont pas touchés.";
        if (!_ctx.Plateforme.Confirmer("Mettre à jour L'ICA Gestion ?", message, "Mettre à jour")) return;

        try
        {
            var progression = new Progress<int>(p => { _progressionMiseAJour = $"Téléchargement… {p} %"; OnPropertyChanged(nameof(TexteBoutonMiseAJour)); });
            _progressionMiseAJour = "Téléchargement…";
            OnPropertyChanged(nameof(TexteBoutonMiseAJour));
            var fichier = await maj.TelechargerAsync(v, progression);

            // Dernières modifications envoyées avant de fermer.
            if (_ctx.Synchro is { } synchro)
                await Task.WhenAny(synchro.SynchroniserAsync(), Task.Delay(TimeSpan.FromSeconds(15)));

            _progressionMiseAJour = "Installation…";
            OnPropertyChanged(nameof(TexteBoutonMiseAJour));
            _ctx.Journal.Info($"Installation de la mise à jour {v.Libelle}");
            _ctx.Plateforme.InstallerMiseAJour(fichier);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _ctx.Journal.Erreur("Mise à jour impossible", ex);
            _progressionMiseAJour = null;
            OnPropertyChanged(nameof(TexteBoutonMiseAJour));
            _ctx.Plateforme.Informer("Mise à jour impossible",
                ex is InvalidDataException ? ex.Message : "La mise à jour n'a pas pu être installée. Vérifiez la connexion Internet et réessayez plus tard.\nVotre version actuelle continue de fonctionner.");
        }
    }

    public RelayCommand NaviguerCommand { get; }
    public RelayCommand AssistanceCommand { get; }

    public void AllerA(string page)
    {
        if (Navigation.All(e => e.Id != page)) page = Pages.Accueil;
        PageCourante = page switch
        {
            Pages.Adherents => Adherents,
            Pages.Cotisations => Cotisations,
            Pages.Messagerie => Messagerie,
            Pages.Activites => Activites,
            Pages.Dons => Dons,
            Pages.Parametres => Parametres,
            _ => Accueil,
        };
        foreach (var e in Navigation) e.EstActif = e.Id == page;
    }

    public void AllerAuxAdherents(string? recherche = null, Guid? selection = null)
    {
        Adherents.Preparer(recherche, selection);
        AllerA(Pages.Adherents);
    }

    public void AllerAuxCotisations(Guid? adherentAEncaisser = null)
    {
        Cotisations.Preparer(adherentAEncaisser);
        AllerA(Pages.Cotisations);
    }

    /// <summary>Ouvre la fenêtre « Nouveau message » avec un modèle pré-choisi.</summary>
    public void EcrireA(IReadOnlyList<ICA.Domain.Adherent> destinataires, string? nomModele = null, string? saison = null,
        string? objetImpose = null, string? corpsImpose = null)
    {
        if (destinataires.Count == 0) return;
        if (destinataires.All(a => _ctx.Depot.EmailDeContact(a) is null))
        {
            _ctx.Plateforme.Informer("Pas d'adresse e-mail",
                destinataires.Count == 1 ? "Cet adhérent n'a pas d'adresse e-mail." : "Aucun de ces adhérents n'a d'adresse e-mail.");
            return;
        }
        _ctx.Plateforme.ComposerMessage(new ComposerMessageViewModel(_ctx, destinataires, nomModele, saison, objetImpose, corpsImpose));
    }

    /// <summary>Ouvre la fenêtre « Nouvel adhérent » ; après enregistrement, affiche sa fiche.</summary>
    public void AjouterAdherent()
    {
        var vm = new EditionAdherentViewModel(_ctx, null);
        if (_ctx.Plateforme.EditerAdherent(vm) && vm.IdEnregistre is { } id)
            AllerAuxAdherents(selection: id);
    }
}

public sealed class PageEnPreparationViewModel(string titre, string description, string enAttendant)
{
    public string Titre { get; } = titre;
    public string Description { get; } = description;
    public string EnAttendant { get; } = enAttendant;
}
