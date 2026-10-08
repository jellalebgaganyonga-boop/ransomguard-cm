# Agent (Windows Endpoint) 

C# .NET 8 service running on Windows 10/11 (and legacy Windows 7 SP1+) endpoints.

Monitors file system events via ETW, detects ransomware behavior, and reports to the management server via mTLS.

## Configuration

The configuration is bound and validated **once**, at startup, before anything consumes it
(`AgentConfigurationLoader`). An invalid configuration is refused with one line per setting,
naming the exact key, on the console (and in the Windows Event Log, best effort); the agent
exits with code 1. Paths (logs, database, keys) must be absolute once environment variables
are resolved; enum-valued settings must name a defined value.

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

## Dette technique

| ID | Priorité | Sujet | Action attendue |
|---|---|---|---|
| AGT-DEP-001 | **Haute**, planifiée au Sprint 8, juste après la clôture du LOT 2 | `SixLabors.ImageSharp` 2.1.x : 5 avis de sécurité en deux semaines (GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w, GHSA-wmxv-xphr-5c9g, GHSA-gwg2-r3hj-4w44), corrigés seulement en 4.1.2, sous licence Six Labors Split. L'agent n'utilise qu'une seule fonction de la bibliothèque : encoder un JPEG. Les avis sont neutralisés un par un via `NuGetAuditSuppress` dans `RansomGuard.Agent.Core.csproj`, car aucun n'est atteignable. Tout nouvel avis reçoit la même analyse avant d'être ajouté (identifiant GHSA, composant touché, preuve fichier:ligne qu'il est inatteignable). Jamais de suppression globale ni par niveau de gravité. | Remplacer ImageSharp et PdfSharpCore par des bibliothèques sous licence MIT. Retirer la suppression dès qu'une version corrigée compatible existe ou que la dépendance a disparu. |
| AGT-BUS-001 | À traiter avant le Response Engine | `InMemoryDetectionEventBus` publie de façon synchrone : chaque abonné est attendu l'un après l'autre, sur le thread du publieur. Un abonné lent bloque donc le module qui publie. Aujourd'hui, `EntropyMonitor` est le seul publieur et `EncryptedExfilCorrelationRule` le seul abonné, dont le gestionnaire est synchrone : l'impact est faible. Le Response Engine s'abonnera à ce bus avec des traitements longs. | Revoir le modèle de diffusion avant le Response Engine : file bornée par abonné, politique de saturation explicite, compteur de pertes réellement alimenté. |
| AGT-ATTR-001 | **Bloquant du Response Engine** | Les signaux ENTROPY ne portent aucun processus : `EntropyMonitor.cs:271` publie `ProcessId = 0` (et `ProcessName` vide), alors que `EncryptedExfilCorrelationRule.cs:28` indexe les signaux par PID. Tous les signaux tombent sur la clé 0 : la corrélation ENTROPY → EXFIL par processus ne peut pas fonctionner, et le Response Engine ne saurait pas quel processus suspendre. C'est le problème d'attribution mesuré à 0 sur 120 par AttributionBench (Restart Manager), vu sous un autre angle ; voir aussi `ModuleStateRegistry.cs:103` (GENEALOGY `degraded`, `attribution_unreliable`). | Ne pas corriger isolément. Rattaché à la décision ETW du LOT 3 (`SPRINT_8_ARBITRAGE_LOT3.md` §B : attribution par ETW FileIo noyau, Restart Manager écarté). Le PID des signaux sera fourni par cette attribution, une fois mesurée. La décision ETW n'est pas encore consignée dans le dépôt (ni ADR ni AttributionBench, commit perdu 90dfb1b, à refaire en R2). |
| AGT-USB-001 | **Haute**, lot USB | USB GUARD décide mais n'agit pas : lecture seule, quarantaine, éjection et `BlockAndEject` logiciel ne sont pas implémentés. Depuis le commit « la vérité dans USB GUARD », ils renvoient `Success = false` / `not_implemented`, l'alerte et le journal d'audit signé l'écrivent tel quel, et USB GUARD en Strict se déclare `degraded` / `actions_not_implemented`. Seule la coupure IRONCLAD agit. Détail : `docs/modules/usb-guard.md`. | Implémenter l'éjection (`CM_Request_Device_Eject`), la lecture seule et la quarantaine (le `QuarantineService` AES-256-GCM existe), avec leurs tests. Prérequis de Strict en production. |
| AGT-USB-002 | **Haute**, lot USB | Une whitelist illisible fait passer le périphérique sans analyse, dans tous les modes : `IsWhitelistedAsync` échoue au branchement, le `catch` général de `UsbDeviceMonitor.HandleConnectionAsync` journalise et s'arrête, avant le contrôle de clé amorçable et l'analyse. | L'échec de lecture ne court-circuite jamais l'analyse. En Strict, le périphérique est traité comme non approuvé (fermeture en cas d'échec). Une alerte est remontée. |
| AGT-USB-003 | **Haute**, lot USB | Aucun chemin pour inscrire un périphérique dans la whitelist : `AddAsync` / `TemporaryApproveAsync` n'ont aucun appelant (ni CLI, ni commande GRID, ni console). | Un écran dans la console où l'admin de l'hôpital approuve un périphérique, avec audit. Prérequis de Strict en production. |
| AGT-IRC-001 | **Haute**, lot USB | Un port coupé par IRONCLAD n'a aucune commande de restauration : `RestoreUsbPortAsync` n'a pas d'appelant, et le redémarrage ne restaure qu'en cas d'incohérence d'état. Dans un hôpital, un port coupé sans retour possible est un problème de disponibilité. | Un chemin de restauration depuis la console, avec audit, avant tout matériel IRONCLAD réel. |
| AGT-CFG-001 | **Corrigé** (R4, « validate configuration once ») | Les enums de la configuration sont lus par `Enum.TryParse` sans contrôle. Deux pièges : (1) une valeur inconnue (`"Stirct"`) retombe en silence sur une valeur par défaut (`UsbDeviceMonitor.ResolveOperatingMode` → `Permissive`) ; (2) `Enum.TryParse("5")` réussit et donne une valeur numérique **non définie** dans l'enum. | La validation FluentValidation refuse le démarrage sur toute valeur qui n'est pas un nom défini (`Enum.IsDefined` sur la valeur parsée, et refus des formes numériques), en nommant la clé fautive et en listant les valeurs autorisées. À appliquer à **tous** les enums de la configuration, pas seulement `OperatingMode`. |
| AGT-TLS-001 | **Corrigé** (`356f2c5`, 2026-10-08) — défaut, pas risque accepté | Le `appsettings.json` livré portait `TrustAnyCertificate: true` : `DangerousAcceptAnyServerCertificateValidator` sur le client GRID, donc un agent installé acceptait n'importe quel certificat serveur (usurpation du GRID : alertes, jeton d'enrôlement, commandes). `install.ps1` téléchargeait aussi le paquet de l'agent (exécutés ensuite en LocalSystem) avec la validation TLS coupée pour tout le processus. | Fait : fichier livré à `false` ; `ServerTlsPolicy` refuse `true` au démarrage hors de l'environnement Development (environnement non défini = Production, refusé) et, en Development, hors de localhost / 127.0.0.1 / ::1 ; avertissement à chaque démarrage quand l'option est active ; `install.ps1` ne contourne plus la validation que pour un GRID local. Conséquence : la CA du GRID (`deployment/grid/pki`) doit être reconnue par le poste pour l'enrôlement. Seul contournement restant, légitime : `dashboard/vite.config.ts:32` (`secure: false`, proxy du serveur de développement Vite uniquement). |
| AGT-DEP-002 | Normale | Versions flottantes `2.1.*`, `1.3.*`, `1.0.*` (et `8.0.*`) dans `RansomGuard.Agent.Core.csproj` : la compilation n'est pas reproductible. | Figer les versions exactes et activer `packages.lock.json` (commit séparé). |
