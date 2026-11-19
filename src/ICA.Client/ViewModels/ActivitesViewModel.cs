using System.Globalization;
using ICA.Client.Mvvm;
using ICA.Client.Plateforme;
using ICA.Client.Services;
using ICA.Domain;

namespace ICA.Client.ViewModels;

public sealed record PuceSeance(string Texte, string Couleur, bool EstAnnulee);

public sealed class JourCalendrier(DateOnly date, bool estDuMois, bool estAujourdhui, IReadOnlyList<PuceSeance> puces, string? plus, RelayCommand choisir) : ObservableObject
{
    private bool _estChoisi;
    public DateOnly Date { get; } = date;
    public string Numero { get; } = date.Day.ToString(CultureInfo.InvariantCulture);
    public bool EstDuMois { get; } = estDuMois;
    public bool EstAujourdhui { get; } = estAujourdhui;
    public IReadOnlyList<PuceSeance> Puces { get; } = puces;
    public string? Plus { get; } = plus;
    public RelayCommand Choisir { get; } = choisir;
    public bool EstChoisi { get => _estChoisi; set => SetProperty(ref _estChoisi, value); }
}

public sealed class DetailSeance
{
    public required Guid Id { get; init; }
    public required string Nom { get; init; }
    public required string Couleur { get; init; }
    public required string Horaire { get; init; }
    public string? Lieu { get; init; }
    public string? Notes { get; init; }
    public required string Participants { get; init; }
    public required bool EstAnnulee { get; init; }
    public bool EstActive => !EstAnnulee;
    public string? Quand { get; init; }
    public required RelayCommand Modifier { get; init; }
    public required RelayCommand BasculerAnnulation { get; init; }
    public required RelayCommand Ecrire { get; init; }
    public required RelayCommand Supprimer { get; init; }
    public string TexteAnnulation => EstAnnulee ? "Rétablir" : "Annuler la séance";
    public string TexteEcrire => EstAnnulee ? "Prévenir de l'annulation" : "Écrire aux participants";
}

public sealed class ElementActivite(Activite activite, int participants, int aVenir, RelayCommand choisir) : ObservableObject
{
    private bool _estChoisi;
    public Activite Activite { get; } = activite;
    public string Nom => Activite.Nom;
    public string Couleur => Activite.Couleur ?? "#9E1B32";
    public string? Description => Activite.Description;
    public string Resume { get; } = $"{participants} participant{(participants > 1 ? "s" : "")} · {aVenir} séance{(aVenir > 1 ? "s" : "")} à venir";
    public RelayCommand Choisir { get; } = choisir;
    public bool EstChoisi { get => _estChoisi; set => SetProperty(ref _estChoisi, value); }
}

public sealed record LigneParticipant(string NomAffiche, string Initiales, string Detail);

