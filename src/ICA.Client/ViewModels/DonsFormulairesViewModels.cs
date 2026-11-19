using System.Globalization;
using ICA.Client.Documents;
using ICA.Client.Mvvm;
using ICA.Client.Services;
using ICA.Domain;

namespace ICA.Client.ViewModels;

/// <summary>Choix dans une liste déroulante.</summary>
public sealed record Option<T>(T Valeur, string Libelle)
{
    public override string ToString() => Libelle;
}

/// <summary>Base des fenêtres de saisie ouvertes par IServicesPlateforme.OuvrirFormulaire.</summary>
public abstract class FormulaireViewModel : ObservableObject
{
    private string? _erreur;

    protected FormulaireViewModel() => AnnulerCommand = new RelayCommand(() => Terminer(false));

    public abstract string TitreFenetre { get; }
    public virtual double LargeurFenetre => 640;
    public RelayCommand AnnulerCommand { get; }

    public string? Erreur
    {
        get => _erreur;
        protected set { if (SetProperty(ref _erreur, value)) OnPropertyChanged(nameof(AErreur)); }
    }
    public bool AErreur => !string.IsNullOrEmpty(_erreur);

    /// <summary>true = validé, false = annulé.</summary>
    public event EventHandler<bool>? Fermer;
    protected void Terminer(bool valide) => Fermer?.Invoke(this, valide);

    protected static decimal? LireMontant(string? texte, out bool valide)
    {
        valide = true;
        if (string.IsNullOrWhiteSpace(texte)) return null;
        var propre = texte.Replace("€", "").Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace('.', ',');
        if (decimal.TryParse(propre, NumberStyles.Number, CultureInfo.GetCultureInfo("fr-FR"), out var m) && m >= 0)
            return Math.Round(m, 2);
        valide = false;
        return null;
    }

    protected static string? Nettoyer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// =====================================================================
//  Entreprise donatrice
// =====================================================================

public sealed class EditionEntrepriseViewModel : FormulaireViewModel
{
    private readonly ContexteApplication _ctx;
    private readonly DonateurEntreprise _brouillon;
    private readonly bool _nouvelle;

    public EditionEntrepriseViewModel(ContexteApplication ctx, DonateurEntreprise? entreprise)
    {
        _ctx = ctx;
        _nouvelle = entreprise is null;
        var e = entreprise ?? new DonateurEntreprise();
        _brouillon = new DonateurEntreprise
        {
            Id = e.Id, RaisonSociale = e.RaisonSociale, FormeJuridique = e.FormeJuridique, Siren = Siren.Afficher(e.Siren), Siret = Siren.Afficher(e.Siret),
            Adresse = e.Adresse, CodePostal = e.CodePostal, Ville = e.Ville, ContactNom = e.ContactNom, ContactPrenom = e.ContactPrenom,
            Email = e.Email, Telephone = e.Telephone is { } t ? Telephone.Afficher(t) : null, Notes = e.Notes,
        };
        EnregistrerCommand = new RelayCommand(Enregistrer);
    }

    public override string TitreFenetre => _nouvelle ? "Nouvelle entreprise" : "Modifier l'entreprise";
    public override double LargeurFenetre => 720;
    public Guid? IdEnregistre { get; private set; }
    public RelayCommand EnregistrerCommand { get; }

    public string RaisonSociale { get => _brouillon.RaisonSociale; set => _brouillon.RaisonSociale = value; }
    public string? FormeJuridique { get => _brouillon.FormeJuridique; set => _brouillon.FormeJuridique = value; }
    public string? NumeroSiren { get => _brouillon.Siren; set => _brouillon.Siren = value; }
    public string? NumeroSiret { get => _brouillon.Siret; set => _brouillon.Siret = value; }
    public string? Adresse { get => _brouillon.Adresse; set => _brouillon.Adresse = value; }
    public string? CodePostal { get => _brouillon.CodePostal; set => _brouillon.CodePostal = value; }
    public string? Ville { get => _brouillon.Ville; set => _brouillon.Ville = value; }
    public string? ContactNom { get => _brouillon.ContactNom; set => _brouillon.ContactNom = value; }
    public string? ContactPrenom { get => _brouillon.ContactPrenom; set => _brouillon.ContactPrenom = value; }
    public string? EmailEntreprise { get => _brouillon.Email; set => _brouillon.Email = value; }
    public string? TelephoneEntreprise { get => _brouillon.Telephone; set => _brouillon.Telephone = value; }
    public string? Notes { get => _brouillon.Notes; set => _brouillon.Notes = value; }

