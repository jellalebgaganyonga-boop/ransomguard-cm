# Registre des risques de dépendances

Chaque avis de sécurité qu'on ne corrige pas tout de suite est inscrit ici
**avant** d'être neutralisé dans un outil (`NuGetAuditSuppress`,
`--ignore-vuln`) ou toléré par un seuil d'audit. Une entrée donne : le paquet,
l'avis, pourquoi il n'est pas atteignable ou pourquoi il est acceptable, la
date de décision, le lot qui le corrigera, et une date de réexamen à **30 jours
au plus tard**.

Règles :

- **Jamais** de neutralisation globale ni par niveau de gravité. Un nouvel avis
  reçoit la même analyse (identifiant, composant touché, preuve fichier:ligne)
  avant d'entrer ici.
- À la date de réexamen : on vérifie qu'une version corrigée n'existe pas, que
  le chemin est toujours inatteignable, et on repousse la date de 30 jours au
  plus, ou on corrige.
- Une entrée corrigée passe au statut **Résolu**, avec le commit, et reste
  dans le registre.

Dernière mise à jour : 2026-10-08.

---

## Agent (.NET)

### DEP-AGT-01 — SixLabors.ImageSharp 2.1.13 (5 avis)

| Avis | Gravité | Composant touché |
|---|---|---|
| GHSA-j3p4-wp97-rph4 | élevée | processeur HistogramEqualization |
| GHSA-j9gm-c75j-xc9q | élevée | encodeur TIFF CCITT T4 |
| GHSA-jjfr-hcj7-qf5w | élevée | encodeur TIFF CCITT T6 |
| GHSA-wmxv-xphr-5c9g | moyenne | décodeur BigTIFF |
| GHSA-gwg2-r3hj-4w44 | moyenne | analyse des profils ICC (CLUT) |

- **Corrigé en** : 4.1.2 seulement (pas de correctif 2.x ; licence Six Labors
  Split en 4.x).
