# Inventaire de la console — état au 2026-10-14

Ce document sert à cadrer le lot « Console » après la clôture du Sprint 8. Il compare, écran par écran et story par story, la console et la GRID au PRD du Sprint 7 v2 (`docs/sprint7/SPRINT_7_PRD_v2.md`). Chaque constat donne sa preuve en fichier:ligne.

**Base.** Branche `review/console-inventory`, construite sur R8 (`36eed70`), qui est encore en revue. Les chemins sont relatifs à la racine du dépôt :
- **FE** = `dashboard/src/` ;
- **BE** = `grid/src/ransomguard_grid/`.

**Méthode.** Chaque statut a été établi en lisant le code, jamais les commentaires ni les changelogs : plusieurs en-têtes de fichiers annoncent « AC… fait » pour un critère qui ne l'est pas. Les constats de sécurité et les plus graves ont été revérifiés un par un.

**Rien n'a été corrigé pendant l'inventaire**, sauf une régression d'accessibilité que R8 avait introduite (`36eed70`). Les défauts de sécurité de la section 1 sont **ouverts et en attente de décision**.

**Statuts.**
- **Fait** : tous les critères sont tenus.
- **Partiel** : au moins un critère n'est pas tenu.
- **Absent** : rien n'est implémenté.
- *(adapté)* : le critère est tenu, mais sur un contrat différent du PRD. Le PRD prévoyait par exemple les statuts new/acknowledged/closed ; la GRID utilise New/Investigating/Resolved/FalsePositive/Suppressed (`BE/db/models/enums.py:32-39`).

---

## Synthèse

| Domaine | État |
|---|---|
| Stories du PRD (21) | **0 faite, 20 partielles, 1 absente ou presque** (US4.3 : la commande d'isolement n'atteint jamais le poste). Aucune story ne tient tous ses critères. |
| Sécurité | **5 défauts ouverts**, dont 4 trouvés pendant cet inventaire (section 1). GRID-SEC-002 reste ouvert. |
| MFA | **Absente** côté GRID, côté console et en base. Elle avait été sortie du périmètre du PRD v2, mais quatre documents la décrivent comme en place. |
| Enrôlement | La règle « rien n'est tapé par l'hôpital » n'est pas tenue, et l'enrôlement échoue environ une fois sur deux en production (GRID-ENR-001). |
| Dettes du Sprint 7 | GRID-SEC-001 corrigée. GRID-SEC-002, GAP-01, GAP-03 et DEP-001 ouvertes. GAP-08 partielle. |
| Journal des actions utilisateur | Vrai backend, 9 points d'écriture. Ni les changements de rôle ni les échecs de connexion ne sont journalisés, et l'adresse IP ne l'est jamais. |
| Contenu affiché sans source | Trois affirmations de conformité et un compteur d'audit sont **codés en dur**. |
| Langues | Une clé manquante et 8 textes codés en dur ; aucune clé n'existe dans une seule langue. |
| Accessibilité | 15 scans axe, en chromium seulement dans la CI. Trois écrans et toutes les modales ne sont jamais scannés. Le tableau de bord opérationnel échoue en `mobile-small`. |

---

## 1. Défauts de sécurité ouverts — décision attendue

Ils ont été trouvés pendant l'inventaire. Conformément à la consigne, **rien n'est corrigé**. Les identifiants sont des propositions, sauf GRID-ENR-001, demandé par le cadrage.

### GRID-SEC-003 — La GRID fabrique la clé de signature d'audit de l'agent

**Constat.**
- À l'enrôlement, la GRID génère une paire Ed25519 (`BE/api/v1/routes/enrollment.py:64-66`).
- Elle conserve la clé publique (`:75`) et renvoie la **clé privée en clair** dans la réponse (`:86`).
- L'agent l'ignore : il signe avec sa propre clé, générée localement (`agent/src/RansomGuard.Agent.Core/Security/Cryptography/AuditLogSigner.cs:47`).
- L'envoi du journal à la GRID existe (`GridApiClient.cs:117`) mais n'est appelé nulle part.

