# PROJET : L’ICA Gestion

Tu es mon développeur principal pour créer une application complète destinée à une association française appelée **L’ICA**.

L’objectif est de créer une solution extrêmement simple à utiliser pour la gérante de l’association, qui n’est pas forcément à l’aise avec l’informatique.

L’application doit remplacer progressivement son fonctionnement actuel basé principalement sur un tableau Excel contenant les adhérents.

Le principe fondamental du projet est :

> Une interface extrêmement simple en façade, avec une architecture propre, robuste, synchronisée et pensée dès le départ pour plusieurs appareils.

Je souhaite pouvoir maintenir ensuite l’application moi-même avec Claude, publier de nouvelles versions à distance et faire évoluer progressivement les fonctionnalités.

IMPORTANT :

La responsable utilise **plusieurs ordinateurs**.

À terme, elle pourra également utiliser **une application mobile Android/iOS**.

Les données doivent donc rester synchronisées entre tous ses appareils.

Une modification effectuée depuis un PC ou depuis le mobile doit pouvoir apparaître automatiquement sur les autres appareils connectés.

---

# 1. NOM DU PROJET

Nom officiel :

**L’ICA Gestion**

Association :

**L’ICA**

Slogan :

**Partageons nos savoirs-faire**

Le logo officiel de L’ICA sera fourni dans le projet.

L’identité graphique de l’application devra s’inspirer du logo sans transformer l’interface en quelque chose de trop coloré ou chargé.

Le résultat doit être :

- moderne ;
- chaleureux ;
- lisible ;
- accessible ;
- professionnel ;
- extrêmement simple à comprendre.

---

# 2. UTILISATRICE CIBLE

L’application est principalement destinée à la responsable de l’association.

Elle doit pouvoir utiliser le logiciel sans connaissance informatique particulière.

Éviter absolument :

- les interfaces techniques ;
- les tableaux complexes façon Excel ;
- les termes informatiques ;
- les manipulations compliquées ;
- les dizaines de menus ;
- les réglages inutiles.

Privilégier :

- gros boutons ;
- textes explicites ;
- confirmations simples ;
- recherche instantanée ;
- filtres visuels ;
- automatisation maximale.

L’application doit presque pouvoir être utilisée sans formation.

---

# 3. APPAREILS CIBLES

Le projet doit être conçu dès le départ pour fonctionner avec plusieurs types d’appareils.

## Phase principale

Application Windows.

Technologies envisagées :

- C#
- .NET
- WPF

## Phase mobile future

Application :

- Android
- iOS

Technologie préférée :

- **.NET MAUI**

Le choix de .NET MAUI permet de conserver un maximum de cohérence avec l’écosystème C#/.NET du projet.

IMPORTANT :

Même si l’application mobile n’est pas nécessairement développée dans la première version, l’architecture du projet doit être conçue dès maintenant pour l’accueillir sans réécriture majeure.

---

# 4. ARCHITECTURE GLOBALE MULTI-APPAREILS

Architecture cible :

```text
                       ┌─────────────────────┐
                       │   SERVEUR L’ICA     │
                       │                     │
                       │ API sécurisée       │
                       │ Base PostgreSQL     │
                       │ Authentification    │
                       │ Synchronisation     │
                       └──────────┬──────────┘
                                  │
                ┌─────────────────┼─────────────────┐
                │                 │                 │
                ▼                 ▼                 ▼
          PC Bureau         PC Portable        Smartphone
           Windows            Windows          Android/iOS
             WPF                WPF              .NET MAUI
```

Tous les clients doivent utiliser le même backend.

Ne jamais créer :

- une base séparée pour Windows ;
- une base séparée pour mobile ;
- des systèmes métier complètement indépendants.

Le backend doit devenir la source centrale de vérité.

---

# 5. UTILISATION SUR PLUSIEURS ORDINATEURS

La responsable possède plusieurs ordinateurs.

L’application doit pouvoir être installée sur plusieurs PC avec le même compte L’ICA.

Exemple :

```text
PC Bureau
     │
     │
     ▼
Serveur L’ICA
     ▲
     │
     │
PC Portable
```

Si elle ajoute un adhérent sur le PC Bureau :

```text
PC Bureau

+ Jean DUPONT
```

le nouvel adhérent doit automatiquement être disponible sur :

```text
PC Portable
```

et inversement.

Même comportement pour :

- modification d’un adhérent ;
- suppression ;
- cotisation ;
- activité ;
- modèle de mail ;
- informations générales ;
- paramètres synchronisables.

