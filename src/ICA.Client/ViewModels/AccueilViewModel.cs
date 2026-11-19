using ICA.Client.Mvvm;
using ICA.Client.Services;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.ViewModels;

public sealed class LigneAdherentCourte(Guid id, string nomAffiche, string initiales, string detail, RelayCommand action)
{
    public Guid Id { get; } = id;
    public string NomAffiche { get; } = nomAffiche;
    public string Initiales { get; } = initiales;
    public string Detail { get; } = detail;
    public RelayCommand Action { get; } = action;
}

public sealed record LigneAnniversaire(string NomAffiche, string Quand, string Age);

public sealed record LigneSemaine(string Quand, string Nom, string Horaire, string Couleur, bool EstAnnulee);

public sealed class AccueilViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private readonly PrincipalViewModel _principal;
    private string _recherche = "";
    private Statistiques _stats = new(0, 0, 0, 0, 0);
    private IReadOnlyList<LigneAdherentCourte> _aRegler = [];
    private IReadOnlyList<LigneAnniversaire> _anniversaires = [];
    private IReadOnlyList<LigneSemaine> _semaine = [];

    public AccueilViewModel(ContexteApplication ctx, PrincipalViewModel principal)
    {
        _ctx = ctx;
        _principal = principal;
        RechercherCommand = new RelayCommand(() => _principal.AllerAuxAdherents(Recherche));
        AjouterCommand = new RelayCommand(_principal.AjouterAdherent);
        MessageCommand = new RelayCommand(() => _principal.EcrireA(_ctx.Depot.Adherents, ModelesParDefaut.NomInformation));
        EncaisserCommand = new RelayCommand(() => _principal.AllerAuxCotisations());
        VoirCotisationsCommand = new RelayCommand(() => _principal.AllerAuxCotisations());
        VoirCalendrierCommand = new RelayCommand(() => _principal.AllerA(Pages.Activites));
        _ctx.Depot.Modifie += (_, _) => Rafraichir();
        Rafraichir();
    }

    public string DateDuJour => Libelles.DateLongue(_ctx.Depot.AujourdHui);
    public string Salutation => DateTime.Now.Hour >= 18 ? "Bonsoir !" : "Bonjour !";
    public string Saison => _ctx.Depot.SaisonCourante;
    public string TitreARegler => $"Cotisations {Saison} à régler";

    public string Recherche { get => _recherche; set => SetProperty(ref _recherche, value); }
    public Statistiques Stats { get => _stats; private set => SetProperty(ref _stats, value); }
    public IReadOnlyList<LigneAdherentCourte> ARegler { get => _aRegler; private set { SetProperty(ref _aRegler, value); OnPropertyChanged(nameof(RienARegler)); } }
    public IReadOnlyList<LigneAnniversaire> Anniversaires { get => _anniversaires; private set { SetProperty(ref _anniversaires, value); OnPropertyChanged(nameof(AucunAnniversaire)); } }
    public IReadOnlyList<LigneSemaine> Semaine { get => _semaine; private set { SetProperty(ref _semaine, value); OnPropertyChanged(nameof(SemaineVide)); } }
    public bool SemaineVide => Semaine.Count == 0;
    public RelayCommand VoirCalendrierCommand { get; }
    public bool RienARegler => ARegler.Count == 0;
    public bool AucunAnniversaire => Anniversaires.Count == 0;

    public RelayCommand RechercherCommand { get; }
    public RelayCommand AjouterCommand { get; }
    public RelayCommand MessageCommand { get; }
    public RelayCommand EncaisserCommand { get; }
    public RelayCommand VoirCotisationsCommand { get; }

    public void Rafraichir()
    {
        var depot = _ctx.Depot;
        var saison = depot.SaisonCourante;
        var aujourdHui = depot.AujourdHui;
        Stats = depot.StatistiquesDe(saison);

        ARegler = depot.Adherents
            .Where(a => depot.StatutDe(a.Id, saison) == StatutCotisation.APayer)
            .OrderBy(a => a.Nom).ThenBy(a => a.Prenom)
            .Take(6)
            .Select(a => new LigneAdherentCourte(a.Id, a.NomAffiche, a.Initiales, DetailCourt(a),
                new RelayCommand(() => _principal.AllerAuxCotisations(a.Id))))
            .ToList();

        Anniversaires = depot.Adherents
            .Where(a => a.DateNaissance is not null)
            .Select(a => (a, date: global::ICA.Domain.Anniversaires.Prochain(a.DateNaissance!.Value, aujourdHui)))
            .Where(x => x.date <= aujourdHui.AddDays(7))
            .OrderBy(x => x.date)
            .Select(x => new LigneAnniversaire(
                x.a.NomAffiche,
                x.date == aujourdHui ? "aujourd'hui" : x.date == aujourdHui.AddDays(1) ? "demain" : Libelles.Jour(x.date),
                $"{x.date.Year - x.a.DateNaissance!.Value.Year} ans"))
            .ToList();

        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        Semaine = depot.SeancesEntre(aujourdHui, aujourdHui.AddDays(6))
            .Select(s => new LigneSemaine(
                s.Date == aujourdHui ? "Aujourd'hui" : s.Date == aujourdHui.AddDays(1) ? "Demain" : s.Date.ToString("dddd", fr) is var j ? char.ToUpper(j[0], fr) + j[1..] : "",
                depot.NomDe(s), s.Annulee ? "Annulé" : RegleSeance.Horaire(s), depot.CouleurDe(s), s.Annulee))
            .ToList();

        OnPropertyChanged(nameof(DateDuJour));
        OnPropertyChanged(nameof(Salutation));
        OnPropertyChanged(nameof(TitreARegler));
    }

    private string DetailCourt(Adherent a)
    {
        if (a.EstEnfant && a.ResponsableId is { } rid && _ctx.Depot.TrouverAdherent(rid) is { } r)
            return $"Enfant · responsable : {r.NomAffiche}";
        return a.Ville is null ? "Adulte" : $"Adulte · {a.Ville}";
    }
}
