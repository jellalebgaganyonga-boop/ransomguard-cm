# ADR-027 — L'enrôlement devient un installeur à double-clic

**Statut** : plan proposé, 2026-10-21, à lire avant tout code (décision du propriétaire). Rang : juste après AGT-PATH-001, les deux touchent le chemin d'installation.
**Décision du propriétaire** : la console garde le code à usage unique (30 min) mais remplace la commande PowerShell par un bouton « Télécharger l'installeur » ; l'installeur est auto-contenu (paquet de l'agent, code d'enrôlement, adresse du GRID) ; au double-clic, SmartScreen « éditeur inconnu » → « Exécuter quand même », UAC « Oui », l'agent s'installe, s'enrôle, le poste passe vert ; aucune frappe.

## 1. Ce qui existe et ce qui change

| Aujourd'hui | Demain |
|---|---|
| La console affiche un code (30 min) et une one-liner PowerShell qui télécharge `install.ps1`, vérifie son SHA-256, le lance avec le code et l'adresse du GRID. | La console affiche le même code et un bouton **« Télécharger l'installeur »** qui donne `RansomGuard-CM-Installeur-<hôte du GRID>.exe` (≈ 85 Mo). |
| `install.ps1` télécharge le paquet depuis le GRID (certificat à faire confiance sur le réseau local, AGT-PKI-001) et écrit `appsettings.json`. | L'installeur **embarque** le paquet, le code et l'adresse : aucun téléchargement, aucun certificat à accepter pendant l'installation. Il écrit le même `appsettings.json` (dossiers surveillés `%EACHUSERPROFILE%`, AGT-PATH-001). |
| Le certificat de l'autorité du GRID doit être installé à la main sur le poste pour que l'agent s'enrôle (AGT-PKI-001). | L'installeur embarque le certificat **public** de l'autorité du GRID et l'ajoute au magasin « Autorités de certification racines de confiance » de la machine avant de démarrer l'agent. Rien d'autre n'est installé dans un magasin. |
| `install.ps1` reste disponible pour un déploiement scripté (GPO, SCCM) ; il n'est plus montré dans la console. | Renommé dans la doc « installation scriptée », pas supprimé : ses tests Pester restent. |

## 2. Décisions

**2a. L'installeur est un exécutable .NET auto-contenu (un seul fichier), `agent/src/RansomGuard.Agent.Installer`.** Pas de PowerShell au double-clic (ExecutionPolicy, console noire), pas de MSI (Sprint 11, AGT-INST-001). Le projet est une application console `net8.0-windows`, publiée `-r win-x64 --self-contained -p:PublishSingleFile=true`, avec un manifeste `requireAdministrator` : Windows affiche l'UAC au lancement, l'utilisateur répond « Oui », rien à retaper.

**2b. Le GRID fabrique l'exécutable par ajout d'une charge utile à un stub.** La CD publie deux fichiers : `RansomGuard-Agent-win-x64.zip` (existant) et `RansomGuard-Installer-stub.exe` (nouveau). Au clic, le GRID écrit : le stub, puis la charge utile, puis une **remorque** de 56 octets (`RGINST01` sur 8 octets, longueur du zip sur 8, longueur de la configuration sur 8, SHA-256 de la charge sur 32 ; 48 annoncés au plan, 56 à l'écriture). La configuration est un JSON : `grid_base_url`, `otp`, `otp_expires_at`, `tenant_code`, `agent_package_sha256`, `grid_ca_pem`. Un hôte .NET à fichier unique lit son lot par un décalage inscrit dans l'exécutable au `publish` : des octets ajoutés en fin de fichier ne le gênent pas ; **un test de la CI le prouve** (stub + charge → démarre et relit sa charge) avant tout le reste.

**2c. Ce que fait le stub, dans l'ordre, et ce qu'il refuse.**
1. Lit sa propre charge (`Environment.ProcessPath`), vérifie la remorque et le SHA-256 ; une charge absente ou altérée : message « installeur incomplet, retéléchargez-le », sortie 2, rien d'écrit.
2. Refuse un code expiré (`otp_expires_at` passé) avant de toucher au poste : « code expiré, générez-en un nouveau dans la console », sortie 3.
3. Mêmes garde-fous qu'`install.ps1` : dossier `C:\Program Files\RansomGuard-CM` (AGT-INST-001, jamais ailleurs), service existant arrêté et supprimé (le service **RansomGuardAgent** de production ; l'ancien service OPS-001 de cette machine n'est pas concerné : l'installeur ne tourne pas ici), extraction du zip, `appsettings.json` écrit (BaseUrl, EnrollmentOtp, Hostname, Data Source, LogFilePath ; rien d'autre n'est modifié : `%EACHUSERPROFILE%` reste), dossiers de données fermés à SYSTEM et Administrators (AGT-KEY-001).
4. Ajoute `grid_ca_pem` au magasin racine machine (`X509Store(StoreName.Root, StoreLocation.LocalMachine)`), **seulement** si l'empreinte n'y est pas déjà ; journalise l'empreinte à l'écran et dans `C:\ProgramData\RansomGuard-CM\logs\install.log`.
5. Crée et démarre le service (LocalSystem, démarrage automatique, redémarrage sur échec), attend `Running`, puis attend l'enrôlement : relit `agent-state` (ou le log) jusqu'à « enrolled » pendant 2 min ; affiche « Poste enrôlé, il apparaît dans la console sous une minute » ou la cause de l'échec (adresse injoignable, code refusé), sortie 0 ou 4.
6. Fenêtre console maintenue ouverte : « Appuyez sur une touche pour fermer ».

