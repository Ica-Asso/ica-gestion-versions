using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using ICA.Client.Configuration;
using ICA.Client.Donnees;
using ICA.Client.Google;
using ICA.Client.Journal;
using ICA.Client.Services;
using ICA.Client.ViewModels;
using ICA.Desktop.Fenetres;
using ICA.Desktop.Plateforme;

namespace ICA.Desktop;

public partial class App : Application
{
    private IJournal? _journal;
    private ServicesWindows? _plateforme;
    private string? _marqueurSession;
    private SynchroDrive? _synchro;
    private DispatcherTimer? _minuterie;
    private DispatcherTimer? _apresModification;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
    public const string ArgumentApresMiseAJour = "--apres-mise-a-jour";
    private static Mutex? _instanceUnique;
    private DispatcherTimer? _minuterieMaj;
    private static EventWaitHandle? _signalPremierPlan;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Toutes les fenêtres (principale, fiches, messages…) restent entières dans l'écran où elles s'ouvrent.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window f) Outils.Ecrans.GarderDans(f); }));

        // Une seule fenêtre à la fois : deux exemplaires écriraient les mêmes données.
        // Un second lancement ramène simplement la fenêtre existante au premier plan.
        _instanceUnique = new Mutex(true, @"Local\L-ICA-Gestion", out var premier);
        _signalPremierPlan = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\L-ICA-Gestion-premier-plan");
        var apresMiseAJour = e.Args.Contains(ArgumentApresMiseAJour);
        if (!premier && apresMiseAJour)
        {
            // L'ancienne version est en train de se fermer : on attend qu'elle ait fini.
            try { premier = _instanceUnique.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { premier = true; }
        }
        if (!premier)
        {
            _signalPremierPlan.Set();
            Shutdown();
            return;
        }
        var signal = _signalPremierPlan;
        new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is not { } f) return;
                    if (f.WindowState == WindowState.Minimized) f.WindowState = WindowState.Normal;
                    f.Activate();
                    f.Topmost = true;
                    f.Topmost = false;
                    f.Focus();
                });
        }) { IsBackground = true, Name = "Premier plan" }.Start();

        // Français partout (dates, calendrier, nombres).
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = fr;
        CultureInfo.DefaultThreadCurrentUICulture = fr;
        Thread.CurrentThread.CurrentCulture = fr;
        Thread.CurrentThread.CurrentUICulture = fr;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(fr.IetfLanguageTag)));

        // %LOCALAPPDATA%\L-ICA\ (CDC §55)
        var racine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "L-ICA");
        foreach (var dossier in new[] { "cache", "logs", "backups", "config" })
            Directory.CreateDirectory(Path.Combine(racine, dossier));

        _journal = new JournalFichier(Path.Combine(racine, "logs"));
        DispatcherUnhandledException += ErreurInterface;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _journal.Erreur("Erreur fatale", args.ExceptionObject as Exception);

        // Si le marqueur existe encore, la session précédente ne s'est pas fermée normalement.
        _marqueurSession = Path.Combine(racine, "config", "session-en-cours");
        var plantagePrecedent = File.Exists(_marqueurSession);
        File.WriteAllText(_marqueurSession, DateTime.Now.ToString("O"));

        var parametres = ParametresApplication.Charger(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        _plateforme = new ServicesWindows();
        _journal.Info($"Démarrage L'ICA Gestion {_plateforme.VersionApplication}");

        DepotIca depot;
        try
        {
            // Ancien nom du fichier (versions de démonstration) : on le renomme une fois.
            var fichier = Path.Combine(racine, "cache", "donnees.json");
            var ancien = Path.Combine(racine, "cache", "donnees-demo.json");
            if (!File.Exists(fichier) && File.Exists(ancien)) File.Move(ancien, fichier);

            depot = new DepotIca(new StockageJson(fichier, _journal), _journal, parametres);
            try { Sauvegardes.CopieQuotidienne(fichier, Path.Combine(racine, "backups")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _journal.Erreur("Copie de sécurité quotidienne impossible", ex); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _journal.Erreur("Impossible d'ouvrir les données locales", ex);
            FenetreMessage.Informer(null, "Démarrage impossible",
                "L'ICA Gestion n'arrive pas à lire ses données sur cet ordinateur. Redémarrez l'ordinateur puis réessayez.");
            Shutdown(1);
            return;
        }

        // Compte Google : identifiants du projet (google-client.json à côté de l'application) + accès chiffré (DPAPI).
        var ids = IdentifiantsClient.Charger(Path.Combine(AppContext.BaseDirectory, "google-client.json"));
        var google = new CompteGoogle(ids, new CoffreWindows(Path.Combine(racine, "config", "google.dat")), _journal, Http, _plateforme.OuvrirLien);
        _synchro = new SynchroDrive(google, depot, Http, _journal);
        if (ids is null) _journal.Info("google-client.json absent : connexion Google désactivée");

        var gmail = new EnvoiGmail(google, Http);
        var logo = ChargerLogo();
        var dossierJustificatifs = Path.Combine(racine, "justificatifs");
        var justificatifs = new ServiceJustificatifs(depot, dossierJustificatifs, logo, _journal, google, _synchro, new DocumentsDrive(google, Http), gmail);
        var copiesEnCours = false;
        _synchro.EtatChange += async (_, _) =>
        {
            // Après une synchronisation réussie : dépose sur le Drive les PDF qui n'y sont pas encore.
            if (_synchro.Etat != EtatSynchro.AJour || copiesEnCours) return;
            copiesEnCours = true;
            try { await justificatifs.CompleterCopiesDriveAsync(); }
            finally { copiesEnCours = false; }
        };

        var contexte = new ContexteApplication(depot, _plateforme, parametres, _journal)
        {
            Preferences = PreferencesLocales.Charger(Path.Combine(racine, "config", "preferences.json")),
            DossierSauvegardes = Path.Combine(racine, "backups"),
            Google = google,
            Synchro = _synchro,
            Gmail = gmail,
            DossierJustificatifs = dossierJustificatifs,
            LogoJpeg = logo,
            Justificatifs = justificatifs,
            MisesAJour = CreerVerificateur(parametres, racine),
        };
        _plateforme.Contexte = contexte;
        NettoyerAncienneVersion();

        var principal = new PrincipalViewModel(contexte);
        var fenetre = new FenetrePrincipale { DataContext = principal };
        MainWindow = fenetre;
        fenetre.Show();
        DemarrerSynchronisation(depot);
        DemarrerMisesAJour(principal);

        if (apresMiseAJour)
            fenetre.Dispatcher.InvokeAsync(() => FenetreMessage.Informer(fenetre, "Mise à jour terminée",
                $"L'ICA Gestion est maintenant en version {_plateforme.VersionApplication}."), DispatcherPriority.ApplicationIdle);

        if (plantagePrecedent)
        {
            _journal.Avertissement("La session précédente s'est terminée de façon inattendue.");
            fenetre.Dispatcher.InvokeAsync(() =>
            {
                if (FenetreMessage.Confirmer(fenetre, "L'ICA Gestion s'est fermée de façon inattendue",
                        "La dernière fois, l'application ne s'est pas fermée normalement. Voulez-vous prévenir le responsable technique ?",
                        "Prévenir le support", danger: false))
                    _plateforme.OuvrirAssistance(apresPlantage: true);
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    private ICA.Client.MisesAJour.VerificateurMisesAJour? CreerVerificateur(ParametresApplication parametres, string racine)
    {
        // Version lancée depuis le code (Lancer la demo.bat, dossier bin) : pas de mise à jour automatique.
        if (AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrWhiteSpace(parametres.MisesAJour.Depot)) return null;
        return new ICA.Client.MisesAJour.VerificateurMisesAJour(parametres.MisesAJour.Depot.Trim(), _plateforme!.VersionApplication,
            Path.Combine(racine, "mises-a-jour"), new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, _journal!);
    }

    /// <summary>Vérifie 20 secondes après le démarrage, puis toutes les 6 heures.</summary>
    private void DemarrerMisesAJour(PrincipalViewModel principal)
    {
        var premiere = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        premiere.Tick += async (_, _) => { premiere.Stop(); await principal.VerifierMiseAJourAsync(manuel: false); };
        premiere.Start();
        _minuterieMaj = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _minuterieMaj.Tick += async (_, _) => await principal.VerifierMiseAJourAsync(manuel: false);
        _minuterieMaj.Start();
    }

    private static byte[]? ChargerLogo()
    {
        try
        {
            var ressource = GetResourceStream(new Uri("pack://application:,,,/Assets/logo-ica.jpg"));
            if (ressource is null) return null;
            using var flux = new MemoryStream();
            ressource.Stream.CopyTo(flux);
            return flux.ToArray();
        }
        catch (IOException) { return null; }
    }

    private void NettoyerAncienneVersion()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && File.Exists(exe + ".ancien")) File.Delete(exe + ".ancien");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* réessayé au prochain démarrage */ }
    }

    /// <summary>Synchronisation : au démarrage, toutes les minutes, et quelques secondes après chaque modification.</summary>
    private void DemarrerSynchronisation(DepotIca depot)
    {
        if (_synchro is null) return;
        _ = _synchro.SynchroniserAsync();

        _minuterie = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _minuterie.Tick += (_, _) => _ = _synchro.SynchroniserAsync();
        _minuterie.Start();

        // Fermeture juste après une modification : on l'envoie d'abord (8 secondes au plus).
        var fermetureEnCours = false;
        MainWindow.Closing += async (_, e) =>
        {
            if (fermetureEnCours || _apresModification?.IsEnabled != true || _synchro.Etat == EtatSynchro.NonConnecte) return;
            e.Cancel = true;
            fermetureEnCours = true;
            _apresModification.Stop();
            await Task.WhenAny(_synchro.SynchroniserAsync(), Task.Delay(TimeSpan.FromSeconds(8)));
            MainWindow.Close();
        };

        _apresModification = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _apresModification.Tick += (_, _) => { _apresModification.Stop(); _ = _synchro.SynchroniserAsync(); };
        depot.Modifie += (_, _) =>
        {
            if (_synchro.EcritureEnCours) return; // modification venue de la synchronisation elle-même
            _apresModification.Stop();
            _apresModification.Start();
        };
    }

    private void ErreurInterface(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _journal?.Erreur("Erreur non gérée dans l'interface", e.Exception);
        e.Handled = true;
        if (FenetreMessage.Confirmer(MainWindow, "Une erreur est survenue",
                "L'opération n'a pas pu aboutir. Vos données déjà enregistrées ne sont pas perdues.\n" +
                "Voulez-vous prévenir le responsable technique ?",
                "Prévenir le support", danger: false))
            _plateforme?.OuvrirAssistance(apresPlantage: true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _journal?.Info("Fermeture normale");
        try { if (_marqueurSession is not null) File.Delete(_marqueurSession); }
        catch (IOException) { }
        base.OnExit(e);
    }
}