    public IReadOnlyList<string> FormesJuridiques { get; } =
        ["SARL", "SAS", "SASU", "EURL", "SA", "SNC", "SCI", "Entreprise individuelle", "Micro-entreprise", "Association", "Autre"];

    private void Enregistrer()
    {
        var e = new DonateurEntreprise
        {
            Id = _brouillon.Id, RaisonSociale = _brouillon.RaisonSociale ?? "", FormeJuridique = Nettoyer(_brouillon.FormeJuridique),
            Siren = Nettoyer(_brouillon.Siren), Siret = Nettoyer(_brouillon.Siret), Adresse = Nettoyer(_brouillon.Adresse),
            CodePostal = Nettoyer(_brouillon.CodePostal), Ville = Nettoyer(_brouillon.Ville), ContactNom = Nettoyer(_brouillon.ContactNom),
            ContactPrenom = Nettoyer(_brouillon.ContactPrenom), Email = Nettoyer(_brouillon.Email), Telephone = Nettoyer(_brouillon.Telephone),
            Notes = Nettoyer(_brouillon.Notes),
        };

        // Même nom déjà dans l'annuaire (sans SIREN pour trancher) : on demande.
        var memeNom = _ctx.Depot.DoublonsEntreprise(e).FirstOrDefault(x => Siren.Normaliser(x.Siren) != Siren.Normaliser(e.Siren) || e.Siren is null);
        if (memeNom is not null && !_ctx.Plateforme.Confirmer("Entreprise déjà connue ?",
                $"« {memeNom.RaisonSociale} » existe déjà dans l'annuaire{(memeNom.Siren is { } s ? $" (SIREN {Siren.Afficher(s)})" : "")}.\nEnregistrer quand même une nouvelle fiche ?",
                "Enregistrer quand même"))
            return;

        var r = _ctx.Depot.EnregistrerEntreprise(e);
        if (!r.Reussi) { Erreur = string.Join("\n", r.Erreurs.Select(x => x.Message)); return; }
        IdEnregistre = e.Id;
        Terminer(true);
    }
}

// =====================================================================
//  Don
// =====================================================================

public sealed class EditionDonViewModel : FormulaireViewModel
{
    private readonly ContexteApplication _ctx;
    private readonly Guid _id;
    private readonly bool _nouveau;
    private readonly DateTimeOffset _cree;
    private IReadOnlyList<DonateurEntreprise> _entreprises;
    private DonateurEntreprise? _entreprise;
    private DateTime? _date;
    private TypeDon _type;
    private string? _montant;

    public EditionDonViewModel(ContexteApplication ctx, Don? don, Guid? entrepriseId = null)
    {
        _ctx = ctx;
        _nouveau = don is null;
        var d = don ?? new Don { Date = ctx.Depot.AujourdHui, Mode = ModeVersementDon.Virement, EntrepriseId = entrepriseId ?? Guid.Empty };
        _id = d.Id;
        _cree = d.CreatedAt;
        _entreprises = ctx.Depot.Entreprises;
        _entreprise = _entreprises.FirstOrDefault(e => e.Id == d.EntrepriseId);
        _date = d.Date.ToDateTime(TimeOnly.MinValue);
        _type = d.Type;
        _montant = d.Montant?.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR"));
        Mode = Modes.FirstOrDefault(m => m.Valeur == (d.Mode ?? ModeVersementDon.Virement));
        Statut = Statuts.First(s => s.Valeur == d.Statut);
        Description = d.Description;
        Quantites = d.Quantites;
        Salaries = d.SalariesMisADisposition;
        Pieces = d.PiecesJustificatives;
        if (don is not null && ctx.Depot.JustificatifDe(don.Id) is { } j)
            AvertissementJustificatif = $"Ce don figure sur le justificatif {j.Numero}, émis le {j.DateEmission:dd/MM/yyyy}. Ce document ne sera pas modifié : " +
                                        "après correction, utilisez « Remplacer » sur ce justificatif pour en émettre un nouveau.";

        EnregistrerCommand = new RelayCommand(Enregistrer);
        NouvelleEntrepriseCommand = new RelayCommand(NouvelleEntreprise);
    }

    public override string TitreFenetre => _nouveau ? "Nouveau don" : "Modifier le don";
    public override double LargeurFenetre => 720;
    public RelayCommand EnregistrerCommand { get; }
    public RelayCommand NouvelleEntrepriseCommand { get; }
    public Guid? IdEnregistre { get; private set; }

