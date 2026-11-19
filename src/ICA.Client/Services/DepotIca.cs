using ICA.Client.Configuration;
using ICA.Client.Donnees;
using ICA.Client.Journal;
using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.Services;

public sealed record Resultat(bool Reussi, IReadOnlyList<ErreurValidation> Erreurs)
{
    public static Resultat Ok() => new(true, []);
    public static Resultat Echec(string champ, string message) => new(false, [new ErreurValidation(champ, message)]);
}

public sealed record Statistiques(int Total, int Adultes, int Enfants, int Payees, int ARegler);

/// <summary>Argent de la saison : encaissé (par mode de paiement) et restant à encaisser.</summary>
public sealed record BilanFinancier(decimal Encaisse, decimal ResteAEncaisser, decimal Especes, decimal Cheques, decimal Virements, decimal Autres)
{
    public decimal Prevu => Encaisse + ResteAEncaisser;
}

/// <summary>
/// Point d'accès unique aux données pour les écrans.
/// v0.1 : enregistre localement. v0.4 : chaque écriture ajoutera aussi une opération à la file de synchronisation.
/// </summary>
public sealed partial class DepotIca
{
    private readonly IStockage _stockage;
    private readonly IJournal _journal;
    private readonly ParametresApplication _parametres;
    private readonly TimeProvider _temps;
    private DonneesIca _donnees;

    public DepotIca(IStockage stockage, IJournal journal, ParametresApplication parametres, TimeProvider? temps = null)
    {
        _stockage = stockage;
        _journal = journal;
        _parametres = parametres;
        _temps = temps ?? TimeProvider.System;

        var charge = _stockage.Charger();
        if (charge is null)
        {
            _donnees = DonneesDemo.Creer(AujourdHui, SaisonCourante, _parametres.Cotisations);
            Enregistrer();
            _journal.Info("Données de démonstration créées.");
        }
        else
        {
            _donnees = charge;
            _journal.Info($"Données chargées : {_donnees.Adherents.Count} fiches.");
            var aMettreAJour = false;
            if (_donnees.ModelesMail.Count == 0)
            {
                // Fichier créé avant l'arrivée des modèles : on les ajoute.
                _donnees.ModelesMail = ModelesParDefaut.Creer();
                aMettreAJour = true;
            }
            if (_donnees.VersionSchema < 2)
            {
                // Fichier de démonstration créé avant le calendrier : on ajoute un planning fictif.
                if (_donnees.Seances.Count == 0)
                    _donnees.Seances = DonneesDemo.CreerPlanning(_donnees.Activites, AujourdHui);
                _donnees.VersionSchema = 2;
                aMettreAJour = true;
            }
            if (_donnees.VersionSchema < 3)
            {
                // Tarifs (25 € / 15 €), nouveaux textes de modèles, planning du mercredi 14h-17h.
                foreach (var c in _donnees.Cotisations.Where(c => c.Montant is null && !c.EstSupprime))
                    if (_donnees.Adherents.FirstOrDefault(a => a.Id == c.AdherentId) is { } a)
                        c.Montant = _parametres.Cotisations.TarifPour(a.TypeAdherent);
                _donnees.ModelesMail.RemoveAll(m => m.EstSysteme);
                _donnees.ModelesMail.AddRange(ModelesParDefaut.Creer());
                _donnees.Seances = DonneesDemo.CreerPlanning(_donnees.Activites, AujourdHui);
                _donnees.VersionSchema = 3;
                aMettreAJour = true;
            }
            if (aMettreAJour) Enregistrer();
            NettoyerReservations();
        }
    }

    /// <summary>Déclenché après chaque modification, pour rafraîchir les écrans.</summary>
    public event EventHandler? Modifie;

    public DateOnly AujourdHui => DateOnly.FromDateTime(_temps.GetLocalNow().DateTime);
    public string SaisonCourante => global::ICA.Domain.Saisons.Courante(AujourdHui, _parametres.Cotisations.ModeSaison);
    public string EmplacementDonnees => _stockage.Emplacement;
    public bool EstDemo => _donnees.EstDemo;

