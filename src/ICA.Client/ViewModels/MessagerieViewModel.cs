using System.Collections.ObjectModel;
using ICA.Client.Mvvm;
using ICA.Client.Services;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.ViewModels;

/// <summary>Un groupe à qui écrire en un clic (« Tous les adhérents », « Cotisation à régler »…).</summary>
public sealed record GroupeDestinataires(string Libelle, string Detail, RelayCommand Ecrire, bool EstAttention = false);

public sealed record LigneMessage(string Quand, string Objet, string Detail, bool EnvoiDirect, RelayCommand Voir, RelayCommand Supprimer)
{
    public bool Prepare => !EnvoiDirect;
}

/// <summary>Page Messagerie : écrire à un groupe, historique des messages.</summary>
public sealed class MessagerieViewModel : ObservableObject
{
    private readonly ContexteApplication _ctx;
    private readonly PrincipalViewModel _principal;
    private IReadOnlyList<GroupeDestinataires> _groupes = [];

    public MessagerieViewModel(ContexteApplication ctx, PrincipalViewModel principal)
    {
        _ctx = ctx;
        _principal = principal;
        ChoisirAdherentsCommand = new RelayCommand(() => _principal.AllerAuxAdherents());
        _ctx.Depot.Modifie += (_, _) => Recharger();
        if (_ctx.Google is not null) _ctx.Google.Change += (_, _) => OnPropertyChanged(nameof(TexteEnvoi));
        Recharger();
    }

    public IReadOnlyList<GroupeDestinataires> Groupes { get => _groupes; private set => SetProperty(ref _groupes, value); }
    public ObservableCollection<LigneMessage> Historique { get; } = [];
    public bool HistoriqueVide => Historique.Count == 0;
    public RelayCommand ChoisirAdherentsCommand { get; }

    public string TexteEnvoi => _ctx.Gmail?.Disponible == true
        ? $"Les messages partent directement depuis {_ctx.Gmail.Expediteur}. Avec plusieurs destinataires, ils sont en copie cachée."
        : "Les messages s'ouvrent dans votre messagerie, prêts à envoyer. Reliez le compte Google (Paramètres) pour les envoyer d'ici.";

    private void Recharger()
    {
        var depot = _ctx.Depot;
        var saison = depot.SaisonCourante;
        var tous = depot.Adherents.ToList();

        string Adresses(IEnumerable<Adherent> liste)
        {
            var n = liste.Select(depot.EmailDeContact).Where(e => e is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return n switch { 0 => "aucune adresse e-mail", 1 => "1 adresse e-mail", _ => $"{n} adresses e-mail" };
        }

        GroupeDestinataires Groupe(string libelle, List<Adherent> liste, string? modele = null, bool attention = false) =>
            new(libelle, Adresses(liste), new RelayCommand(() => _principal.EcrireA(liste, modele, saison), () => liste.Count > 0), attention);

        var aRegler = tous.Where(a => depot.StatutDe(a.Id, saison) is not (StatutCotisation.Payee or StatutCotisation.Exoneree)).ToList();
        var groupes = new List<GroupeDestinataires>
        {
            Groupe("Tous les adhérents", tous, ModelesParDefaut.NomInformation),
            Groupe($"Cotisation {saison} à régler", aRegler, ModelesParDefaut.NomRappelCotisation, attention: aRegler.Count > 0),
            Groupe("Adultes", tous.Where(a => !a.EstEnfant).ToList(), ModelesParDefaut.NomInformation),
            Groupe("Parents des enfants inscrits", tous.Where(a => a.EstEnfant).ToList(), ModelesParDefaut.NomInformation),
        };
        foreach (var activite in depot.Activites)
            groupes.Add(Groupe($"Participants : {activite.Nom}", depot.ParticipantsDe(activite.Id).ToList(), ModelesParDefaut.NomInformation));
        Groupes = groupes;

        Historique.Clear();
        foreach (var m in depot.Messages)
        {
            var quand = m.DateEnvoi.ToLocalTime().ToString("dddd d MMMM yyyy 'à' HH:mm", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            quand = char.ToUpper(quand[0]) + quand[1..];
            var adresses = m.NombreAdresses == 1 ? "1 adresse" : $"{m.NombreAdresses} adresses";
            var detail = m.EnvoiDirect ? $"Envoyé par Gmail · {adresses}" : $"Préparé dans la messagerie · {adresses}";
            var id = m.Id;
            Historique.Add(new LigneMessage(quand, string.IsNullOrWhiteSpace(m.Objet) ? "(sans objet)" : m.Objet, detail, m.EnvoiDirect,
                new RelayCommand(() => _ctx.Plateforme.Informer(m.Objet, $"{quand}\nÀ : {m.Destinataires}\n\n{m.Corps}")),
                new RelayCommand(() =>
                {
                    if (_ctx.Plateforme.Confirmer("Retirer ce message de l'historique ?",
                            "Le message reste dans votre boîte mail ; il disparaît seulement de cette liste.", "Retirer", danger: true))
                        _ctx.Depot.SupprimerMessage(id);
                })));
        }
        OnPropertyChanged(nameof(HistoriqueVide));
    }
}