L’utilisateur ne doit jamais avoir à :

- exporter ;
- copier une clé USB ;
- importer ;
- synchroniser manuellement.

La synchronisation doit être automatique.

---

# 6. APPLICATION MOBILE FUTURE

Je souhaite pouvoir développer une application mobile officielle :

**L’ICA Gestion Mobile**

Technologie privilégiée :

**.NET MAUI**

Cibles :

- Android
- iOS

L’application mobile doit utiliser :

- le même backend ;
- la même authentification ;
- la même base centrale ;
- les mêmes modèles métier ;
- les mêmes règles de validation ;
- les mêmes données.

---

# 7. OBJECTIF DE L’APPLICATION MOBILE

L’application mobile ne doit pas nécessairement reproduire toute l’application Windows.

Le PC reste l’outil principal pour les tâches administratives importantes.

Le mobile doit surtout permettre les actions rapides.

Fonctions prioritaires mobile :

- consulter les adhérents ;
- rechercher un adhérent ;
- afficher une fiche ;
- modifier une fiche ;
- ajouter un adhérent ;
- consulter les cotisations ;
- marquer une cotisation comme payée ;
- consulter les activités ;
- consulter les coordonnées ;
- appeler directement un adhérent ;
- envoyer rapidement un email ;
- voir les anniversaires ;
- consulter les alertes ;
- voir l’état de synchronisation.

---

# 8. FONCTIONS À NE PAS PRIORISER SUR MOBILE

Ne pas nécessairement intégrer immédiatement sur mobile :

- import massif Excel ;
- restauration complète de base ;
- administration serveur ;
- exports complexes ;
- configuration technique ;
- gestion avancée des sauvegardes ;
- création d’installateurs ;
- fonctions de diagnostic avancé.

Ces fonctions restent prioritairement disponibles sur Windows.

---

# 9. PARTAGE DU CODE

Je souhaite éviter de dupliquer inutilement le code entre Windows et mobile.

Prévoir des projets partagés.

Exemple :

```text
ICA.Core
ICA.Models
ICA.Api.Client
ICA.Validation
```

Ils doivent pouvoir être utilisés par :

```text
ICA.Desktop
ICA.Mobile
```

Ne pas mettre de code spécifique WPF dans le Core.

Ne pas mettre de code spécifique MAUI dans le Core.

Les règles métier doivent être indépendantes de l’interface.

---

# 10. ARCHITECTURE DU REPOSITORY

Architecture possible :

```text
ICA-Gestion/
│
├── src/
│   │
│   ├── shared/
│   │   ├── ICA.Core/
│   │   ├── ICA.Models/
│   │   ├── ICA.Api.Client/
│   │   └── ICA.Validation/
│   │
│   ├── desktop/
│   │   ├── ICA.Desktop/
│   │   ├── ICA.Desktop.Data/
│   │   ├── ICA.Desktop.Sync/
│   │   ├── ICA.Gmail/
│   │   └── ICA.Update/
│   │
│   ├── mobile/
│   │   └── ICA.Mobile/
│   │
│   └── server/
│       ├── ICA.Api/
│       ├── ICA.Api.Core/
│       ├── ICA.Api.Data/
│       └── ICA.Api.Auth/
│
├── tests/
│
├── assets/
│
├── installer/
│
├── docs/
│
├── .github/
│   └── workflows/
│
├── README.md
├── PROJECT.md
└── VERSION
```

Tu peux améliorer cette architecture si nécessaire.

Évite cependant la surarchitecture.

---

# 11. BASE DE DONNÉES CENTRALE

La source principale des données doit être une vraie base serveur.

Étudier une solution moderne.

Exemples :

- PostgreSQL ;
- Supabase ;
- backend ASP.NET Core + PostgreSQL ;
- autre solution sécurisée et adaptée.

Tu dois choisir la solution la plus adaptée en tenant compte de :

- simplicité ;
- coût ;
- sécurité ;
- maintenance ;
- RGPD ;
- disponibilité ;
- sauvegardes ;
- utilisation avec C#/.NET ;
- compatibilité avec Windows ;
- compatibilité avec Android/iOS.

La base centrale ne doit jamais être directement accessible avec des identifiants administrateur contenus dans les applications.

---

# 12. CACHE LOCAL

Chaque appareil peut avoir un cache local.

Windows :

```text
SQLite
```

Mobile :

```text
SQLite ou solution locale adaptée à .NET MAUI
```

Le cache sert à :