    // ---------- Lecture ----------

    public IReadOnlyList<Adherent> Adherents =>
        _donnees.Adherents.Where(a => !a.EstSupprime).ToList();

    public Adherent? TrouverAdherent(Guid id) =>
        _donnees.Adherents.FirstOrDefault(a => a.Id == id && !a.EstSupprime);

    public IReadOnlyList<Adherent> EnfantsDe(Guid responsableId) =>
        _donnees.Adherents.Where(a => !a.EstSupprime && a.ResponsableId == responsableId)
            .OrderBy(a => a.Prenom).ToList();

    public IReadOnlyList<Adherent> AdultesPourResponsable() =>
        _donnees.Adherents.Where(a => !a.EstSupprime && a.TypeAdherent == TypeAdherent.Adulte)
            .OrderBy(a => a.Nom).ThenBy(a => a.Prenom).ToList();

    public Cotisation? CotisationDe(Guid adherentId, string saison) =>
        _donnees.Cotisations.FirstOrDefault(c => !c.EstSupprime && c.AdherentId == adherentId && c.Saison == saison);

    /// <summary>Un adhérent sans cotisation enregistrée pour la saison est considéré « à régler ».</summary>
    public StatutCotisation StatutDe(Guid adherentId, string saison) =>
        CotisationDe(adherentId, saison)?.Statut ?? StatutCotisation.APayer;

    public IReadOnlyList<string> Saisons() =>
        _donnees.Cotisations.Where(c => !c.EstSupprime).Select(c => c.Saison)
            .Append(SaisonCourante).Distinct().OrderDescending().ToList();

    public IReadOnlyList<Activite> ActivitesDe(Guid adherentId)
    {
        var ids = _donnees.AdherentActivites.Where(l => !l.EstSupprime && l.AdherentId == adherentId)
            .Select(l => l.ActiviteId).ToHashSet();
        return _donnees.Activites.Where(a => !a.EstSupprime && ids.Contains(a.Id)).OrderBy(a => a.Nom).ToList();
    }

    public IReadOnlyList<ModeleMail> ModelesMail =>
        _donnees.ModelesMail.Where(m => !m.EstSupprime).OrderBy(m => m.Ordre).ThenBy(m => m.Nom).ToList();

    /// <summary>Adresse à utiliser : celle de l'adhérent, ou celle de son responsable pour un enfant.</summary>
    public string? EmailDeContact(Adherent a) =>
        a.Email ?? (a.ResponsableId is { } rid ? TrouverAdherent(rid)?.Email : null);

    // ---------- Activités et calendrier ----------

    public IReadOnlyList<Activite> Activites =>
        _donnees.Activites.Where(a => !a.EstSupprime && !a.Archivee).OrderBy(a => a.Nom).ToList();

    public Activite? TrouverActivite(Guid id) => _donnees.Activites.FirstOrDefault(a => a.Id == id && !a.EstSupprime);

    public IReadOnlyList<Adherent> ParticipantsDe(Guid activiteId)
    {
        var ids = _donnees.AdherentActivites.Where(l => !l.EstSupprime && l.ActiviteId == activiteId)
            .Select(l => l.AdherentId).ToHashSet();
        return Adherents.Where(a => ids.Contains(a.Id)).OrderBy(a => a.Nom).ThenBy(a => a.Prenom).ToList();
    }

    public IReadOnlyList<Seance> SeancesEntre(DateOnly du, DateOnly au) =>
        _donnees.Seances.Where(s => !s.EstSupprime && s.Date >= du && s.Date <= au)
            .OrderBy(s => s.Date).ThenBy(s => s.HeureDebut ?? TimeOnly.MinValue).ToList();

    public Seance? TrouverSeance(Guid id) => _donnees.Seances.FirstOrDefault(s => s.Id == id && !s.EstSupprime);

