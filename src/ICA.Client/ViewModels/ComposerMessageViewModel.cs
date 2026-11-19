using ICA.Client.Mvvm;
using ICA.Client.Services;
using ICA.Domain;

namespace ICA.Client.ViewModels;

public sealed class ChoixModele(ModeleMail modele, RelayCommand choisir) : ObservableObject
{
    private bool _estChoisi;
    public ModeleMail Modele { get; } = modele;
    public string Nom => Modele.Nom;
    public RelayCommand Choisir { get; } = choisir;
    public bool EstChoisi { get => _estChoisi; set => SetProperty(ref _estChoisi, value); }
}

/// <summary>
/// Fenêtre « Nouveau message » : choix d'un modèle, relecture, puis ouverture dans la messagerie.
/// v0.1 : messagerie de l'ordinateur (lien mailto). v0.8 : envoi direct par Gmail.
/// </summary>
public sealed class ComposerMessageViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private readonly IReadOnlyList<Adherent> _destinataires;
    private readonly List<string> _emails;
    private readonly List<string> _sansEmail;
    private ChoixModele? _modele;
    private string _objet = "";
    private string _corps = "";
    private readonly string _saison;

    public ComposerMessageViewModel(ContexteApplication ctx, IReadOnlyList<Adherent> destinataires, string? nomModele = null, string? saison = null,
        string? objetImpose = null, string? corpsImpose = null)
    {
        _ctx = ctx;
        _saison = saison ?? ctx.Depot.SaisonCourante;
        _destinataires = destinataires;
        _emails = [];
        _sansEmail = [];
        foreach (var a in destinataires)
        {
            var email = ctx.Depot.EmailDeContact(a);
            if (email is null) _sansEmail.Add(a.NomAffiche); else _emails.Add(email);
        }
        _emails = _emails.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Modeles = ctx.Depot.ModelesMail.Select(m =>
        {
            ChoixModele? choix = null;
            choix = new ChoixModele(m, new RelayCommand(() => AppliquerModele(choix!)));
            return choix;
        }).ToList();

        OuvrirCommand = new RelayCommand(Ouvrir, () => _emails.Count > 0);
        EnvoyerCommand = new CommandeAsync(EnvoyerAsync, () => _emails.Count > 0 && EnvoiDirect);
        AnnulerCommand = new RelayCommand(() => Fermer?.Invoke(this, EventArgs.Empty));

        var initial = Modeles.FirstOrDefault(m => m.Nom == nomModele)
                      ?? Modeles.FirstOrDefault(m => m.Nom == ModelesParDefaut.NomLibre)
                      ?? Modeles.FirstOrDefault();
        if (initial is not null) AppliquerModele(initial);

        // Texte déjà préparé (ex. annulation d'une séance précise) : il remplace celui du modèle.
        if (objetImpose is not null || corpsImpose is not null)
        {
            var valeurs = Valeurs();
            if (objetImpose is not null) Objet = RenduModele.Appliquer(objetImpose, valeurs);
            if (corpsImpose is not null) Corps = RenduModele.Appliquer(corpsImpose, valeurs);
        }
    }

    public event EventHandler? Fermer;

    public IReadOnlyList<ChoixModele> Modeles { get; }

    public string Objet { get => _objet; set => SetProperty(ref _objet, value); }
    public string Corps { get => _corps; set => SetProperty(ref _corps, value); }

    public bool EnvoiGroupe => _emails.Count > 1;

    public string TitreDestinataires => _destinataires.Count switch
    {
        1 => _destinataires[0].NomAffiche,
        var n => $"{n} adhérents",
    };

    public string DetailDestinataires
    {
        get
        {
            if (_emails.Count == 0) return "Aucune adresse e-mail : impossible d'écrire à ces adhérents par mail.";
            if (_emails.Count == 1 && _destinataires.Count == 1) return _emails[0];
            return $"{_emails.Count} adresse{(_emails.Count > 1 ? "s" : "")} en copie cachée : personne ne voit les adresses des autres.";
        }
    }

    public string? SansEmail => _sansEmail.Count == 0
        ? null
        : $"Sans adresse e-mail, donc non inclus : {string.Join(", ", _sansEmail.Take(8))}{(_sansEmail.Count > 8 ? $" et {_sansEmail.Count - 8} autre(s)" : "")}.";

    public string AideVariables => EnvoiGroupe
        ? "Les passages entre crochets [ ] sont à compléter. Pour un envoi groupé, « Bonjour, » remplace « Bonjour Prénom, »."
        : "Les passages entre crochets [ ] sont à compléter.";

    /// <summary>Compte Google connecté : l'application envoie elle-même, depuis le Gmail de l'association.</summary>
    public bool EnvoiDirect => _ctx.Gmail?.Disponible == true;
    public bool PasEnvoiDirect => !EnvoiDirect;
    public string TexteEnvoyer => _emails.Count <= 1 ? "Envoyer" : $"Envoyer à {_emails.Count} adresses";
    public string? Expediteur => _ctx.Gmail?.Expediteur is { } e ? $"Envoyé depuis {e}" : null;
    public CommandeAsync EnvoyerCommand { get; }

    public string TexteOuvrir => _ctx.Preferences.Messagerie == ModeMessagerie.Gmail ? "Ouvrir dans Gmail" : "Ouvrir dans ma messagerie";

    public RelayCommand OuvrirCommand { get; }
    public RelayCommand AnnulerCommand { get; }

    private void AppliquerModele(ChoixModele choix)
    {
        if (_modele is not null) _modele.EstChoisi = false;
        _modele = choix;
        choix.EstChoisi = true;

        var valeurs = Valeurs();
        Objet = RenduModele.Appliquer(choix.Modele.Objet, valeurs);
        Corps = RenduModele.Appliquer(choix.Modele.Corps, valeurs);
    }

    private async Task EnvoyerAsync()
    {
        if (_ctx.Gmail is null) return;
        if (Objet.Contains('[') || Corps.Contains('['))
        {
            if (!_ctx.Plateforme.Confirmer("Des passages sont encore entre crochets",
                    "Le message contient encore des passages à compléter entre [ ]. L'envoyer quand même ?", "Envoyer quand même"))
                return;
        }
        else if (_emails.Count > 1 && !_ctx.Plateforme.Confirmer("Envoyer ce message ?",
                     $"Il partira maintenant vers {_emails.Count} adresses, en copie cachée.", "Envoyer"))
        {
            return;
        }

        try
        {
            await _ctx.Gmail.EnvoyerAsync(_emails, Objet, Corps);
            _ctx.Journal.Info($"Message envoyé par Gmail ({_emails.Count} destinataire(s), modèle « {_modele?.Nom} »)");
            _ctx.Depot.EnregistrerMessage(Objet, Corps, _destinataires, _emails.Count, _modele?.Nom, envoiDirect: true);
            _ctx.Plateforme.Informer("Message envoyé",
                _emails.Count == 1 ? "Votre message est parti." : $"Votre message est parti vers {_emails.Count} adresses.\nVous en recevez une copie dans votre boîte Gmail.");
            Fermer?.Invoke(this, EventArgs.Empty);
        }
        catch (ICA.Client.Google.SansBoiteGmailException ex)
        {
            _ctx.Journal.Info("Compte Google sans boîte Gmail : envoi par la messagerie");
            _ctx.Plateforme.Informer("Envoi direct impossible", ex.Message);
            OnPropertyChanged(nameof(EnvoiDirect));
            OnPropertyChanged(nameof(PasEnvoiDirect));
            Ouvrir();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ICA.Client.Google.GoogleDeconnecteException)
        {
            _ctx.Journal.Erreur("Envoi Gmail impossible", ex);
            _ctx.Plateforme.Informer("Envoi impossible",
                ex is ICA.Client.Google.GoogleDeconnecteException ? ex.Message
                : ex is HttpRequestException { StatusCode: null } or TaskCanceledException
                    ? "Pas de connexion Internet. Réessayez plus tard, ou utilisez « Ouvrir dans Gmail »."
                    : "Gmail a refusé l'envoi. Utilisez « Ouvrir dans Gmail » en attendant, et signalez-le avec « Un problème ? ».");
        }
    }

    private Dictionary<string, string?> Valeurs()
    {
        var unique = _emails.Count == 1 && _destinataires.Count == 1 ? _destinataires[0] : null;
        // Pour un enfant, on écrit au responsable : pas de prénom pour éviter « Bonjour Léa » adressé au parent.
        if (unique is { EstEnfant: true }) unique = null;
        var valeurs = RenduModele.Valeurs(unique, _saison, _ctx.Depot.AujourdHui);
        valeurs["tarif_adulte"] = Libelles.Montant(_ctx.Parametres.Cotisations.TarifAdulte);
        valeurs["tarif_enfant"] = Libelles.Montant(_ctx.Parametres.Cotisations.TarifEnfant);
        return valeurs;
    }

    private void Ouvrir()
    {
        var plateforme = _ctx.Plateforme;
        var mode = _ctx.Preferences.Messagerie;
        var max = LienCourriel.LongueurMaxLien(mode);
        var unSeul = _emails.Count == 1;
        string Lien(bool avecCorps) => unSeul
            ? LienCourriel.Creer(mode, _emails, Objet, avecCorps ? Corps : "")
            : LienCourriel.Creer(mode, [], Objet, avecCorps ? Corps : "", cci: _emails);

        var lien = Lien(avecCorps: true);
        string? consigne = null;

        if (lien.Length > max || Corps.Length > LienCourriel.LongueurMaxCorps(mode))
        {
            // Message trop long pour un lien : on le met dans le presse-papiers.
            lien = Lien(avecCorps: false);
            plateforme.CopierTexte(Corps);
            consigne = "Le texte du message a été copié. Dans votre messagerie, cliquez dans le message puis faites Ctrl + V pour le coller.";

            if (lien.Length > max)
            {
                // Même les adresses ne tiennent pas : on ouvre un message vide et on copie les adresses.
                lien = LienCourriel.Creer(mode, [], Objet, "");
                plateforme.CopierTexte(string.Join("; ", _emails));
                plateforme.Informer("Beaucoup de destinataires",
                    "Les adresses ont été copiées. Dans votre messagerie, cliquez dans « Cci » et faites Ctrl + V.\n" +
                    "Revenez ensuite ici : le texte du message vous sera proposé.");
                if (!plateforme.OuvrirLien(lien)) { MessagerieIntrouvable(); return; }
                plateforme.CopierTexte(Corps);
                plateforme.Informer("Texte du message", "Le texte du message est maintenant copié : collez-le avec Ctrl + V.");
                _ctx.Depot.EnregistrerMessage(Objet, Corps, _destinataires, _emails.Count, _modele?.Nom, envoiDirect: false);
                Fermer?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        if (!plateforme.OuvrirLien(lien)) { MessagerieIntrouvable(); return; }
        if (consigne is not null) plateforme.Informer("Votre message est prêt", consigne);
        _ctx.Journal.Info($"Message préparé ({_emails.Count} destinataire(s), modèle « {_modele?.Nom} »)");
        _ctx.Depot.EnregistrerMessage(Objet, Corps, _destinataires, _emails.Count, _modele?.Nom, envoiDirect: false);
        Fermer?.Invoke(this, EventArgs.Empty);
    }

    private void MessagerieIntrouvable()
    {
        _ctx.Plateforme.CopierTexte(Corps);
        _ctx.Plateforme.Informer("Messagerie introuvable",
            _ctx.Preferences.Messagerie == ModeMessagerie.Gmail
                ? "Le navigateur n'a pas pu s'ouvrir.\nLe texte du message a été copié : ouvrez Gmail et collez-le avec Ctrl + V."
                : "Aucune application de messagerie n'est configurée sur cet ordinateur.\n" +
                  "Le texte du message a été copié. Astuce : dans Paramètres, choisissez « Gmail dans le navigateur ».");
    }
}