- accélérer l’application ;
- consulter les données hors connexion ;
- stocker temporairement des modifications ;
- synchroniser plus tard.

La base locale n’est pas la source de vérité principale.

---

# 13. MODE HORS LIGNE

Si Internet est temporairement indisponible, l’application ne doit pas devenir inutilisable.

Cela concerne :

- PC ;
- smartphone ;
- tablette.

Afficher par exemple :

```text
⚠ Connexion Internet indisponible

Vous pouvez continuer à utiliser L’ICA Gestion.

Les modifications seront synchronisées automatiquement dès que la connexion reviendra.
```

Lorsque Internet revient :

```text
Synchronisation...

✓ Données synchronisées
```

L’utilisateur ne doit rien faire.

---

# 14. SYNCHRONISATION

Créer un vrai système de synchronisation.

Chaque enregistrement doit idéalement contenir :

```text
ID
CreatedAt
UpdatedAt
Version
DeletedAt
```

La synchronisation doit détecter :

- nouvelles données ;
- modifications ;
- suppressions ;
- changements provenant d’un autre appareil.

Ne pas télécharger toute la base en permanence.

Utiliser une synchronisation incrémentale.

---

# 15. SYNCHRONISATION MOBILE

Même comportement sur mobile.

Exemple :

```text
Téléphone

Cotisation de Jean DUPONT
→ marquée comme payée
```

Puis :

```text
Serveur
→ modification enregistrée
```

Puis :

```text
PC Bureau
→ synchronisation
→ cotisation affichée comme payée
```

Et inversement.

---

# 16. CONFLITS DE SYNCHRONISATION

Prévoir le cas suivant :

PC Bureau modifie :

```text
Téléphone
```

pendant que le smartphone modifie :

```text
Adresse
```

Le système doit autant que possible préserver les deux modifications.

Si deux appareils modifient exactement la même donnée simultanément, créer une stratégie de résolution fiable.

Éviter les messages techniques.

Afficher seulement si nécessaire :

```text
Cette fiche a été modifiée depuis un autre appareil.

[ Utiliser la version la plus récente ]

[ Vérifier les différences ]
```

Cela doit rester exceptionnel.

---

# 17. ÉTAT DE SYNCHRONISATION

Afficher discrètement :

```text
🟢 Synchronisé
```

ou :

```text
🟡 Synchronisation...
```

ou :

```text
🔴 Hors connexion
```

Même principe sur mobile.

---

# 18. COMPTE L’ICA

Prévoir un système permettant d’identifier l’association.

Exemple :

```text
L’ICA Gestion

Compte :
L’ICA

🟢 Connecté
```

Lorsqu’on installe l’application sur un nouveau PC ou téléphone :

```text
Bienvenue dans L’ICA Gestion

[ Connecter cet appareil ]
```

Après authentification :

```text
Récupération des données de L’ICA...

✓ Adhérents
✓ Cotisations
✓ Activités
✓ Paramètres

Synchronisation terminée.
```

---

# 19. AUTHENTIFICATION

Ne pas créer un système artisanal.

Utiliser une solution reconnue.

Possibilités :

- OAuth ;
- Supabase Auth ;
- OpenID Connect ;
- solution gérée adaptée à ASP.NET Core.

Les mots de passe ne doivent jamais être stockés en clair.

Les sessions doivent être sécurisées.

---

# 20. GESTION DES APPAREILS

Prévoir à terme :

```text
Appareils connectés

PC Bureau
Windows
Dernière connexion : aujourd’hui

PC Portable
Windows
Dernière connexion : aujourd’hui

Téléphone
Android
Dernière connexion : aujourd’hui

Ancien PC
Dernière connexion : 12/06/2026

[ Déconnecter cet appareil ]
```

Cette fonction peut arriver après la première version, mais l’architecture doit la permettre.

---

# 21. TECHNOLOGIES SOUHAITÉES

Écosystème privilégié :

## Backend

- C#
- .NET
- ASP.NET Core
- PostgreSQL

## Desktop

- C#
- .NET
- WPF

## Mobile

- C#
- .NET
- .NET MAUI

## Cache local

- SQLite

Cette cohérence technologique est importante.

---

# 22. DONNÉES ADHÉRENTS

Créer une entité :

```text
Adherent
```

Avec :

```text
ID
Nom
Prénom
Adresse
ComplémentAdresse
CodePostal
Ville
Email
Telephone1
Telephone2
DateNaissance
TypeAdherent
AnneeAdhesion
Notes
CreatedAt
UpdatedAt
Version
DeletedAt
```