    /// <summary>« Couture », ou « Couture — Spécial Noël », ou le nom de l'événement.</summary>
    public string NomDe(Seance s)
    {
        var activite = s.ActiviteId is { } id ? TrouverActivite(id)?.Nom : null;
        return (activite, s.Titre) switch
        {
            ({ } a, { Length: > 0 } t) => $"{a} — {t}",
            ({ } a, _) => a,
            (null, { Length: > 0 } t) => t,
            _ => "Événement",
        };
    }

    public string CouleurDe(Seance s) =>
        (s.ActiviteId is { } id ? TrouverActivite(id)?.Couleur : null) ?? "#9E1B32";

    /// <summary>Participants d'une séance : les inscrits de l'activité, ou tout le monde pour un événement.</summary>
    public IReadOnlyList<Adherent> ParticipantsDe(Seance s) =>
        s.ActiviteId is { } id ? ParticipantsDe(id) : Adherents;

    public Resultat EnregistrerSeances(IReadOnlyList<Seance> seances)
    {
        foreach (var s in seances)
        {
            s.Titre = Texte.Nettoyer(s.Titre);
            s.Lieu = Texte.Nettoyer(s.Lieu);
            s.Notes = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Trim();
            var erreurs = RegleSeance.Valider(s);
            if (erreurs.Count > 0) return new Resultat(false, erreurs);
        }

        var maintenant = _temps.GetUtcNow();
        foreach (var s in seances)
        {
            var existante = _donnees.Seances.FirstOrDefault(x => x.Id == s.Id);
            if (existante is null)
            {
                s.CreatedAt = maintenant;
                s.UpdatedAt = maintenant;
                _donnees.Seances.Add(s);
            }
            else
            {
                existante.ActiviteId = s.ActiviteId;
                existante.Titre = s.Titre;
                existante.Date = s.Date;
                existante.HeureDebut = s.HeureDebut;
                existante.HeureFin = s.HeureFin;
                existante.Lieu = s.Lieu;
                existante.Notes = s.Notes;
                existante.Annulee = s.Annulee;
                existante.MarquerModifie(maintenant);
            }
        }
        _journal.Info($"{seances.Count} séance(s) enregistrée(s)");
        Enregistrer();
        return Resultat.Ok();
    }

    public void ChangerAnnulation(Guid seanceId, bool annulee)
    {
        if (TrouverSeance(seanceId) is not { } s || s.Annulee == annulee) return;
        s.Annulee = annulee;
        s.MarquerModifie(_temps.GetUtcNow());
        _journal.Info($"Séance {seanceId} {(annulee ? "annulée" : "rétablie")}");
        Enregistrer();
    }

    /// <summary>Supprime une séance, ou toutes les séances à venir de la même série.</summary>
    public void SupprimerSeance(Guid seanceId, bool toutesLesSuivantes)
    {
        if (TrouverSeance(seanceId) is not { } s) return;
        var maintenant = _temps.GetUtcNow();
        var cibles = toutesLesSuivantes && s.SerieId is { } serie
            ? _donnees.Seances.Where(x => !x.EstSupprime && x.SerieId == serie && x.Date >= s.Date).ToList()
            : [s];
        foreach (var c in cibles) c.Supprimer(maintenant);
        _journal.Info($"{cibles.Count} séance(s) supprimée(s)");
        Enregistrer();
    }

    public Resultat EnregistrerActivite(Activite modifiee)
    {
        modifiee.Nom = Texte.Nettoyer(modifiee.Nom) ?? "";
        modifiee.Description = Texte.Nettoyer(modifiee.Description);
        if (modifiee.Nom.Length == 0) return Resultat.Echec("Nom", "Donnez un nom à l'activité.");
        if (Activites.Any(a => a.Id != modifiee.Id && Texte.PourRecherche(a.Nom) == Texte.PourRecherche(modifiee.Nom)))
            return Resultat.Echec("Nom", "Une activité porte déjà ce nom.");

        var maintenant = _temps.GetUtcNow();
        var existante = _donnees.Activites.FirstOrDefault(a => a.Id == modifiee.Id);
        if (existante is null)
        {
            modifiee.CreatedAt = maintenant;
            modifiee.UpdatedAt = maintenant;
            _donnees.Activites.Add(modifiee);
        }
        else
        {
            existante.Nom = modifiee.Nom;
            existante.Description = modifiee.Description;
            existante.Couleur = modifiee.Couleur;
            existante.Archivee = modifiee.Archivee;
            existante.MarquerModifie(maintenant);
        }
        Enregistrer();
        return Resultat.Ok();
    }

