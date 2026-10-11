# Agent (Windows Endpoint) 

C# .NET 8 service running on Windows 10/11 (and legacy Windows 7 SP1+) endpoints.

Monitors file system events via ETW, detects ransomware behavior, and reports to the management server via mTLS.

## Configuration

The configuration is bound and validated **once**, at startup, before anything consumes it
(`AgentConfigurationLoader`). An invalid configuration is refused with one line per setting,
naming the exact key, on the console (and in the Windows Event Log, best effort); the agent
exits with code 1. Paths (logs, database, keys) must be absolute once environment variables
are resolved; enum-valued settings must name a defined value.

The configuration is read from the **directory of the executable**, never from the working
directory (`AgentHost`): a Windows service starts in `C:\Windows\System32`. A missing
`appsettings.json` there is a refusal, naming the path it looked at — the agent never starts
on default values or on environment variables alone.

At every start, before anything opens a file there, the agent **closes its data directories**
(`AgentDataProtection`, AGT-KEY-001): the whole `%ProgramData%\RansomGuard-CM` tree (database,
keys, logs), plus any configured data directory outside it, gets a protected DACL granting
SYSTEM and Administrators only, inheritance cut, an allowed owner, no reparse point followed.
The result is verified; a tree it cannot close is a refusal to start (exit code 1), never a
protection left open. A configured data directory that is a drive root or a shared system
folder is refused. In `Development` only, the account running the agent keeps full control,
with a Warning at every start; the agent must otherwise run elevated or as LocalSystem.

- **`Agent:Server:BaseUrl` has no default** in the shipped `appsettings.json`: an agent
  without it refuses to start and names the missing setting. The installer
  (`install.ps1`) writes it on the installed machine.
- **In development** it comes from `appsettings.Development.json` (tracked by git), which
  points at the local Docker stack (`https://localhost`) and is the only file allowed to set
  `Agent:Server:TrustAnyCertificate` to `true`. Run the agent with
  `DOTNET_ENVIRONMENT=Development`; without it the host environment is Production.

CLI sub-commands (`--verify-audit-log`, `--baseline-reset`, `--baseline-export`,
`--apply-threat-intel-update`) load the same validated configuration, rooted at the
executable's directory, and open the **installed** database. Exit codes: `0` success, `1`
failure (integrity check failed, update failed, crash), `2` refused — nothing was done
(invalid configuration, database missing, database schema older than the verifier).

### Trusting the GRID certificate authority (manual, until the installer does it — AGT-PKI-001)

Since AGT-TLS-001 the agent and `install.ps1` validate the GRID's TLS certificate. A GRID
whose certificate is issued by the deployment's own PKI (`deployment/grid/pki/init-pki.sh`:
root `ca.crt`, intermediate, server) is only accepted once the workstation trusts that root.
On each workstation, before running `install.ps1`, as an administrator:

1. Copy **only the public certificate** `deployment/grid/pki/ca.crt` to the workstation —
   never `ca.key` nor any other key.
2. Check its fingerprint against the one the GRID administrator gives you through another
   channel (phone, signed e-mail):
   `Get-PfxCertificate .\ca.crt | Select-Object Thumbprint, Subject, NotAfter`
3. Import it into the machine store, which is the one the agent (LocalSystem) uses:
   `Import-Certificate -FilePath .\ca.crt -CertStoreLocation Cert:\LocalMachine\Root`
4. Check, from the workstation, that the GRID is now trusted without any bypass:
   `Invoke-WebRequest https://<grid-host>/api/v1/health -UseBasicParsing`
   An error here means the root is not trusted or the GRID's host name is not in its
   certificate; the agent would refuse it the same way.

A root in `LocalMachine\Root` is trusted for every TLS connection of the machine, not only
the GRID: whoever holds `ca.key` could impersonate any site to that workstation. Keep
`ca.key` offline. Whether the installer should rather pin this root for the agent alone is
part of AGT-PKI-001.

## Dette technique

