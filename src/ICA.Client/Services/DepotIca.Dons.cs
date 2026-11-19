using ICA.Domain;

namespace ICA.Client.Services;

public sealed record BilanDons(
    int Annee,
    decimal TotalFinancier,
    decimal TotalNature,
    decimal TotalCompetences,
    int NombreEntreprises,
    int NombreDons,
    int RecusFiscauxEmis,
    decimal MontantRecusFiscaux,
    int AttestationsEmises,
    int DonsSansJustificatif,
    int JustificatifsNonEnvoyes)
{
    public decimal TotalNatureEtCompetences => TotalNature + TotalCompetences;
}

/// <summary>Dons & Mécénat : entreprises, dons, justificatifs, traçabilité.</summary>
public sealed partial class DepotIca
{
    /// <summary>Identifiant fixe : tous les ordinateurs partagent la même fiche « Association ».</summary>
    public static readonly Guid IdAssociation = new("1ca00000-0000-7000-8000-000000000001");

    // ---------- Lecture ----------

    public IReadOnlyList<DonateurEntreprise> Entreprises =>
        _donnees.Entreprises.Where(e => !e.EstSupprime).OrderBy(e => e.RaisonSociale, StringComparer.CurrentCultureIgnoreCase).ToList();

    public DonateurEntreprise? TrouverEntreprise(Guid id) => _donnees.Entreprises.FirstOrDefault(e => e.Id == id);

    public IReadOnlyList<Don> Dons =>
        _donnees.Dons.Where(d => !d.EstSupprime).OrderByDescending(d => d.Date).ThenByDescending(d => d.CreatedAt).ToList();

    public Don? TrouverDon(Guid id) => _donnees.Dons.FirstOrDefault(d => d.Id == id && !d.EstSupprime);

    public IReadOnlyList<Don> DonsDe(Guid entrepriseId) => Dons.Where(d => d.EntrepriseId == entrepriseId).ToList();

    public IReadOnlyList<JustificatifDon> Justificatifs =>
        _donnees.Justificatifs.Where(j => !j.EstSupprime && j.Statut != StatutJustificatif.Reserve)
            .OrderByDescending(j => j.DateEmission).ThenByDescending(j => j.Numero, StringComparer.Ordinal).ToList();

    public JustificatifDon? TrouverJustificatif(Guid id) => _donnees.Justificatifs.FirstOrDefault(j => j.Id == id);

    /// <summary>Justificatif en vigueur (émis, ni annulé ni remplacé) qui couvre ce don.</summary>
    public JustificatifDon? JustificatifDe(Guid donId) =>
        _donnees.Justificatifs.Where(j => j.EstActif && j.DonIds.Contains(donId)).OrderByDescending(j => j.DateEmission).FirstOrDefault();

    public IReadOnlyList<HistoriqueDon> HistoriqueDe(Guid cibleId) =>
        _donnees.HistoriqueDons.Where(h => h.CibleId == cibleId).OrderByDescending(h => h.Quand).ToList();

    public ParametresAssociation Association =>
        _donnees.Association.FirstOrDefault(a => a.Id == IdAssociation)
        ?? _donnees.Association.OrderByDescending(a => a.UpdatedAt).FirstOrDefault()
        ?? new ParametresAssociation { Id = IdAssociation };

    /// <summary>Éligibilité confirmée, informations obligatoires complètes et spécimen vérifié contre le Cerfa en vigueur.</summary>
    public bool RecusFiscauxAutorises => ConditionsRecuFiscal.Association(Association).Count == 0;

    public IReadOnlyList<string> ManquesRecuFiscal => ConditionsRecuFiscal.Association(Association);

    public byte[]? SignatureJpeg
    {
        get
        {
            try { return Association.SignatureJpeg is { Length: > 0 } s ? Convert.FromBase64String(s) : null; }
            catch (FormatException) { return null; }
        }
    }

    public IReadOnlyList<int> AnneesDons =>
        Dons.Select(d => d.Date.Year).Append(AujourdHui.Year).Distinct().OrderDescending().ToList();

    /// <summary>Tous les numéros déjà attribués (y compris annulés : jamais réutilisés).</summary>
    public IEnumerable<string> NumerosAttribues => _donnees.Justificatifs.Select(j => j.Numero);