    /// <summary>Inscrit l'adhérent exactement aux activités indiquées.</summary>
    public void DefinirActivites(Guid adherentId, IReadOnlySet<Guid> activites)
    {
        var maintenant = _temps.GetUtcNow();
        var actuels = _donnees.AdherentActivites.Where(l => !l.EstSupprime && l.AdherentId == adherentId).ToList();
        var change = false;
        foreach (var l in actuels.Where(l => !activites.Contains(l.ActiviteId)))
        {
            l.Supprimer(maintenant);
            change = true;
        }
        foreach (var id in activites.Where(id => actuels.All(l => l.ActiviteId != id)))
        {
            _donnees.AdherentActivites.Add(new AdherentActivite { AdherentId = adherentId, ActiviteId = id, CreatedAt = maintenant, UpdatedAt = maintenant });
            change = true;
        }
        if (change) Enregistrer();
    }

    public BilanFinancier BilanDe(string saison)
    {
        decimal encaisse = 0, reste = 0, especes = 0, cheques = 0, virements = 0, autres = 0;
        foreach (var a in Adherents)
        {
            var c = CotisationDe(a.Id, saison);
            if (c?.Statut == StatutCotisation.Payee)
            {
                var m = c.Montant ?? 0;
                encaisse += m;
                switch (c.ModePaiement)
                {
                    case ModePaiement.Especes: especes += m; break;
                    case ModePaiement.Cheque: cheques += m; break;
                    case ModePaiement.Virement: virements += m; break;
                    default: autres += m; break;
                }
            }
            else if (c?.Statut != StatutCotisation.Exoneree)
            {
                reste += c?.Montant ?? _parametres.Cotisations.TarifPour(a.TypeAdherent) ?? 0;
            }
        }
        return new BilanFinancier(encaisse, reste, especes, cheques, virements, autres);
    }

    public Statistiques StatistiquesDe(string saison)
    {
        var liste = Adherents;
        var payees = liste.Count(a => StatutDe(a.Id, saison) is StatutCotisation.Payee or StatutCotisation.Exoneree);
        return new Statistiques(
            liste.Count,
            liste.Count(a => a.TypeAdherent == TypeAdherent.Adulte),
            liste.Count(a => a.TypeAdherent == TypeAdherent.Enfant),
            payees,
            liste.Count - payees);
    }

    public IReadOnlyList<Adherent> DoublonsPossibles(Adherent candidat) =>
        Adherents.Where(a => RegleAdherent.SontProbablementIdentiques(candidat, a)).ToList();

    // ---------- Écriture ----------

    public Resultat EnregistrerAdherent(Adherent modifie)
    {
        RegleAdherent.Normaliser(modifie);
        var erreurs = RegleAdherent.Valider(modifie, TrouverAdherent, AujourdHui);
        if (erreurs.Count > 0) return new Resultat(false, erreurs);

        var maintenant = _temps.GetUtcNow();
        var existant = _donnees.Adherents.FirstOrDefault(a => a.Id == modifie.Id);
        if (existant is null)
        {
            modifie.CreatedAt = maintenant;
            modifie.UpdatedAt = maintenant;
            modifie.Version = 1;
            modifie.AnneeAdhesion ??= AujourdHui.Year;
            _donnees.Adherents.Add(modifie);
            _donnees.Cotisations.Add(new Cotisation
            {
                AdherentId = modifie.Id,
                Saison = SaisonCourante,
                Montant = _parametres.Cotisations.TarifPour(modifie.TypeAdherent),
            });
            _journal.Info($"Adhérent créé {modifie.Id}");
        }
        else
        {
            if (existant.TypeAdherent == TypeAdherent.Adulte && modifie.TypeAdherent == TypeAdherent.Enfant
                && EnfantsDe(existant.Id).Count > 0)
            {
                return Resultat.Echec("TypeAdherent",
                    "Cet adhérent est responsable d'enfants : il doit rester « Adulte ».");
            }
            existant.CopierChampsMetierDepuis(modifie);
            existant.MarquerModifie(maintenant);
            _journal.Info($"Adhérent modifié {existant.Id} (v{existant.Version})");
        }

        Enregistrer();
        return Resultat.Ok();
    }