| ID | Priorité | Sujet | Action attendue |
|---|---|---|---|
| AGT-DEP-001 | **Haute**, planifiée au Sprint 8, juste après la clôture du LOT 2 | `SixLabors.ImageSharp` 2.1.x : 5 avis de sécurité en deux semaines (GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w, GHSA-wmxv-xphr-5c9g, GHSA-gwg2-r3hj-4w44), corrigés seulement en 4.1.2, sous licence Six Labors Split. L'agent n'utilise qu'une seule fonction de la bibliothèque : encoder un JPEG. Les avis sont neutralisés un par un via `NuGetAuditSuppress` dans `RansomGuard.Agent.Core.csproj`, car aucun n'est atteignable. Tout nouvel avis reçoit la même analyse avant d'être ajouté (identifiant GHSA, composant touché, preuve fichier:ligne qu'il est inatteignable). Jamais de suppression globale ni par niveau de gravité. | Remplacer ImageSharp et PdfSharpCore par des bibliothèques sous licence MIT. Retirer la suppression dès qu'une version corrigée compatible existe ou que la dépendance a disparu. |
| AGT-BUS-001 | À traiter avant le Response Engine | `InMemoryDetectionEventBus` publie de façon synchrone : chaque abonné est attendu l'un après l'autre, sur le thread du publieur. Un abonné lent bloque donc le module qui publie. Aujourd'hui, `EntropyMonitor` est le seul publieur et `EncryptedExfilCorrelationRule` le seul abonné, dont le gestionnaire est synchrone : l'impact est faible. Le Response Engine s'abonnera à ce bus avec des traitements longs. | Revoir le modèle de diffusion avant le Response Engine : file bornée par abonné, politique de saturation explicite, compteur de pertes réellement alimenté. |
| AGT-ATTR-001 | **Bloquant du Response Engine** | Les signaux ENTROPY ne portent aucun processus : `EntropyMonitor.cs:271` publie `ProcessId = 0` (et `ProcessName` vide), alors que `EncryptedExfilCorrelationRule.cs:28` indexe les signaux par PID. Tous les signaux tombent sur la clé 0 : la corrélation ENTROPY → EXFIL par processus ne peut pas fonctionner, et le Response Engine ne saurait pas quel processus suspendre. C'est le problème d'attribution mesuré à 0 sur 120 par AttributionBench (Restart Manager), vu sous un autre angle ; voir aussi `ModuleStateRegistry.cs:103` (GENEALOGY `degraded`, `attribution_unreliable`). | Ne pas corriger isolément. Rattaché à la décision ETW du LOT 3 (`SPRINT_8_ARBITRAGE_LOT3.md` §B : attribution par ETW FileIo noyau, Restart Manager écarté). Le PID des signaux sera fourni par cette attribution, une fois mesurée. La mesure et la décision ETW sont consignées dans `agent/tools/AttributionBench/README.md` (banc refait en R2, `c57449c`) ; ses contrôles d'instrument manquants : AGT-BENCH-001. |
| AGT-USB-001 | **Haute**, lot USB | USB GUARD décide mais n'agit pas : lecture seule, quarantaine, éjection et `BlockAndEject` logiciel ne sont pas implémentés. Depuis le commit « la vérité dans USB GUARD », ils renvoient `Success = false` / `not_implemented`, l'alerte et le journal d'audit signé l'écrivent tel quel, et USB GUARD en Strict se déclare `degraded` / `actions_not_implemented`. Seule la coupure IRONCLAD agit. Détail : `docs/modules/usb-guard.md`. | Implémenter l'éjection (`CM_Request_Device_Eject`), la lecture seule et la quarantaine (le `QuarantineService` AES-256-GCM existe), avec leurs tests. Prérequis de Strict en production. |
| AGT-USB-002 | **Haute**, lot USB | Une whitelist illisible fait passer le périphérique sans analyse, dans tous les modes : `IsWhitelistedAsync` échoue au branchement, le `catch` général de `UsbDeviceMonitor.HandleConnectionAsync` journalise et s'arrête, avant le contrôle de clé amorçable et l'analyse. | L'échec de lecture ne court-circuite jamais l'analyse. En Strict, le périphérique est traité comme non approuvé (fermeture en cas d'échec). Une alerte est remontée. |
| AGT-USB-003 | **Haute**, lot USB | Aucun chemin pour inscrire un périphérique dans la whitelist : `AddAsync` / `TemporaryApproveAsync` n'ont aucun appelant (ni CLI, ni commande GRID, ni console). | Un écran dans la console où l'admin de l'hôpital approuve un périphérique, avec audit. Prérequis de Strict en production. |
| AGT-IRC-001 | **Haute**, lot USB | Un port coupé par IRONCLAD n'a aucune commande de restauration : `RestoreUsbPortAsync` n'a pas d'appelant, et le redémarrage ne restaure qu'en cas d'incohérence d'état. Dans un hôpital, un port coupé sans retour possible est un problème de disponibilité. | Un chemin de restauration depuis la console, avec audit, avant tout matériel IRONCLAD réel. |
| AGT-CFG-001 | **Corrigé** (R4, « validate configuration once ») | Les enums de la configuration sont lus par `Enum.TryParse` sans contrôle. Deux pièges : (1) une valeur inconnue (`"Stirct"`) retombe en silence sur une valeur par défaut (`UsbDeviceMonitor.ResolveOperatingMode` → `Permissive`) ; (2) `Enum.TryParse("5")` réussit et donne une valeur numérique **non définie** dans l'enum. | La validation FluentValidation refuse le démarrage sur toute valeur qui n'est pas un nom défini (`Enum.IsDefined` sur la valeur parsée, et refus des formes numériques), en nommant la clé fautive et en listant les valeurs autorisées. À appliquer à **tous** les enums de la configuration, pas seulement `OperatingMode`. |
| AGT-TLS-001 | **Corrigé** (`356f2c5`, 2026-10-08) — défaut, pas risque accepté | Le `appsettings.json` livré portait `TrustAnyCertificate: true` : `DangerousAcceptAnyServerCertificateValidator` sur le client GRID, donc un agent installé acceptait n'importe quel certificat serveur (usurpation du GRID : alertes, jeton d'enrôlement, commandes). `install.ps1` téléchargeait aussi le paquet de l'agent (exécutés ensuite en LocalSystem) avec la validation TLS coupée pour tout le processus. | Fait : fichier livré à `false` ; `ServerTlsPolicy` refuse `true` au démarrage hors de l'environnement Development (environnement non défini = Production, refusé) et, en Development, hors de localhost / 127.0.0.1 / ::1 ; avertissement à chaque démarrage quand l'option est active ; `install.ps1` ne contourne plus la validation que pour un GRID local. Conséquence : la CA du GRID (`deployment/grid/pki`) doit être reconnue par le poste pour l'enrôlement. Seul contournement restant, légitime : `dashboard/vite.config.ts:32` (`secure: false`, proxy du serveur de développement Vite uniquement). |
| AGT-SVC-001 | **Corrigé** (2026-10-11) — défaut | Un agent installé comme service Windows ne trouvait pas sa configuration : `Host.CreateApplicationBuilder(args)` prend le répertoire courant comme racine de contenu, et le Service Control Manager lance les services dans `C:\Windows\System32`. `AddWindowsService` ne corrige pas cela sur un `HostApplicationBuilder` (seul `UseWindowsService` sur l'ancien `IHostBuilder` le faisait). `appsettings.json` étant facultatif pour l'hôte, des variables d'environnement seules auraient pu faire démarrer l'agent sur des valeurs par défaut. | Fait : `AgentHost.CreateBuilder` fixe `ContentRootPath = AppContext.BaseDirectory` pour le service et chaque sous-commande ; `AgentHost.LoadConfiguration` refuse le démarrage sans `appsettings.json` à cet endroit, en nommant le chemin. Tests : `AgentHostTests` (répertoire courant différent du dossier de l'exécutable ; fichier absent refusé même quand l'environnement fournit tous les réglages). Vérifié sur l'exécutable lancé depuis System32. |
| AGT-BENCH-001 | **Haute**, avant la mesure après ETW (LOT 3) | AttributionBench a été reconstruit (R2, `c57449c`) sans trois des quatre contrôles d'instrument qui fondaient le 0/120 du 2026-09-30 : (1) **appel de référence** — l'appel de production `RestartManagerHelper.GetProcessesLockingFile` comparé, pour chaque événement, à une séquence Restart Manager écrite indépendamment, pour exclure un défaut du code de production ; (2) **sonde du fil propriétaire** — l'écrivain interroge le Restart Manager sur son propre fichier pendant qu'il tient le handle, pour exclure un poste où le Restart Manager ne voit rien ; (3) **Restart Manager en isolation**, hors du pipeline du watcher, à 0, 250, 1 000 et 3 000 ms. Seul le contrôle (4), l'arrivée de l'événement par rapport à la libération du handle, est reconstruit. Le 0/120 rejoué le 2026-10-08 montre donc que la requête de production ne nomme jamais l'écrivain, mais n'exclut pas à lui seul un instrument aveugle : cette partie repose sur la mesure d'origine. Détail : `agent/tools/AttributionBench/README.md`. | Reconstruire les contrôles 1 et 2 (et 3) dans le banc, et les rejouer avec la mesure « avant », avant que le banc ne mesure l'attribution ETW. Un chiffre « après » n'est comparable qu'à un « avant » produit par le même instrument, entièrement contrôlé. |
| AGT-PKI-001 | **Haute**, installateur (Sprint 12) | Conséquence d'AGT-TLS-001 : chaque poste doit faire confiance à l'autorité de certification racine du GRID (`deployment/grid/pki/ca.crt`), sinon l'enrôlement et `install.ps1` refusent le GRID — ce qui est voulu. Aujourd'hui, rien ne l'installe : c'est une opération manuelle par poste. | L'installateur installe la racine, après vérification de son empreinte. À trancher à ce moment : `Cert:\LocalMachine\Root` (toute la machine lui fait confiance, pour tout site) ou épinglage de cette racine pour l'agent seul (`X509ChainPolicy.CustomTrustStore`, `X509ChainTrustMode.CustomRootTrust`). En attendant : procédure manuelle, section « Trusting the GRID certificate authority » ci-dessus. |
| AGT-INST-001 | **Haute**, avant l'installateur (Sprint 12) ; rattaché au risque R12 (signature de code) | `install.ps1` ne vérifie l'intégrité du paquet de l'agent que par TLS : `WebClient.DownloadFile` (`install.ps1:92`), puis `Expand-Archive` (`install.ps1:114`) et création du service sous `LocalSystem` (`install.ps1:166`). Les exécutables d'un paquet altéré à la source (serveur GRID compromis, dépôt du paquet modifié) ou servi par un GRID dont la CA est compromise seraient installés et lancés avec les plus hauts privilèges du poste. TLS authentifie le serveur, pas le contenu. | Avant toute extraction : vérifier la signature Authenticode de chaque exécutable du paquet (`Get-AuthenticodeSignature` : statut `Valid` **et** certificat de signataire attendu, épinglé dans le script), ou un SHA-256 du paquet publié par un canal séparé du GRID. Refus en cas d'écart, rien d'extrait. La signature Authenticode est déjà le prérequis (1) de R12 (`docs/architecture/02-design-doc.md`). |
| AGT-KEY-001 | **Bloquant avant la démo de R10** (défaut de sécurité, plus une dette différée), juste après R7 | Le dossier des clés `%ProgramData%\RansomGuard-CM\keys` (clé de la base SQLCipher, clé Ed25519 de signature du journal d'audit) hérite des ACL de `C:\ProgramData`, dont `BUILTIN\Users:(OI)(CI)(RX)` ; `install.ps1:149` le crée sans ACL. Les clés y sont protégées par DPAPI en portée `LocalMachine` (`DatabaseKeyManager.cs:61`, `AuditLogSigner.cs:134`) : tout compte local, même non administrateur, peut les lire et les déchiffrer. `DaclEnforcer` existe mais n'a aucun appelant en production. Les analyses STRIDE déclarent pourtant ces menaces « Mitigated » par une DACL (`docs/security/sentinel-stride.md:31`, `:63`, `:64` ; `docs/security/usb-guard-stride.md:36` ; `docs/security/exfil-watch-stride.md:20`) : elles sont marquées « Not implemented — AGT-KEY-001 », et repasseront « Mitigated » avec le SHA du correctif. **Bloquant avant la démo de R10.** | Rompre l'héritage et restreindre `keys\` à SYSTEM et Administrators ; réparer au démarrage les installations existantes ; l'installateur du Sprint 12 pose les mêmes ACL. Conséquence assumée : un agent lancé en développement sous un compte non administrateur perd l'accès aux clés. Limite connue : une clé de signature présente sur la machine auditée reste récupérable par un attaquant local privilégié ; seule l'ancre de la chaîne par le GRID rend le journal vérifiable. |
| AGT-ATP-001 | À traiter avec le Watchdog | ANTI_TAMPERING est affiché (`inactive` / `not_implemented`) mais exclu du verdict de protection calculé par le GRID (`grid/src/ransomguard_grid/services/agent_posture.py`, `DETECTION_MODULES`) : il n'existe pas encore. GENEALOGY en est exclu aussi, comme enrichisseur (`degraded` / `attribution_unreliable`). | Le jour où le Watchdog existe, ANTI_TAMPERING entre dans `DETECTION_MODULES` avec sa règle (arbitrage 3 de `LOT2_CONTRAT_V2_VERDICT.md`) et ses tests ; GENEALOGY y entre quand l'attribution ETW aura été mesurée (AGT-ATTR-001). |
| AGT-DEP-002 | Normale | Versions flottantes `2.1.*`, `1.3.*`, `1.0.*` (et `8.0.*`) dans `RansomGuard.Agent.Core.csproj` : la compilation n'est pas reproductible. | Figer les versions exactes et activer `packages.lock.json` (commit séparé). |