`TypeAdherent` pourra notamment contenir :

- Adulte
- Enfant

---

# 23. IMPORT DU TABLEAU EXISTANT

La responsable possède actuellement un tableau contenant les adhérents.

Importer :

- `.xlsx`
- `.csv`

Reconnaître :

```text
Nom
Prénom
Adresse
Email
Téléphone
Téléphone secondaire
Date de naissance
Cotisation
```

Avant import :

```text
47 adhérents détectés

44 lignes valides

3 lignes nécessitent une vérification

[ Vérifier ]

[ Importer ]
```

Prévoir une détection des doublons.

Une fois importées, les données doivent être envoyées vers la base centrale puis disponibles sur tous les appareils.

---

# 24. ÉCRAN D’ACCUEIL WINDOWS

Créer un Dashboard simple.

```text
------------------------------------------------
                    L'ICA
          Partageons nos savoirs-faire
------------------------------------------------

47
Adhérents

40
Adultes

7
Enfants

43
Cotisations payées

4
Cotisations à vérifier

------------------------------------------------

[ ADHÉRENTS ]

[ MESSAGERIE ]

[ COTISATIONS ]

[ ACTIVITÉS ]

------------------------------------------------

🟢 Synchronisé
```

---

# 25. ÉCRAN D’ACCUEIL MOBILE

Créer plus tard une interface adaptée au téléphone.

Exemple :

```text
L'ICA Gestion

47 adhérents
4 cotisations à vérifier

[ Rechercher un adhérent ]

[ Adhérents ]

[ Cotisations ]

[ Activités ]

[ Messagerie ]

🟢 Synchronisé
```

Ne pas simplement reproduire l’interface PC en plus petit.

Créer une vraie ergonomie mobile.

---

# 26. GESTION DES ADHÉRENTS

Créer une page dédiée.

```text
🔎 Rechercher un adhérent...

Filtres :

[Tous]
[Adultes]
[Enfants]
[Cotisation payée]
[À régler]
```

Permettre le tri par :

- nom ;
- prénom ;
- ville ;
- code postal ;
- date de naissance ;
- cotisation ;
- type.

Recherche dans :

- nom ;
- prénom ;
- adresse ;
- commune ;
- email ;
- téléphone.

---

# 27. SÉLECTION MULTIPLE

Sur PC :

```text
15 adhérents sélectionnés

[ Envoyer un mail ]
[ Exporter ]
[ Imprimer ]
```

Sur mobile, la sélection multiple peut être adaptée à l’ergonomie tactile.

---

# 28. FICHE ADHÉRENT

Champs :

```text
Nom
Prénom
Adresse
Code postal
Ville
Email
Téléphone
Téléphone secondaire
Date de naissance
Type
Notes
```

Permettre :

- ajouter ;
- modifier ;
- supprimer.

Une modification doit automatiquement être synchronisée.

---

# 29. ACTIONS MOBILES RAPIDES

Depuis une fiche mobile, prévoir par exemple :

```text
[ Appeler ]

[ Envoyer un email ]

[ Copier l’adresse ]

[ Modifier ]

[ Voir les cotisations ]
```

Le bouton Appeler doit utiliser le téléphone normalement.

---

# 30. GMAIL

La responsable utilise Gmail.

Utiliser :

**Gmail API + OAuth 2.0**

Ne jamais demander son mot de passe Gmail.

Créer :

```text
[ Connecter mon compte Gmail ]
```

Puis :

```text
Gmail

🟢 Connecté

association@gmail.com

[ Déconnecter ]
```

---

# 31. GMAIL SUR PLUSIEURS APPAREILS

L’authentification Gmail doit être pensée pour :

- PC Bureau ;
- PC Portable ;
- mobile.

Ne pas copier naïvement un token OAuth d’un appareil vers un autre.

Chaque appareil doit pouvoir être autorisé proprement.

---

# 32. MESSAGERIE

Créer :

```text
Nouveau message

Destinataires :
15 adhérents sélectionnés

Objet :
[________________________________]

Message :
[                                   ]

Pièces jointes :
[ + Ajouter ]

[ ENVOYER ]
```

Respecter la confidentialité.

Utiliser :

- CCI/BCC ;
- ou envois individuels.

---

# 33. MESSAGERIE MOBILE

L’application mobile doit pouvoir à terme :

- sélectionner un adhérent ;
- envoyer un message ;
- utiliser un modèle ;
- éventuellement sélectionner plusieurs adhérents.