    public IReadOnlyList<DonateurEntreprise> Entreprises { get => _entreprises; private set => SetProperty(ref _entreprises, value); }
    public DonateurEntreprise? Entreprise { get => _entreprise; set => SetProperty(ref _entreprise, value); }
    public bool AucuneEntreprise => Entreprises.Count == 0;
    public DateTime? Date { get => _date; set => SetProperty(ref _date, value); }

    public bool EstFinancier { get => _type == TypeDon.Financier; set { if (value) ChangerType(TypeDon.Financier); } }
    public bool EstNature { get => _type == TypeDon.Nature; set { if (value) ChangerType(TypeDon.Nature); } }
    public bool EstCompetences { get => _type == TypeDon.Competences; set { if (value) ChangerType(TypeDon.Competences); } }
    public bool PasFinancier => !EstFinancier;

    public string LibelleMontant => EstFinancier ? "Montant versé (€) *" : "Valorisation communiquée par l'entreprise (€)";
    public string AideMontant => EstFinancier ? "" :
        "La valeur d'un don en nature ou de compétences est déterminée par l'entreprise donatrice : reportez celle qu'elle vous a communiquée.";
    public string LibelleDescription => _type switch
    {
        TypeDon.Nature => "Description détaillée des biens *",
        TypeDon.Competences => "Prestation réalisée *",
        _ => "Description (facultatif)",
    };

    public string? Montant { get => _montant; set => SetProperty(ref _montant, value); }
    public IReadOnlyList<Option<ModeVersementDon>> Modes { get; } =
        Enum.GetValues<ModeVersementDon>().Select(m => new Option<ModeVersementDon>(m, RegleDon.Libelle(m))).ToList();
    public Option<ModeVersementDon>? Mode { get; set; }
    public IReadOnlyList<Option<StatutDon>> Statuts { get; } =
        Enum.GetValues<StatutDon>().Select(s => new Option<StatutDon>(s, RegleDon.Libelle(s))).ToList();
    public Option<StatutDon> Statut { get; set; }

    public string? Description { get; set; }
    public string? Quantites { get; set; }
    public string? Salaries { get; set; }
    public string? Pieces { get; set; }
    public string? AvertissementJustificatif { get; }
    public bool AAvertissement => AvertissementJustificatif is not null;

    private void ChangerType(TypeDon t)
    {
        _type = t;
        foreach (var n in new[] { nameof(EstFinancier), nameof(EstNature), nameof(EstCompetences), nameof(PasFinancier), nameof(LibelleMontant), nameof(AideMontant), nameof(LibelleDescription) })
            OnPropertyChanged(n);
    }

    private void NouvelleEntreprise()
    {
        var vm = new EditionEntrepriseViewModel(_ctx, null);
        if (!_ctx.Plateforme.OuvrirFormulaire(vm) || vm.IdEnregistre is not { } id) return;
        Entreprises = _ctx.Depot.Entreprises;
        OnPropertyChanged(nameof(AucuneEntreprise));
        Entreprise = Entreprises.FirstOrDefault(e => e.Id == id);
    }