**2d. Côté GRID : `GET /api/v1/dashboard/agents/installer/{otp_id}`**, rôle `tenant_admin`, le code appartenant au tenant de l'appelant et non expiré, sinon 404. Réponse en flux (`Content-Disposition: attachment`), taille ≈ taille du stub + du zip + 4 Ko. Pas de cache. Journal `UserActionLog` « installer_downloaded » (sans le code). Le code reste à usage unique : le fichier téléchargé vaut exactement la one-liner d'aujourd'hui, 30 min, un poste.

**2e. Côté console** : dans « Ajouter un agent », le code et son compte à rebours restent ; la one-liner et le SHA-256 disparaissent au profit du bouton et de trois lignes : *1. Copiez l'installeur sur le poste. 2. Double-cliquez. Windows affiche « Éditeur inconnu » : « Informations complémentaires » puis « Exécuter quand même », puis « Oui » à la demande d'administrateur. 3. Le poste apparaît vert ici sous deux minutes.* Un lien discret « installation scriptée » renvoie à la doc d'`install.ps1`.

**2f. Non signé, dit tel quel.** L'exécutable n'est pas signé Authenticode : SmartScreen affiche « Windows a protégé votre ordinateur / Éditeur inconnu ». La doc le montre, capture à l'appui, et explique le geste. La signature (certificat EV, horodatage) est une dette : **AGT-INST-002**, à traiter avec l'installeur MSI (AGT-INST-001). La confiance repose sur la session de la console et sur le réseau local, pas sur la signature.

## 3. Test d'acceptation, avant la fusion

Un job `installer` dans `agent-full`, sur un runner Windows propre :
1. le GRID démarre sur le runner (`uvicorn`, SQLite, certificat auto-signé de test, magasin de session Redis d'un binaire Windows ou le magasin mémoire de test, à vérifier au point 1 du plan) ; un tenant et un administrateur sont créés par le script de seed ;
2. le script se connecte comme administrateur, crée un code (API `provision`), télécharge l'installeur ;
3. il **lance l'exécutable** (c'est le double-clic ; l'UAC ne s'affiche pas, le runner est déjà administrateur ; SmartScreen ne s'applique pas à un fichier sans marque d'Internet), attend sa sortie ;
4. il interroge `GET /api/v1/dashboard/agents` jusqu'à ce que le poste soit `protected` (vert) en 3 min au plus, ou échoue ;
5. il vérifie que le certificat de l'autorité de test est dans le magasin racine, que les canaris sont dans le Bureau et les Documents de l'utilisateur du runner (AGT-PATH-001), et qu'aucune commande n'a été tapée (le script n'a lancé que l'exécutable).

Sur le banc réel (J4), le même geste est filmé : double-clic, SmartScreen, UAC, poste vert.

## 4. Découpage et estimation

| N° | Contenu | Diff lu avant commit |
|---|---|---|
| 1 | Stub : projet, remorque, lecture de la charge, test « bytes ajoutés » en CI ; CD publie le stub | 🔎 |
| 2 | GRID : fabrication et route de téléchargement, tests (tenant, expiration, 404, taille, journal) | 🔎 |
| 3 | Stub : installation (garde-fous, appsettings, ACL, magasin racine, service, attente d'enrôlement, codes de sortie) | 🔎 |
| 4 | Console : bouton, trois lignes, lien « installation scriptée », E2E | — |
| 5 | Job `installer` d'agent-full (3), docs (README, SmartScreen avec capture, dettes AGT-INST-002, AGT-PKI-001 mis à jour) | — |

Estimation : 1,5 jour. Il entre dans le plan de finition en prenant sur J3 § 3.3 (identité de l'agent), que je propose de réduire au seul refus de démarrage de la GRID en mode `agent_id` hors développement.

## 5. Points à trancher à la lecture

- Le magasin racine : l'installeur y ajoute le certificat de l'autorité du GRID (nécessaire pour que l'agent fasse confiance au GRID sur le réseau local). C'est l'écriture la plus sensible du plan ; elle est journalisée et limitée à ce certificat. D'accord ?
- Le nom du fichier porte l'hôte du GRID, pas le code : un fichier oublié sur un partage ne révèle le code qu'à qui l'ouvre, et le code expire en 30 min.
- La fenêtre console comme interface (pas de fenêtre graphique) : suffisante pour le jury, deux lignes de résultat et un code de sortie ; à confirmer.