Pour les opérations massives importantes, l’application Windows peut rester prioritaire.

---

# 34. MODÈLES DE MAIL

Créer :

- Information générale
- Assemblée générale
- Nouvelle activité
- Annulation
- Rappel cotisation
- Événement
- Message libre

Les modèles doivent être synchronisés entre tous les appareils.

---

# 35. VARIABLES DE MAIL

Prévoir :

```text
{{prenom}}
{{nom}}
{{date}}
{{activite}}
```

---

# 36. COTISATIONS

Créer une vraie table :

```text
Cotisation

ID
AdherentId
Annee
Montant
Statut
DatePaiement
ModePaiement
Notes
CreatedAt
UpdatedAt
Version
DeletedAt
```

Afficher :

```text
Cotisations 2026

43 Payées

4 À régler
```

Permettre :

```text
[ Envoyer un rappel ]
```

---

# 37. COTISATIONS SUR MOBILE

Fonction importante.

La responsable doit pouvoir par exemple être lors d’une activité et faire :

```text
Jean DUPONT

Cotisation 2026

À régler

[ Marquer comme payée ]
```

La modification doit immédiatement être envoyée au serveur si Internet est disponible.

Sinon elle sera mise en attente.

---

# 38. ACTIVITÉS

Prévoir :

```text
Activite

ID
Nom
Description
CreatedAt
UpdatedAt
Version
```

et :

```text
AdherentActivite
```

Un adhérent peut participer à plusieurs activités.

---

# 39. ACTIVITÉS SUR MOBILE

Permettre à terme :

- voir les activités ;
- voir les participants ;
- rechercher un participant ;
- éventuellement noter une présence.

---

# 40. SAUVEGARDES

La base centrale doit être sauvegardée automatiquement.

Créer aussi sur Windows :

```text
[ Sauvegarder mes données ]
```

Exemple :

```text
ICA_Backup_2026-10-31.zip
```

Créer :

```text
[ Restaurer une sauvegarde ]
```

La restauration complète reste prioritairement une fonction Windows/administrateur.

---

# 41. RESTAURATION MULTI-APPAREILS

Une restauration centrale doit ensuite être propagée correctement vers :

- PC Bureau ;
- PC Portable ;
- téléphone ;
- tablette.

Empêcher qu’un appareil hors ligne puisse réinjecter accidentellement de vieilles données après une restauration.

---

# 42. EXPORT

Sur PC :

- CSV ;
- Excel ;
- PDF plus tard.

Exemples :

```text
Exporter tous les adhérents

Exporter les adhérents sélectionnés

Exporter les cotisations à régler
```

L’export avancé n’est pas prioritaire sur mobile.

---

# 43. IMPRESSION

Fonction prioritairement Windows :

- liste d’adhérents ;
- sélection ;
- activité ;
- cotisations.

---

# 44. PARAMÈTRES WINDOWS

Créer :

```text
PARAMÈTRES

Compte L’ICA
🟢 Connecté

Synchronisation
🟢 Synchronisé

Compte Gmail
🟢 Connecté

Sauvegardes
✓ Dernière sauvegarde

Mises à jour
Version 1.0.0

Données
Importer
Exporter
Restaurer
```

---

# 45. PARAMÈTRES MOBILE

Version simplifiée :

```text
Compte L’ICA
🟢 Connecté

Gmail
🟢 Connecté

Synchronisation
🟢 À jour

Version
1.0.0

[ Se déconnecter ]
```

---

# 46. GITHUB

Le repository doit être :

**PRIVÉ**

GitHub contient :

- code source ;
- workflows ;
- documentation ;
- versions.

GitHub ne contient jamais :

- adhérents ;
- emails ;
- téléphones ;
- dates de naissance ;
- base de production ;
- tokens ;
- mots de passe ;
- sauvegardes réelles ;
- secrets.

---

# 47. VERSIONNING

Utiliser :

```text
MAJOR.MINOR.PATCH
```

Exemple :

```text
1.0.0
1.0.1
1.1.0
2.0.0
```

Desktop et mobile peuvent avoir des versions différentes si nécessaire.

---

# 48. GITHUB ACTIONS

Automatiser :

```text
Code
 ↓
Tests
 ↓
Build
 ↓
Package
 ↓
Release
```

Prévoir progressivement :

## Desktop

- compilation Windows ;
- tests ;
- package ;
- installateur.

## Mobile

Plus tard :

- build Android ;
- build iOS ;
- tests ;
- package Android ;
- préparation publication Play Store/App Store.

