using System.Security.Cryptography;
using ICA.Client.Documents;
using ICA.Client.Google;
using ICA.Client.Journal;
using ICA.Domain;

namespace ICA.Client.Services;

/// <summary>
/// Émission des justificatifs de dons : numéro unique entre ordinateurs, PDF, copie conservée, envoi.
/// </summary>
public sealed class ServiceJustificatifs(
    DepotIca depot,
    string dossier,
    byte[]? logo,
    IJournal journal,
    CompteGoogle? google = null,
    SynchroDrive? synchro = null,
    DocumentsDrive? drive = null,
    EnvoiGmail? gmail = null)
{
    public string Dossier => dossier;
    private bool Connecte => google?.EstConnecte == true && synchro is not null;

    public string Chemin(JustificatifDon j) => Path.Combine(dossier, j.Annee.ToString(System.Globalization.CultureInfo.InvariantCulture), j.NomFichier);

    /// <summary>
    /// Réserve un numéro, vérifie qu'aucun autre ordinateur ne l'a pris, crée le PDF, le conserve et l'enregistre.
    /// </summary>
    public async Task<JustificatifDon> EmettreAsync(TypeJustificatif type, DonateurEntreprise entreprise, IReadOnlyList<Don> dons, JustificatifDon? remplace = null)
    {
        if (dons.Count == 0) throw new InvalidOperationException("Aucun don à justifier.");
        if (dons.Select(d => d.Date.Year).Distinct().Count() > 1)
            throw new InvalidOperationException("Un justificatif ne peut regrouper que des dons de la même année.");
        if (dons.Any(d => d.EntrepriseId != entreprise.Id))
            throw new InvalidOperationException("Tous les dons doivent venir de la même entreprise.");
        if (dons.Any(d => d.Statut != StatutDon.Recu))
            throw new InvalidOperationException("Seuls les dons « reçus et vérifiés » peuvent être justifiés.");
        if (type == TypeJustificatif.RecuFiscal)
        {
            if (depot.ManquesRecuFiscal is { Count: > 0 } manquesAsso)
                throw new InvalidOperationException("Reçu fiscal bloqué. Il manque (Paramètres > Association et reçus fiscaux) : " + string.Join(", ", manquesAsso) + ".");
            if (ConditionsRecuFiscal.Entreprise(entreprise) is { Count: > 0 } manquesEntreprise)
                throw new InvalidOperationException("Reçu fiscal bloqué. Complétez la fiche de l'entreprise : " + string.Join(", ", manquesEntreprise) + ".");
        }
        var asso = depot.Association.Identite;

        var annee = dons[0].Date.Year;
        var donIds = dons.Select(d => d.Id).ToList();
        JustificatifDon? reserve = null;

        for (var essai = 0; essai < 5 && reserve is null; essai++)
        {
            if (Connecte)
            {
                await synchro!.SynchroniserAsync(attendre: true);
                if (synchro.Etat != EtatSynchro.AJour)
                    throw new InvalidOperationException("Connexion à Google Drive nécessaire pour attribuer un numéro unique entre vos ordinateurs. Vérifiez Internet et réessayez.");
            }
            var j = depot.ReserverJustificatif(type, entreprise.Id, donIds, annee, remplace?.Id);
            if (!Connecte) { reserve = j; break; }

            // Le numéro est publié tout de suite, puis on relit : si un autre ordinateur l'a pris au même moment,
            // la fusion annule l'une des deux réservations (la même partout) et on recommence.
            await synchro!.SynchroniserAsync(attendre: true);
            await synchro.SynchroniserAsync(attendre: true);
            if (synchro.Etat != EtatSynchro.AJour)
            {
                depot.AbandonnerReservation(j.Id, "Connexion perdue pendant la réservation du numéro.");
                throw new InvalidOperationException("La connexion a été perdue : aucun document n'a été créé. Réessayez.");
            }
            if (depot.TrouverJustificatif(j.Id) is { Statut: StatutJustificatif.Reserve } ok) reserve = ok;
            else journal.Info($"Numéro {j.Numero} pris par un autre ordinateur : nouvel essai");
        }
        if (reserve is null) throw new InvalidOperationException("Impossible d'obtenir un numéro unique pour le moment. Réessayez dans un instant.");

        try
        {
            var contenu = ModelesJustificatifs.Construire(asso, entreprise, dons, remplace?.Numero);
            reserve.Contenu = contenu;
            reserve.DateEmission = depot.AujourdHui;
            var pdf = ModelesJustificatifs.Generer(reserve, logo, depot.SignatureJpeg);
            var chemin = Chemin(reserve);
            Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
            if (File.Exists(chemin)) throw new IOException($"Un fichier {reserve.NomFichier} existe déjà : il n'est jamais écrasé.");
            await File.WriteAllBytesAsync(chemin, pdf);
            depot.EmettreJustificatif(reserve.Id, contenu, Empreinte(pdf));
            journal.Info($"Justificatif {reserve.Numero} émis");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            depot.AbandonnerReservation(reserve.Id, "Erreur pendant la création du PDF.");
            journal.Erreur("Création du justificatif impossible", ex);
            throw new InvalidOperationException("Le document n'a pas pu être créé : " + ex.Message, ex);
        }

        var emis = depot.TrouverJustificatif(reserve.Id)!;
        await CopierSurDriveAsync(emis);
        if (Connecte) _ = synchro!.SynchroniserAsync();
        return emis;
    }

    /// <summary>Copie conservée dans le Drive de l'association (réessayée plus tard en cas d'échec).</summary>
    public async Task CopierSurDriveAsync(JustificatifDon j)
    {
        if (drive?.Disponible != true || j.DriveFichierId is not null || !File.Exists(Chemin(j))) return;
        try
        {
            var id = await drive.DeposerAsync(j.NomFichier, await File.ReadAllBytesAsync(Chemin(j)));
            depot.DefinirFichierDrive(j.Id, id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or GoogleDeconnecteException or IOException)
        {
            journal.Avertissement($"Copie Drive de {j.Numero} reportée : {ex.GetType().Name}");
        }
    }

    /// <summary>Dépose sur le Drive les justificatifs émis ici qui n'y sont pas encore.</summary>
    public async Task CompleterCopiesDriveAsync()
    {
        foreach (var j in depot.Justificatifs.Where(j => j.DriveFichierId is null && File.Exists(Chemin(j))))
            await CopierSurDriveAsync(j);
    }

    /// <summary>
    /// Le PDF tel qu'il a été remis : copie locale, sinon copie du Drive (autre ordinateur),
    /// sinon reconstitution à l'identique à partir du contenu figé.
    /// </summary>
    public async Task<string> FichierAsync(JustificatifDon j)
    {
        var chemin = Chemin(j);
        if (File.Exists(chemin)) return chemin;
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);

        if (j.DriveFichierId is { } id && drive?.Disponible == true)
        {
            try
            {
                var octets = await drive.TelechargerAsync(id);
                await File.WriteAllBytesAsync(chemin, octets);
                return chemin;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or GoogleDeconnecteException)
            {
                journal.Avertissement($"Téléchargement de {j.Numero} impossible : {ex.GetType().Name}");
            }
        }

        if (j.Contenu is null) throw new InvalidOperationException("Ce document n'a pas de contenu enregistré.");
        var pdf = ModelesJustificatifs.Generer(j, logo, depot.SignatureJpeg);
        await File.WriteAllBytesAsync(chemin, pdf);
        journal.Info($"Copie de {j.Numero} reconstituée à partir du contenu enregistré");
        return chemin;
    }

    public bool PeutEnvoyerDirectement => gmail?.Disponible == true;
    public string? Expediteur => gmail?.Expediteur;

    /// <summary>Envoi par Gmail, PDF joint. L'appelant a déjà demandé la confirmation.</summary>
    public async Task EnvoyerAsync(JustificatifDon j, string destinataire, string objet, string corps)
    {
        if (gmail is null) throw new InvalidOperationException("Gmail n'est pas relié.");
        VerifierEnvoiPossible(j);
        var chemin = await FichierAsync(j);
        var autres = j.Type == TypeJustificatif.RecuFiscal ? new[] { LettreAccompagnement(j) } : null;
        await gmail.EnvoyerAsync([destinataire], objet, corps, new PieceJointe(j.NomFichier, "application/pdf", await File.ReadAllBytesAsync(chemin)),
            autresPieces: autres);
        depot.MarquerJustificatifEnvoye(j.Id, destinataire);
        journal.Info($"Justificatif {j.Numero} envoyé par Gmail");
    }

    /// <summary>Un reçu fiscal ne part plus si les conditions ne sont plus remplies (éligibilité retirée, etc.).</summary>
    public void VerifierEnvoiPossible(JustificatifDon j)
    {
        if (!j.EstActif) throw new InvalidOperationException("Ce document est annulé ou remplacé : il ne peut plus être envoyé.");
        if (j.Type == TypeJustificatif.RecuFiscal && depot.Association.Eligibilite != EligibiliteFiscale.Confirmee)
            throw new InvalidOperationException("Envoi bloqué : l'éligibilité de L'ICA au mécénat n'est plus confirmée. Aucun reçu fiscal ne doit être remis.");
    }

    /// <summary>Lettre de remerciement sur papier à en-tête, jointe au reçu fiscal.</summary>
    public PieceJointe LettreAccompagnement(JustificatifDon j) =>
        new($"Lettre de remerciement {j.Numero}.pdf", "application/pdf", ModelesJustificatifs.LettreAccompagnement(j, logo, depot.SignatureJpeg));

    /// <summary>Spécimen filigrané avec les informations actuelles de l'association (fichier temporaire, non enregistré).</summary>
    public string Specimen(ParametresAssociation? brouillon = null)
    {
        var p = brouillon ?? depot.Association;
        byte[]? signature = null;
        try { signature = p.SignatureJpeg is { Length: > 0 } s ? Convert.FromBase64String(s) : null; } catch (FormatException) { }
        var pdf = ModelesJustificatifs.Specimen(p.Identite, signature, depot.AujourdHui);
        var chemin = Path.Combine(Path.GetTempPath(), $"SPECIMEN recu fiscal {CategoriesFiscales.ModeleEnVigueur.Replace('*', '-')}.pdf");
        File.WriteAllBytes(chemin, pdf);
        return chemin;
    }

    public static string Empreinte(byte[] octets) => Convert.ToHexString(SHA256.HashData(octets)).ToLowerInvariant();

    public const string ObjetMail = "Justificatif de don — Association L'ICA";

    public const string CorpsMail =
        "Bonjour,\n\n" +
        "Nous vous remercions pour votre soutien à l'association L'ICA.\n\n" +
        "Vous trouverez en pièce jointe le justificatif correspondant à votre don.\n\n" +
        "Grâce à votre contribution, nous pouvons poursuivre nos actions et activités associatives.\n\n" +
        "Bien cordialement,\n\n" +
        "L'équipe de L'ICA\n" +
        "Partageons nos savoirs-faire";
}