    public BilanDons BilanDonsDe(int annee)
    {
        var dons = Dons.Where(d => d.Date.Year == annee && d.Statut != StatutDon.Annule).ToList();
        var recus = dons.Where(d => d.Statut == StatutDon.Recu).ToList();
        var emis = _donnees.Justificatifs.Where(j => j.EstActif && j.Annee == annee).ToList();
        var recusFiscaux = emis.Where(j => j.Type == TypeJustificatif.RecuFiscal).ToList();
        return new BilanDons(
            annee,
            recus.Where(d => d.Type == TypeDon.Financier).Sum(d => d.Montant ?? 0),
            recus.Where(d => d.Type == TypeDon.Nature).Sum(d => d.Montant ?? 0),
            recus.Where(d => d.Type == TypeDon.Competences).Sum(d => d.Montant ?? 0),
            dons.Select(d => d.EntrepriseId).Distinct().Count(),
            dons.Count,
            recusFiscaux.Count,
            recusFiscaux.Sum(j => j.Contenu?.Total ?? 0),
            emis.Count(j => j.Type == TypeJustificatif.Attestation),
            recus.Count(d => JustificatifDe(d.Id) is null),
            emis.Count(j => j.DateEnvoi is null));
    }

    // ---------- Association ----------

    public void EnregistrerAssociation(ParametresAssociation p)
    {
        var maintenant = _temps.GetUtcNow();
        var existant = _donnees.Association.FirstOrDefault(a => a.Id == IdAssociation);
        var ancienStatut = existant?.Eligibilite ?? EligibiliteFiscale.NonVerifiee;
        p.Id = IdAssociation;
        if (existant is null)
        {
            p.CreatedAt = maintenant;
            p.UpdatedAt = maintenant;
            _donnees.Association.Add(p);
        }
        else
        {
            p.CreatedAt = existant.CreatedAt;
            p.Version = existant.Version;
            p.MarquerModifie(maintenant);
            _donnees.Association[_donnees.Association.IndexOf(existant)] = p;
        }
        if (p.Eligibilite != ancienStatut)
            Tracer("Statut fiscal modifié", IdAssociation, $"{ancienStatut} → {p.Eligibilite}" + (p.ReferenceRescrit is { Length: > 0 } r ? $" (rescrit {r})" : ""));
        else
            Tracer("Coordonnées de l'association modifiées", IdAssociation, "");
        Enregistrer();
    }

    // ---------- Entreprises ----------

    public IReadOnlyList<DonateurEntreprise> DoublonsEntreprise(DonateurEntreprise candidat) =>
        _donnees.Entreprises.Where(e => RegleEntreprise.EstDoublon(e, candidat)).ToList();

    public Resultat EnregistrerEntreprise(DonateurEntreprise e)
    {
        e.RaisonSociale = e.RaisonSociale.Trim();
        e.Siren = Siren.Normaliser(e.Siren);
        e.Siret = Siren.Normaliser(e.Siret);
        e.Email = string.IsNullOrWhiteSpace(e.Email) ? null : e.Email.Trim();
        if (!string.IsNullOrWhiteSpace(e.Telephone) && Telephone.TryNormaliser(e.Telephone, out var tel)) e.Telephone = tel;
        var erreurs = RegleEntreprise.Valider(e);
        if (erreurs.Count > 0) return new Resultat(false, erreurs);
        if (Siren.Normaliser(e.Siren) is { } s && _donnees.Entreprises.Any(x => x.Id != e.Id && !x.EstSupprime && x.Siren == s))
            return Resultat.Echec("Siren", "Une entreprise avec ce numéro SIREN existe déjà dans l'annuaire.");

        var maintenant = _temps.GetUtcNow();
        var existante = _donnees.Entreprises.FirstOrDefault(x => x.Id == e.Id);
        if (existante is null)
        {
            e.CreatedAt = maintenant;
            e.UpdatedAt = maintenant;
            _donnees.Entreprises.Add(e);
            Tracer("Entreprise ajoutée", e.Id, e.RaisonSociale);
        }
        else
        {
            e.CreatedAt = existante.CreatedAt;
            e.Version = existante.Version;
            e.MarquerModifie(maintenant);
            _donnees.Entreprises[_donnees.Entreprises.IndexOf(existante)] = e;
            Tracer("Entreprise modifiée", e.Id, e.RaisonSociale);
        }
        Enregistrer();
        return Resultat.Ok();
    }