---

# 49. MISES À JOUR WINDOWS

Afficher :

```text
Une nouvelle version de L’ICA Gestion est disponible.

Version actuelle :
1.0.0

Nouvelle version :
1.0.1

[ METTRE À JOUR ]

[ PLUS TARD ]
```

---

# 50. MISES À JOUR MOBILE

Prévoir l’architecture pour les mises à jour via les mécanismes standards :

- Google Play Store ;
- Apple App Store.

Ne pas essayer de contourner les systèmes de distribution mobile standards sans raison.

---

# 51. COMPATIBILITÉ DES VERSIONS

Il peut arriver temporairement que :

```text
PC Bureau
1.1.0

PC Portable
1.0.9

Mobile
1.0.3
```

Le backend doit gérer correctement cette situation.

Prévoir une politique de compatibilité API.

Éviter de casser immédiatement les anciennes versions.

Si une mise à jour devient obligatoire :

```text
Cette version de L’ICA Gestion doit être mise à jour pour continuer.

[ Mettre à jour ]
```

---

# 52. VERSIONNING API

Prévoir une stratégie propre pour l’API.

Exemple :

```text
/api/v1/
```

Ne pas casser les applications existantes sans migration contrôlée.

---

# 53. SÉCURITÉ DE L’UPDATER

Ne jamais intégrer :

- Personal Access Token GitHub ;
- clé privée ;
- mot de passe ;
- accès administrateur au repository.

Architecture possible :

```text
GitHub privé
      ↓
GitHub Actions
      ↓
Build
      ↓
Package
      ↓
Service de mise à jour
      ↓
PC L’ICA
```

---

# 54. INSTALLATEUR WINDOWS

Créer à terme :

```text
ICA-Gestion-Setup.exe
```

Installation simple.

Créer :

- raccourci Bureau ;
- menu Démarrer ;
- désinstallation Windows.

---

# 55. DONNÉES LOCALES WINDOWS

Utiliser par exemple :

```text
%LOCALAPPDATA%\L-ICA\
```

Avec :

```text
L-ICA/
├── cache/
│   └── local.db
├── backups/
├── logs/
└── config/
```

---

# 56. DONNÉES LOCALES MOBILE

Utiliser les mécanismes standards Android/iOS.

Ne pas stocker de secrets sensibles sans protection.

Utiliser le stockage sécurisé fourni par la plateforme pour :

- tokens ;
- informations de session ;
- secrets locaux.

---

# 57. JOURNALISATION

Créer des logs techniques.

Ne jamais écrire :

- contenu complet des emails ;
- mots de passe ;
- tokens ;
- données personnelles inutiles.

---

# 58. DIAGNOSTIC WINDOWS

Créer :

```text
Paramètres > Diagnostic
```

Afficher :

```text
Base locale
🟢 OK

Serveur L’ICA
🟢 Connecté

Synchronisation
🟢 À jour

Gmail
🟢 Connecté

Internet
🟢 Disponible

Mises à jour
🟢 À jour

Sauvegardes
🟢 OK
```

---

# 59. DIAGNOSTIC MOBILE

Version simplifiée :

```text
Serveur
🟢 Connecté

Synchronisation
🟢 À jour

Dernière synchronisation
10:42

Application
1.0.0
```

---

# 60. CONFIDENTIALITÉ / RGPD

L’application manipule :

- noms ;
- adresses ;
- emails ;
- téléphones ;
- dates de naissance.

La solution centrale doit tenir compte du RGPD.

Privilégier si possible :

- France ;
- Union Européenne.

Pas de publicité.

Pas de profilage.

Pas de télémétrie commerciale.

---

# 61. COMMUNICATION RÉSEAU

Toutes les communications entre :

```text
Desktop
Mobile
Serveur
```

doivent utiliser HTTPS.

Ne jamais transmettre de données sensibles en clair.

---

# 62. DESIGN WINDOWS

Utiliser le logo L’ICA comme inspiration.

Interface principalement :

- blanche ;
- gris clair ;
- texte foncé.

Utiliser les couleurs du logo comme accents.

---

# 63. DESIGN MOBILE

Même identité graphique que Windows.

Mais interface pensée spécifiquement pour :

- téléphone ;
- tactile ;
- navigation rapide ;
- grands boutons ;
- utilisation debout ou en déplacement.

Ne pas copier simplement les écrans WPF.

---

# 64. ACCESSIBILITÉ

Utiliser :

