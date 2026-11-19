# Module Dons & Mécénat

## Ce que fait le module

- **Annuaire des entreprises donatrices**, séparé des adhérents. Le SIREN est contrôlé (9 chiffres et clé de contrôle). Un même SIREN ne peut pas être enregistré deux fois, et l'appli prévient si le nom existe déjà.
- **Dons financiers, en nature ou de compétences.** Pour un don en nature ou de compétences, la valeur est **celle communiquée par l'entreprise** : l'application ne la calcule jamais.
- **Justificatifs PDF.** Il y en a deux sortes :
  - *Attestation simple* (`ICA-ATT-AAAA-NNNN`). Elle porte la mention : « Cette attestation confirme la réception d'un don. Elle ne constitue pas un reçu fiscal ouvrant droit à une réduction d'impôt. »
  - *Reçu fiscal* (`ICA-DON-AAAA-NNNN`) au format du **formulaire 2041-MEC-SD, Cerfa n° 16216\*03** (art. 238 bis CGI) : rubriques et libellés dans l'ordre officiel, toutes les catégories d'organismes, montants en chiffres et en toutes lettres, forme des versements, date ou période, date et signature, notes du formulaire. À l'envoi, une **lettre de remerciement sur papier à en-tête** de L'ICA (logo, signature) est jointe en plus du reçu officiel.
- **Envoi par Gmail** avec le PDF joint, toujours après confirmation. La date et le destinataire sont notés, et l'appli prévient avant un renvoi.
- **Bilan annuel** en PDF et en Excel, avec les chiffres utiles à la déclaration des dons (art. 222 bis CGI : montant total et nombre de reçus fiscaux). L'application ne fait pas la déclaration.

## Règles de sécurité fiscale

Un reçu fiscal ne peut être **ni émis ni envoyé** tant qu'il manque l'un des éléments suivants (*Paramètres > Association et reçus fiscaux*) :

- l'éligibilité au mécénat **confirmée** (elle vaut « non vérifiée » par défaut) ;
- le nom officiel, le SIREN ou le RNA, l'adresse complète, l'objet, la catégorie fiscale (avec une précision pour « Autres ») ;
- le nom et la fonction du signataire, et l'image de la **signature du représentant habilité** ;
- la case « J'ai vérifié le spécimen » pour le **Cerfa en vigueur** (16216\*03). Après avoir généré le spécimen filigrané « SPÉCIMEN — NON VALABLE », on le compare rubrique par rubrique au formulaire officiel. Si le Cerfa change un jour, la case doit être cochée de nouveau.

L'entreprise doit aussi être complète : dénomination, forme juridique, SIREN valide et adresse complète.
Le rescrit mécénat se saisit avec sa référence, sa date, les pièces justificatives et les éléments qui justifient l'éligibilité.

1. Le statut fiscal vaut *non vérifié* au départ, et les reçus fiscaux sont alors bloqués.
2. Pour passer à *confirmé*, il faut :
   - le SIREN ou le RNA de l'association ;
   - la référence du rescrit fiscal ou des éléments qui justifient l'éligibilité ;
   - une confirmation explicite.
   L'écran précise que ce choix **n'est pas une validation par l'administration fiscale**. Le statut « association loi 1901 » ne suffit jamais à lui seul.
3. Un reçu fiscal exige le SIREN de l'entreprise.
4. Aucun pourcentage de réduction d'impôt n'est affiché.
5. Un justificatif regroupe uniquement des dons d'**une même entreprise** et d'**une même année**. Si l'exercice de l'entreprise ne suit pas l'année civile, il faut faire un reçu par exercice.
6. Les libellés des catégories sont dans `src/ICA.Domain/Dons.cs` (`CategoriesFiscales`). Les plus longs sont abrégés (…), le texte complet figure sur le formulaire officiel. Les anciens choix de catégorie sont repris automatiquement.

## Numéros uniques et documents figés

- **Émission en deux temps.**
  1. L'appli réserve un numéro (statut *Réservé*, aucun PDF).
  2. Le PDF est créé, le contenu est **figé** et le document passe en *Émis*.
- **Avec le compte Google relié :**
  1. L'appli synchronise.
  2. Elle réserve le numéro, le publie sur le Drive, puis relit.
  3. Si un autre ordinateur a réservé le même numéro au même moment, la fusion annule la même réservation sur tous les ordinateurs (celle dont l'identifiant est le plus grand), et l'appli recommence.

  Sans Internet, l'appli **refuse** de numéroter plutôt que de risquer un doublon.
- **Un numéro n'est jamais réutilisé**, même après une annulation ou une réservation abandonnée.
- **Correction :** on modifie le don, puis on clique « Remplacer » sur le justificatif. Le nouveau document porte « Annule et remplace le n° … » et l'ancien passe en *Remplacé*. Un document émis n'est jamais modifié.
- **Copies conservées :**
  - `%LOCALAPPDATA%\L-ICA\justificatifs\AAAA\<numéro>.pdf`, avec une empreinte SHA-256 enregistrée ;
  - le dossier Google Drive « L'ICA Gestion - Justificatifs (ne pas modifier) » ;
  - les sauvegardes `.zip`, qui incluent les PDF. Une restauration ne remplace jamais un PDF existant.
- **Traçabilité :** chaque création, correction, annulation, émission et envoi est daté, avec le nom de l'ordinateur (bouton *Historique*).

## Données (synchronisées comme le reste)

| Liste | Contenu |
|---|---|
| `Entreprises` | `DonateurEntreprise` |
| `Dons` | `Don` (type, montant ou valeur déclarée, mode, description, quantités, salariés, pièces, statut) |
| `Justificatifs` | `JustificatifDon` (numéro, type, dons couverts, statut, contenu figé, empreinte, id Drive, envoi, remplacement) |
| `HistoriqueDons` | `HistoriqueDon` |
| `Association` | `ParametresAssociation`, une seule fiche avec un identifiant fixe |

Aucune donnée d'entreprise ni aucun PDF ne va dans GitHub. Les tests utilisent des données fictives.
