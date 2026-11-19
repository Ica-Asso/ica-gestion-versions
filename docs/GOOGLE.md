# Connexion Google — installation technique

L'ICA Gestion utilise le compte Google de l'association pour deux choses :

| Droit demandé | Pourquoi |
|---|---|
| `drive.file` | Lire/écrire **uniquement les fichiers créés par l'application** (le fichier de synchronisation). Elle ne voit rien d'autre du Drive. |
| `gmail.send` | **Envoyer** des messages. L'application ne peut pas lire la boîte mail. |
| `openid email` | Afficher l'adresse du compte connecté. |

## Projet Google Cloud (déjà fait une fois, gratuit)

1. console.cloud.google.com → nouveau projet « L'ICA Gestion ».
2. **API et services → Bibliothèque** : activer *Google Drive API* et *Gmail API*.
3. **Écran de consentement OAuth / Branding** : type *Externe*, nom « L'ICA Gestion », e-mail d'assistance.
   Rester en mode **Test** et ajouter le(s) compte(s) Google de l'association dans **Utilisateurs test**.
   Passer en « Production » (sans validation Google) évite que la connexion expire au bout de 7 jours :
   l'écran « application non validée » s'affiche alors, il suffit de cliquer « Paramètres avancés → Accéder ».
4. **Identifiants → Créer → ID client OAuth → Application de bureau**. Télécharger le JSON.
5. Renommer ce fichier `google-client.json` et le placer dans `src\ICA.Desktop\`.
   Il est copié à côté de `ICA-Gestion.exe` à la compilation, et sur la clé USB par `Preparer la cle USB.bat`.

`google-client.json` est **exclu de Git** (`.gitignore`). Pour une « application de bureau », Google considère ce
secret comme non confidentiel ; il ne donne accès à aucune donnée sans la connexion de la gérante.

## Fonctionnement

- **Connexion** : OAuth 2.0 + PKCE, navigateur par défaut, retour sur `http://127.0.0.1:<port>/`.
  Le jeton de renouvellement est chiffré avec DPAPI dans `%LOCALAPPDATA%\L-ICA\config\google.dat`
  (illisible depuis un autre compte Windows ou un autre PC).
- **Synchronisation** : au démarrage, toutes les 60 s, 4 s après chaque modification, et à la fermeture.
  Télécharger → fusionner (la modification la plus récente gagne, enregistrement par enregistrement) → renvoyer si besoin.
  Un ordinateur encore en démonstration remplace simplement ses données fictives par celles du Drive.
- **Hors connexion** : tout fonctionne, voyant rouge ; l'envoi se fait au retour d'Internet.
- **Sauvegardes** : inchangées (copie quotidienne locale, 30 jours) ; Google Drive garde aussi l'historique des versions du fichier.

## Dépannage

| Symptôme | Cause probable |
|---|---|
| « fichier google-client.json manquant » | Le fichier n'est pas à côté de l'exe |
| Erreur 403 `access_denied` à la connexion | Compte absent des *Utilisateurs test* |
| Voyant rouge « Synchro à réessayer » | Voir `%LOCALAPPDATA%\L-ICA\logs` (aucune donnée personnelle dedans) |
| Déconnexion après 7 jours | Projet resté en mode Test : passer en Production |