    public Resultat SupprimerEntreprise(Guid id)
    {
        if (TrouverEntreprise(id) is not { EstSupprime: false } e) return Resultat.Ok();
        if (_donnees.Dons.Any(d => d.EntrepriseId == id && !d.EstSupprime))
            return Resultat.Echec("", "Cette entreprise a des dons enregistrés : elle reste dans l'annuaire pour l'historique.");
        e.Supprimer(_temps.GetUtcNow());
        Tracer("Entreprise retirée de l'annuaire", id, e.RaisonSociale);
        Enregistrer();
        return Resultat.Ok();
    }

    // ---------- Dons ----------

    /// <summary>Même entreprise, même date, même montant : probablement saisi deux fois.</summary>
    public IReadOnlyList<Don> DoublonsDon(Don candidat) =>
        Dons.Where(d => d.Id != candidat.Id && d.EntrepriseId == candidat.EntrepriseId && d.Date == candidat.Date
                        && d.Type == candidat.Type && d.Montant == candidat.Montant && d.Statut != StatutDon.Annule).ToList();

    public Resultat EnregistrerDon(Don d)
    {
        if (d.Type != TypeDon.Financier) d.Mode = null;
        d.Description = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description.Trim();
        var erreurs = RegleDon.Valider(d);
        if (erreurs.Count > 0) return new Resultat(false, erreurs);
        if (TrouverEntreprise(d.EntrepriseId) is not { EstSupprime: false })
            return Resultat.Echec("EntrepriseId", "Cette entreprise n'existe plus dans l'annuaire.");

        var maintenant = _temps.GetUtcNow();
        var existant = _donnees.Dons.FirstOrDefault(x => x.Id == d.Id);
        var montant = d.Montant is { } m ? Libelles.Montant(m) : "sans montant";
        if (existant is null)
        {
            d.CreatedAt = maintenant;
            d.UpdatedAt = maintenant;
            _donnees.Dons.Add(d);
            Tracer("Don enregistré", d.Id, $"{RegleDon.Libelle(d.Type)}, {montant}, {d.Date:dd/MM/yyyy}");
        }
        else
        {
            var avant = $"{RegleDon.Libelle(existant.Type)}, {(existant.Montant is { } a ? Libelles.Montant(a) : "sans montant")}, {existant.Date:dd/MM/yyyy}, {RegleDon.Libelle(existant.Statut)}";
            d.CreatedAt = existant.CreatedAt;
            d.Version = existant.Version;
            d.MarquerModifie(maintenant);
            _donnees.Dons[_donnees.Dons.IndexOf(existant)] = d;
            Tracer("Don corrigé", d.Id, $"Avant : {avant}. Après : {RegleDon.Libelle(d.Type)}, {montant}, {d.Date:dd/MM/yyyy}, {RegleDon.Libelle(d.Statut)}");
        }
        Enregistrer();
        return Resultat.Ok();
    }

    // ---------- Justificatifs ----------

    /// <summary>Étape 1 : attribue le prochain numéro (statut « Réservé », aucun PDF encore).</summary>
    public JustificatifDon ReserverJustificatif(TypeJustificatif type, Guid entrepriseId, IReadOnlyList<Guid> donIds, int annee, Guid? remplace = null)
    {
        if (type == TypeJustificatif.RecuFiscal && !RecusFiscauxAutorises)
            throw new InvalidOperationException("Reçu fiscal impossible : il manque " + string.Join(", ", ManquesRecuFiscal) + ".");
        var maintenant = _temps.GetUtcNow();
        var j = new JustificatifDon
        {
            Type = type, Annee = annee, EntrepriseId = entrepriseId, DonIds = donIds.ToList(), RemplaceId = remplace,
            Numero = NumerosJustificatif.Suivant(type, annee, NumerosAttribues),
            CreatedAt = maintenant, UpdatedAt = maintenant,
        };
        _donnees.Justificatifs.Add(j);
        Enregistrer();
        return j;
    }