    public Resultat SupprimerAdherent(Guid id)
    {
        var a = TrouverAdherent(id);
        if (a is null) return Resultat.Echec("", "Cet adhérent n'existe plus.");

        var enfants = EnfantsDe(id);
        if (enfants.Count > 0)
        {
            var noms = string.Join(", ", enfants.Select(e => e.NomAffiche));
            return Resultat.Echec("", $"{a.NomAffiche} est responsable de : {noms}. " +
                                      "Supprimez ces enfants ou changez leur responsable d'abord.");
        }

        var maintenant = _temps.GetUtcNow();
        a.Supprimer(maintenant);
        foreach (var c in _donnees.Cotisations.Where(c => c.AdherentId == id && !c.EstSupprime))
            c.Supprimer(maintenant);
        foreach (var l in _donnees.AdherentActivites.Where(l => l.AdherentId == id && !l.EstSupprime))
            l.Supprimer(maintenant);

        _journal.Info($"Adhérent supprimé {id}");
        Enregistrer();
        return Resultat.Ok();
    }

    public void MarquerCotisationPayee(Guid adherentId, string saison, decimal? montant, ModePaiement mode, DateOnly date)
    {
        var maintenant = _temps.GetUtcNow();
        var c = CotisationDe(adherentId, saison);
        if (c is null)
        {
            c = new Cotisation { AdherentId = adherentId, Saison = saison, CreatedAt = maintenant };
            _donnees.Cotisations.Add(c);
        }
        c.MarquerPayee(montant, mode, date, maintenant);
        _journal.Info($"Cotisation payée {c.Id} saison {saison}");
        Enregistrer();
    }

    public void AnnulerPaiement(Guid adherentId, string saison)
    {
        var c = CotisationDe(adherentId, saison);
        if (c is null || c.Statut == StatutCotisation.APayer) return;
        c.AnnulerPaiement(_temps.GetUtcNow());
        _journal.Info($"Paiement annulé {c.Id}");
        Enregistrer();
    }

    /// <summary>
    /// Importe les adhérents analysés. remplacer = vrai : efface toutes les données (démonstration) avant,
    /// en gardant les modèles de message.
    /// </summary>
    public int Importer(ICA.Client.Import.ResultatAnalyse analyse, bool remplacer)
    {
        var maintenant = _temps.GetUtcNow();
        var saison = SaisonCourante;
        if (remplacer)
        {
            // On remplace les adhérents, pas le reste (modèles, messages, dons, entreprises, justificatifs).
            var ancien = _donnees;
            _donnees = new DonneesIca
            {
                ModelesMail = ancien.ModelesMail, EstDemo = false,
                Messages = ancien.EstDemo ? [] : ancien.Messages,
                Entreprises = ancien.Entreprises, Dons = ancien.Dons, Justificatifs = ancien.Justificatifs,
                HistoriqueDons = ancien.HistoriqueDons, Association = ancien.Association,
            };
        }
        _donnees.EstDemo = false;

        foreach (var ligne in analyse.Lignes)
        {
            var a = ligne.Adherent;
            RegleAdherent.Normaliser(a);
            if (ligne.AVerifier)
                a.Notes = (a.Notes is null ? "" : a.Notes + "\n") + "À vérifier après import : " + string.Join(" ; ", ligne.Remarques) + ".";
            a.CreatedAt = maintenant;
            a.UpdatedAt = maintenant;
            _donnees.Adherents.Add(a);

            var c = new Cotisation
            {
                AdherentId = a.Id, Saison = saison, CreatedAt = maintenant, UpdatedAt = maintenant,
                Montant = _parametres.Cotisations.TarifPour(a.TypeAdherent),
            };
            if (ligne.CotisationPayee)
            {
                c.Statut = StatutCotisation.Payee;
                c.ModePaiement = ModePaiement.Autre; // le tableau ne précise ni le mode ni la date
            }
            _donnees.Cotisations.Add(c);
        }

        _journal.Info($"Import : {analyse.Lignes.Count} fiches ({analyse.AVerifier} à vérifier), remplacement={remplacer}");
        Enregistrer();
        return analyse.Lignes.Count;
    }

