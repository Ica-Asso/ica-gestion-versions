# L'ICA Gestion — Architecture technique de référence

> Document d'architecture v0.1 — 31/10/2026 — mis à jour avec la v0.1.0 desktop
> Complète le **Cahier des charges maître** (PROJECT.md). En cas de divergence, le cahier des charges fixe le *quoi*, ce document fixe le *comment*.

---

## 0. Synthèse des décisions

| Sujet | Décision |
|---|---|
| Runtime | **.NET 10 (LTS, supporté jusqu'à fin 2028)** partout |
| Backend | **ASP.NET Core 10 (API minimale versionnée `/api/v1`) + PostgreSQL managé** |
| Hébergement | Fournisseur **français/UE** (Clever Cloud ou Scaleway), PostgreSQL managé avec sauvegardes automatiques |
| Authentification | **ASP.NET Core Identity + OpenIddict** (OpenID Connect standard), Authorization Code + PKCE, refresh token **par appareil** |
| Desktop | **WPF** (.NET 10), MVVM avec CommunityToolkit.Mvvm |
| Mobile | **.NET MAUI** (Android + iOS), mêmes ViewModels métiers que le desktop quand c'est pertinent |
| Cache local | **SQLite via EF Core** (même code sur Windows et mobile) |
| Synchronisation | Moteur **partagé** desktop/mobile : *outbox* locale + *pull incrémental* par numéro de séquence serveur + notification temps réel **SignalR** |
| Conflits | Fusion **champ par champ** automatique ; dialogue uniquement si le *même champ* a été modifié des deux côtés |
| Gmail | **Gmail API, scope `gmail.send` uniquement**, autorisation OAuth faite **une seule fois**, envoi **par le serveur** (jeton chiffré côté serveur) |
| Mises à jour Windows | **Velopack** (installateur + mises à jour delta), paquets publiés par GitHub Actions vers un stockage **sans aucun secret dans l'application** |
| Mobile (distribution) | Google Play (piste de test interne) + Apple TestFlight / App Store |
| Sauvegardes | PITR du PostgreSQL managé + `pg_dump` chiffré nocturne vers un second stockage UE + export manuel depuis Windows |

---

## 1. Analyse du cahier des charges

**Ce qui structure tout le projet :**

1. **Multi-appareils + hors ligne** → impose un vrai moteur de synchronisation (et non un simple client d'API). C'est la pièce la plus complexe et la plus risquée : elle est traitée en priorité et testée de façon automatisée.
2. **Une seule utilisatrice principale, peu technique** → aucune intervention manuelle : connexion une fois par appareil, synchronisation invisible, mises à jour en un clic.
3. **Données personnelles (dont des mineurs)** → hébergement UE, chiffrement en transit, minimisation des logs, pas de télémétrie commerciale.
4. **Maintenance par un développeur seul assisté de Claude** → une seule stack (C#/.NET), peu de projets, peu de dépendances, conventions claires. **La simplicité de maintenance prime sur l'élégance théorique.**

**Ajustements proposés par rapport au cahier des charges :**

- Le moteur de synchronisation et le cache SQLite ne sont **pas** spécifiques au desktop (le CDC les plaçait dans `ICA.Desktop.Sync` / `ICA.Desktop.Data`). Ils deviennent **partagés** (`ICA.Client`) pour que le mobile en hérite sans réécriture.
- `ICA.Core`, `ICA.Models` et `ICA.Validation` sont **fusionnés en un seul projet `ICA.Domain`** : trois projets pour quelques entités serait de la surarchitecture.
- Le serveur est **un seul projet ASP.NET Core** organisé en dossiers (+ un projet de tests), plutôt que 4 projets.
- `Activite` et `AdherentActivite` reçoivent aussi `DeletedAt` (le CDC l'omettait) : toute table synchronisée doit pouvoir propager une suppression.
- Gmail : **envoi côté serveur** plutôt qu'un jeton par appareil (voir §13). Cela respecte l'exigence « ne pas copier un token d'un appareil à l'autre » tout en évitant à la gérante de connecter Gmail sur chaque appareil.

---

## 2. Backend : ASP.NET Core + PostgreSQL vs Supabase

| Critère | ASP.NET Core + PostgreSQL | Supabase + PostgreSQL |
|---|---|---|
| Cohérence C#/.NET | ✅ 100 % C#, modèles et validations partagés avec les clients | ⚠️ SDK C# communautaire ; logique serveur en SQL (RLS) ou TypeScript (Edge Functions) |
| Synchronisation sur mesure | ✅ Totalement maîtrisée (séquences, idempotence, fusion champ par champ, génération de données) | ⚠️ Possible mais logique éclatée entre SQL, triggers et fonctions |
| Gmail côté serveur, jobs planifiés | ✅ Natif (BackgroundService) | ⚠️ Edge Functions + cron, autre langage |
| Démarrage rapide | ⚠️ Plus de code initial | ✅ Auth, API et temps réel prêts à l'emploi |
| Hébergement UE / RGPD | ✅ N'importe quel hébergeur français | ✅ Région UE disponible (société américaine) |
| Dépendance fournisseur | ✅ Faible : conteneur + PostgreSQL standard, déplaçable | ⚠️ Plus forte |
| Coût | Faible (petite instance + PG managé) | Gratuit au début, puis offre payante pour les sauvegardes sérieuses |
| Maintenance avec Claude | ✅ Un seul langage, un seul modèle mental | ⚠️ Trois langages |

**Choix : ASP.NET Core 10 + PostgreSQL managé.**
Supabase ferait gagner quelques semaines au début, mais le cœur du projet — la synchronisation fiable avec fusion et hors ligne — devrait de toute façon être écrit à la main, et il serait réparti entre SQL, TypeScript et C#. Avec ASP.NET Core, tout le métier est en C#, partagé entre serveur, Windows et mobile.

> **Décision du 31/10/2026 — remplace le serveur ci-dessous pour la v1 :** pas de serveur. La synchronisation passe par
> **le Google Drive de l'association** (voir [GOOGLE.md](GOOGLE.md)). Chaque ordinateur garde sa copie complète ; un fichier
> unique dans le Drive sert de point de rendez-vous ; la fusion est faite par l'application (enregistrement par enregistrement,
> la modification la plus récente gagne, suppressions douces). Coût : zéro, rien à administrer. Le serveur ASP.NET Core +
> PostgreSQL décrit ici reste l'option prévue si l'application mobile ou le nombre d'utilisateurs l'exigent un jour.

**Hébergement envisagé à l'origine (budget zéro) : Oracle Cloud « Always Free », région Paris ou Marseille.**
Une VM Arm (2 OCPU / 12 Go depuis la réduction de juin 2026) toujours allumée, sur laquelle tourne un `docker compose` :

```text
VM Oracle Always Free (France)
├── caddy        HTTPS automatique (Let's Encrypt) sur un nom gratuit (DuckDNS)
├── ica-server   ASP.NET Core 10 (image arm64)
├── postgres     PostgreSQL 17, volume chiffré
└── backup       pg_dump nocturne chiffré → Oracle Object Storage (Always Free)
```

Risques assumés et parades :
- **Pas de SLA, conditions modifiables sans préavis** (le quota Arm a été divisé par deux en 2026 sans annonce) → tout est dans `docker compose` + sauvegardes : migration vers un hébergeur payant (Clever Cloud, Scaleway, Hetzner) en moins d'une heure, sans changer le code.
- **Carte bancaire demandée à l'inscription** (vérification d'identité, pas de prélèvement tant qu'on reste dans les limites gratuites) → alerte budget à 0 € configurée.
- **Comptes inactifs récupérés** → l'usage quotidien + un petit job de santé suffisent.
- **Base non managée** → c'est le développeur qui gère PostgreSQL ; d'où la sauvegarde à trois niveaux (§14), dont une copie automatique sur les PC.

Alternative écartée : base gratuite Neon/Supabase + API sur un hébergeur gratuit (l'API s'endort après inactivité, ce qui casse le temps réel et ralentit chaque réveil).

L'application étant un conteneur standard + PostgreSQL standard, on peut changer d'hébergeur sans réécriture.

---

## 3. Vue d'ensemble

```text
                         ┌──────────────────────────────────────┐
                         │          SERVEUR L'ICA (UE)          │
                         │  ASP.NET Core 10  —  https://…       │
                         │                                      │
                         │  /connect/*    OpenIddict (OIDC)     │
                         │  /api/v1/*     API REST versionnée   │
                         │  /hubs/sync    SignalR (notif.)      │
                         │  BackgroundServices : envoi mails,   │
                         │    purge tombstones, sauvegardes     │
                         └───────────────┬──────────────────────┘
                                         │ (réseau privé)
                                ┌────────▼────────┐
                                │ PostgreSQL géré │◄── PITR + pg_dump chiffré
                                └─────────────────┘
          HTTPS uniquement               ▲
     ┌──────────────────┬────────────────┼────────────────┬───────────────┐
     │                  │                │                │               │
┌────┴─────┐      ┌─────┴────┐     ┌─────┴────┐     ┌─────┴────┐          │
│PC Bureau │      │PC Portable│    │ Android  │     │   iOS    │          │
│  WPF     │      │   WPF     │    │  MAUI    │     │  MAUI    │          │
│ SQLite   │      │  SQLite   │    │  SQLite  │     │  SQLite  │          │
└──────────┘      └───────────┘    └──────────┘     └──────────┘          │
                                                                          │
   GitHub privé ──► GitHub Actions ──► paquets Velopack (sans données) ───┘
                                       (stockage objet / serveur L'ICA)
```

**Principes non négociables :**
- Le **serveur est la seule source de vérité**. Les SQLite locaux sont des caches + files d'attente.
- **Aucune** chaîne de connexion PostgreSQL, clé API ou secret dans les applications clientes. Les clients ne connaissent que l'URL publique de l'API.
- Les clients n'accèdent aux données **que** via l'API authentifiée.

---

## 4. Partage du code entre WPF et .NET MAUI

```text
                    ┌──────────────────────┐
                    │  ICA.Contracts       │  DTO de l'API, enums, constantes de version
                    └──────────▲───────────┘
                               │
                    ┌──────────┴───────────┐
                    │  ICA.Domain          │  Entités, règles métier, validateurs,
                    │                      │  normalisation (téléphone, nom…), import
                    └──────────▲───────────┘
             ┌─────────────────┼──────────────────┐
             │                 │                  │
   ┌─────────┴──────┐ ┌────────┴─────────┐        │
   │  ICA.Server    │ │  ICA.Client      │  Cache SQLite, moteur de sync, client API,
   │  (ASP.NET)     │ │                  │  auth OIDC, ViewModels partagés
   └────────────────┘ └────────▲─────────┘
                       ┌───────┴────────┐
                ┌──────┴─────┐   ┌──────┴─────┐
                │ICA.Desktop │   │ ICA.Mobile │   Uniquement : vues, navigation,
                │   (WPF)    │   │   (MAUI)   │   services propres à la plateforme
                └────────────┘   └────────────┘
```

**Ce qui est partagé (≈ 70–80 % du code client) :**
- Règles métier et validations (une adresse e-mail invalide l'est partout, un adhérent « Enfant » suit les mêmes règles partout).
- Moteur de synchronisation, file d'attente hors ligne, détection et fusion des conflits.
- Accès au cache SQLite (EF Core Sqlite fonctionne sur Windows, Android et iOS).
- Client API typé, gestion des jetons, politique de nouvelle tentative.
- **ViewModels « métier »** (`AdherentListViewModel`, `CotisationViewModel`, `SyncStatusViewModel`…) écrits avec CommunityToolkit.Mvvm, qui fonctionne avec WPF comme avec MAUI.

**Ce qui reste propre à chaque plateforme :**
- Les vues (XAML WPF ≠ XAML MAUI : contrôles différents).
- Navigation, dialogues, notifications.
- Stockage sécurisé des jetons : **DPAPI** (Windows) / **SecureStorage** (Android Keystore, iOS Keychain).
- Navigateur de connexion : boucle locale `http://127.0.0.1:port` (Windows) / `WebAuthenticator` (MAUI).
- Appel téléphonique, ouverture d'un mail natif (mobile), impression, import Excel, mise à jour (desktop).

Ces différences passent par de petites interfaces définies dans `ICA.Client` (`ISecureTokenStore`, `IBrowserLogin`, `IDialogService`, `IConnectivity`) et implémentées dans chaque application.

**Règle :** `ICA.Domain` et `ICA.Client` ciblent `net10.0` pur — aucune référence à WPF ni à MAUI. Une vérification en CI (test d'architecture) l'empêche.

---

## 5. Synchronisation

### 5.1 Champs techniques de chaque table synchronisée

| Champ | Rôle |
|---|---|
| `Id` (`uuid`, **UUID v7** généré par le client) | Identifiant global. Généré hors ligne sans collision, triable dans le temps. |
| `CreatedAt`, `UpdatedAt` (`timestamptz`, UTC) | Information. **Jamais** utilisés pour décider de l'ordre de sync (horloges des appareils non fiables). |
| `Version` (`int`) | Incrémenté par le serveur à chaque modification acceptée. Sert de contrôle de concurrence. |
| `DeletedAt` (`timestamptz?`) | Suppression logique (« tombstone »), propagée aux autres appareils. |
| `ChangeSeq` (`bigint`) | Numéro attribué par **une séquence PostgreSQL unique** à chaque écriture. C'est le curseur de synchronisation. |
| `UpdatedByDeviceId` (`uuid`) | Appareil auteur de la dernière modification (diagnostic, anti-écho). |

### 5.2 Écriture locale (même hors ligne)

Dans **une seule transaction SQLite** :
1. la fiche est modifiée dans le cache local ;
2. une **opération** est ajoutée à la table `Outbox` :

```text
OpId (UUID v7) | Entité | EntityId | Type (Create/Update/Delete)
BaseVersion    | ChangedFields (JSON : champ → {ancienne, nouvelle valeur})
CreatedAtLocal | Attempts | Status
```

L'interface est mise à jour immédiatement. Si l'application est fermée ou le téléphone éteint juste après, l'opération est déjà sur le disque.

### 5.3 Envoi (push)

`POST /api/v1/sync/push` avec un lot d'opérations, dans l'ordre.

Pour chaque opération, le serveur :
1. vérifie si `OpId` est déjà dans `ProcessedOperations` → **déjà traité, renvoie le même résultat** (idempotence : renvoyer deux fois après une coupure ne crée jamais de doublon) ;
2. si `BaseVersion == Version actuelle` → applique, `Version + 1`, nouveau `ChangeSeq` ;
3. sinon → **fusion champ par champ** (§6) ;
4. enregistre `OpId` et le résultat ;
5. le tout dans une transaction PostgreSQL.

La réponse renvoie, par opération : `Applied`, `Merged`, `Conflict` ou `Rejected` (validation), avec l'état serveur de la fiche.

### 5.4 Réception (pull)

`GET /api/v1/sync/pull?since={dernierChangeSeq}&limit=500`

Renvoie toutes les lignes (y compris supprimées) dont `ChangeSeq > since`, triées, par pages, avec le nouveau curseur. **Seules les différences transitent**, jamais la base entière (sauf première connexion ou après restauration).

> Point technique traité : une transaction PostgreSQL longue peut valider un `ChangeSeq` plus petit qu'un autre déjà lu. Le serveur ne renvoie que jusqu'à un « plafond sûr » (plus petit ChangeSeq encore en cours de transaction, via `pg_snapshot_xmin`), ce qui garantit qu'aucune modification n'est sautée.

### 5.5 Déclenchement automatique

```text
Modification locale ──► push immédiat (si en ligne)
                        │
Serveur accepte ───────► SignalR : « nouveaux changements (seq 1234) »
                        │           à tous les autres appareils connectés
Autres appareils ──────► pull depuis leur curseur
```

Filets de sécurité : pull à l'ouverture de l'application, au retour au premier plan (mobile), au retour du réseau, et toutes les 2 minutes si SignalR est indisponible. **L'utilisatrice n'a jamais de bouton « Synchroniser » à presser** (un bouton discret « Synchroniser maintenant » peut exister dans Diagnostic).

### 5.6 Ordre des opérations

Push **avant** pull, et un seul cycle de synchronisation à la fois par appareil (verrou). Les modifications locales en attente sont réappliquées par-dessus les données reçues, ce qui évite qu'un pull « écrase » visuellement une modification pas encore envoyée.

### 5.7 Scénario cible

```text
Téléphone (hors ligne) : cotisation Jean DUPONT → Payée
   └─ SQLite : cotisation mise à jour + opération dans Outbox      🔴 Hors connexion
Retour du réseau
   └─ push → serveur applique (Version 3 → 4, ChangeSeq 5812)     🟡 Synchronisation…
   └─ SignalR → PC Bureau, PC Portable
PC Bureau
   └─ pull since 5790 → reçoit la cotisation → écran mis à jour    🟢 Synchronisé
```

---

## 6. Conflits

### 6.1 Fusion automatique (cas normal)

Chaque opération transporte les **champs modifiés avec leur valeur d'origine**. Le serveur compare :

| Situation | Résultat |
|---|---|
| PC modifie *Téléphone*, mobile modifie *Adresse* | Les deux sont appliqués. **Aucun message.** |
| Les deux mettent la **même valeur** dans le même champ | Appliqué. Aucun message. |
| Cotisation marquée « Payée » sur deux appareils | Appliqué (opération naturellement idempotente). |
| Modification d'une fiche supprimée ailleurs | La suppression l'emporte ; la modification est conservée dans le journal des conflits. |
| Même champ, **valeurs différentes** | Conflit réel → §6.2 |

Champs « texte libre » (`Notes`) : si les deux versions ont été modifiées, le serveur conserve la plus récente et place l'autre en conflit (pas de fusion de texte automatique, source d'erreurs).

### 6.2 Conflit réel (rare)

- Le serveur conserve la **valeur déjà enregistrée** (aucune perte : l'autre valeur est stockée dans `SyncConflict`).
- Sur l'appareil concerné, la fiche affiche un bandeau :

```text
Cette fiche a été modifiée depuis un autre appareil.

[ Utiliser la version la plus récente ]   [ Vérifier les différences ]
```

- « Vérifier les différences » montre uniquement les champs concernés côte à côte, en langage simple (« Téléphone : 06 00 00 00 01 *(PC Bureau, 10:42)* ou 06 00 00 00 02 *(ce téléphone, 10:44)* »).
- Si elle ignore le bandeau, la version serveur reste en vigueur : rien n'est bloqué.

### 6.3 Doublons créés hors ligne

Deux appareils créent « Jean DUPONT » hors ligne → deux UUID différents, donc deux fiches. Le serveur ne fusionne pas automatiquement (risque d'homonymes) mais la détection de doublons (nom + prénom + date de naissance ou e-mail) les signale sur l'accueil : « 1 doublon possible à vérifier » avec un bouton **Fusionner**.

---

## 7. Mode hors ligne

| Élément | Hors ligne |
|---|---|
| Consulter, rechercher, filtrer, trier | ✅ (cache SQLite) |
| Ajouter / modifier / supprimer un adhérent | ✅ (mis en attente) |
| Marquer une cotisation payée | ✅ (mis en attente) |
| Envoyer des e-mails | ⏳ Mis en file, envoyés au retour du réseau (avec indication claire) |
| Import Excel | ✅ local, envoi au retour du réseau |
| Restauration, connexion d'un nouvel appareil | ❌ Nécessite Internet |

Affichage : indicateur permanent discret (`🟢 Synchronisé` / `🟡 Synchronisation…` / `🔴 Hors connexion` + nombre de modifications en attente si > 0) et le message du cahier des charges lors du passage hors ligne.

**Durée de session hors ligne :** le refresh token reste valable 60 jours glissants ; un appareil peut donc rester hors ligne longtemps sans redemander de mot de passe. Les modifications restent en attente sans limite de temps.

---

## 8. Restauration et « génération de données »

Une table serveur `ServerState` contient un entier `DataGeneration`, envoyé dans chaque réponse de sync.

Lors d'une restauration centrale :
1. le serveur restaure les données et **incrémente `DataGeneration`** ;
2. tout appareil qui se présente avec une génération plus ancienne voit ses opérations en attente **refusées** ;
3. l'appareil **ne les supprime pas silencieusement** : il les exporte dans `%LOCALAPPDATA%\L-ICA\backups\non-synchronise-AAAA-MM-JJ.json` (ou équivalent mobile) puis vide son cache et recharge tout depuis le serveur ;
4. message : « Les données de L'ICA ont été restaurées. Cet appareil a été remis à jour. »

Résultat : **un appareil resté hors ligne ne peut jamais réinjecter de vieilles données après une restauration.**

---

## 9. Arborescence du repository

```text
ICA-Gestion/
├── src/
│   ├── ICA.Contracts/          # DTO API v1, enums, ApiVersion, constantes
│   ├── ICA.Domain/             # Entités, règles, validateurs, normalisation, import
│   ├── ICA.Client/             # Cache SQLite, sync, outbox, auth OIDC, client API,
│   │                           # ViewModels partagés, interfaces plateforme
│   ├── ICA.Server/             # ASP.NET Core : API, OpenIddict, SignalR, Gmail,
│   │   ├── Endpoints/          #   jobs, migrations EF Core
│   │   ├── Sync/
│   │   ├── Auth/
│   │   ├── Mail/
│   │   ├── Backup/
│   │   └── Data/Migrations/
│   ├── ICA.Desktop/            # WPF : vues, thèmes, impression, import, Velopack
│   └── ICA.Mobile/             # .NET MAUI (créé en phase 6)
├── tests/
│   ├── ICA.Domain.Tests/       # Règles, validations, normalisation, import
│   ├── ICA.Client.Tests/       # Moteur de sync, outbox, fusion (SQLite en mémoire)
│   ├── ICA.Server.Tests/       # API + PostgreSQL réel via Testcontainers
│   └── ICA.Sync.Scenarios/     # Scénarios multi-appareils simulés (PC A / PC B / mobile)
├── assets/
│   ├── logo/                   # Logo officiel (source + déclinaisons)
│   └── icons/
├── deploy/
│   ├── Dockerfile
│   ├── docker-compose.dev.yml  # PostgreSQL local pour le développement
│   └── README.md
├── docs/
│   ├── ARCHITECTURE.md         # ce document
│   ├── SYNC.md                 # spécification détaillée du protocole
│   ├── SECURITY.md
│   ├── RUNBOOK.md              # restauration, rotation des clés, incidents
│   ├── USER_GUIDE_WINDOWS.md
│   └── USER_GUIDE_MOBILE.md    # phase 6
├── samples/
│   └── adherents-fictifs.xlsx  # données 100 % fictives pour tester l'import
├── .github/workflows/
│   ├── ci.yml                  # build + tests à chaque push / PR
│   ├── server-deploy.yml       # déploiement serveur sur tag server-v*
│   ├── desktop-release.yml     # paquet Velopack sur tag desktop-v*
│   └── mobile-build.yml        # phase 6
├── Directory.Build.props       # version .NET, nullable, analyzers communs
├── Directory.Packages.props    # versions NuGet centralisées
├── ICA-Gestion.sln
├── .gitignore                  # exclut *.db, backups, appsettings.*.local.json, secrets
├── README.md
├── PROJECT.md                  # cahier des charges maître
└── VERSION
```

L'installateur n'a pas besoin d'un dossier `installer/` séparé : Velopack le génère à partir de la publication de `ICA.Desktop` dans la CI.

---

## 10. Projets .NET

| Projet | Cible | Références | Rôle |
|---|---|---|---|
| `ICA.Contracts` | `net10.0` | — | Formes échangées sur le réseau. Stable, versionné avec l'API. |
| `ICA.Domain` | `net10.0` | Contracts | Logique métier pure, sans I/O. |
| `ICA.Client` | `net10.0` | Domain, Contracts | Tout le « cerveau » client sauf l'affichage. |
| `ICA.Server` | `net10.0` (ASP.NET) | Domain, Contracts | Backend complet. |
| `ICA.Desktop` | `net10.0-windows` | Client | Application WPF. |
| `ICA.Mobile` | `net10.0-android`, `net10.0-ios` | Client | Application MAUI. |

---

## 11. Modèle de données

### 11.1 Schéma

```text
Adherent 1───* Cotisation
Adherent 1───* AdherentActivite *───1 Activite
Adherent 0..1──* Adherent (ResponsableId, pour les enfants — à valider)
ModeleMail, Parametre        (synchronisés, indépendants)
Activite 1───* Seance        (séance ou événement daté ; SerieId regroupe une répétition hebdomadaire)
MailEnvoi 1───* MailDestinataire *───1 Adherent
```

### 11.2 Tables synchronisées
*(toutes avec `Id, CreatedAt, UpdatedAt, Version, DeletedAt, ChangeSeq, UpdatedByDeviceId`)*

**Adherent**
| Champ | Type | Remarques |
|---|---|---|
| Nom, Prenom | text | Nom stocké tel que saisi ; affichage NOM en majuscules |
| Adresse, ComplementAdresse | text? | |
| CodePostal | text? | Texte (codes commençant par 0) |
| Ville | text? | |
| Email | text? | Validé, stocké en minuscules |
| Telephone1, Telephone2 | text? | Stockés normalisés (`+33600000001`), affichés `06 00 00 00 01` |
| DateNaissance | date? | |
| TypeAdherent | enum | `Adulte`, `Enfant` |
| AnneeAdhesion | int? | |
| ResponsableId | uuid? | Lien enfant → adulte responsable (**validé**). Obligatoire si `TypeAdherent = Enfant` ; e-mail/téléphone de l'enfant facultatifs, ceux du responsable utilisés par défaut |
| Notes | text? | |
| NomRecherche | text | Calculé : sans accents, minuscules (recherche « dupont » trouve « Dupont ») |

**Cotisation** — unique (`AdherentId`, `Annee`) parmi les non supprimées
`AdherentId, Annee (int), Montant (numeric(10,2)), Statut (APayer | Payee | Exoneree), DatePaiement (date?), ModePaiement (Especes | Cheque | Virement | Autre ?), Notes`

**Activite** — `Nom, Description, Couleur?, Archivee (bool)`

**AdherentActivite** — `AdherentId, ActiviteId` (unique parmi les non supprimées)

**ModeleMail** — `Nom, Categorie, Objet, Corps, EstSysteme (bool)` — les 7 modèles du CDC sont créés par migration.

**Seance** — `ActiviteId?, Titre?, Date, HeureDebut?, HeureFin?, Lieu?, Notes?, Annulee, SerieId?` — une ligne par date (une répétition crée N séances : annuler ou déplacer une date ne touche pas les autres, et la synchronisation reste simple).

**Parametre** — `Cle, Valeur (jsonb)` — paramètres synchronisables (montants de cotisation, signature des mails, saison en cours…).

### 11.3 Tables serveur uniquement

| Table | Rôle |
|---|---|
| Tables ASP.NET Identity (`Users`, …) | Comptes, mots de passe hachés |
| Tables OpenIddict | Applications clientes, autorisations, jetons |
| **Device** | `Id, UserId, Nom ("PC Bureau"), Plateforme, VersionApp, DerniereConnexion, RevoqueLe` — base de l'écran « Appareils connectés » |
| **ProcessedOperation** | `OpId, DeviceId, Resultat, TraiteLe` — idempotence (purgée après 90 jours) |
| **SyncConflict** | Valeurs perdantes conservées pour vérification |
| **ServerState** | `DataGeneration`, `MinClientVersion` par plateforme |
| **GmailConnection** | Adresse, refresh token **chiffré** (Data Protection), date |
| **MailEnvoi / MailDestinataire** | Journal d'envoi : objet, date, statut par destinataire. **Pas le corps du message après envoi.** |
| **AuditLog** | Qui/quel appareil/quand/quelle action (sans valeurs personnelles) |
| DataProtectionKeys | Clés de chiffrement ASP.NET (persistées en base) |

### 11.4 Tables locales (SQLite) en plus des copies des tables synchronisées
`Outbox`, `SyncState (curseur ChangeSeq, DataGeneration, dernière sync)`, `LocalConflict`.

### 11.5 Règles transverses
- Suppression d'un adhérent = suppression logique de ses cotisations et participations dans la même opération.
- Les tombstones sont purgés côté serveur après **180 jours** ; un appareil absent plus longtemps refait une synchronisation complète (détecté automatiquement).
- Migrations : EF Core côté serveur (`dotnet ef migrations`), appliquées au déploiement ; côté client, migrations EF Core Sqlite appliquées au démarrage.

---

## 12. Authentification L'ICA

**ASP.NET Core Identity** (gestion des comptes, hachage PBKDF2, verrouillage après échecs, 2FA possible plus tard) + **OpenIddict** (serveur OpenID Connect standard, open source, intégré à l'application). Pas de système maison : seuls des flux standards et éprouvés sont utilisés.

**Flux :** Authorization Code + PKCE (recommandé pour les applications natives).

```text
Nouvel appareil
  [ Connecter cet appareil ]
     → ouverture du navigateur sur la page de connexion L'ICA (HTTPS)
     → e-mail + mot de passe (une fois)
     → retour automatique dans l'application
     → nom proposé : « PC de <nom Windows> » (modifiable)
     → Récupération des données de L'ICA… ✓ Adhérents ✓ Cotisations ✓ Activités ✓ Paramètres
```

- **Access token** : 15 min. **Refresh token** : rotatif, 60 jours glissants, **lié à un `DeviceId`**.
- Stockage : DPAPI (Windows, lié à la session utilisateur) / SecureStorage (mobile).
- « Déconnecter cet appareil » révoque ses jetons côté serveur : l'appareil perd l'accès à la prochaine requête et efface son cache local.
- Rôles prévus dès maintenant (`Gerante`, `Membre bureau`, `Admin technique`) mais un seul utilisé en v1.
- Mot de passe oublié : lien par e-mail (envoyé via le compte Gmail connecté ou un service SMTP transactionnel UE).
- Compte développeur séparé (`Admin technique`) pour la maintenance : on ne partage pas le compte de la gérante.

---

## 13. Gmail + OAuth

### 13.1 Choix : autorisation unique, envoi par le serveur

```text
Paramètres > Compte Gmail > [ Connecter mon compte Gmail ]
   → page Google (consentement, scope gmail.send uniquement)
   → le serveur L'ICA reçoit le code, l'échange, chiffre et stocke le refresh token
Gmail 🟢 Connecté — association@gmail.com   [ Déconnecter ]
```

**Pourquoi côté serveur :**
- connexion **une seule fois**, valable sur tous les PC et le téléphone ;
- aucun jeton Gmail sur les appareils, donc aucune copie d'appareil à appareil ;
- file d'envoi fiable : un envoi de 40 mails continue même si le PC est fermé ou passe hors ligne ;
- respect des limites Gmail (≈ 500 destinataires/jour pour un compte Gmail gratuit) avec mise en file et reprise le lendemain si nécessaire.

**Contrepartie :** le serveur détient un accès d'**envoi** (et seulement d'envoi : il ne peut pas lire la boîte de réception). Le jeton est chiffré au repos, jamais journalisé, révocable depuis l'application ou depuis le compte Google.

### 13.2 Confidentialité des destinataires
**Envois individuels** (un mail par adhérent) : aucune adresse n'est visible des autres, et c'est indispensable pour personnaliser `{{prenom}}`. Envoi en CCI possible en option pour les très gros volumes.

### 13.3 Variables
`{{prenom}}`, `{{nom}}`, `{{date}}`, `{{activite}}` (+ `{{annee_cotisation}}`, `{{montant_cotisation}}` pour les rappels). Rendu dans `ICA.Domain` (partagé), aperçu avant envoi sur un destinataire réel.

### 13.4 Mobile
- Message à un seul adhérent : bouton « Envoyer un email » → ouvre l'application Gmail/Mail du téléphone (`mailto:`), le plus naturel au quotidien.
- Message à partir d'un modèle ou à plusieurs : passe par l'API → même file d'envoi serveur.

### 13.5 Contraintes Google (sans nom de domaine)
- Un projet Google Cloud est nécessaire (créé par le développeur, au nom de l'association).
- `gmail.send` est un scope **sensible**. En statut « Test », Google **expire les refresh tokens au bout de 7 jours** → à proscrire.
- L'association n'ayant **pas de nom de domaine**, la validation officielle Google (qui exige un domaine vérifié) n'est pas envisagée. Choix retenu : application OAuth **publiée en « Production » sans validation**. Conséquences acceptables ici :
  - un écran « Google n'a pas validé cette application » s'affiche **une seule fois**, lors de la connexion Gmail (faite par le développeur avec la gérante) ;
  - limite de 100 comptes Google autorisés au total (un seul utilisé) ;
  - les refresh tokens n'expirent pas au bout de 7 jours.
- Plan B si Google durcit ces règles : SMTP Gmail avec mot de passe d'application (nécessite la validation en deux étapes sur le compte de l'association).

---

## 14. Sauvegardes

| Niveau | Mécanisme | Rétention |
|---|---|---|
| 1. Serveur | Job nocturne `pg_dump` → chiffré (age) → Oracle Object Storage (Always Free, hors de la VM) | 30 quotidiennes + 12 mensuelles |
| 2. Hors Oracle (gratuit) | Chaque PC Windows connecté télécharge automatiquement la dernière sauvegarde chiffrée dans `%LOCALAPPDATA%\L-ICA\backups\` | 14 dernières |
| 3. Manuel (Windows) | `[ Sauvegarder mes données ]` → `ICA_Backup_2026-10-31.zip` (JSON complet + CSV lisibles), **protégé par mot de passe** (AES-256) | Géré par l'utilisatrice |

- **Restauration** (Windows, rôle autorisé) : `[ Restaurer une sauvegarde ]` → aperçu (« 47 adhérents, 43 cotisations, du 31/10/2026 ») → confirmation renforcée → le serveur effectue d'abord une sauvegarde de sécurité de l'état actuel → restaure → incrémente `DataGeneration` (§8).
- Test de restauration automatisé mensuel dans la CI sur une base jetable (une sauvegarde jamais testée n'est pas une sauvegarde).
- Aucune sauvegarde réelle dans GitHub.

---

## 15. Mises à jour Windows

### 15.1 Outil : Velopack
Successeur moderne de Squirrel, open source, conçu pour .NET :
- génère `ICA-Gestion-Setup.exe` : installation **sans droits administrateur**, raccourcis Bureau + menu Démarrer, désinstallation dans « Applications » de Windows ;
- mises à jour **delta** (petits téléchargements), application au redémarrage, retour automatique en cas d'échec ;
- API simple pour afficher la fenêtre du cahier des charges (`Nouvelle version disponible… [METTRE À JOUR] [PLUS TARD]`).

### 15.2 Chaîne sécurisée

```text
git tag desktop-v1.0.1
      ↓
GitHub Actions (repo privé)
  build → tests → publish win-x64 → signature du code → vpk pack
      ↓
Envoi des paquets vers le canal de mise à jour
  (stockage objet UE en lecture seule, ou /updates sur le serveur L'ICA)
      ↓
Application : vérifie au démarrage puis toutes les 6 h
```

- L'application **ne contacte jamais GitHub** et ne contient **aucun jeton** : elle lit simplement un flux de mise à jour public en lecture seule. Les paquets ne contiennent que du code compilé, aucune donnée ni secret.
- Les secrets de publication (clé de stockage, certificat) vivent uniquement dans les **GitHub Actions secrets**.
- **Pas de signature du code** (décision : éviter les frais). L'installation initiale est faite par le développeur sur chaque PC, qui valide une fois l'avertissement SmartScreen (« Informations complémentaires → Exécuter quand même »). Les mises à jour téléchargées par Velopack ne déclenchent pas cet avertissement. Intégrité garantie par HTTPS + empreintes SHA-256 du flux Velopack. La signature pourra être ajoutée plus tard sans changer l'architecture.
- Canaux `stable` et `beta` (le développeur teste en beta avant de publier en stable).

### 15.3 Compatibilité des versions
- API versionnée par URL (`/api/v1`). Ajouts de champs = compatibles (les clients ignorent les champs inconnus). Changement incompatible = `/api/v2`, `v1` maintenue au moins 6 mois.
- Chaque requête envoie `X-ICA-Client: desktop/1.0.9`. Le serveur expose `MinClientVersion` par plateforme.
- En dessous du minimum : réponse `426 Upgrade Required` → écran « Cette version de L'ICA Gestion doit être mise à jour pour continuer. [Mettre à jour] ». **Les modifications en attente sont conservées** et envoyées après la mise à jour.
- Le protocole de sync (`/sync/push`, `/sync/pull`) est conçu pour ne jamais casser : nouveaux champs optionnels uniquement.

---

## 16. Distribution Android / iOS (phase 6)

| | Android | iOS |
|---|---|---|
| Compte | Google Play Console (25 $ une fois) | Apple Developer Program (99 $/an) |
| Distribution initiale | **Piste de test interne** (non publique, installation via le Play Store, mises à jour automatiques) | **TestFlight** (builds valables 90 jours, à renouveler) |
| Distribution stable | Publication Play Store (éventuellement non répertoriée) | App Store, idéalement en **distribution non répertoriée** (lien direct, invisible dans la recherche) |
| Build CI | Runner Ubuntu/Windows GitHub | **Runner macOS obligatoire** (GitHub Actions macOS) |
| Signature | Keystore dans les secrets GitHub | Certificats + profils dans les secrets GitHub |

Mises à jour exclusivement par les stores (pas de contournement). La même politique `MinClientVersion` s'applique.

**Apple et Google exigent une politique de confidentialité publique** : elle sera de toute façon nécessaire pour la validation Gmail ; une seule page servira aux deux.

---

## 17. Sécurité & RGPD

- HTTPS partout (HSTS), TLS géré par l'hébergeur.
- Hébergement et sauvegardes en **France/UE**.
- Logs : identifiants techniques uniquement (Id d'adhérent, jamais nom/e-mail/téléphone/contenu de mail/jeton). Serilog avec filtre de masquage.
- Limitation de débit sur `/connect/token` et les endpoints sensibles.
- Données de mineurs : minimisation (pas de champ superflu), accès réservé au compte de l'association.
- À prévoir côté association (hors logiciel) : registre des traitements, mention d'information aux adhérents, durée de conservation (proposition : suppression ou anonymisation des anciens adhérents après 3 ans sans adhésion — fonction prévue en v1.x).
- Pas de publicité, pas de télémétrie, pas de SDK d'analyse tiers. Rapports d'erreurs uniquement vers les logs du serveur L'ICA.

### 17 bis. Assistance « Un problème ? »

Bouton permanent en bas du menu latéral (Windows ; plus tard dans « Plus » sur mobile) → fenêtre « Un problème ? On s'en occupe. ».

**Ce que la gérante fait :** écrire quelques mots (facultatif) → `[ Envoyer au support ]`. Rien d'autre.

**Rapport joint automatiquement** (construit dans `ICA.Client`, partagé avec le mobile) :
- version de l'application, version de Windows/Android/iOS, nom de l'appareil ;
- état de la synchronisation (dernière réussite, opérations en attente, dernière erreur) ;
- les ~200 dernières lignes du journal technique — déjà sans données personnelles (§17) ;
- un identifiant de rapport (`SUP-2026-0031`) affiché à la gérante pour qu'elle puisse en parler au téléphone ;
- capture de la fenêtre **seulement si elle coche la case** (décochée par défaut : elle peut montrer des adhérents).
- « Voir ce qui sera envoyé » affiche le rapport en clair avant l'envoi.

**Acheminement :**
| Version | Chemin |
|---|---|
| Démo locale (avant serveur) | Ouverture du logiciel de messagerie de Windows avec un mail pré-rempli (destinataire, objet, rapport dans le corps) ; le journal complet est copié dans un `.zip` placé sur le Bureau, à joindre |
| Avec serveur | `POST /api/v1/support` → table `SupportTicket` + mail envoyé automatiquement au développeur via la connexion Gmail de l'association (§13). Rien à joindre à la main |
| Hors ligne | Rapport mis en file comme toute modification, envoyé au retour du réseau |

- Destinataire du support : **mickaellejuez@hotmail.fr**. C'est un **paramètre** (`Support:Email`), jamais codé en dur dans le code : dans la démo locale, il est lu depuis `appsettings.json` ; une fois le serveur en place, depuis la configuration serveur. Changer de responsable technique ne demande pas de nouvelle version.
- En cas de plantage de l'application, au redémarrage suivant : « L'ICA Gestion s'est fermée de façon inattendue. Prévenir le support ? [ Oui ] [ Non ] » — même rapport, pré-rempli avec l'erreur.

---

## 18. Dépendances NuGet

Versions centralisées dans `Directory.Packages.props`. Toutes open source, licences permissives.

**Serveur**
| Paquet | Usage |
|---|---|
| Npgsql.EntityFrameworkCore.PostgreSQL | Accès PostgreSQL |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | Comptes |
| OpenIddict.AspNetCore, OpenIddict.EntityFrameworkCore | Serveur OIDC |
| Asp.Versioning.Http | Versionnement d'API |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | Clés de chiffrement persistées |
| Google.Apis.Gmail.v1, Google.Apis.Auth | Gmail |
| Serilog.AspNetCore | Journalisation |
| SignalR | Inclus dans ASP.NET Core |

**Partagé (Domain / Client)**
| Paquet | Usage |
|---|---|
| FluentValidation | Validations partagées |
| CommunityToolkit.Mvvm | ViewModels partagés WPF/MAUI |
| Microsoft.EntityFrameworkCore.Sqlite | Cache local |
| Microsoft.AspNetCore.SignalR.Client | Notifications temps réel |
| Microsoft.Extensions.Http.Resilience | Nouvelles tentatives / disjoncteur réseau |
| Duende.IdentityModel.OidcClient | Connexion OIDC native (PKCE) |
| libphonenumber-csharp | Normalisation des numéros |

**Desktop**
| Paquet | Usage |
|---|---|
| WPF-UI (lepoco) | Style moderne Fluent, sobre et lisible |
| ClosedXML | Import/export `.xlsx` |
| CsvHelper | Import/export `.csv` |
| Velopack | Installateur + mises à jour |

**Mobile** : CommunityToolkit.Maui.

**Tests** : xUnit, Shouldly (et non FluentAssertions, passé sous licence commerciale en v8), Testcontainers.PostgreSql, Microsoft.AspNetCore.Mvc.Testing, NetArchTest (règles d'architecture).

Volontairement **absents** : ORM supplémentaire, MediatR, AutoMapper, framework de DI tiers — inutiles à cette échelle.

---

## 19. Identité visuelle

Tirée du logo, utilisée avec retenue :
- Fond blanc / gris très clair, texte gris très foncé.
- **Couleur principale : bordeaux du texte « L'ICA »** (boutons principaux, titres, sélection).
- Brun du tronc : éléments secondaires.
- Les couleurs vives des mains : petites touches seulement (pastilles d'activités, illustrations de l'accueil vide, écran de démarrage).
- Contraste AA minimum, police système (Segoe UI Variable / police native mobile), taille de base 15–16 px, boutons ≥ 44 px de haut, toujours **icône + texte**.

---

## 20. Feuilles de route

Les composants ont des versions indépendantes (`server-v*`, `desktop-v*`, `mobile-v*`) ; le tableau ci-dessous les aligne par jalon.

### ROADMAP BACKEND

| Version | Contenu | Fonctionnel à la fin |
|---|---|---|
| 0.1.0 | Solution, `Directory.*.props`, CI build+tests, ASP.NET Core minimal, PostgreSQL local (Docker), migrations, `/health` | La CI est verte, le serveur démarre en local |
| 0.2.0 | Identity + OpenIddict, table `Device`, déploiement de l'environnement **staging** chez l'hébergeur | Connexion d'un client de test sur un vrai serveur HTTPS |
| 0.3.0 | Entités Adherent + API CRUD v1 + validations partagées | API adhérents utilisable |
| 0.4.0 | Protocole de sync : `ChangeSeq`, push idempotent, pull incrémental, tombstones, SignalR | Deux clients simulés restent synchronisés (tests automatisés) |
| 0.5.0 | Fusion champ par champ, `SyncConflict`, `DataGeneration`, `MinClientVersion` | Tous les scénarios de conflit testés |
| 0.6.0 | Cotisations, Activités, Paramètres, ModeleMail (+ modèles par défaut) | Toutes les entités synchronisées |
| 0.7.0 | Gmail (OAuth serveur, file d'envoi, journal) — **lancement de la validation Google** | Envoi réel de mails |
| 0.8.0 | Sauvegardes nocturnes chiffrées, export/restauration API, test de restauration CI | Plan de reprise opérationnel |
| 1.0.0 | Environnement **production**, durcissement (rate limiting, en-têtes), runbook | Prêt pour les vraies données |
| 1.1.0 | Endpoints optimisés mobile (si besoin), notifications push (option) | Prêt pour le mobile |

### ROADMAP DESKTOP

> Choix d'ordre (31/10/2026) : l'application Windows passe **avant** le serveur pour valider les écrans avec la gérante. La v0.1 stocke les données en JSON derrière l'interface `IStockage` ; le cache SQLite + file de synchronisation la remplaceront en v0.4 sans toucher aux écrans. Elle n'utilise volontairement **aucun paquet NuGet** (MVVM maison minimal, styles WPF faits main) : WPF-UI, CommunityToolkit.Mvvm, EF Core arriveront quand ils seront réellement nécessaires.

| Version | Contenu | Fonctionnel à la fin |
|---|---|---|
| 0.1.0 ✅ | **Démonstration** sur données fictives locales (JSON) : accueil, adhérents (recherche, filtres, tri, fiche, édition, enfants/responsable, doublons), cotisations (encaissement, annulation, rappel), sélection multiple (mail CCI, export CSV, impression), « Un problème ? », fenêtre arrondie intégrée | Montrable à la gérante ; validation des écrans |
| 0.1.x ✅ | Modèles de message (7 modèles du CDC, fenêtre « Nouveau message ») ; **calendrier des activités** (vue mois, séances hebdomadaires, événements ponctuels, annulation + message aux participants), activités et participants, inscriptions depuis la fiche, encart « Cette semaine » | Démo enrichie |
| 0.1.y ✅ | Tarifs 25 €/15 € + bilan financier ; Gmail dans le navigateur ; **import Excel/CSV** (lecteur .xlsx sans dépendance, colonnes reconnues par en-tête ou par contenu, enfants rattachés par nom + adresse) ; **mode réel** ; sauvegarde/restauration + copie quotidienne ; **version clé USB** autonome + installateur | Installation chez la gérante, **sur un seul PC** en attendant le serveur |
| 0.2.0 | « Connecter cet appareil », stockage DPAPI | Connexion au compte L'ICA |
| 0.3.0 | Liste adhérents, recherche instantanée, filtres, tri, fiche (ajout/modif/suppression) en ligne | Gestion des adhérents en ligne |
| 0.4.0 | Cache SQLite, outbox, moteur de sync partagé, indicateur 🟢🟡🔴, mode hors ligne | **Multi-PC + hors ligne** fonctionnels |
| 0.5.0 | Bandeau de conflit, comparaison, détection et fusion de doublons | Conflits gérés sans jargon |
| 0.6.0 | Import Excel/CSV (aperçu, lignes à vérifier, doublons), sélection multiple, export CSV/Excel | Reprise du tableau Excel existant |
| 0.7.0 | Cotisations (saison, tableau de bord, marquer payée), tableau de bord chiffré complet | Accueil conforme au CDC |
| 0.8.0 | Messagerie : connexion Gmail, modèles, variables, aperçu, suivi d'envoi, rappels de cotisation | Envoi de mails groupés |
| 0.9.0 | Activités + participants, impression, sauvegarde/restauration, Paramètres, Diagnostic | Fonctionnellement complet |
| 0.10.0 | Velopack : installateur, mises à jour, signature, canal beta ; `USER_GUIDE_WINDOWS.md` | Installable par la gérante |
| 1.0.0 | Recette avec la gérante, import des données réelles, passage en production | **Mise en service** |

### ROADMAP MOBILE (phase 6, après la mise en service desktop)

| Version | Contenu | Fonctionnel à la fin |
|---|---|---|
| 0.1.0 | Projet MAUI, thème, barre de navigation basse, CI Android | Application qui s'installe sur Android |
| 0.2.0 | Connexion (WebAuthenticator + SecureStorage), synchronisation via `ICA.Client` | Données présentes sur le téléphone |
| 0.3.0 | Liste, recherche, fiche, actions rapides (Appeler, Email, Copier l'adresse) | Consultation terrain |
| 0.4.0 | Ajout / modification, hors ligne, conflits | Édition terrain |
| 0.5.0 | Cotisations : « Marquer comme payée » | Encaissement lors d'une activité |
| 0.6.0 | Activités, participants, (présences en option) | |
| 0.7.0 | Messagerie avec modèles, Paramètres, Diagnostic | |
| 0.8.0 | Build iOS (runner macOS), TestFlight | Disponible sur iPhone |
| 1.0.0 | Piste interne Play Store + TestFlight/App Store non répertorié, `USER_GUIDE_MOBILE.md` | **Mobile en service** |

### Correspondance avec les phases du cahier des charges

| Phase CDC | Versions |
|---|---|
| 1. Backend et base centrale | Backend 0.1 → 0.3 |
| 2. Application Windows | Desktop 0.1 → 0.3 |
| 3. Synchronisation multi-PC | Backend 0.4–0.5, Desktop 0.4–0.6 |
| 4. Gmail et cotisations | Backend 0.6–0.7, Desktop 0.7–0.8 |
| 5. Sauvegardes et mises à jour | Backend 0.8–1.0, Desktop 0.9–1.0 |
| 6. Application mobile | Mobile 0.1 → 1.0 |

---

## 21. Stratégie de tests

- **Domaine** : chaque règle (validation, normalisation téléphone, rendu des variables, détection de doublons, parsing de l'import).
- **Serveur** : tests d'intégration sur un vrai PostgreSQL (Testcontainers), y compris concurrence (deux push simultanés sur la même fiche).
- **Scénarios de sync** (`ICA.Sync.Scenarios`) : plusieurs « appareils » simulés dans le même test, chacun avec son SQLite, contre un serveur de test :
  - PC A crée → serveur → PC B reçoit → mobile reçoit ;
  - mobile hors ligne modifie → réseau revient → PC Bureau reçoit ;
  - coupure au milieu d'un push (réessai idempotent, pas de doublon) ;
  - application tuée entre l'écriture locale et le push ;
  - modifications concurrentes de champs différents / du même champ ;
  - restauration pendant qu'un appareil est hors ligne ;
  - client en version inférieure au minimum.
- Données **exclusivement fictives** (`samples/adherents-fictifs.xlsx`, générateur Bogus-like maison).

---

## 22. Décisions et questions ouvertes

**Décidé (31/10/2026)**
- Saison des cotisations : **année civile** (janvier → décembre, « 2026 »).
- Tarifs : **25 € adulte, 15 € enfant** (`Cotisations:TarifAdulte` / `TarifEnfant`), proposés automatiquement à l'encaissement selon le type d'adhérent.
- Rythme : L'ICA se réunit **tous les mercredis de 14h à 17h, de janvier à décembre** (horaires et fin de répétition proposés par défaut dans « Planifier une activité »).
- Utilisateurs : **la gérante et son mari uniquement**. Pas de gestion de rôles en v1 (le rôle « Admin technique » reste réservé à la maintenance).
- Messagerie v0.1 : ouverture du message pré-rempli dans **Gmail (navigateur)** par défaut, ou dans l'application de messagerie de l'ordinateur — réglage par appareil (`%LOCALAPPDATA%\L-ICA\config\preferences.json`). Remplacé par l'envoi direct Gmail API en v0.8.
- Enfants rattachés à un adulte responsable (`ResponsableId`).
- Pas de signature de code : installation faite par le développeur (§15.2).
- Pas de nom de domaine : URL fournie par l'hébergeur (ex. `ica-gestion.<hébergeur>`, HTTPS inclus) ; page de confidentialité servie par le serveur L'ICA lui-même ; Gmail en OAuth « Production » non validé (§13.5).


- **Budget zéro pour l'instant** : hébergement Oracle Cloud Always Free (§2), sauvegardes gratuites (§14). À réévaluer après la mise en service.

**Note**
- Note mobile : la publication iOS imposera l'abonnement Apple Developer (99 $/an) ; Android : 25 $ une fois, ou installation directe de l'APK sans frais.
