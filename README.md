# L'ICA Gestion

Application de gestion de l'association **L'ICA** — *Partageons nos savoirs-faire*.

- Cahier des charges : [`PROJECT.md`](PROJECT.md)
- Architecture et feuille de route : [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- Guide de la démonstration : [`docs/USER_GUIDE_WINDOWS.md`](docs/USER_GUIDE_WINDOWS.md)

## Version actuelle : 0.1.0 — démonstration Windows

Application Windows complète **avec des données fictives enregistrées sur l'ordinateur**,
pour valider les écrans avec la gérante avant de construire le serveur et la synchronisation.

| Fonctionne | Arrive ensuite |
|---|---|
| Accueil (chiffres clés, cotisations à régler, anniversaires) | Compte L'ICA, serveur, synchronisation multi-PC (v0.2 → v0.5) |
| Adhérents : recherche instantanée, filtres, tri, fiche, ajout / modification / suppression | Doublons : fusion de deux fiches |
| Enfants rattachés à un adulte responsable | Envoi direct par Gmail et suivi (v0.8) |
| Détection des doublons à la création | Listes de présence, vos propres modèles de message |
| Cotisations : bilan, encaissement (espèces / chèque / virement), annulation, rappel par mail | Installateur et mises à jour automatiques (v0.10) |
| Sélection multiple : mail en copie cachée, export Excel (CSV), impression | |
| « Un problème ? » : rapport d'assistance + mail pré-rempli | |
| Messages pré-remplis : 7 modèles (AG, rappel de cotisation, annulation…) | |
| Calendrier des activités : séances hebdomadaires, événements, annulation + message aux participants | |
| Import Excel / CSV avec aperçu et lignes à vérifier (Paramètres) | |
| Sauvegarde / restauration + copie de sécurité quotidienne automatique | |
| Version autonome pour clé USB : `Preparer la cle USB.bat` | |

## Prérequis

- Windows 10 ou 11
- [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 (charge de travail « Développement .NET Desktop ») — ou VS Code / Rider

## Lancer l'application

```powershell
dotnet run --project src/ICA.Desktop
```

Ou dans Visual Studio : ouvrir `ICA-Gestion.sln`, projet de démarrage `ICA.Desktop`, puis F5.

## Installer chez la gérante (clé USB)

1. Double-cliquer sur `Preparer la cle USB.bat` : crée le dossier `Pour la cle USB` (application autonome, sans .NET à installer).
2. Copier ce dossier sur la clé, avec une sauvegarde (`Paramètres → Sauvegarder mes données`).
3. Sur son PC : `Installer L'ICA Gestion.bat`, puis `Paramètres → Restaurer une sauvegarde`.

⚠ Tant que le serveur n'existe pas, les données sont **sur un seul ordinateur** : pas de synchronisation.

## Tests

```powershell
dotnet test
```

## Organisation

```text
src/
  ICA.Contracts/   Énumérations et (plus tard) formats échangés avec le serveur
  ICA.Domain/      Règles métier pures : adhérent, cotisation, validation, recherche, téléphone…
  ICA.Client/      « Cerveau » partagé Windows + mobile : stockage, ViewModels, export, assistance
  ICA.Desktop/     Application WPF : fenêtres, vues, thème L'ICA
tests/
  ICA.Domain.Tests/
```

Règle d'or : **aucune logique métier dans `ICA.Desktop`**. Tout ce qui pourra servir au mobile va dans
`ICA.Domain` ou `ICA.Client` (qui ne référencent ni WPF ni MAUI).

## Données de la démonstration

- Fichier : `%LOCALAPPDATA%\L-ICA\cache\donnees.json` (copie `.bak` à chaque enregistrement, copie quotidienne dans `backups\`, 30 jours)
- Journal technique : `%LOCALAPPDATA%\L-ICA\logs\` (14 jours, sans données personnelles)
- Pour repartir de zéro : *Paramètres → Réinitialiser la démonstration*

## Réglages

`src/ICA.Desktop/appsettings.json` :

| Clé | Rôle |
|---|---|
| `Support:Email` | Destinataire des rapports « Un problème ? » |
| `Cotisations:ModeSaison` | `Civile` (« 2026 ») ou `Scolaire` (« 2026-2027 ») |
| `Cotisations:TarifAdulte` / `TarifEnfant` | Cotisation annuelle (25 € / 15 €), proposée à l'encaissement |

## Sécurité des données

Le `.gitignore` exclut tous les fichiers Excel, CSV, bases et sauvegardes.
**Ne jamais** ajouter de vraies données d'adhérents au dépôt, même temporairement :
un fichier poussé sur GitHub reste dans l'historique.


- Connexion Google et synchronisation : [docs/GOOGLE.md](docs/GOOGLE.md)
- Publier une mise à jour : [docs/MISES_A_JOUR.md](docs/MISES_A_JOUR.md)
- Module Dons & Mécénat : [docs/DONS.md](docs/DONS.md)