- grands textes ;
- contraste suffisant ;
- boutons larges ;
- vocabulaire simple ;
- icônes + texte.

---

# 65. NAVIGATION WINDOWS

```text
Accueil
Adhérents
Messagerie
Cotisations
Activités
Paramètres
```

---

# 66. NAVIGATION MOBILE

Navigation possible :

```text
Accueil
Adhérents
Cotisations
Activités
Plus
```

Avec une barre de navigation basse adaptée au mobile.

---

# 67. PRIORITÉS GLOBALES

Ne pas développer tout en même temps.

## PHASE 1

Backend et base centrale.

## PHASE 2

Application Windows.

## PHASE 3

Synchronisation multi-PC.

## PHASE 4

Gmail et cotisations.

## PHASE 5

Sauvegardes et mises à jour.

## PHASE 6

Application mobile.

---

# 68. PRIORITÉS V1 WINDOWS

1. Architecture client + serveur.
2. Authentification L’ICA.
3. Base centrale.
4. Synchronisation multi-PC.
5. Cache local.
6. Gestion des adhérents.
7. Recherche et filtres.
8. Import Excel/CSV.
9. Sélection multiple.
10. Gmail.
11. Messagerie.
12. Cotisations.
13. Sauvegarde/restauration.
14. Packaging Windows.
15. GitHub Actions.
16. Mise à jour automatique.

---

# 69. PRIORITÉS MOBILE V1

Lorsque le développement mobile commencera :

1. Authentification.
2. Synchronisation.
3. Liste des adhérents.
4. Recherche.
5. Fiche adhérent.
6. Modification.
7. Ajout.
8. Cotisations.
9. Activités.
10. Actions rapides email/téléphone.
11. Mode hors ligne.
12. Notifications éventuelles plus tard.

---

# 70. FONCTIONNALITÉS FUTURES

Préparer l’architecture pour :

- planning ;
- calendrier ;
- événements ;
- inscriptions ;
- listes de présence ;
- statistiques ;
- documents ;
- reçus ;
- gestion du bureau ;
- rôles utilisateurs ;
- Google Calendar ;
- Google Drive ;
- SMS ;
- notifications push ;
- QR codes ;
- scan de documents ;
- signatures.

Ne pas les développer avant demande.

---

# 71. RÈGLES DE DÉVELOPPEMENT

1. Ne casse jamais une fonction existante sans raison.

2. Analyse les impacts avant une grosse modification.

3. Architecture maintenable.

4. Évite les dépendances inutiles.

5. Aucun secret dans le code.

6. Aucune donnée réelle dans les tests.

7. Utilise des migrations.

8. Le projet doit toujours compiler.

9. Les erreurs doivent être correctement gérées.

10. Toujours penser :

```text
Que se passe-t-il si plusieurs appareils font cette opération en même temps ?
```

11. Toujours penser :

```text
Que se passe-t-il si Internet coupe ?
```

12. Toujours penser :

```text
Que se passe-t-il si le smartphone se ferme pendant la synchronisation ?
```

13. Toujours penser :

```text
Cette règle métier peut-elle être partagée entre Windows et mobile ?
```

14. Ne jamais enfermer la logique métier dans WPF ou MAUI.

---

# 72. TESTS

Tester :

- création ;
- modification ;
- suppression ;
- filtres ;
- import ;
- doublons ;
- cotisations ;
- sauvegardes ;
- Gmail ;
- synchronisation ;
- fonctionnement hors ligne ;
- conflits ;
- mises à jour ;
- authentification ;
- appels API.

Créer notamment :

```text
PC A crée un adhérent
↓
Serveur
↓
PC B reçoit
↓
Mobile reçoit
```

Et :

```text
Mobile hors ligne
↓
Modification locale
↓
Internet revient
↓
Serveur
↓
PC Bureau reçoit la modification
```

---

# 73. DONNÉES DE TEST

Uniquement des données fictives.

Exemple :

```text
Jean DUPONT
12 rue des Fleurs
23000 Guéret
jean.dupont@example.com
06 00 00 00 01
```

Ne jamais reprendre les vraies données visibles dans mes captures.

---

# 74. DOCUMENTATION

Créer :

```text
README.md
```

pour le développeur.

Créer :

```text
docs/USER_GUIDE_WINDOWS.md
```

et plus tard :

```text
docs/USER_GUIDE_MOBILE.md
```

---

# 75. TON RÔLE

Tu dois agir comme :