**Risque.** Le défaut est latent aujourd'hui. Le jour où la GRID ancre la chaîne :
- en l'état, la GRID vérifie avec la clé publique de **sa** paire (`BE/api/v1/routes/audit_log.py:26-75`), donc elle rejetterait toutes les entrées ;
- si l'on « répare » en faisant signer l'agent avec la clé reçue, l'éditeur du SaaS pourrait forger le journal de chaque poste de chaque hôpital.

Entre-temps, une clé privée circule sur le réseau sans servir à rien.

**Correction proposée.** L'agent envoie à l'enrôlement la clé publique de sa propre `audit.key`, et la GRID la stocke. La GRID ne génère plus aucune clé.

### GRID-SEC-004 — Une session déconnectée reste utilisable

**Constat.**
- La liste de révocation des jetons est un `set` Python, propre à chaque processus (`BE/core/jwt_service.py:71-82`). Elle est vidée à chaque redémarrage.
- La production lance `uvicorn --workers 2` (`deployment/grid/Dockerfile:34`).
- Après une déconnexion, le jeton d'accès n'est refusé que par le worker qui l'a traitée. L'autre l'accepte jusqu'à son expiration, soit 60 min (`BE/core/config.py:38`).

**Correction proposée.** La liste de révocation passe dans Redis, désormais dépendance obligatoire.

### GRID-SEC-005 — Un refresh token volé reste valable 7 jours, même après déconnexion

**Constat.**
- `/auth/refresh` ne consulte jamais la liste de révocation (`BE/api/v1/routes/auth.py:112-162`).
- La rotation émet un nouveau refresh token, mais **l'ancien reste valable** (`auth.py:153-156`).
- La déconnexion ne révoque que le jeton d'accès (`auth.py:174-175`) ; le cookie est effacé côté navigateur seulement.
- Le refresh token accepté dans le corps de la requête reste ouvert (`auth.py:127`).
- La « déconnexion de toutes les sessions » (US1.2.2) n'existe pas. Pourtant `docs/sprint7/05_stride_csp_privacy_data.md:123` affirme « refresh token revoked on logout-all ».
- Le serveur ne vérifie jamais l'en-tête `X-Requested-With` envoyé par la console (`FE/api/client.ts:40,51`).

**Correction proposée.** Identifiants des refresh tokens dans Redis. L'ancien est invalidé à chaque rotation, la déconnexion révoque le refresh token, et le chemin par le corps de requête est fermé.

### GRID-SEC-006 — La commande d'installation de la console désactive TLS et écrit dans `C:\`