    // ---------- Synchronisation ----------

    /// <summary>Copie complète et indépendante des données (pour l'envoi vers Google Drive).</summary>
    public DonneesIca Instantane()
    {
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        return System.Text.Json.JsonSerializer.Deserialize<DonneesIca>(System.Text.Json.JsonSerializer.Serialize(_donnees, options), options)!;
    }

    /// <summary>Intègre les données d'un autre appareil ; enregistre si quelque chose a changé ici.</summary>
    public Fusion.Bilan FusionnerAvec(DonneesIca distant)
    {
        var bilan = Fusion.Fusionner(_donnees, distant);
        if (bilan.LocalModifie)
        {
            _journal.Info("Synchro : modifications reçues d'un autre appareil");
            Enregistrer();
        }
        return bilan;
    }

    /// <summary>Remplace tout par les données reçues (premier branchement d'un nouvel ordinateur).</summary>
    public void RemplacerPar(DonneesIca donnees)
    {
        donnees.EstDemo = false;
        _donnees = donnees;
        Enregistrer();
    }

    /// <summary>Relit les données depuis le disque (après une restauration).</summary>
    public void Recharger()
    {
        _donnees = _stockage.Charger() ?? _donnees;
        _journal.Info("Données rechargées");
        Modifie?.Invoke(this, EventArgs.Empty);
    }

    // ---------- Messagerie ----------

    public IReadOnlyList<MessageEnvoye> Messages =>
        _donnees.Messages.Where(m => !m.EstSupprime).OrderByDescending(m => m.DateEnvoi).ToList();

    public void EnregistrerMessage(string objet, string corps, IEnumerable<Adherent> destinataires, int nombreAdresses, string? modele, bool envoiDirect)
    {
        var maintenant = _temps.GetUtcNow();
        var noms = destinataires.Select(a => a.NomAffiche).ToList();
        _donnees.Messages.Add(new MessageEnvoye
        {
            DateEnvoi = maintenant, CreatedAt = maintenant, UpdatedAt = maintenant,
            Objet = objet, Corps = corps, NombreAdresses = nombreAdresses, Modele = modele, EnvoiDirect = envoiDirect,
            Destinataires = noms.Count <= 60 ? string.Join(", ", noms) : string.Join(", ", noms.Take(60)) + $" et {noms.Count - 60} autres",
        });
        Enregistrer();
    }

    public void SupprimerMessage(Guid id)
    {
        if (_donnees.Messages.FirstOrDefault(m => m.Id == id && !m.EstSupprime) is not { } m) return;
        m.Supprimer(_temps.GetUtcNow());
        Enregistrer();
    }

    public void ReinitialiserDemo()
    {
        _donnees = DonneesDemo.Creer(AujourdHui, SaisonCourante, _parametres.Cotisations);
        _journal.Info("Données de démonstration réinitialisées.");
        Enregistrer();
    }

    private void Enregistrer()
    {
        try
        {
            _stockage.Enregistrer(_donnees);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _journal.Erreur("Échec de l'enregistrement local", ex);
            throw;
        }
        Modifie?.Invoke(this, EventArgs.Empty);
    }
}