    private void Enregistrer()
    {
        var montant = LireMontant(Montant, out var montantValide);
        if (!montantValide) { Erreur = "Le montant n'est pas un nombre valide (exemple : 150 ou 150,50)."; return; }
        if (Date is null) { Erreur = "Indiquez la date du don."; return; }
        var d = new Don
        {
            Id = _id, CreatedAt = _cree, EntrepriseId = Entreprise?.Id ?? Guid.Empty, Date = DateOnly.FromDateTime(Date.Value), Type = _type,
            Montant = montant, Mode = _type == TypeDon.Financier ? Mode?.Valeur : null, Statut = Statut.Valeur,
            Description = Nettoyer(Description),
            Quantites = _type == TypeDon.Nature ? Nettoyer(Quantites) : null,
            SalariesMisADisposition = _type == TypeDon.Competences ? Nettoyer(Salaries) : null,
            PiecesJustificatives = _type == TypeDon.Financier ? null : Nettoyer(Pieces),
        };
        if (d.Date > _ctx.Depot.AujourdHui) { Erreur = "La date du don ne peut pas être dans le futur."; return; }

        var doublon = _nouveau ? _ctx.Depot.DoublonsDon(d).FirstOrDefault() : null;
        if (doublon is not null && !_ctx.Plateforme.Confirmer("Don déjà enregistré ?",
                $"Un don identique de cette entreprise existe déjà ({doublon.Date:dd/MM/yyyy}, {(doublon.Montant is { } m ? ModelesJustificatifs.Euros(m) : "sans montant")}).\nL'enregistrer quand même ?",
                "Enregistrer quand même"))
            return;

        var r = _ctx.Depot.EnregistrerDon(d);
        if (!r.Reussi) { Erreur = string.Join("\n", r.Erreurs.Select(x => x.Message)); return; }
        IdEnregistre = d.Id;
        Terminer(true);
    }
}

// =====================================================================
//  Générer un justificatif
// =====================================================================

public sealed class ChoixDon(Don don, bool coche, Action changement) : ObservableObject
{
    private bool _coche = coche;
    public Don Don { get; } = don;
    public string Libelle { get; } = $"{don.Date:dd/MM/yyyy} — {RegleDon.Libelle(don.Type)} — {(don.Montant is { } m ? ModelesJustificatifs.Euros(m) : "valeur non communiquée")}";
    public string Detail { get; } = RegleDon.Description(don);
    public bool Coche { get => _coche; set { if (SetProperty(ref _coche, value)) changement(); } }
}

public sealed class GenerationJustificatifViewModel : FormulaireViewModel
{
    private readonly ContexteApplication _ctx;
    private readonly DonateurEntreprise _entreprise;
    private readonly JustificatifDon? _remplace;
    private bool _recuFiscal;

    public GenerationJustificatifViewModel(ContexteApplication ctx, Don don, JustificatifDon? remplace = null)
    {
        _ctx = ctx;
        _remplace = remplace;
        _entreprise = ctx.Depot.TrouverEntreprise(don.EntrepriseId) ?? throw new InvalidOperationException("Entreprise introuvable.");
        _recuFiscal = remplace?.Type == TypeJustificatif.RecuFiscal;

        var annee = don.Date.Year;
        var ids = remplace?.DonIds.ToHashSet() ?? [don.Id];
        Dons = ctx.Depot.DonsDe(_entreprise.Id)
            .Where(d => d.Date.Year == annee && d.Statut == StatutDon.Recu && (ids.Contains(d.Id) || ctx.Depot.JustificatifDe(d.Id) is null))
            .OrderBy(d => d.Date)
            .Select(d => new ChoixDon(d, ids.Contains(d.Id), Rafraichir)).ToList();
        Annee = annee;
        GenererCommand = new CommandeAsync(GenererAsync, () => Dons.Any(d => d.Coche));
    }

    public override string TitreFenetre => _remplace is null ? "Générer le justificatif" : $"Remplacer le justificatif {_remplace.Numero}";
    public override double LargeurFenetre => 720;
    public string Entreprise => _entreprise.RaisonSociale;
    public int Annee { get; }
    public IReadOnlyList<ChoixDon> Dons { get; }
    public CommandeAsync GenererCommand { get; }
    public JustificatifDon? Resultat { get; private set; }

    public bool TypeModifiable => _remplace is null;
    public bool EstAttestation { get => !_recuFiscal; set { if (value) Choisir(false); } }
    public bool EstRecuFiscal { get => _recuFiscal; set { if (value && RecuFiscalPossible) Choisir(true); } }
    public bool RecuFiscalPossible => _ctx.Depot.RecusFiscauxAutorises;
    public string TexteRecuFiscal
    {
        get
        {
            if (!RecuFiscalPossible)
                return "Reçu fiscal indisponible. Il manque (Paramètres > Association et reçus fiscaux) : " + string.Join(", ", _ctx.Depot.ManquesRecuFiscal) + ".";
            var entreprise = ConditionsRecuFiscal.Entreprise(_entreprise);
            return entreprise.Count > 0
                ? "Reçu fiscal : complétez d'abord la fiche de l'entreprise (" + string.Join(", ", entreprise) + ")."
                : $"Reçu fiscal (2041-MEC-SD, Cerfa n° {CategoriesFiscales.ModeleEnVigueur}), envoyé avec une lettre de remerciement sur papier à en-tête.";
        }
    }
    public string TexteAttestation => "Attestation simple : confirme la réception du don, sans valeur fiscale.";

