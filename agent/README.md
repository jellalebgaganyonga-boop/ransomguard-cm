# Agent (Windows Endpoint) 

C# .NET 8 service running on Windows 10/11 (and legacy Windows 7 SP1+) endpoints.

Monitors file system events via ETW, detects ransomware behavior, and reports to the management server via mTLS.

## Dette technique

| ID | Priorité | Sujet | Action attendue |
|---|---|---|---|
| AGT-DEP-001 | **Haute**, planifiée au Sprint 8, juste après la clôture du LOT 2 | `SixLabors.ImageSharp` 2.1.x : 5 avis de sécurité en deux semaines (GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w, GHSA-wmxv-xphr-5c9g, GHSA-gwg2-r3hj-4w44), corrigés seulement en 4.1.2, sous licence Six Labors Split. L'agent n'utilise qu'une seule fonction de la bibliothèque : encoder un JPEG. Les avis sont neutralisés un par un via `NuGetAuditSuppress` dans `RansomGuard.Agent.Core.csproj`, car aucun n'est atteignable. Tout nouvel avis reçoit la même analyse avant d'être ajouté (identifiant GHSA, composant touché, preuve fichier:ligne qu'il est inatteignable). Jamais de suppression globale ni par niveau de gravité. | Remplacer ImageSharp et PdfSharpCore par des bibliothèques sous licence MIT. Retirer la suppression dès qu'une version corrigée compatible existe ou que la dépendance a disparu. |
| AGT-DEP-002 | Normale | Versions flottantes `2.1.*`, `1.3.*`, `1.0.*` (et `8.0.*`) dans `RansomGuard.Agent.Core.csproj` : la compilation n'est pas reproductible. | Figer les versions exactes et activer `packages.lock.json` (commit séparé). |
