using ICA.Client.Mvvm;
using ICA.Client.Services;

namespace ICA.Client.ViewModels;

public sealed class ParametresViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private readonly PrincipalViewModel _principal;

    public ParametresViewModel(ContexteApplication ctx, PrincipalViewModel principal)
    {
        _ctx = ctx;
        _principal = principal;
        ReinitialiserCommand = new RelayCommand(Reinitialiser);
        ImporterCommand = new RelayCommand(Importer);
        SauvegarderCommand = new RelayCommand(Sauvegarder);
        RestaurerCommand = new RelayCommand(Restaurer);
        ConnecterGoogleCommand = new CommandeAsync(ConnecterGoogleAsync, () => _ctx.Google?.EstConfigure == true);
        SynchroniserCommand = new CommandeAsync(() => _ctx.Synchro?.SynchroniserAsync() ?? Task.CompletedTask);
        DeconnecterGoogleCommand = new CommandeAsync(DeconnecterGoogleAsync);
        if (_ctx.Google is not null) _ctx.Google.Change += (_, _) => RafraichirGoogle();
        if (_ctx.Synchro is not null) _ctx.Synchro.EtatChange += (_, _) => RafraichirGoogle();
        _ctx.Depot.Modifie += (_, _) =>
        {
            OnPropertyChanged(nameof(EstDemo));
            OnPropertyChanged(nameof(EstReel));
            OnPropertyChanged(nameof(TexteCompte));
            OnPropertyChanged(nameof(DerniereCopie));
            OnPropertyChanged(nameof(TexteAssociation));
            OnPropertyChanged(nameof(TexteStatutFiscal));
        };
        ModifierAssociationCommand = new RelayCommand(() => _ctx.Plateforme.OuvrirFormulaire(new EditionAssociationViewModel(_ctx)));
        OuvrirDossierCommand = new RelayCommand(() =>
            _ctx.Plateforme.OuvrirLien(Path.GetDirectoryName(_ctx.Depot.EmplacementDonnees) ?? ""));
        AssistanceCommand = new RelayCommand(() => _ctx.Plateforme.OuvrirAssistance());
    }

    public CommandeAsync RechercherMiseAJourCommand => _rechercherMaj ??= new CommandeAsync(() => _principal.VerifierMiseAJourAsync(manuel: true));
    private CommandeAsync? _rechercherMaj;
    // ---------- Association et reçus fiscaux ----------

    public RelayCommand ModifierAssociationCommand { get; }

    public string TexteAssociation
    {
        get
        {
            var i = _ctx.Depot.Association.Identite;
            var id = string.Join(" — ", new[] { string.IsNullOrWhiteSpace(i.Siren) ? null : "SIREN " + ICA.Domain.Siren.Afficher(i.Siren),
                string.IsNullOrWhiteSpace(i.Rna) ? null : "RNA " + i.Rna }.Where(s => s is not null));
            return $"{i.Denomination}{(id.Length > 0 ? " — " + id : "")}{(string.IsNullOrWhiteSpace(i.Ville) ? "" : " — " + i.Ville)}";
        }
    }

    public string TexteStatutFiscal => _ctx.Depot.Association.Eligibilite switch
    {
        ICA.Domain.EligibiliteFiscale.Confirmee when _ctx.Depot.RecusFiscauxAutorises =>
            "Éligibilité au mécénat confirmée par l'association, spécimen vérifié : reçus fiscaux possibles. " +
            "(Ce choix ne vaut pas validation par l'administration fiscale.)",
        ICA.Domain.EligibiliteFiscale.Confirmee => "Éligibilité confirmée, mais reçus fiscaux bloqués. Il manque : " + string.Join(", ", _ctx.Depot.ManquesRecuFiscal) + ".",
        ICA.Domain.EligibiliteFiscale.Desactivee => "Reçus fiscaux désactivés : seules les attestations simples sont proposées.",
        _ => "Éligibilité non vérifiée : seules les attestations simples sont proposées.",
    };

    public string Version => _ctx.Plateforme.VersionApplication;
    public bool EstDemo => _ctx.Depot.EstDemo;
    public bool EstReel => !EstDemo;
    public string TexteCompte => EstDemo
        ? "Ce sont des adhérents fictifs. Importez votre tableau Excel pour commencer avec vos vrais adhérents."
        : "Vos données sont enregistrées sur cet ordinateur. La connexion au compte L'ICA et la synchronisation automatique entre vos ordinateurs arrivent dans une prochaine version : en attendant, utilisez L'ICA Gestion sur un seul ordinateur.";
    public string DerniereCopie => Sauvegardes.DerniereCopie(_ctx.DossierSauvegardes) is { } d
        ? $"Copie de sécurité automatique : chaque jour (dernière le {d:dd/MM/yyyy à HH:mm})."
        : "Copie de sécurité automatique : chaque jour, au démarrage.";
    public string Emplacement => _ctx.Depot.EmplacementDonnees;
    public string EmailSupport => string.IsNullOrWhiteSpace(_ctx.Parametres.Support.Email) ? "(non renseigné)" : _ctx.Parametres.Support.Email;
    public string Tarifs => $"Adulte : {Libelles.Montant(_ctx.Parametres.Cotisations.TarifAdulte)} · Enfant : {Libelles.Montant(_ctx.Parametres.Cotisations.TarifEnfant)}";
    public string Saison => $"{_ctx.Depot.SaisonCourante} ({(_ctx.Parametres.Cotisations.ModeSaison == Contracts.ModeSaison.Scolaire ? "septembre → août" : "janvier → décembre")})";

    public bool MessagerieGmail
    {
        get => _ctx.Preferences.Messagerie == ModeMessagerie.Gmail;
        set { if (value) ChangerMessagerie(ModeMessagerie.Gmail); }
    }

    public bool MessagerieApplication
    {
        get => _ctx.Preferences.Messagerie == ModeMessagerie.Application;
        set { if (value) ChangerMessagerie(ModeMessagerie.Application); }
    }

    private void ChangerMessagerie(ModeMessagerie mode)
    {
        if (_ctx.Preferences.Messagerie == mode) return;
        _ctx.Preferences.Messagerie = mode;
        _ctx.Preferences.Enregistrer();
        _ctx.Journal.Info($"Messagerie : {mode}");
        OnPropertyChanged(nameof(MessagerieGmail));
        OnPropertyChanged(nameof(MessagerieApplication));
    }

    // ---------- Compte Google ----------

    public bool GoogleConfigure => _ctx.Google?.EstConfigure == true;
    public bool GoogleNonConfigure => !GoogleConfigure;
    public bool GoogleConnecte => _ctx.Google?.EstConnecte == true;
    public bool GoogleDeconnecte => GoogleConfigure && !GoogleConnecte;
    public string? EmailGoogle => _ctx.Google?.Email;
    public string TexteSynchro => _ctx.Synchro switch
    {
        null => "",
        { Etat: Google.EtatSynchro.EnCours } => "Synchronisation en cours…",
        { Etat: Google.EtatSynchro.AJour, DerniereReussite: { } d } s => (s.Message ?? "Vos ordinateurs sont à jour.") + $" Dernière synchronisation : {d:HH:mm}.",
        var s => s.Message ?? "",
    };

    public CommandeAsync ConnecterGoogleCommand { get; }
    public CommandeAsync SynchroniserCommand { get; }
    public CommandeAsync DeconnecterGoogleCommand { get; }

    private void RafraichirGoogle()
    {
        foreach (var n in new[] { nameof(GoogleConfigure), nameof(GoogleNonConfigure), nameof(GoogleConnecte), nameof(GoogleDeconnecte), nameof(EmailGoogle), nameof(TexteSynchro) })
            OnPropertyChanged(n);
    }

    private async Task ConnecterGoogleAsync()
    {
        if (_ctx.Google is null) return;
        var demo = _ctx.Depot.EstDemo;
        try
        {
            await _ctx.Google.ConnecterAsync(CancellationToken.None);
        }
        catch (OperationCanceledException ex)
        {
            _ctx.Plateforme.Informer("Connexion non terminée", string.IsNullOrEmpty(ex.Message) || ex.Message.StartsWith("The operation", StringComparison.Ordinal)
                ? "La connexion à Google n'a pas été terminée. Vous pouvez réessayer."
                : ex.Message);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            _ctx.Journal.Erreur("Connexion Google impossible", ex);
            _ctx.Plateforme.Informer("Connexion impossible", "La connexion à Google a échoué. Vérifiez Internet puis réessayez.");
            return;
        }

        if (_ctx.Synchro is not null) await _ctx.Synchro.SynchroniserAsync();
        RafraichirGoogle();
        _ctx.Plateforme.Informer("Compte Google connecté",
            demo && !_ctx.Depot.EstDemo
                ? $"Les données de L'ICA ont été récupérées : {_ctx.Depot.Adherents.Count} adhérents. Cet ordinateur est maintenant synchronisé."
                : demo
                    ? "Compte relié. Importez maintenant vos adhérents : ils seront ensuite disponibles sur vos autres ordinateurs."
                    : "Cet ordinateur est maintenant synchronisé avec vos autres ordinateurs. Les mails peuvent aussi partir directement depuis votre Gmail.");
    }

    private async Task DeconnecterGoogleAsync()
    {
        if (_ctx.Google is null) return;
        if (!_ctx.Plateforme.Confirmer("Déconnecter le compte Google ?",
                "Cet ordinateur ne sera plus synchronisé. Vos données restent sur cet ordinateur et dans votre Google Drive.", "Déconnecter"))
            return;
        await _ctx.Google.DeconnecterAsync();
        RafraichirGoogle();
    }

    public RelayCommand ImporterCommand { get; }
    public RelayCommand SauvegarderCommand { get; }
    public RelayCommand RestaurerCommand { get; }
    public RelayCommand ReinitialiserCommand { get; }
    public RelayCommand OuvrirDossierCommand { get; }
    public RelayCommand AssistanceCommand { get; }

    private void Importer()
    {
        var chemin = _ctx.Plateforme.ChoisirFichierAOuvrir("Choisir le tableau des adhérents",
            "Tableau Excel ou CSV|*.xlsx;*.xlsm;*.csv|Tous les fichiers|*.*");
        if (chemin is null) return;
        if (_ctx.Plateforme.OuvrirImport(new ImportViewModel(_ctx, chemin)))
        {
            _ctx.Plateforme.Informer("Import terminé",
                $"{_ctx.Depot.Adherents.Count} adhérents sont maintenant dans L'ICA Gestion.\n" +
                "Les fiches à vérifier ont une remarque dans leurs notes.");
            _principal.AllerAuxAdherents();
        }
    }

    private void Sauvegarder()
    {
        var chemin = _ctx.Plateforme.ChoisirFichierAEnregistrer("Sauvegarder mes données",
            $"ICA_Sauvegarde_{_ctx.Depot.AujourdHui:yyyy-MM-dd}.zip", "Sauvegarde L'ICA Gestion|*.zip");
        if (chemin is null) return;
        try
        {
            Sauvegardes.Exporter(_ctx.Depot.EmplacementDonnees, chemin, _ctx.Depot.Adherents.Count, _ctx.DossierJustificatifs);
            _ctx.Journal.Info("Sauvegarde manuelle effectuée");
            _ctx.Plateforme.Informer("Sauvegarde terminée",
                $"Vos données ont été enregistrées dans « {Path.GetFileName(chemin)} ».\nGardez ce fichier en lieu sûr : il contient des données personnelles.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ctx.Journal.Erreur("Échec de la sauvegarde manuelle", ex);
            _ctx.Plateforme.Informer("Sauvegarde impossible", "Vérifiez que la clé USB est bien branchée, puis réessayez.");
        }
    }

    private void Restaurer()
    {
        var chemin = _ctx.Plateforme.ChoisirFichierAOuvrir("Choisir une sauvegarde", "Sauvegarde L'ICA Gestion|*.zip;*.json");
        if (chemin is null) return;
        try
        {
            var contenu = Sauvegardes.Lire(chemin);
            var nb = contenu.Adherents.Count(a => !a.EstSupprime);
            if (!_ctx.Plateforme.Confirmer("Restaurer cette sauvegarde ?",
                    $"Elle contient {nb} adhérents.\nVos données actuelles seront remplacées (une copie en est gardée par sécurité).",
                    "Restaurer", danger: true))
                return;
            Sauvegardes.Restaurer(chemin, _ctx.Depot.EmplacementDonnees, _ctx.DossierSauvegardes, _ctx.DossierJustificatifs);
            _ctx.Depot.Recharger();
            _ctx.Plateforme.Informer("Restauration terminée", $"{nb} adhérents ont été restaurés.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _ctx.Journal.Erreur("Échec de la restauration", ex);
            _ctx.Plateforme.Informer("Restauration impossible", ex is InvalidDataException ? ex.Message : "Le fichier n'a pas pu être lu.");
        }
    }

    private void Reinitialiser()
    {
        if (_ctx.Plateforme.Confirmer("Réinitialiser la démonstration ?",
                "Toutes les modifications faites dans cette démonstration seront effacées et les adhérents fictifs recréés.",
                "Réinitialiser", danger: true))
            _ctx.Depot.ReinitialiserDemo();
    }
}

/// <summary>Fenêtre « Un problème ? On s'en occupe. » (ARCHITECTURE.md §17 bis).</summary>
public sealed class AssistanceViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private readonly string _identifiant;
    private string _message = "";
    private bool _joindreCapture;
    private bool _apercuVisible;
    private string? _confirmation;

    public AssistanceViewModel(ContexteApplication ctx, bool apresPlantage)
    {
        _ctx = ctx;
        ApresPlantage = apresPlantage;
        _identifiant = RapportAssistance.NouvelIdentifiant(DateTimeOffset.Now);
        EnvoyerCommand = new CommandeAsync(EnvoyerAsync, () => !Envoye);
        BasculerApercuCommand = new RelayCommand(() => ApercuVisible = !ApercuVisible);
        FermerCommand = new RelayCommand(() => Fermer?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? Fermer;

    public bool ApresPlantage { get; }
    public string Titre => ApresPlantage ? "L'ICA Gestion s'est fermée de façon inattendue" : "Un problème ? On s'en occupe.";
    public string SousTitre => ApresPlantage
        ? "Prévenez le responsable technique en un clic : il recevra les informations utiles pour corriger."
        : "Votre message part directement chez le responsable technique.";

    public string Message { get => _message; set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(Apercu)); } }
    public bool JoindreCapture { get => _joindreCapture; set => SetProperty(ref _joindreCapture, value); }
    public bool ApercuVisible { get => _apercuVisible; set { SetProperty(ref _apercuVisible, value); OnPropertyChanged(nameof(TexteApercu)); } }
    public string TexteApercu => ApercuVisible ? "Masquer ce qui sera envoyé" : "Voir ce qui sera envoyé";
    public string Apercu => CreerRapport().Complet();

    public string? Confirmation
    {
        get => _confirmation;
        private set { SetProperty(ref _confirmation, value); OnPropertyChanged(nameof(Envoye)); OnPropertyChanged(nameof(PasEnvoye)); EnvoyerCommand.NotifierChangement(); }
    }
    public bool Envoye => Confirmation is not null;
    public bool PasEnvoye => !Envoye;

    public CommandeAsync EnvoyerCommand { get; }
    public RelayCommand BasculerApercuCommand { get; }
    public RelayCommand FermerCommand { get; }

    private RapportAssistance CreerRapport() => new()
    {
        Identifiant = _identifiant,
        Message = Message,
        ApresPlantage = ApresPlantage,
        Informations = new Dictionary<string, string>
        {
            ["Date"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
            ["Application"] = $"L'ICA Gestion {_ctx.Plateforme.VersionApplication}",
            ["Système"] = _ctx.Plateforme.SystemeExploitation,
            ["Appareil"] = _ctx.Plateforme.NomAppareil,
            ["Mode"] = _ctx.Depot.EstDemo ? "Démonstration (données fictives)" : "Données réelles, locales (pas encore de serveur)",
            ["Fiches"] = _ctx.Depot.Adherents.Count.ToString(),
            ["Synchronisation"] = DecrireSynchro(),
            ["Compte Google"] = _ctx.Google?.EstConnecte == true ? "connecté" : "non connecté",
        },
        Journal = _ctx.Journal.DernieresLignes(200),
    };

    private string DecrireSynchro()
    {
        if (_ctx.Synchro is null) return "Non configurée";
        var derniere = _ctx.Synchro.DerniereReussite is { } d ? $" (dernière réussite {d:dd/MM HH:mm})" : "";
        return _ctx.Synchro.Etat + derniere;
    }

    private async Task EnvoyerAsync()
    {
        try
        {
            Confirmation = await _ctx.Plateforme.EnvoyerAssistanceAsync(CreerRapport(), JoindreCapture);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            _ctx.Journal.Erreur("Échec de la préparation du rapport d'assistance", ex);
            Confirmation = $"Le rapport n'a pas pu être préparé. Écrivez directement à {_ctx.Parametres.Support.Email} " +
                           $"en indiquant le code {_identifiant}.";
        }
    }
}