- architecte logiciel ;
- développeur C#/.NET ;
- développeur WPF ;
- développeur .NET MAUI ;
- développeur backend ;
- spécialiste PostgreSQL ;
- spécialiste synchronisation ;
- spécialiste SQLite ;
- spécialiste Gmail API ;
- spécialiste OAuth ;
- spécialiste GitHub Actions ;
- spécialiste UX desktop ;
- spécialiste UX mobile ;
- spécialiste sécurité ;
- spécialiste packaging Windows ;
- spécialiste publication Android/iOS.

Ne génère pas du code sans vision générale.

---

# 76. MÉTHODE DE TRAVAIL

Avant de coder énormément :

1. analyse le cahier des charges ;
2. propose l’architecture définitive ;
3. choisis le backend ;
4. choisis la base centrale ;
5. explique le système multi-PC ;
6. explique comment le mobile s’intégrera ;
7. explique ce qui sera partagé entre WPF et MAUI ;
8. explique la synchronisation ;
9. explique le mode hors ligne ;
10. explique les conflits ;
11. définis l’arborescence ;
12. définis les projets .NET ;
13. définis les tables ;
14. définis les dépendances ;
15. définis Gmail/OAuth ;
16. définis l’updater ;
17. définis les sauvegardes ;
18. définis les étapes de développement.

Présente ce plan AVANT de générer toute l’application.

---

# 77. AUTONOMIE

Tu peux décider seul des choix techniques lorsque tu sélectionnes la solution :

- la plus fiable ;
- la plus maintenable ;
- la plus sécurisée ;
- la plus simple.

Demande mon avis lorsque cela modifie :

- l’expérience utilisateur ;
- le design ;
- une fonctionnalité importante.

---

# 78. OBJECTIF FINAL UTILISATRICE

La responsable doit pouvoir faire :

```text
PC Bureau

Je modifie un adhérent.

Je ferme.
```

Puis :

```text
PC Portable

La modification est déjà là.
```

Puis :

```text
Téléphone

J’ouvre L’ICA Gestion.

La même modification est déjà présente.
```

Elle peut ensuite faire :

```text
Je marque une cotisation comme payée depuis mon téléphone.
```

Puis :

```text
PC Bureau

La cotisation est automatiquement indiquée comme payée.
```

Elle ne doit jamais avoir besoin de :

- copier une base ;
- exporter pour synchroniser ;
- utiliser Git ;
- manipuler SQLite ;
- comprendre OAuth ;
- lancer une synchronisation manuelle.

---

# 79. OBJECTIF DÉVELOPPEUR

De mon côté :

```text
Claude / Code
      ↓
GitHub privé
      ↓
GitHub Actions
      ↓
Desktop / Mobile / Backend
```

Concernant les données :

```text
PC Bureau
      ↕
PC Portable
      ↕
Serveur L’ICA
      ↕
Téléphone
      ↕
Tablette
```

Le code et les données doivent rester séparés.

---

# 80. PREMIÈRE MISSION

Commence maintenant par :

1. analyser complètement ce cahier des charges ;

2. proposer l’architecture technique définitive ;

3. comparer rapidement :

```text
ASP.NET Core + PostgreSQL

vs

Supabase + PostgreSQL
```

et choisir la solution la plus adaptée ;

4. expliquer comment le backend sera partagé entre Windows et mobile ;

5. expliquer comment le code métier pourra être partagé entre WPF et .NET MAUI ;

6. expliquer comment fonctionnera la synchronisation entre :

```text
PC Bureau
PC Portable
Android
iOS
```

7. expliquer le mode hors ligne ;

8. définir la résolution des conflits ;

9. proposer l’arborescence complète du repository ;

10. définir tous les projets `.NET` ;

11. définir les tables principales et leurs relations ;

12. définir les dépendances NuGet ;

13. définir Gmail + OAuth ;

14. définir l’authentification L’ICA ;

15. définir les sauvegardes ;

16. définir la stratégie sécurisée de mise à jour Windows ;

17. expliquer la stratégie future de distribution Android/iOS ;

18. découper le développement en versions :

```text
0.1.0
0.2.0
0.3.0
...
1.0.0
```

19. séparer clairement :

```text
ROADMAP DESKTOP
ROADMAP BACKEND
ROADMAP MOBILE
```

20. expliquer ce qui sera fonctionnel à chaque version ;

21. NE COMMENCE PAS encore à générer toute l’application tant que ce plan d’architecture n’est pas établi.

À partir de maintenant, considère ce document comme le :

# CAHIER DES CHARGES MAÎTRE DE L’ICA GESTION