    public string Total
    {
        get
        {
            var coches = Dons.Where(d => d.Coche).Select(d => d.Don).ToList();
            var versements = coches.Where(d => d.Type == TypeDon.Financier).Sum(d => d.Montant ?? 0);
            var nature = coches.Where(d => d.Type != TypeDon.Financier).Sum(d => d.Montant ?? 0);
            return coches.Count == 0 ? "Cochez au moins un don." :
                $"{coches.Count} don(s) — versements {ModelesJustificatifs.Euros(versements)}" + (nature > 0 ? $" — en nature {ModelesJustificatifs.Euros(nature)}" : "");
        }
    }

    public string TexteNumerotation => _ctx.Google?.EstConnecte == true
        ? "Le numéro est attribué en ligne, pour qu'il soit unique sur tous vos ordinateurs : Internet est nécessaire."
        : "Le document sera numéroté sur cet ordinateur.";

    private void Choisir(bool recu)
    {
        _recuFiscal = recu;
        OnPropertyChanged(nameof(EstAttestation));
        OnPropertyChanged(nameof(EstRecuFiscal));
    }

    private void Rafraichir()
    {
        OnPropertyChanged(nameof(Total));
        GenererCommand.NotifierChangement();
    }

    private async Task GenererAsync()
    {
        if (_ctx.Justificatifs is not { } service) { Erreur = "Service indisponible."; return; }
        var dons = Dons.Where(d => d.Coche).Select(d => d.Don).ToList();
        var type = _recuFiscal ? TypeJustificatif.RecuFiscal : TypeJustificatif.Attestation;
        var libelle = type == TypeJustificatif.RecuFiscal ? "ce reçu fiscal" : "cette attestation";
        if (!_ctx.Plateforme.Confirmer("Émettre le document ?",
                $"Un numéro officiel va être attribué à {libelle} pour {Entreprise}. Une fois émis, le document ne pourra plus être modifié " +
                "(seulement annulé ou remplacé).", "Émettre"))
            return;
        Erreur = null;
        try
        {
            Resultat = await service.EmettreAsync(type, _entreprise, dons, _remplace);
            Terminer(true);
        }
        catch (InvalidOperationException ex)
        {
            Erreur = ex.Message;
        }
    }
}

// =====================================================================
//  Envoyer un justificatif
// =====================================================================

public sealed class EnvoiJustificatifViewModel : FormulaireViewModel
{
    private readonly ContexteApplication _ctx;
    private readonly JustificatifDon _j;

    public EnvoiJustificatifViewModel(ContexteApplication ctx, JustificatifDon j)
    {
        _ctx = ctx;
        _j = j;
        Destinataire = ctx.Depot.TrouverEntreprise(j.EntrepriseId)?.Email ?? "";
        Objet = ServiceJustificatifs.ObjetMail;
        Corps = ServiceJustificatifs.CorpsMail;
        EnvoyerCommand = new CommandeAsync(EnvoyerAsync, () => EnvoiDirect);
        PreparerCommand = new CommandeAsync(PreparerAsync);
    }

    public override string TitreFenetre => "Envoyer le justificatif";
    public override double LargeurFenetre => 720;
    public string Destinataire { get; set; }
    public string Objet { get; set; }
    public string Corps { get; set; }
    public string PieceJointe => _j.Type == TypeJustificatif.RecuFiscal
        ? $"{_j.NomFichier} + lettre de remerciement sur papier à en-tête (joints automatiquement)"
        : $"{_j.NomFichier} (joint automatiquement)";
    public CommandeAsync EnvoyerCommand { get; }
    public CommandeAsync PreparerCommand { get; }
    public bool EnvoiDirect => _ctx.Justificatifs?.PeutEnvoyerDirectement == true;
    public bool PasEnvoiDirect => !EnvoiDirect;
    public string Expediteur => EnvoiDirect ? $"Envoi depuis {_ctx.Justificatifs!.Expediteur}" : "";
    public string? DejaEnvoye => _j.DateEnvoi is { } d ? $"Déjà envoyé le {d.ToLocalTime():dd/MM/yyyy à HH:mm} à {_j.EnvoyeA}." : null;
    public bool ADejaEnvoye => DejaEnvoye is not null;

    private bool Verifier()
    {
        try { _ctx.Justificatifs!.VerifierEnvoiPossible(_j); }
        catch (InvalidOperationException ex) { Erreur = ex.Message; return false; }
        Destinataire = Destinataire.Trim();
        if (!Email.EstValide(Destinataire)) { Erreur = "Indiquez une adresse e-mail valide pour l'entreprise."; OnPropertyChanged(nameof(Destinataire)); return false; }
        if (_j.DateEnvoi is not null && !_ctx.Plateforme.Confirmer("Renvoyer le justificatif ?", DejaEnvoye + "\nL'envoyer à nouveau ?", "Renvoyer"))
            return false;
        return true;
    }