    /// <summary>Étape 2 : le PDF est créé ; le contenu est figé et le document devient officiel.</summary>
    public void EmettreJustificatif(Guid id, ContenuJustificatif contenu, string empreinte)
    {
        var j = TrouverJustificatif(id) ?? throw new InvalidOperationException("Justificatif introuvable.");
        if (j.Statut != StatutJustificatif.Reserve) throw new InvalidOperationException("Ce justificatif n'est plus en réservation.");
        var maintenant = _temps.GetUtcNow();
        j.Contenu = contenu;
        j.Empreinte = empreinte;
        j.DateEmission = AujourdHui;
        j.Statut = StatutJustificatif.Emis;
        j.MarquerModifie(maintenant);
        Tracer(j.Type == TypeJustificatif.RecuFiscal ? "Reçu fiscal émis" : "Attestation émise", j.Id,
            $"{j.Numero} — {contenu.EntrepriseNom} — {Libelles.Montant(contenu.Total)}");

        if (j.RemplaceId is { } ancienId && TrouverJustificatif(ancienId) is { } ancien && ancien.Statut is StatutJustificatif.Emis or StatutJustificatif.Annule)
        {
            ancien.Statut = StatutJustificatif.Remplace;
            ancien.RemplacePar = j.Id;
            ancien.MarquerModifie(maintenant);
            Tracer("Justificatif remplacé", ancien.Id, $"{ancien.Numero} remplacé par {j.Numero}");
        }
        Enregistrer();
    }

    /// <summary>Réservation abandonnée (erreur pendant la création du PDF) : le numéro est perdu, pas réutilisé.</summary>
    public void AbandonnerReservation(Guid id, string motif)
    {
        if (TrouverJustificatif(id) is not { Statut: StatutJustificatif.Reserve } j) return;
        j.Statut = StatutJustificatif.Annule;
        j.MotifAnnulation = motif;
        j.MarquerModifie(_temps.GetUtcNow());
        Tracer("Numéro non utilisé", j.Id, $"{j.Numero} : {motif}");
        Enregistrer();
    }

    public void AnnulerJustificatif(Guid id, string motif)
    {
        if (TrouverJustificatif(id) is not { Statut: StatutJustificatif.Emis } j) return;
        j.Statut = StatutJustificatif.Annule;
        j.MotifAnnulation = motif;
        j.MarquerModifie(_temps.GetUtcNow());
        Tracer("Justificatif annulé", j.Id, $"{j.Numero} : {motif}");
        Enregistrer();
    }

    public void MarquerJustificatifEnvoye(Guid id, string destinataire)
    {
        if (TrouverJustificatif(id) is not { } j) return;
        j.DateEnvoi = _temps.GetUtcNow();
        j.EnvoyeA = destinataire;
        j.MarquerModifie(_temps.GetUtcNow());
        Tracer("Justificatif envoyé", j.Id, $"{j.Numero} envoyé à {destinataire}");
        Enregistrer();
    }

    public void DefinirFichierDrive(Guid id, string driveId)
    {
        if (TrouverJustificatif(id) is not { } j || j.DriveFichierId == driveId) return;
        j.DriveFichierId = driveId;
        j.MarquerModifie(_temps.GetUtcNow());
        Enregistrer();
    }

    /// <summary>Réservations restées en plan (coupure pendant l'émission) : le numéro est abandonné, jamais réutilisé.</summary>
    private void NettoyerReservations()
    {
        var limite = _temps.GetUtcNow().AddHours(-1);
        var anciennes = _donnees.Justificatifs.Where(j => j.Statut == StatutJustificatif.Reserve && j.CreatedAt < limite).ToList();
        foreach (var j in anciennes)
        {
            j.Statut = StatutJustificatif.Annule;
            j.MotifAnnulation = "Émission interrompue : numéro non utilisé.";
            j.MarquerModifie(_temps.GetUtcNow());
            Tracer("Numéro non utilisé", j.Id, $"{j.Numero} : émission interrompue");
        }
        if (anciennes.Count > 0) Enregistrer();
    }

    // ---------- Traçabilité ----------

    private void Tracer(string action, Guid cible, string detail)
    {
        var maintenant = _temps.GetUtcNow();
        _donnees.HistoriqueDons.Add(new HistoriqueDon
        {
            Quand = maintenant, CreatedAt = maintenant, UpdatedAt = maintenant,
            Action = action, CibleId = cible, Detail = detail, Poste = Environment.MachineName,
        });
    }
}