public sealed class ActivitesViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly ContexteApplication _ctx;
    private readonly PrincipalViewModel _principal;

    private bool _vueCalendrier = true;
    private DateOnly _mois;
    private DateOnly _jourChoisi;
    private IReadOnlyList<JourCalendrier> _jours = [];
    private IReadOnlyList<DetailSeance> _seancesDuJour = [];
    private IReadOnlyList<DetailSeance> _prochaines = [];
    private IReadOnlyList<ElementActivite> _activites = [];
    private ElementActivite? _activiteChoisie;
    private IReadOnlyList<LigneParticipant> _participants = [];
    private IReadOnlyList<DetailSeance> _seancesActivite = [];

    public ActivitesViewModel(ContexteApplication ctx, PrincipalViewModel principal)
    {
        _ctx = ctx;
        _principal = principal;
        var aujourdHui = ctx.Depot.AujourdHui;
        _jourChoisi = aujourdHui;
        _mois = new DateOnly(aujourdHui.Year, aujourdHui.Month, 1);

        AfficherCalendrierCommand = new RelayCommand(() => VueCalendrier = true);
        AfficherListeCommand = new RelayCommand(() => VueCalendrier = false);
        MoisPrecedentCommand = new RelayCommand(() => ChangerMois(-1));
        MoisSuivantCommand = new RelayCommand(() => ChangerMois(1));
        AujourdhuiCommand = new RelayCommand(() => { _mois = new DateOnly(_ctx.Depot.AujourdHui.Year, _ctx.Depot.AujourdHui.Month, 1); ChoisirJour(_ctx.Depot.AujourdHui); });
        PlanifierCommand = new RelayCommand(() => Planifier(VueCalendrier ? JourChoisi : null, VueCalendrier ? null : _activiteChoisie?.Activite.Id));
        NouvelleActiviteCommand = new RelayCommand(() => EditerActivite(null));
        ModifierActiviteCommand = new RelayCommand(() => { if (_activiteChoisie is not null) EditerActivite(_activiteChoisie.Activite); });
        EcrireParticipantsCommand = new RelayCommand(() =>
        {
            if (_activiteChoisie is not null) _principal.EcrireA(_ctx.Depot.ParticipantsDe(_activiteChoisie.Activite.Id));
        });
        ImprimerParticipantsCommand = new RelayCommand(ImprimerParticipants);

        _ctx.Depot.Modifie += (_, _) => Rafraichir();
        Rafraichir();
    }

    // ---------- Bascule des vues ----------

    public bool VueCalendrier
    {
        get => _vueCalendrier;
        set { if (SetProperty(ref _vueCalendrier, value)) OnPropertyChanged(nameof(VueListe)); }
    }
    public bool VueListe { get => !VueCalendrier; set { if (value) VueCalendrier = false; } }

    // ---------- Calendrier ----------

    public string TitreMois => Majuscule(_mois.ToString("MMMM yyyy", Fr));
    public IReadOnlyList<string> JoursSemaine { get; } = ["Lun.", "Mar.", "Mer.", "Jeu.", "Ven.", "Sam.", "Dim."];
    public IReadOnlyList<JourCalendrier> Jours { get => _jours; private set => SetProperty(ref _jours, value); }

    public DateOnly JourChoisi => _jourChoisi;
    public string TitreJour => Majuscule(_jourChoisi.ToString("dddd d MMMM", Fr));
    public IReadOnlyList<DetailSeance> SeancesDuJour { get => _seancesDuJour; private set { SetProperty(ref _seancesDuJour, value); OnPropertyChanged(nameof(JourVide)); } }
    public bool JourVide => SeancesDuJour.Count == 0;
    public IReadOnlyList<DetailSeance> Prochaines { get => _prochaines; private set => SetProperty(ref _prochaines, value); }

    // ---------- Liste des activités ----------

    public IReadOnlyList<ElementActivite> ListeActivites { get => _activites; private set => SetProperty(ref _activites, value); }
    public ElementActivite? ActiviteChoisie
    {
        get => _activiteChoisie;
        private set
        {
            if (_activiteChoisie is not null) _activiteChoisie.EstChoisi = false;
            SetProperty(ref _activiteChoisie, value);
            if (value is not null) value.EstChoisi = true;
            OnPropertyChanged(nameof(AActiviteChoisie));
        }
    }
    public bool AActiviteChoisie => ActiviteChoisie is not null;
    public IReadOnlyList<LigneParticipant> Participants { get => _participants; private set { SetProperty(ref _participants, value); OnPropertyChanged(nameof(AucunParticipant)); } }
    public bool AucunParticipant => Participants.Count == 0;
    public IReadOnlyList<DetailSeance> SeancesActivite { get => _seancesActivite; private set => SetProperty(ref _seancesActivite, value); }

    public RelayCommand AfficherCalendrierCommand { get; }
    public RelayCommand AfficherListeCommand { get; }
    public RelayCommand MoisPrecedentCommand { get; }
    public RelayCommand MoisSuivantCommand { get; }
    public RelayCommand AujourdhuiCommand { get; }
    public RelayCommand PlanifierCommand { get; }
    public RelayCommand NouvelleActiviteCommand { get; }
    public RelayCommand ModifierActiviteCommand { get; }
    public RelayCommand EcrireParticipantsCommand { get; }
    public RelayCommand ImprimerParticipantsCommand { get; }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Fr) + s[1..];

    private void ChangerMois(int delta)
    {
        _mois = _mois.AddMonths(delta);
        // On garde un jour choisi visible dans le mois affiché.
        var jour = Math.Min(_jourChoisi.Day, DateTime.DaysInMonth(_mois.Year, _mois.Month));
        ChoisirJour(new DateOnly(_mois.Year, _mois.Month, jour));
    }

    private void ChoisirJour(DateOnly jour)
    {
        _jourChoisi = jour;
        if (jour.Year != _mois.Year || jour.Month != _mois.Month) _mois = new DateOnly(jour.Year, jour.Month, 1);
        Rafraichir();
    }

    public void Rafraichir()
    {
        var depot = _ctx.Depot;
        var aujourdHui = depot.AujourdHui;

        // Grille de 6 semaines commençant un lundi.
        var debut = _mois;
        while (debut.DayOfWeek != DayOfWeek.Monday) debut = debut.AddDays(-1);
        var fin = debut.AddDays(41);
        var parJour = depot.SeancesEntre(debut, fin).GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.ToList());

        var jours = new List<JourCalendrier>(42);
        for (var d = debut; d <= fin; d = d.AddDays(1))
        {
            var date = d;
            var seances = parJour.GetValueOrDefault(date) ?? [];
            var puces = seances.Take(3).Select(s => new PuceSeance(
                (s.HeureDebut is { } h ? RegleSeance.AfficherHeure(h) + " " : "") + depot.NomDe(s),
                depot.CouleurDe(s), s.Annulee)).ToList();
            var plus = seances.Count > 3 ? $"+ {seances.Count - 3} autre{(seances.Count > 4 ? "s" : "")}" : null;
            jours.Add(new JourCalendrier(date, date.Month == _mois.Month, date == aujourdHui, puces, plus,
                new RelayCommand(() => ChoisirJour(date))) { EstChoisi = date == _jourChoisi });
        }
        Jours = jours;

        SeancesDuJour = depot.SeancesEntre(_jourChoisi, _jourChoisi).Select(s => Detail(s, avecDate: false)).ToList();
        Prochaines = depot.SeancesEntre(aujourdHui, aujourdHui.AddDays(30)).Where(s => !s.Annulee).Take(5)
            .Select(s => Detail(s, avecDate: true)).ToList();

        OnPropertyChanged(nameof(TitreMois));
        OnPropertyChanged(nameof(TitreJour));
        OnPropertyChanged(nameof(JourChoisi));

        // Liste des activités
        var choisieId = _activiteChoisie?.Activite.Id;
        ListeActivites = depot.Activites.Select(a =>
        {
            ElementActivite? e = null;
            e = new ElementActivite(a, depot.ParticipantsDe(a.Id).Count,
                depot.SeancesEntre(aujourdHui, aujourdHui.AddYears(1)).Count(s => s.ActiviteId == a.Id && !s.Annulee),
                new RelayCommand(() => ChoisirActivite(e!)));
            return e;
        }).ToList();
        var cible = ListeActivites.FirstOrDefault(a => a.Activite.Id == choisieId) ?? ListeActivites.FirstOrDefault();
        if (cible is not null) ChoisirActivite(cible); else ActiviteChoisie = null;
    }

    private void ChoisirActivite(ElementActivite e)
    {
        _activiteChoisie = null;
        ActiviteChoisie = e;
        var depot = _ctx.Depot;
        Participants = depot.ParticipantsDe(e.Activite.Id).Select(a => new LigneParticipant(a.NomAffiche, a.Initiales,
            a.EstEnfant ? "Enfant" : string.Join(" · ", new[] { a.Ville, Telephone.Afficher(a.Telephone1) }.Where(s => !string.IsNullOrEmpty(s))))).ToList();
        SeancesActivite = depot.SeancesEntre(depot.AujourdHui, depot.AujourdHui.AddDays(60))
            .Where(s => s.ActiviteId == e.Activite.Id).Take(6).Select(s => Detail(s, avecDate: true)).ToList();
    }

    private DetailSeance Detail(Seance s, bool avecDate)
    {
        var depot = _ctx.Depot;
        var nbParticipants = s.ActiviteId is null ? -1 : depot.ParticipantsDe(s).Count;
        return new DetailSeance
        {
            Id = s.Id,
            Nom = depot.NomDe(s),
            Couleur = depot.CouleurDe(s),
            Horaire = RegleSeance.Horaire(s),
            Lieu = s.Lieu,
            Notes = s.Notes,
            Participants = nbParticipants < 0 ? "Ouvert à tous les adhérents" : $"{nbParticipants} participant{(nbParticipants > 1 ? "s" : "")}",
            EstAnnulee = s.Annulee,
            Quand = avecDate ? Majuscule(s.Date.ToString("ddd d MMM", Fr)) : null,
            Modifier = new RelayCommand(() => ModifierSeance(s.Id)),
            BasculerAnnulation = new RelayCommand(() => BasculerAnnulation(s.Id)),
            Ecrire = new RelayCommand(() => EcrireParticipants(s.Id)),
            Supprimer = new RelayCommand(() => SupprimerSeance(s.Id)),
        };
    }

    // ---------- Actions ----------

    private void Planifier(DateOnly? date, Guid? activiteId)
    {
        var vm = new PlanificationViewModel(_ctx, null, date ?? _ctx.Depot.AujourdHui, activiteId);
        if (_ctx.Plateforme.PlanifierSeance(vm) && vm.PremiereDate is { } d && VueCalendrier) ChoisirJour(d);
    }

    private void ModifierSeance(Guid id)
    {
        if (_ctx.Depot.TrouverSeance(id) is not { } s) return;
        _ctx.Plateforme.PlanifierSeance(new PlanificationViewModel(_ctx, s, s.Date, s.ActiviteId));
    }

    private void BasculerAnnulation(Guid id)
    {
        if (_ctx.Depot.TrouverSeance(id) is not { } s) return;
        if (!s.Annulee)
        {
            if (!_ctx.Plateforme.Confirmer("Annuler cette séance ?",
                    $"{_ctx.Depot.NomDe(s)} du {s.Date.ToString("dddd d MMMM", Fr)} sera marquée « Annulée ».\n" +
                    "Vous pourrez ensuite prévenir les participants en un clic.", "Annuler la séance"))
                return;
            _ctx.Depot.ChangerAnnulation(id, true);
            if (_ctx.Plateforme.Confirmer("Prévenir les participants ?",
                    "Voulez-vous préparer un message pour prévenir les participants de l'annulation ?", "Préparer le message"))
                EcrireParticipants(id);
        }
        else
        {
            _ctx.Depot.ChangerAnnulation(id, false);
        }
    }

    private void SupprimerSeance(Guid id)
    {
        if (_ctx.Depot.TrouverSeance(id) is not { } s) return;
        var nom = _ctx.Depot.NomDe(s);
        var dansSerie = s.SerieId is not null &&
                        _ctx.Depot.SeancesEntre(s.Date.AddDays(1), s.Date.AddYears(2)).Any(x => x.SerieId == s.SerieId);
        if (dansSerie)
        {
            var toutes = _ctx.Plateforme.Confirmer("Supprimer quelles séances ?",
                $"« {nom} » se répète chaque semaine.\nSupprimer aussi toutes les séances suivantes ?",
                "Supprimer toutes les suivantes", danger: true);
            if (toutes) { _ctx.Depot.SupprimerSeance(id, true); return; }
        }
        if (_ctx.Plateforme.Confirmer("Supprimer cette date ?",
                $"« {nom} » du {s.Date.ToString("dddd d MMMM", Fr)} sera retirée du calendrier.\n" +
                "Pour garder une trace, préférez « Annuler la séance ».", "Supprimer cette date", danger: true))
            _ctx.Depot.SupprimerSeance(id, false);
    }

    private void EcrireParticipants(Guid id)
    {
        if (_ctx.Depot.TrouverSeance(id) is not { } s) return;
        var nom = _ctx.Depot.NomDe(s);
        var quand = s.Date.ToString("dddd d MMMM", Fr);
        var horaire = s.HeureDebut is null ? "" : $" à {RegleSeance.AfficherHeure(s.HeureDebut)}";
        var lieu = s.Lieu is null ? "" : $"\n• Lieu : {s.Lieu}";

        string objet, corps;
        if (s.Annulee)
        {
            objet = $"L'ICA — Annulation : {nom} du {quand}";
            corps = "Bonjour {{prenom}},\n\n" +
                    $"Nous sommes au regret de vous informer que « {nom} » prévu le {quand}{horaire} est annulé.\n\n" +
                    "[Raison et éventuelle date de report]\n\nMerci de votre compréhension.\n\n" + ModelesParDefaut.Signature;
        }
        else
        {
            objet = $"L'ICA — {nom} du {quand}";
            corps = "Bonjour {{prenom}},\n\n" +
                    $"Petit rappel : « {nom} » aura lieu le {quand}{horaire}.{lieu}\n\n" +
                    "[Informations complémentaires]\n\nÀ bientôt,\nL'ICA — Partageons nos savoirs-faire";
        }

        var destinataires = _ctx.Depot.ParticipantsDe(s);
        if (destinataires.Count == 0)
        {
            _ctx.Plateforme.Informer("Aucun participant", "Personne n'est encore inscrit à cette activité. Les inscriptions se font depuis la fiche de chaque adhérent (« Modifier »).");
            return;
        }
        _principal.EcrireA(destinataires, s.Annulee ? "Annulation" : null, objetImpose: objet, corpsImpose: corps);
    }

    private void EditerActivite(Activite? activite)
    {
        var vm = new EditionActiviteViewModel(_ctx, activite);
        if (_ctx.Plateforme.EditerActivite(vm) && vm.IdEnregistre is { } id)
        {
            VueCalendrier = false;
            if (ListeActivites.FirstOrDefault(a => a.Activite.Id == id) is { } e) ChoisirActivite(e);
        }
    }

    private void ImprimerParticipants()
    {
        if (_activiteChoisie is null) return;
        var saison = _ctx.Depot.SaisonCourante;
        var lignes = _ctx.Depot.ParticipantsDe(_activiteChoisie.Activite.Id).Select(a => new LigneImpression(
            a.NomAffiche, a.EstEnfant ? "Enfant" : "Adulte",
            string.Join(" ", new[] { a.CodePostal, a.Ville }.Where(s => !string.IsNullOrEmpty(s))),
            Telephone.Afficher(a.Telephone1 ?? (a.ResponsableId is { } r ? _ctx.Depot.TrouverAdherent(r)?.Telephone1 : null)),
            _ctx.Depot.EmailDeContact(a) ?? "",
            Libelles.Statut(_ctx.Depot.StatutDe(a.Id, saison)))).ToList();
        if (lignes.Count == 0) { _ctx.Plateforme.Informer("Aucun participant", "Personne n'est encore inscrit à cette activité."); return; }
        _ctx.Plateforme.Imprimer($"{_activiteChoisie.Nom} — participants", lignes);
    }
}