**Constat.**
- La console fait télécharger le script d'installation avec `curl.exe -k`, donc sans validation TLS (`FE/pages/agents/agents-list-page.tsx:330`).
- Elle le fait ensuite exécuter avec `powershell -EP Bypass`, en administrateur (`:333`).
- Le script est écrit dans `C:\inst.ps1`. La racine `C:\` laisse tout utilisateur authentifié y créer et modifier des fichiers.

**Risque.**
- Quiconque se trouve sur le chemin réseau peut substituer le script et exécuter du code en administrateur sur chaque poste enrôlé.
- Un autre compte local peut remplacer le fichier entre le téléchargement et l'exécution.

C'est la même classe de défaut qu'AGT-TLS-001 (`356f2c5`), mais un cran plus tôt : `install.ps1` valide désormais TLS pour le paquet, alors que le script lui-même arrive toujours sans validation.

**Correction proposée.**
- Téléchargement validé : la CA du GRID est reconnue par le poste, voir AGT-PKI-001.
- Le fichier est écrit dans un dossier propre à l'administrateur, pas dans `C:\`.
- Empreinte du script affichée par la console et vérifiée avant l'exécution (rattaché à AGT-INST-001).

### GRID-SEC-002 — Le backend ne refuse pas qu'un administrateur se désactive lui-même (dette du Sprint 7, toujours ouverte)

**Constat.**
- `disable_user` ne fait aucun contrôle `user_id == admin.id` (`BE/api/v1/routes/dashboard.py:448-468`), alors que `enable_user` en fait un (`:480-481`).
- Aucune protection du dernier administrateur.
- Seule la console protège (`FE/lib/user-permissions.ts:193-196`).

**Risque.** Un tenant peut se retrouver sans administrateur.

### Autres défauts constatés (intégrité, disponibilité)

| Proposition | Constat | Preuve |
|---|---|---|
| GRID-ENR-001 | Les OTP d'enrôlement vivent dans un dictionnaire en mémoire. Ils sont perdus à chaque redémarrage, et avec 2 workers un OTP créé sur l'un est inconnu de l'autre : l'enrôlement échoue environ une fois sur deux. | `BE/services/enrollment_otp_service.py:26,65` ; `deployment/grid/Dockerfile:34` |
| GRID-USR-001 | Aucune contrainte d'unicité sur `users.email`, et aucun contrôle à la création. Un deuxième compte au même e-mail fait échouer la connexion des deux comptes (`scalar_one_or_none` lève une exception, la connexion répond 500). | `BE/db/models/tenant_user.py:37` ; `dashboard.py:414-445` ; `BE/db/repositories/user_repository.py:23` |
| GRID-AUD-001 | Les changements de rôle ne sont pas journalisés : une élévation de privilège ne laisse aucune trace. | `dashboard.py:532-569` (aucun appel à `_log_user_action`) |
| GRID-ALR-001 | La GRID accepte n'importe quel changement de statut d'alerte (Résolue → Nouvelle, par exemple) ; il n'y a jamais de 409. | `dashboard.py:218-219` |

**Vérifié et écarté.** La force brute sur l'OTP d'enrôlement n'est pas un défaut :
- l'OTP n'a qu'environ 31 bits aléatoires derrière un préfixe prévisible (`RG-CAM-2026-`) ;
- mais nginx limite `/agents/enroll` à 10 requêtes par minute et par IP (`deployment/grid/nginx/conf.d/agent.conf:58-59`), et seul nginx publie des ports.

---

## 2. Les points demandés

| Point | Réponse | Preuve |
|---|---|---|
| **MFA** | **Absente** côté GRID, côté console et en base (aucune migration). Elle avait été sortie du périmètre (PRD v2, lignes 22 et 2048). En revanche, quatre documents la décrivent comme en place : la référence API (« Bearer JWT + MFA », flux en 2 étapes avec `/auth/mfa/verify`), l'OpenAPI, la stratégie de base (table `mfa_devices`) et un SLO « 100 % des actions admin avec MFA ». | `docs/api/ransomguard_api_reference.md:17-36` ; `docs/api/ransomguard_openapi.yaml:14-15,737-749` ; `docs/architecture/ransomguard_db_strategy.md:17,288` ; `docs/architecture/ransomguard_slo_sla.md:225-227` |
| **Enrôlement** | Écart avec « rien n'est tapé par l'hôpital ». **(1)** La GRID et la console demandent de copier l'OTP dans `appsettings.json`. **(2)** La commande d'installation existe, mais l'informaticien la recopie à la main, en deux étapes, sur chaque poste, en administrateur. **(3)** L'enrôlement lui-même est automatique : `install.ps1` écrit l'OTP et l'adresse du GRID. Ce qui manque pour un enrôlement piloté depuis la console : un paquet signé avec un jeton de durée courte, sans copier-coller ; un téléchargement validé (GRID-SEC-006, AGT-PKI-001) ; le suivi dans la console de « OTP émis → poste enrôlé ». | `BE/api/v1/routes/dashboard.py:780` ; `FE/i18n/locales/fr.json` (`agents.provision.subtitle`) ; `FE/pages/agents/agents-list-page.tsx:330-333` ; `agent/install.ps1:144-156` |
| **OTP d'enrôlement** | **Perdus au redémarrage : oui. Refusés par l'autre worker : oui.** C'est **GRID-ENR-001**. | Section 1 |
| **Authentification des agents** (constatée en vérifiant l'enrôlement) | L'enrôlement ne délivre aucun vrai certificat X.509 : le numéro de série et l'empreinte sont des UUID aléatoires. Le mode `mtls` est donc inatteignable pour un agent enrôlé, et le déploiement force `agent_id` : quiconque connaît l'UUID d'un agent parle en son nom. C'est déjà documenté comme acceptable seulement sur le tailnet. | `enrollment.py:68-76` ; `deployment/grid/bootstrap.sh:79` ; `BE/core/config.py` (`agent_auth_mode`) |
| **Journal des actions utilisateur** | **Vrai backend** : table `user_action_logs`, route `GET /dashboard/user-actions` (admin et auditeur, filtres type, auteur et dates), page `/audit/user-actions`. Elle affiche des données réelles et n'est vide que sur un tenant neuf. Détail en section 4.6. | `grid/alembic/versions/3a8f5c201e47_*.py:36-46` ; `dashboard.py:659-705` ; `FE/pages/audit/user-actions-page.tsx` |
| **Langues et accessibilité** | Section 5. | |

### Dettes du Sprint 7 — statut réel

| ID | Statut | Preuve |
|---|---|---|
| GRID-SEC-001 (refresh token dans le corps JSON) | **Corrigée.** Cookie HttpOnly, Secure, SameSite=Strict, path `/api/v1/auth`, renouvelé à chaque refresh ; le corps renvoie `""`. **Restes :** le chemin par le corps est toujours accepté, et la révocation est absente (GRID-SEC-005). `dashboard/README.md:66,103,188` et `SPRINT_7_REPORT.md:98` la disent encore ouverte : ils sont périmés. | `BE/api/v1/routes/auth.py:30-44,103,106,127,153-156` ; `FE/stores/auth.store.ts:44-68` |
| GRID-SEC-002 (se désactiver soi-même) | **Ouverte.** | Section 1 |
| GAP-01 (`enrolled_at` absent) | **Ouverte.** Le champ existe dans le modèle, mais ni `AgentItem` ni `AgentDetailItem` ne l'exposent. | `BE/db/models/agent.py:26` ; `BE/api/v1/schemas/dashboard.py:42-56,86-87` |
| GAP-03 (chronologie des heartbeats) | **Ouverte.** Aucune route ne l'expose. La table par battement a été supprimée (R7). Les transitions de modules (`agent_posture_changes`) existent mais ne sont pas exposées. Les onglets restent inertes. | `BE/db/models/agent.py` (`AgentPostureChange`) ; `FE/pages/agents/agent-detail-page.tsx:202-208` |
| GAP-08 (`GET /users` sans rôles) | **Partielle.** `GET /dashboard/users-with-roles` existe, avec une requête de rôles par utilisateur, mais la console ne l'utilise pas : pas de colonne Rôles, et l'éditeur de rôles s'ouvre décoché. | `dashboard.py:744-766` ; `FE/components/users/role-editor-modal.tsx:185-186` |
| DEP-001 (vite/esbuild) | **Ouverte, risque accepté.** vite 5.4.21 embarque esbuild 0.21.5, toujours concerné par GHSA-67mh-4wv8-2f99. Le risque est inscrit au registre (réexamen le 2026-11-07). | `dashboard/package.json:66,79` ; `dashboard/package-lock.json:11465,11959` ; `docs/security/dependency-risk-register.md:71-100` |

---

## 3. Contenu affiché sans source

Ce sont des affirmations, en partie réglementaires, affichées à des décideurs sans aucune donnée derrière. C'est le même problème qu'un module « actif » qui ne surveille rien.

| Écran | Affiché | Preuve |
|---|---|---|
| Accueil lecture seule (auditeur) | « CONFORMITÉ ACTUELLE : 98% » | `FE/pages/dashboard/read-only-dashboard.tsx:40` |
| Accueil lecture seule | « Loi 2024/017 : Conforme » | `read-only-dashboard.tsx:43` |
| Accueil lecture seule | « 487 » entrées d'audit sur 7 jours | `read-only-dashboard.tsx:61` |
| Accueil direction | « ✅ Conforme » et date du prochain audit | `FE/pages/dashboard/executive-dashboard.tsx:233-240` |
| Fiche agent | Statut de la dernière commande `Pending`, fabriqué dans le navigateur et perdu au rechargement | `FE/pages/agents/agent-detail-page.tsx:167-178,323-331` |
| Fiche agent | Onglets « Heartbeats » et « Configuration » marqués « Disponible à partir de Sprint 8 » | `agent-detail-page.tsx:202-208` ; `read-only-dashboard.tsx:115` |

---

## 4. Écran par écran, story par story

Pour chaque story, le statut et les écarts. Les critères tenus ne sont pas répétés.

### 4.1 Phase 0 (backend)

| Élément | Statut | Écarts |
|---|---|---|
| `GET /dashboard/me` | Partiel | Contrat différent : `user_id`, `tenant` imbriqué, pas de `created_at`, avec `preferences` en plus. Un compte désactivé reçoit 401 au lieu de 403 : le 403 de `dashboard.py:85-86` est inatteignable. 12 tests. |
| `POST /dashboard/users/{id}/enable` | Partiel | Pas de 409 : l'appel est idempotent et répond 200. L'action est journalisée `user_enable`, pas `user_enabled`. Les tests de cloisonnement entre tenants sont dans `test_user_enable.py`, pas dans `test_tenant_isolation.py`. |

### 4.2 Authentification et session (Epic 1)

| Story | Statut | Écarts |
|---|---|---|
| US1.1 Connexion | Partiel | Limite de débit à **30/min** (rafale 5), et non 5 par 15 min (`deployment/grid/nginx/nginx.conf:38`). Temps de réponse non uniforme : attente de 0,1 s pour un compte inconnu, aucune pour un mauvais mot de passe (`auth.py:68-79`). La préférence de langue n'est pas mémorisée (`FE/i18n/index.ts:37-42`) et le sélecteur est en pied de carte. Aucun test pour un compte désactivé. |
| US1.2 Déconnexion | Partiel | « Déconnecter tous les appareils » est absent. La révocation est inefficace sur 2 workers (GRID-SEC-004). Hors ligne, le cookie HttpOnly survit et le prochain démarrage restaure la session (`FE/hooks/use-auth.ts:64-77`). La mise en veille à 30 min est faite et testée en E2E. |
| US1.3 Rafraîchissement | Partiel | L'ancien refresh token n'est jamais invalidé (GRID-SEC-005). L'intercepteur n'a aucun test unitaire. |

### 4.3 Accueils par rôle (Epic 2)

| Story | Statut | Écarts |
|---|---|---|
| US2.1 Routage par rôle | Partiel | La destination après connexion est gardée dans l'état du routeur, pas dans un paramètre de requête : elle est perdue au rechargement. Le `console.error` du rôle inconnu est inatteignable. |
| US2.2 Accueil direction | Partiel | Cibles tactiles de 32 et 40 px, sous les 44 px exigés (`FE/components/ui/button.tsx:42-45`). La carte de conformité est codée en dur (section 3). Les E2E simulent `/dashboard/metrics` alors que la console appelle `/metrics/summary` (`dashboard/e2e/fixtures/index.ts:259`) : les métriques ne sont jamais simulées. |
| US2.3 Accueil opérationnel | Partiel | Pas de tri par sévérité (la GRID trie par date seulement). Le nom du poste n'est pas affiché. Le « top 10 » des agents est arbitraire (pas de `order_by`). « Hors ligne » vaut total − actifs, donc compte aussi les postes provisionnés et décommissionnés. La fenêtre « Dernière heure » est figée au montage (`operational-dashboard.tsx:95-102`). Le bandeau « sans signal » ne regarde que 10 agents. |
| US2.4 Accueil lecture seule | Partiel | Le compteur d'audit et la conformité sont codés en dur (section 3). Une route interdite redirige vers `/dashboard` au lieu d'afficher un 403 ; `ForbiddenPage` n'est importée nulle part. Le E2E teste la redirection de l'analyste, pas celle de l'auditeur. |

### 4.4 Alertes (Epic 3)

| Story | Statut | Écarts |
|---|---|---|
| US3.1 Liste | Partiel | **Le tri ne fait rien** : les en-têtes écrivent `sort_by` dans l'URL, `fetchAlertList` les ignore (`FE/api/alerts.ts:87-88`), et la GRID n'a pas de paramètre de tri. **La recherche par nom de poste ne marche pas** : le texte est envoyé comme `agent_id` exact. Pas de filtre par module (ni console ni GRID). La plage de dates n'existe que dans l'URL, sans contrôle à l'écran. Pas d'action « Clôturer » sur la ligne. |
| US3.2 Détail | Partiel | Pièces jointes, historique des statuts, niveau de confiance et charge brute : **la GRID ne les renvoie pas**. La console a les sections, toujours vides. Les noms de champs de l'historique ne correspondent pas au modèle. Pas d'horodatage absolu. Toute erreur, 500 compris, s'affiche « non trouvée ». |
| US3.3 Acquitter | Partiel | Pas de 409 (GRID-ALR-001). **Aucun système de notification (toast)** dans la console : en cas d'échec, l'utilisateur ne voit rien (`FE/components/alerts/acknowledge-modal.tsx:48`). Une note de 1 à 4 caractères est remplacée en silence (minimum 5 côté GRID). |
| US3.4 Clôturer | Partiel | Pas de 409. Les notes de résolution ne sont jamais affichées (pas d'historique). **Bogue** : le préfixe de catégorie (en français, en dur) plus 2 000 caractères de notes dépassent la limite de 2 000 de la GRID, qui répond 422 (`FE/components/alerts/close-modal.tsx`). |

### 4.5 Agents (Epic 4)

| Story | Statut | Écarts |
|---|---|---|
| US4.1 Inventaire | Partiel | Pas de colonne « renseignement sur les menaces ». Pas de filtre par OS ni par âge du signal. Commande d'isolement réservée à l'administrateur, comme la GRID, alors que le PRD l'ouvre aussi à l'analyste. Ajouts R8 : colonne Protection (verdict de la GRID) et export CSV. |
| US4.2 Fiche | Partiel | Pas de date d'enrôlement (GAP-01). Onglets Heartbeats et Configuration inertes (GAP-03). L'onglet Alertes n'a ni pagination ni filtres. Ajout R8 : section posture (verdict, 6 modules, transmission, poste). |
| US4.3 Commande d'isolement | **Absent en pratique** | La console met la commande en file. **Elle n'atteint jamais le poste** : le heartbeat renvoie seulement des **identifiants** (`BE/api/v1/routes/heartbeat.py`, `pending_commands`). L'agent les journalise « deferred to Sprint 9 » sans les exécuter (`agent/src/RansomGuard.Agent.Service/GridCommandService.cs:44`). Rien ne passe jamais une commande à `Dispatched` ou `Completed` : elle reste `Pending` indéfiniment. Aucun code d'isolement dans l'agent. La console envoie `isolate`, le commentaire de l'agent parle de `isolate_endpoint`. La route répond 200, pas 202. Pas de restauration. |
| US4.4 Indicateurs | Partiel | Depuis R8, la couleur vient du verdict de la GRID, jamais d'un seuil de la console. Le bandeau « sans signal » ne couvre que 10 agents à l'accueil et la page courante dans la liste ; l'accueil direction n'en a pas. Le rafraîchissement toutes les 30 s, en pause onglet masqué, est fait. |

### 4.6 Utilisateurs (Epic 5) et journal des actions

| Story | Statut | Écarts |
|---|---|---|
| US5.1 Liste | Partiel | Pas de colonnes Rôles ni Création (GAP-08). Pas de tri. Page de 20, pas 50. Pas de notification « accès refusé ». Le bouton Désactiver est masqué sur sa propre ligne au lieu d'être désactivé avec une infobulle. |
| US5.2 Création | Partiel | **E-mail en double accepté** (GRID-USR-001). Mot de passe de 8 caractères minimum, contre 12 avec casse, chiffre et symbole exigés ; pas d'indicateur de force ; pas de champ de confirmation. Un rôle inconnu est ignoré en silence au lieu d'être refusé. |
| US5.3 Désactiver ou réactiver | Partiel | Pas de justification. Pas de protection côté GRID contre soi-même ni pour le dernier administrateur (GRID-SEC-002). Actions journalisées `user_disable` et `user_enable`. Les sessions sont bien coupées : la GRID refuse un compte inactif (`BE/api/v1/dependencies/jwt_auth.py:51`, `auth.py:142`). |
| US5.4 Rôles | Partiel | **Non journalisé** (GRID-AUD-001). Rôles actuels inconnus à l'ouverture (GAP-08). Une liste vide est acceptée. Se retirer soi-même `tenant_admin` donne 400, pas 403. Pas de protection du dernier administrateur. |

**Journal des actions utilisateur.** Points d'écriture :
- `login` (réussites seulement) ;
- `logout` ;
- `alert_status_change` ;
- `command_issued` ;
- `user_create` ;
- `user_disable` ;
- `user_enable` ;
- `agent_decommission` ;
- `agent_provision`.

Ils passent tous par `_log_user_action` (`dashboard.py:635-656`, `auth.py:93-99,180-186`).

**Non journalisés :**
- changements de rôle ;
- préférences de notification ;
- échecs de connexion ;
- rafraîchissements de jeton ;
- lectures et exports du journal d'audit.

**Autres écarts :**
- `ip_address` n'est jamais renseignée.
- La console n'appelle jamais `decommissionAgent` (`FE/api/agents.ts:170`, aucun appelant).
- La clé de cache de la page n'est pas propre au tenant, contrairement aux autres (`FE/hooks/use-user-actions.ts:4-10`).
- Aucun test, ni dans la GRID ni en E2E, ne couvre ce journal.

### 4.7 Journaux d'audit (Epic 6)

`/audit` affiche la **chaîne d'audit signée des agents** (agent, séquence, clé de signature). Ce n'est pas un journal d'activité des utilisateurs, qui est la page `/audit/user-actions`.

| Story | Statut | Écarts |
|---|---|---|
| US6.1 Recherche | Partiel | Pas de recherche libre : seulement un filtre exact par `agent_id`, sans paramètre `search` dans la GRID. Le refus de l'analyste est fait et testé. |
| US6.2 Filtres | Partiel | La plage de dates existe sur `/audit`. Le type d'action est un choix unique, sur la page des actions utilisateur seulement. Pas de filtre par auteur à l'écran (la GRID le supporte). Les filtres ne sont pas dans l'URL. |
| US6.3 Export CSV | Partiel | Export dans le navigateur, page courante seulement. Colonnes différentes du PRD, pas de BOM UTF-8. **Le CSV des actions utilisateur est mal formé** : les guillemets ne sont pas échappés, alors que la colonne de détails contient du JSON (`FE/pages/audit/user-actions-page.tsx:58`). L'export n'est pas journalisé. |

---

## 5. Langues et accessibilité

### 5.1 Textes non traduits

Comparaison par script des 281 clés utilisées dans les appels `t('…')` avec `fr.json` et `en.json` :

- **Clés absentes : 1**, dans les deux langues. `common.exportCsv` (`FE/pages/alerts/alerts-list-page.tsx:81`) : le bouton affiche le fallback « CSV ». La clé existe sous `audit.exportCsv`.
- **Clés présentes dans une seule langue : aucune.**
- **Textes et attributs codés en dur : 8.**

| Fichier:ligne | Texte | Problème |
|---|---|---|
| `FE/components/layout/protected-route.tsx:29` | `aria-label="Chargement"` | En français pour un lecteur d'écran anglophone |
| `FE/components/ui/dialog.tsx:55` | `Fermer` (texte pour lecteur d'écran) | Idem, sur toutes les modales |
| `FE/components/layout/header.tsx:58` | `aria-label="Menu"` | Neutre, mais hors i18n |
| `FE/pages/agents/agents-list-page.tsx:339` | `— Step 1` | En anglais dans l'interface française |
| `FE/pages/agents/agents-list-page.tsx:358` | `Step 2 —` | Idem |
| `FE/pages/agents/agents-list-page.tsx:348,370` | `aria-label="Copy download command"`, `"Copy install command"` | Idem |
| `FE/components/users/create-user-modal.tsx:93,111` | `analyst@hopital.cm`, `Jean Dupont` | Exemples de saisie, hors i18n |
| `FE/pages/dashboard/executive-dashboard.tsx:236` | `✅ Conforme` | En dur, et sans source (section 3) |

**Libellés périmés.** « Disponible à partir de Sprint 8 » est affiché pendant le Sprint 8 (`fr.json`, `common.comingInSprint8`). Le texte d'aide de l'ajout d'agent dit encore de copier l'OTP dans `appsettings.json` (section 2).

### 5.2 Scans d'accessibilité (axe)

**Scans existants : 15**, tous filtrés sur les impacts `critical` et `serious`, avec les règles par défaut d'axe (aucun `withTags`). Les impacts `moderate` et `minor` ne sont jamais signalés.
- Connexion : `dashboard/e2e/auth.spec.ts:54`.
- Trois accueils : `dashboard.spec.ts:33,44,55`.
- Liste et détail des alertes : `alerts.spec.ts:27,152`.
- Liste des agents et fiche agent : `agents.spec.ts:26,92`.
- Fiche agent avec les bandeaux rouge, gris et vert : `agents.spec.ts:131`.
- Liste des utilisateurs : `users.spec.ts:135`.
- Journal d'audit : `audit.spec.ts:73`.

**Jamais scannés :**
- les écrans `/audit/user-actions`, `/settings/notifications` et la page 404 ;
- **aucune modale ouverte** : acquittement, clôture, commande, création d'utilisateur, désactivation, rôles, panneau d'ajout d'agent.

**Projets.** La CI ne lance que chromium (`.github/workflows/ci.yml`, `--project=chromium`). Les projets `firefox`, `edge`, `mobile-chrome` et `mobile-small` (`dashboard/playwright.config.ts:41-60`) ne tournent jamais en CI.

**Résultats mesurés sur les projets mobiles pendant cet inventaire :**
- **Fiche agent : 8 échecs** `scrollable-region-focusable`, une régression de R8. **Corrigée** dans `36eed70`, désormais 18/18.
- **Accueil opérationnel en `mobile-small` : toujours en échec**, même règle, sur un conteneur `overflow-x-auto`. Antérieur à R8, non corrigé pendant l'inventaire.

**Non vérifié.** La conformité WCAG 2.2 AA au-delà de ce qu'axe teste par défaut. En particulier, la taille minimale des cibles (2.5.8) est sous les 44 px du PRD selon les classes Tailwind (`button.tsx:42-45`), sans mesure au rendu.

### 5.3 Exigences non fonctionnelles

La revue critère par critère de la section 14 du PRD n'a pas été menée à terme : l'analyse a été interrompue par une limite d'API. **Ce point reste à faire.**

Constats déjà établis :
- cibles tactiles (5.2) ;
- E2E en chromium seulement (5.2) ;
- mise en veille de session à 30 min, faite ;
- jetons : accès en mémoire, refresh en cookie HttpOnly, faits, mais sans révocation (GRID-SEC-004, GRID-SEC-005).

---

## 6. Pour cadrer le lot Console

Proposition d'ordre, du plus grave au plus visible.

1. **Sécurité** (section 1) :
   - GRID-SEC-006 (commande d'installation) ;
   - GRID-SEC-004 et GRID-SEC-005 (sessions dans Redis) ;
   - GRID-SEC-003 (clé d'audit propre à l'agent) ;
   - GRID-SEC-002 ;
   - GRID-USR-001 ;
   - GRID-AUD-001.

   Dans la foulée, GRID-ENR-001, qui a la même cause que GRID-SEC-004 : un état gardé en mémoire de processus sur 2 workers.
2. **Honnêteté de l'écran.** Supprimer ou alimenter les chiffres codés en dur (section 3). Masquer ou implémenter les onglets inertes.
3. **Ce qui est promis mais ne marche pas :**
   - tri des alertes ;
   - recherche par poste ;
   - historique et notes des alertes, côté GRID ;
   - système de notification (toast) ;
   - transitions d'alerte contrôlées (GRID-ALR-001) ;
   - CSV mal formé.
4. **Commande d'isolement de bout en bout.** Elle dépend du Response Engine (Sprint 9). D'ici là, l'écran ne devrait pas laisser croire qu'une commande est partie.
5. **Gestion des utilisateurs** : rôles visibles (GAP-08), politique de mot de passe, protection du dernier administrateur, justification.
6. **Enrôlement piloté depuis la console** (section 2).
7. **Accessibilité** :
   - scans des écrans et des modales manquants ;
   - projets mobiles en CI ;
   - accueil opérationnel en `mobile-small` ;
   - cibles tactiles.
8. **Documentation alignée** : la MFA décrite comme existante, `logout-all` dans la STRIDE de la console, les dettes du Sprint 7 périmées dans `dashboard/README.md`.