    private async Task EnvoyerAsync()
    {
        Erreur = null;
        if (!Verifier()) return;
        if (!_ctx.Plateforme.Confirmer("Envoyer maintenant ?", $"Le justificatif {_j.Numero} va partir à {Destinataire}, le PDF en pièce jointe.", "Envoyer"))
            return;
        try
        {
            await _ctx.Justificatifs!.EnvoyerAsync(_j, Destinataire, Objet, Corps);
            _ctx.Plateforme.Informer("Justificatif envoyé", $"Le justificatif {_j.Numero} a été envoyé à {Destinataire}.");
            Terminer(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException
                                       or ICA.Client.Google.GoogleDeconnecteException or ICA.Client.Google.SansBoiteGmailException or IOException)
        {
            _ctx.Journal.Erreur("Envoi du justificatif impossible", ex);
            Erreur = ex is ICA.Client.Google.SansBoiteGmailException s ? s.Message
                : "L'envoi a échoué. Vérifiez Internet, ou utilisez « Préparer dans ma messagerie ».";
        }
    }

    /// <summary>Sans Gmail relié : le PDF est copié sur le Bureau et la messagerie s'ouvre, prête.</summary>
    private async Task PreparerAsync()
    {
        Erreur = null;
        if (!Verifier()) return;
        try
        {
            var source = await _ctx.Justificatifs!.FichierAsync(_j);
            var bureau = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var copie = Path.Combine(bureau, _j.NomFichier);
            File.Copy(source, copie, overwrite: true);
            var pieces = $"« {_j.NomFichier} »";
            if (_j.Type == TypeJustificatif.RecuFiscal)
            {
                var lettre = _ctx.Justificatifs.LettreAccompagnement(_j);
                File.WriteAllBytes(Path.Combine(bureau, lettre.Nom), lettre.Contenu);
                pieces += $" et « {lettre.Nom} »";
            }
            var lien = LienCourriel.Creer(_ctx.Preferences.Messagerie, [Destinataire], Objet, Corps);
            if (!_ctx.Plateforme.OuvrirLien(lien))
            {
                _ctx.Plateforme.CopierTexte(Corps);
                Erreur = "Aucune messagerie trouvée. Le texte est copié et le PDF est sur votre Bureau.";
                return;
            }
            _ctx.Plateforme.Informer("Message préparé",
                $"Votre messagerie s'est ouverte. Ajoutez en pièce jointe {pieces} (sur votre Bureau), puis envoyez.\n\n" +
                "Le justificatif sera noté « envoyé ».");
            _ctx.Depot.MarquerJustificatifEnvoye(_j.Id, Destinataire + " (via la messagerie)");
            Terminer(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Erreur = "Préparation impossible : " + ex.Message;
        }
    }
}

// =====================================================================
//  Association et statut fiscal
// =====================================================================

public sealed class EditionAssociationViewModel : FormulaireViewModel
{
    private readonly ContexteApplication _ctx;
    private readonly ParametresAssociation _p;
    private EligibiliteFiscale _eligibilite;

    public EditionAssociationViewModel(ContexteApplication ctx)
    {
        _ctx = ctx;
        var actuel = ctx.Depot.Association;
        _p = new ParametresAssociation
        {
            Identite = actuel.Identite.Copie(), Eligibilite = actuel.Eligibilite, ReferenceRescrit = actuel.ReferenceRescrit,
            JustificationEligibilite = actuel.JustificationEligibilite, DateDecisionEligibilite = actuel.DateDecisionEligibilite,
            DateRescrit = actuel.DateRescrit, PiecesRescrit = actuel.PiecesRescrit, SignatureJpeg = actuel.SignatureJpeg,
            ModeleVerifie = actuel.ModeleVerifie, DateVerificationModele = actuel.DateVerificationModele,
        };
        _eligibilite = actuel.Eligibilite;
        _categorie = CategoriesFiscales.Trouver(_p.Identite.Categorie) ?? CategoriesFiscales.Toutes[0];
        _dateRescrit = actuel.DateRescrit?.ToDateTime(TimeOnly.MinValue);
        _specimenVerifie = actuel.ModeleVerifie == CategoriesFiscales.ModeleEnVigueur;
        EnregistrerCommand = new RelayCommand(Enregistrer);
        ChoisirSignatureCommand = new RelayCommand(ChoisirSignature);
        RetirerSignatureCommand = new RelayCommand(() => { _p.SignatureJpeg = null; OnPropertyChanged(nameof(TexteSignature)); OnPropertyChanged(nameof(ASignature)); }, () => true);
        SpecimenCommand = new RelayCommand(Specimen);
    }

    private CategorieFiscale _categorie;
    private DateTime? _dateRescrit;
    private bool _specimenVerifie;
    private bool _specimenOuvert;

    public override string TitreFenetre => "Association et reçus fiscaux";
    public override double LargeurFenetre => 760;
    public RelayCommand EnregistrerCommand { get; }
    private IdentiteAssociation I => _p.Identite;

    public string Denomination { get => I.Denomination; set => I.Denomination = value; }
    public string? NumeroSiren { get => I.Siren; set => I.Siren = value; }
    public string? Rna { get => I.Rna; set => I.Rna = value; }
    public string? Adresse { get => I.Adresse; set => I.Adresse = value; }
    public string? CodePostal { get => I.CodePostal; set => I.CodePostal = value; }
    public string? Ville { get => I.Ville; set => I.Ville = value; }
    public string? EmailAsso { get => I.Email; set => I.Email = value; }
    public string? TelephoneAsso { get => I.Telephone; set => I.Telephone = value; }
    public string Objet { get => I.Objet; set => I.Objet = value; }
    public IReadOnlyList<CategorieFiscale> Categories => CategoriesFiscales.Toutes;
    public CategorieFiscale Categorie
    {
        get => _categorie;
        set { if (SetProperty(ref _categorie, value)) OnPropertyChanged(nameof(AvecPrecision)); }
    }
    public bool AvecPrecision => _categorie.AvecPrecision;
    public string? CategoriePrecision { get => I.CategoriePrecision; set => I.CategoriePrecision = value; }
    public string? SignataireNom { get => I.SignataireNom; set => I.SignataireNom = value; }
    public string? SignataireQualite { get => I.SignataireQualite; set => I.SignataireQualite = value; }
    public string? LieuSignature { get => I.LieuSignature; set => I.LieuSignature = value; }

    public bool NonVerifiee { get => _eligibilite == EligibiliteFiscale.NonVerifiee; set { if (value) Changer(EligibiliteFiscale.NonVerifiee); } }
    public bool Confirmee { get => _eligibilite == EligibiliteFiscale.Confirmee; set { if (value) Changer(EligibiliteFiscale.Confirmee); } }
    public bool Desactivee { get => _eligibilite == EligibiliteFiscale.Desactivee; set { if (value) Changer(EligibiliteFiscale.Desactivee); } }
    public string? ReferenceRescrit { get => _p.ReferenceRescrit; set => _p.ReferenceRescrit = value; }
    public DateTime? DateRescrit { get => _dateRescrit; set => SetProperty(ref _dateRescrit, value); }
    public string? PiecesRescrit { get => _p.PiecesRescrit; set => _p.PiecesRescrit = value; }
    public string? Justification { get => _p.JustificationEligibilite; set => _p.JustificationEligibilite = value; }

    // ---------- Signature du représentant habilité ----------

    public RelayCommand ChoisirSignatureCommand { get; }
    public RelayCommand RetirerSignatureCommand { get; }
    public bool ASignature => !string.IsNullOrEmpty(_p.SignatureJpeg);
    public string TexteSignature => ASignature
        ? "Signature enregistrée : elle sera apposée sur les reçus et les lettres."
        : "Aucune signature : obligatoire pour les reçus fiscaux. Scannez ou photographiez la signature sur fond blanc.";

    private void ChoisirSignature()
    {
        var chemin = _ctx.Plateforme.ChoisirFichierAOuvrir("Image de la signature", "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp");
        if (chemin is null) return;
        var jpeg = _ctx.Plateforme.ImageEnJpeg(chemin);
        if (jpeg is null) { Erreur = "Cette image n'a pas pu être lue. Essayez une photo ou un scan au format JPEG ou PNG."; return; }
        Erreur = null;
        _p.SignatureJpeg = Convert.ToBase64String(jpeg);
        OnPropertyChanged(nameof(TexteSignature));
        OnPropertyChanged(nameof(ASignature));
    }

    // ---------- Spécimen ----------

    public RelayCommand SpecimenCommand { get; }
    public string ModeleEnVigueur => $"Cerfa n° {CategoriesFiscales.ModeleEnVigueur} (2041-MEC-SD)";
    public bool SpecimenVerifie { get => _specimenVerifie; set => SetProperty(ref _specimenVerifie, value); }

    private void Specimen()
    {
        if (_ctx.Justificatifs is not { } service) return;
        AppliquerSaisie();
        try
        {
            _ctx.Plateforme.OuvrirLien(service.Specimen(_p));
            _specimenOuvert = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Erreur = "Le spécimen n'a pas pu être créé : " + ex.Message;
        }
    }

    private void AppliquerSaisie()
    {
        I.Categorie = _categorie.Code;
        if (!_categorie.AvecPrecision) I.CategoriePrecision = null;
        _p.DateRescrit = _dateRescrit is { } d ? DateOnly.FromDateTime(d) : null;
    }

    public const string Avertissement =
        "Le statut « association loi 1901 » ne suffit pas : l'association doit remplir les conditions de l'intérêt général " +
        "(activité non lucrative, gestion désintéressée, pas de cercle restreint de personnes…). En cas de doute, demandez un rescrit au service des impôts. " +
        "Cocher « confirmée » ici n'est pas une validation de l'administration fiscale : c'est l'association qui engage sa responsabilité " +
        "en délivrant des reçus (amende en cas de reçu indu).";
    public string TexteAvertissement => Avertissement;

    private void Changer(EligibiliteFiscale e)
    {
        _eligibilite = e;
        OnPropertyChanged(nameof(NonVerifiee));
        OnPropertyChanged(nameof(Confirmee));
        OnPropertyChanged(nameof(Desactivee));
    }

    private void Enregistrer()
    {
        if (string.IsNullOrWhiteSpace(I.Denomination)) { Erreur = "La dénomination est obligatoire."; return; }
        if (!string.IsNullOrWhiteSpace(I.Siren) && !Siren.EstValide(I.Siren)) { Erreur = "Le SIREN de l'association n'est pas valide (9 chiffres)."; return; }
        if (!string.IsNullOrWhiteSpace(I.Rna) && !System.Text.RegularExpressions.Regex.IsMatch(I.Rna.Trim(), @"^W\d{9}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        { Erreur = "Le numéro RNA s'écrit W suivi de 9 chiffres (exemple : W123456789)."; return; }
        if (_eligibilite == EligibiliteFiscale.Confirmee)
        {
            if (string.IsNullOrWhiteSpace(I.Siren) && string.IsNullOrWhiteSpace(I.Rna)) { Erreur = "Pour émettre des reçus fiscaux, indiquez le SIREN ou le RNA de l'association."; return; }
            if (string.IsNullOrWhiteSpace(_p.ReferenceRescrit) && string.IsNullOrWhiteSpace(_p.JustificationEligibilite))
            { Erreur = "Indiquez la référence du rescrit fiscal ou les éléments qui justifient l'éligibilité."; return; }
            if (_ctx.Depot.Association.Eligibilite != EligibiliteFiscale.Confirmee &&
                !_ctx.Plateforme.Confirmer("Confirmer l'éligibilité ?", Avertissement + "\n\nConfirmez-vous que L'ICA remplit ces conditions ?", "Je confirme"))
                return;
        }
        if (_specimenVerifie && _p.ModeleVerifie != CategoriesFiscales.ModeleEnVigueur && !_specimenOuvert)
        { Erreur = "Ouvrez d'abord le spécimen et comparez-le, rubrique par rubrique, au formulaire officiel."; return; }
        I.Siren = Siren.Normaliser(I.Siren);
        I.Rna = Nettoyer(I.Rna)?.ToUpperInvariant();
        AppliquerSaisie();
        if (_specimenVerifie && _p.ModeleVerifie != CategoriesFiscales.ModeleEnVigueur)
        {
            _p.ModeleVerifie = CategoriesFiscales.ModeleEnVigueur;
            _p.DateVerificationModele = _ctx.Depot.AujourdHui;
        }
        else if (!_specimenVerifie)
        {
            _p.ModeleVerifie = null;
            _p.DateVerificationModele = null;
        }
        if (_eligibilite != _ctx.Depot.Association.Eligibilite) _p.DateDecisionEligibilite = _ctx.Depot.AujourdHui;
        _p.Eligibilite = _eligibilite;
        _ctx.Depot.EnregistrerAssociation(_p);
        Terminer(true);
    }
}