- **Pourquoi non atteignable** : l'agent ne fait qu'encoder un JPEG.
  - `JpgCanaryGenerator.cs:21,40` crée une image en mémoire et l'encode ;
    c'est le seul fichier de production qui utilise ImageSharp.
  - Aucun code de production ne décode d'image, ne touche au TIFF, à
    l'égalisation d'histogramme, aux métadonnées ni aux profils ICC.
  - Le seul `Image.Load` est en test (`AdditionalFormatTests.cs:92`), sur les
    octets que le générateur vient de produire.
  - `PdfCanaryGenerator.cs:54` (PdfSharpCore, qui dépend aussi d'ImageSharp)
    ne dessine que du texte : ni `XImage`, ni image lue depuis le disque.
- **Neutralisation** : `NuGetAuditSuppress` par URL exacte, dans
  `agent/src/RansomGuard.Agent.Core/RansomGuard.Agent.Core.csproj`.
- **Décision** : 2026-10-08.
- **Corrigé par** : remplacement d'ImageSharp et de PdfSharpCore par des
  bibliothèques MIT, Sprint 8 après la clôture du LOT 2 (dette AGT-DEP-001,
  priorité haute).
- **Réexamen** : 2026-11-07.
- **Statut** : accepté.

---

## GRID (Python)

Aucun avis accepté : python-jose a été remplacé par PyJWT (voir *Résolus*,
DEP-GRID-01 et DEP-GRID-02). `pip-audit --strict` tourne sans exclusion.

---

## Dashboard (npm, dépendances de développement uniquement)

Aucune de ces dépendances n'est livrée au navigateur. Le contrôle de la CI
(`npm audit --omit=dev --audit-level=high`) ne porte que sur la production.

### DEP-DASH-01 — Vite 5.4.21 et sa chaîne (Vitest 2.1.9)

| Paquet | Avis | Gravité | Composant |
|---|---|---|---|
| vite | GHSA-4w7w-66w2-5vf9 | élevée | traversée de chemin dans les `.map` des dépendances optimisées (serveur de dev) |
| vite | GHSA-v6wh-96g9-6wx3 | élevée | launch-editor : fuite de hash NTLMv2 via un chemin UNC (serveur de dev) |
| vite | GHSA-fx2h-pf6j-xcff | élevée | contournement de `server.fs.deny` sous Windows (serveur de dev) |
| esbuild | GHSA-67mh-4wv8-2f99 | moyenne | serveur de dev joignable par n'importe quel site |
| vitest | GHSA-5xrq-8626-4rwp | critique | lecture/exécution de fichiers via le serveur UI de Vitest |
| vitest, @vitest/mocker | GHSA-82fw-gwwq-j7x9 | critique / moyenne | lecture de fichiers via la redirection des mocks |
| tinypool | GHSA-5gmw-xhrv-c9v3, GHSA-85c8-ppgw-ccpr | critique | pollution de prototype dans les options des workers |
| vite-node | (via vite) | moyenne | — |

- **Corrigé en** : vite 8.3.4, vitest 4.1.11 (versions majeures).
- **Pourquoi acceptable** : tous ces chemins passent par un serveur de
  développement ou par le lanceur de tests, jamais par le bundle livré.
  - `vite.config.ts` ne définit pas `server.host`, aucun script ni le
    `webServer` de Playwright ne passent `--host` : Vite n'écoute que sur
    `localhost`.
  - `@vitest/ui` a été retiré (commit 708a644) : le serveur UI de Vitest
    n'existe plus. Règle du README du dashboard : jamais `vitest --ui` ni
    `--api`, jamais `--host`.
  - tinypool et la redirection des mocks ne reçoivent que nos propres tests.
  - Exposition résiduelle : un poste de développement sous Windows avec le
    serveur de dev lancé (GHSA-fx2h-pf6j-xcff, GHSA-v6wh-96g9-6wx3).
- **Décision** : 2026-10-08.
- **Corrigé par** : lot « chaîne d'outils frontend », juste après la clôture
  du LOT 2 (Vite 5 → 8, Vitest 2 → 4, CI Node 20 → 24).
- **Réexamen** : 2026-11-07.
- **Statut** : accepté.

### DEP-DASH-02 — tailwindcss 3.4.19 et sa chaîne

| Paquet | Avis | Gravité |
|---|---|---|
| braces (via micromatch, chokidar, fast-glob) | GHSA-vfj7-8cjw-p6xm | élevée |
| postcss-selector-parser (via postcss-nested) | GHSA-rj75-hqrm-r3gf | moyenne |
| tailwindcss, tailwindcss-animate | (via les précédents) | élevée / moyenne |

- **Corrigé en** : aucune version en Tailwind v3 ; seule issue, Tailwind v4.
- **Pourquoi acceptable** : déni de service (complexité des globs et des
  sélecteurs) **au moment de la construction seulement**, sur nos propres
  fichiers source. Rien n'est livré au navigateur, aucune entrée externe.
- **Décision** : 2026-10-08.
- **Corrigé par** : migration vers Tailwind v4 avec la refonte du design
  system de la console, pas avant.
- **Réexamen** : 2026-11-07.
- **Statut** : accepté.

### DEP-DASH-03 — style-dictionary 4.4.0

| Paquet | Avis | Gravité |
|---|---|---|
| style-dictionary | GHSA-vj5c-m527-mpff | élevée |
| patch-package, find-yarn-workspace-root, @bundled-es-modules/glob, fast-glob | (via micromatch / braces) | élevée |

- **Corrigé en** : style-dictionary 5.4.4 et plus (version majeure).
- **Pourquoi acceptable** : pollution de prototype dans `convertTokenData`.
  style-dictionary ne sert qu'au script `tokens:build`
  (`design-tokens/build.cjs`) et ne lit que nos propres jetons de design.
- **Décision** : 2026-10-08.
- **Corrigé par** : lot « chaîne d'outils frontend ».
- **Réexamen** : 2026-11-07.
- **Statut** : accepté.

---

## Résolus

### DEP-DASH-04 — react-router / react-router-dom 6.30.6 (production)

- **Avis** : GHSA-wrjc-x8rr-h8h6 (redirection ouverte via une barre oblique
  inverse dans `<Link>` et `useNavigate`), GHSA-337j-9hxr-rhxg (injection de
  constructeur à la désérialisation). Gravité moyenne.
- **Décision** : 2026-10-08, corriger avant R1 : la console d'un produit de
  sécurité ne doit pas porter une redirection ouverte.
- **Correction** : future flags v7 activés sur la v6 (cf9a53d), puis
  react-router-dom 7.18.4 (commit du passage en v7). `npm audit --omit=dev` :
  0 vulnérabilité.
- **Statut** : **Résolu** le 2026-10-08.

### DEP-GRID-01 — python-jose / ecdsa, PYSEC-2026-1325

- **Paquet** : `ecdsa`, tiré par `python-jose` pour les algorithmes ES*.
- **Corrigé en** : aucune version.
- **Pourquoi il était non atteignable** : le GRID signe et vérifie en HS256
  uniquement ; aucun algorithme ES* n'était utilisé.
- **Neutralisation (retirée)** : `--ignore-vuln PYSEC-2026-1325` dans
  `.github/workflows/ci.yml`.
- **Correction** : python-jose remplacé par PyJWT ; python-jose et ecdsa ne
  sont plus installés, l'exclusion est supprimée.
- **Statut** : **Résolu** le 2026-10-08.

### DEP-GRID-02 — python-jose 3.5.0, CVE-2026-85394

- **Alias** : GHSA-3qf3-8w2g-rqmx (correctif incomplet de CVE-2024-33663),
  publié le 2026-09-03. Confusion d'algorithme : une clé publique DER acceptée
  comme secret HMAC.
- **Corrigé en** : aucune version de python-jose.
- **Pourquoi il était non atteignable** : seule clé de vérification, le secret
  symétrique `jwt_secret_key` ; chaque `decode` restreignait les algorithmes.
- **Neutralisation (retirée)** : `--ignore-vuln CVE-2026-85394` dans
  `.github/workflows/ci.yml`.
- **Correction** : python-jose remplacé par PyJWT. Chaque `decode` passe
  `algorithms=["HS256"]` en dur (`core/jwt_service.py`, seul chemin JWT du
  GRID depuis la suppression des fonctions mortes de `core/security.py`),
  jamais lu depuis le jeton ni depuis la configuration ; `jwt_algorithm` n'accepte
  plus que `"HS256"`. Tests de sécurité écrits avant la migration et passés
  avant et après (`grid/tests/test_jwt_security.py`) : alg none, confusion
  d'algorithme, signature modifiée, jeton expiré, et décodage par PyJWT d'un
  jeton émis par python-jose.
- **Statut** : **Résolu** le 2026-10-08.
