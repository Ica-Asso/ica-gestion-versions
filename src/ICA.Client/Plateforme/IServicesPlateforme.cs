using ICA.Client.Services;
using ICA.Client.ViewModels;

namespace ICA.Client.Plateforme;

public sealed record LigneImpression(string Nom, string Type, string Adresse, string Telephone, string Email, string Cotisation);

/// <summary>
/// Ce que chaque application (Windows, mobile) fournit à la partie partagée :
/// fenêtres, fichiers, impression, messagerie. Implémenté dans ICA.Desktop (puis ICA.Mobile).
/// </summary>
public interface IServicesPlateforme
{
    string VersionApplication { get; }
    string SystemeExploitation { get; }
    string NomAppareil { get; }

    bool Confirmer(string titre, string message, string boutonOui, bool danger = false);
    void Informer(string titre, string message);

    /// <summary>Ouvre la fenêtre d'édition ; vrai si l'adhérent a été enregistré.</summary>
    bool EditerAdherent(EditionAdherentViewModel vm);

    void OuvrirAssistance(bool apresPlantage = false);

    /// <summary>Ouvre la fenêtre « Nouveau message » (choix du modèle, relecture).</summary>
    void ComposerMessage(ComposerMessageViewModel vm);

    void CopierTexte(string texte);

    /// <summary>Fenêtre « Planifier une activité » ; vrai si enregistré.</summary>
    bool PlanifierSeance(PlanificationViewModel vm);

    /// <summary>Fenêtre « Nouvelle activité » / « Modifier l'activité » ; vrai si enregistré.</summary>
    bool EditerActivite(EditionActiviteViewModel vm);

    /// <summary>Crée le rapport (fichier + mail pré-rempli). Renvoie le texte à afficher à l'utilisatrice.</summary>
    Task<string> EnvoyerAssistanceAsync(RapportAssistance rapport, bool joindreCapture);

    /// <summary>Demande où enregistrer un export ; null si annulé.</summary>
    string? ChoisirFichierExport(string nomParDefaut);

    /// <summary>Fenêtre « Ouvrir » ; filtre au format « Libellé|*.xlsx;*.csv ». Null si annulé.</summary>
    string? ChoisirFichierAOuvrir(string titre, string filtre);

    /// <summary>Fenêtre « Enregistrer sous ». Null si annulé.</summary>
    string? ChoisirFichierAEnregistrer(string titre, string nomParDefaut, string filtre);

    /// <summary>Fenêtre d'aperçu de l'import ; vrai si l'import a été fait.</summary>
    bool OuvrirImport(ImportViewModel vm);

    void Imprimer(string titre, IReadOnlyList<LigneImpression> lignes);

    /// <summary>Ouvre une fenêtre de saisie (entreprise, don, justificatif…). Vrai si validée.</summary>
    bool OuvrirFormulaire(ViewModels.FormulaireViewModel vm);

    /// <summary>Convertit une image (JPEG, PNG, BMP) en JPEG sur fond blanc ; null si illisible.</summary>
    byte[]? ImageEnJpeg(string chemin);

    /// <summary>Imprime un fichier PDF (ou l'ouvre si l'impression directe n'est pas possible).</summary>
    void ImprimerFichier(string chemin);

    /// <summary>Remplace le programme par la version téléchargée puis redémarre l'application.</summary>
    void InstallerMiseAJour(string nouveauProgramme);

    /// <summary>Ouvre un lien (mailto:, https:, dossier). Faux en cas d'échec.</summary>
    bool OuvrirLien(string lien);
}
