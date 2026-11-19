# Publier une mise à jour

Les versions sont publiées dans les **Releases** du dépôt public `Ica-Asso/ica-gestion-versions`
(il ne contient que les programmes, jamais le code ni des données). Le dépôt se règle dans `appsettings.json` → `MisesAJour.Depot`.

## Une fois

Sur GitHub, organisation **Ica-Asso** → *New repository* → nom `ica-gestion-versions`, **Public**, avec un README.

## À chaque nouvelle version

1. Double-cliquez **`Publier une mise a jour.bat`**, tapez le nouveau numéro (ex. `0.2.0`, toujours plus grand) et une phrase de nouveautés.
2. Le script écrit le numéro dans `VERSION`, construit le programme et prépare `Pour la cle USB`.
3. Si l'outil GitHub `gh` est installé et connecté, la publication est automatique. Sinon, la page GitHub s'ouvre :
   glissez `ICA-Gestion.exe` (dossier ouvert automatiquement), collez la phrase, **Publish release**.

Les ordinateurs vérifient 20 secondes après le démarrage puis toutes les 6 heures, et proposent « Mettre à jour ».

## Fonctionnement

- Seul `ICA-Gestion.exe` est remplacé : données (`%LOCALAPPDATA%\L-ICA`), `appsettings.json`, `google-client.json`
  et connexion Google restent en place. `google-client.json` n'est donc **jamais** publié.
- Vérifications : téléchargement HTTPS depuis github.com, empreinte SHA-256 fournie par GitHub, en-tête d'un programme Windows.
- Installation : l'exe en cours est renommé `.ancien` (effacé au démarrage suivant), le nouveau prend sa place,
  les dernières modifications sont synchronisées, puis l'application redémarre. En cas d'échec, l'ancienne version est remise.
- La version lancée par `Lancer la demo.bat` (Debug) ne se met jamais à jour : seule la version installée le fait.
- Si une version change le format des données, mettre à jour **tous** les ordinateurs.
