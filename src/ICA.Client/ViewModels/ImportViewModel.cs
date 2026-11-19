using ICA.Client.Import;
using ICA.Client.Mvvm;
using ICA.Domain;

namespace ICA.Client.ViewModels;

public sealed record LigneApercuImport(string Numero, string Nom, string Detail, string? Remarques, bool AVerifier);

/// <summary>Fenêtre « Importer mes adhérents » : aperçu, lignes à vérifier, puis import (CDC §23).</summary>
public sealed class ImportViewModel : ObservableObject
{
    private static readonly Dictionary<RoleColonne, string> NomsRoles = new()
    {
        [RoleColonne.NomComplet] = "Nom et prénom",
        [RoleColonne.Nom] = "Nom",
        [RoleColonne.Prenom] = "Prénom",
        [RoleColonne.Adresse] = "Adresse",
        [RoleColonne.CodePostal] = "Code postal",
        [RoleColonne.Ville] = "Ville",
        [RoleColonne.Email] = "E-mail",
        [RoleColonne.Telephone] = "Téléphone",
        [RoleColonne.Telephone2] = "Téléphone secondaire",
        [RoleColonne.DateNaissance] = "Date de naissance",
        [RoleColonne.Cotisation] = "Cotisation",
        [RoleColonne.Notes] = "Notes",
    };

    private readonly ContexteApplication _ctx;
    private readonly ResultatAnalyse? _analyse;
    private bool _remplacer;
    private bool _seulementAVerifier;

    public ImportViewModel(ContexteApplication ctx, string chemin)
    {
        _ctx = ctx;
        NomFichier = Path.GetFileName(chemin);
        try
        {
            _analyse = AnalyseImport.Analyser(LecteurTableur.Lire(chemin), ctx.Depot.AujourdHui);
            if (_analyse.Detectes == 0) Erreur = "Aucun adhérent n'a été trouvé dans ce fichier.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            ctx.Journal.Erreur("Lecture du fichier d'import impossible", ex);
            Erreur = ex is IOException
                ? "Le fichier est peut-être ouvert dans Excel. Fermez-le puis réessayez."
                : ex.Message;
        }

        _remplacer = ctx.Depot.EstDemo;
        ImporterCommand = new RelayCommand(Importer, () => _analyse is { Detectes: > 0 });
        AnnulerCommand = new RelayCommand(() => Fermer?.Invoke(this, false));
    }

    public event EventHandler<bool>? Fermer;

    public string NomFichier { get; }
    public string? Erreur { get; }
    public bool AErreur => Erreur is not null;
    public bool Analyse => Erreur is null && _analyse is not null;

    public int Detectes => _analyse?.Detectes ?? 0;
    public int Prets => _analyse?.Prets ?? 0;
    public int AVerifier => _analyse?.AVerifier ?? 0;
    public string Precisions => _analyse is null ? "" :
        $"dont {_analyse.Enfants} enfant{(_analyse.Enfants > 1 ? "s" : "")} (d'après la date de naissance) · " +
        $"{_analyse.CotisationsPayees} cotisation{(_analyse.CotisationsPayees > 1 ? "s" : "")} {_ctx.Depot.SaisonCourante} déjà payée{(_analyse.CotisationsPayees > 1 ? "s" : "")}";

    public IReadOnlyList<string> Colonnes => _analyse?.Colonnes.OrderBy(c => c.Key)
        .Where(c => NomsRoles.ContainsKey(c.Value))
        .Select(c => $"Colonne {LecteurTableur.LettreColonne(c.Key)} : {NomsRoles[c.Value]}").ToList() ?? [];

    public bool SeulementAVerifier
    {
        get => _seulementAVerifier;
        set { if (SetProperty(ref _seulementAVerifier, value)) OnPropertyChanged(nameof(Lignes)); }
    }

    public IReadOnlyList<LigneApercuImport> Lignes => _analyse?.Lignes
        .Where(l => !SeulementAVerifier || l.AVerifier)
        .Select(l =>
        {
            var a = l.Adherent;
            var responsable = a.ResponsableId is { } rid ? _analyse.Lignes.FirstOrDefault(x => x.Adherent.Id == rid)?.Adherent.NomAffiche : null;
            var detail = string.Join(" · ", new[]
            {
                a.EstEnfant ? (responsable is null ? "Enfant" : $"Enfant de {responsable}") : "Adulte",
                string.Join(' ', new[] { a.CodePostal, a.Ville }.Where(s => !string.IsNullOrEmpty(s))),
                Telephone.Afficher(a.Telephone1),
                a.Email,
                l.CotisationPayee ? "cotisation payée" : "cotisation à régler",
            }.Where(s => !string.IsNullOrEmpty(s)));
            return new LigneApercuImport($"Ligne {l.NumeroLigne}", a.NomAffiche, detail,
                l.AVerifier ? string.Join(" ; ", l.Remarques) : null, l.AVerifier);
        }).ToList() ?? [];

    public bool EstDemo => _ctx.Depot.EstDemo;
    public string TexteRemplacer => EstDemo
        ? "Remplacer les adhérents fictifs de la démonstration (recommandé)"
        : "Remplacer tous mes adhérents actuels (sinon, les fiches sont ajoutées)";

    public bool Remplacer { get => _remplacer; set => SetProperty(ref _remplacer, value); }

    public string TexteBouton => $"Importer {Detectes} adhérent{(Detectes > 1 ? "s" : "")}";
    public string Explication =>
        "Les lignes « à vérifier » sont quand même importées : la remarque est recopiée dans les notes de la fiche, " +
        "pour que vous puissiez la corriger tranquillement ensuite (bouton « Modifier »).";

    public RelayCommand ImporterCommand { get; }
    public RelayCommand AnnulerCommand { get; }

    private void Importer()
    {
        if (_analyse is null) return;
        if (Remplacer && !EstDemo && !_ctx.Plateforme.Confirmer("Remplacer tous vos adhérents ?",
                $"Vos {_ctx.Depot.Adherents.Count} fiches actuelles, leurs cotisations et le calendrier seront remplacés par le contenu du fichier.\n" +
                "Une copie de sécurité est faite automatiquement chaque jour.", "Remplacer", danger: true))
            return;

        _ctx.Depot.Importer(_analyse, Remplacer);
        Fermer?.Invoke(this, true);
    }
}
