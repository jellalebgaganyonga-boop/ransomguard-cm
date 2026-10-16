# ADR-025 — Attribution SENTINEL par ETW FileIO

**Statut** : accepté, révision 3 (revue du 2026-10-16 : corrections A et B). L'implémentation suit la section 7, un commit à la fois, diff soumis avant commit.
**Mesures** : `agent/tools/EtwFileIoProbe`, sur un runner Windows de la CI (Windows Server 2025, 4 vCPU, administrateur), 60 s par phase.
- Run 38805679885 : jeux de mots-clés.
- Run 38809979940 : Flush, Process, patron 7.
- Run 38813859055 : lecteur concurrent, dispositions.

La copie est synthétique et volontairement lourde (≈ 1 300 fichiers de 64 Ko par seconde) : c'est un plafond.

## 1. Les chiffres

| FileIOInit + DiskFileIO | Repos (év./s) | Copie (év./s) | CPU consommateur (repos / copie) | Perdus | Livraison p50 / p95, repos | Copie |
|---|---|---|---|---|---|---|
| sans Flush | 7 832 | 47 658 | 0,5 % / 2,2 % d'un cœur | 0 | 577 / 1 067 ms | 65 / 154 ms |
| Flush 250 ms | 6 427 | 45 936 | 0,5 % / 1,9 % | 0 | 204 / 497 ms | 57 / 141 ms |
| **Flush 100 ms** | 7 475 | 45 829 | 0,3 % / 2,4 % | 0 | **97 / 203 ms** | 44 / 92 ms |

**Coûts comparés**
- CPU de l'agent actuel sur la même copie, dans un dossier qu'il surveille : 34 à 50 % d'un cœur.
- Débit du rédacteur : identique avec ou sans session.
- Analyse d'un événement par TraceEvent : environ 0,4 µs. Filtre canari : 7 à 10 ns.

**PID sur les écritures** (rédacteur ; lecteur concurrent en boucle, comme un antivirus)
- Write, Rename et Delete portent le PID du rédacteur 100 % du temps.
- Le lecteur et l'agent n'apparaissent dans **aucune** opération d'écriture.
- Environ un Write sur trois, et tous les SetInfo, arrivent avec le **PID 4** : c'est l'écriture différée du cache.

**Ouvertures (Create)**
- Le lecteur : **81 770** ouvertures. L'agent : **12**. Toutes en **FILE_OPEN**.
- Les réécritures du rédacteur : FILE_OVERWRITE_IF (202).
- TraceEvent expose la disposition NT dans `CreateDisposition`, avec des noms Win32 mais les valeurs NT : CREATE_ALWAYS = 5 = FILE_OVERWRITE_IF, OPEN_EXISTING = 1 = FILE_OPEN. `PayloadByName("CreateOptions")` est déjà masqué : son octet haut vaut 0.

**Patron 7** (vue mappée, avec flush explicite)
- Le Write porte le PID du rédacteur, 101 sur 101 ; le lecteur, 0.
- Le `FileSystemWatcher` ne voit **aucune** réécriture mappée, flushée ou non (banc : 40 sur 40 sans événement en 3 s). Seul l'ETW peut la détecter.

**Nom de fichier** : 100 % des événements nommés avec DiskFileIO, et 202 sur 202 pour un handle ouvert avant la session (0 sur 409 sans DiskFileIO).

**Heure de démarrage** : Process/Start et `GetProcessTimes` diffèrent de 0,6 à 2,6 ms.

**Sans droits administrateur** : session refusée ; le watcher détecte en 10 à 16 ms.

## 2. Décisions

1. **Mots-clés FileIOInit + DiskFileIO + Process, `Flush()` toutes les 100 ms.**
2. **Session séparée d'EXFIL WATCH** (`RansomGuard-Sentinel-FileIO`, 64 Mo de tampons).
3. **Filtre dans le callback, sans attente.**
   - Préfixe de dossier, puis recherche dans un ensemble immuable des chemins de canaris.
   - Les succès partent dans un canal de 1 024 places en `DropNewest` ; les pertes sont comptées et envoyées dans le heartbeat.
4. **Seules les écritures attribuent (correction A).** Un PID faux est pire qu'un PID absent.
   - **Opérations qui attribuent** : Write ; SetInfo, pour les seules classes renommage (`FileRenameInformation`, `…Ex`), disposition de suppression (`FileDispositionInformation`, `…Ex`) et fin de fichier (`FileEndOfFileInformation`) ; Delete ; Rename.
   - **Un Create n'attribue que si sa disposition est une écriture** : FILE_SUPERSEDE (0), FILE_OVERWRITE (4), FILE_OVERWRITE_IF (5), ou FILE_CREATE (2) sur un canari recréé. **FILE_OPEN (1) et FILE_OPEN_IF (3) n'attribuent jamais.**
   - Jamais le PID 0, ni le PID 4, ni **le PID de l'agent**.
   - **`ambiguous` ne compte que les PID qui écrivent.** Un lecteur dans la fenêtre ne rend pas l'attribution ambiguë.
5. **Le watcher reste**, comme déclencheur parallèle, comme repli et comme contrôle. Un canari vu par le watcher sans événement ETW dans les 2 s compte comme une perte ETW. L'inverse est normal : c'est le patron 7.

## 3. Détection et attribution : le contrat (A.1 révisé)

L'alerte part au **premier** signal, watcher ou ETW. L'ETW complète l'attribution.

**Une seule détection par opération**
- Clé de corrélation : `(chemin du canari, normalisé en minuscules ; classe d'opération : modification | renommage | suppression)`. Pour un renommage, c'est l'ancien chemin.
- Correspondances : `Changed` ↔ Write ou SetInfo de fin de fichier ; `Renamed` ↔ Rename ou SetInfo de renommage ; `Deleted` ↔ Delete ou SetInfo de disposition.
- Fenêtre : 2 s à partir du premier signal. Cette clé remplace, pour les canaris, celle du déduplicateur actuel (`changeType|chemin`, 500 ms).

**L'alerte n'attend jamais l'attribution**
- Elle part avec `attribution: pending` et son `client_message_id`.
- Le complément arrive ensuite, par la même file persistante que les alertes. Il est `resolved`, `unavailable` ou `ambiguous`, et vaut `unavailable` si rien n'est résolu 1,5 s après le premier signal.
- Console : « Attribution en cours… », puis le processus, ou « indisponible » avec sa raison. Au-delà de 10 min sans complément : « attribution non reçue ».

**L'endpoint de complément est cloisonné (correction B)** : `POST /agents/{id}/alerts/{client_message_id}/attribution`.
- **L'agent vient du certificat**, jamais du chemin ni du corps. Un `{id}` différent du certificat donne **403**.
- L'alerte est cherchée sur `client_message_id` **et** `agent_id` **et** `tenant_id`. L'alerte d'un autre agent ou d'un autre tenant donne **404**, sans rien révéler de son existence.
- **Le corps est borné** (schéma strict, champ inconnu refusé) :
  - `state` dans `resolved | unavailable | ambiguous` ;
  - `pid`, entier de 1 à 2³²−1 ;
  - `process_created_at`, horodatage UTC ;
  - `process_name`, 260 caractères au plus ;
  - `image_path`, 1 024 caractères au plus ;
  - `reason` dans un code fermé : `no_write_event`, `system_pid`, `pid_reused`, `etw_unavailable`, `timeout`.
- **Idempotent** : un second complément sur une attribution déjà finale ne change rien, répond 200, et il est journalisé dans l'audit (`AlertAttributionIgnored`).
- **Test d'isolation**, comme pour HOST : un complément visant l'alerte d'un autre tenant, avec le même `client_message_id`, donne 404 et ne change rien. Le test est écrit pour échouer si la recherche se fait sur `client_message_id` seul.

**Le moteur de réponse attend l'attribution, la détection non**
- Une suspension ne part qu'avec une attribution `resolved`, après vérification de la paire (PID, heure de création), voir 4b.
- La rafale de canaris sans attribution mène à l'isolement (table A1, LOT 5).

## 4. Points de la revue précédente

**4a. Le plafond CPU ne coupe jamais l'attribution pendant une attaque.**
- Le repli pour charge (au-delà de 5 % d'un cœur pendant 60 s) n'a lieu que si aucun signal de détection n'est actif : aucun canari touché, aucune alerte ENTROPY depuis 10 min.
- Pendant un incident, la session reste ouverte ; son coût et ses pertes sont comptés et remontés.
- Avant de couper : les tampons passent de 64 à 128, puis à 256 Mo.
- Pas de filtre de chemin côté noyau : le fournisseur FileIO n'en accepte pas.

**4b. La réutilisation des PID.**
- L'attribution enregistre la paire (PID, heure de création), prise sur Process/Start, ou par `GetProcessTimes` lu dans le callback.
- Une heure postérieure à l'événement donne `unavailable / pid_reused`.
- Avant d'agir, le moteur relit `GetProcessTimes` et refuse d'agir si l'heure diffère de plus de 50 ms ; il journalise pourquoi.
- Test prévu.

**4c. Slots de session (R12).** Faute de slot : repli watcher, `degraded / attribution_unavailable`, raison `etw_no_session_slot` dans le log avec la liste des sessions actives. Ce cas est ajouté au test R12.

**4d. Écritures par mappage mémoire.**
- Avec flush explicite : bon PID sur le Write.
- Sans flush, les pages partent avec le **PID 4** : attribution `unavailable`, raison `system_pid`.
- **Le seul filet est la rafale de canaris sans attribution, qui mène à l'isolement.** Le filet par Create est retiré : rien ne prouve qu'il ne nommerait jamais un lecteur.

**Restart Manager.** Le commit 4 le retire du chemin de SENTINEL. `RestartManagerHelper` reste, parce qu'il sert encore à GENEALOGY (`GenealogyEnricher.cs:51`) et à la mesure « avant » du banc. Son premier `RmGetList`, qui passe la même variable en `out` et en `ref` (`RestartManagerHelper.cs:71`), est corrigé dans ce même commit.

## 5. Condition de sortie avant le pilote (AGT-ETW-001)

EtwFileIoProbe est rejoué sur un **vieux poste réel**, avec Bitdefender actif, dans le cadre du test R12. Le run CI suffit pour concevoir, pas pour déployer. `etw-probe.yml` garde `workflow_dispatch`.

## 6. Mesure du résultat

AttributionBench est rejoué **à l'identique** (`-- 20 10`), avec ses contrôles 1 et 2 et le **lecteur concurrent** dans tous les patrons.
- **Avant**, avec le lecteur (2026-10-16) :
  - patrons 3, 5 et 6 à **0/60** ;
  - patron 1 à 20/20 (les ouvertures du lecteur font arriver l'événement du watcher 5 s avant la libération du handle) ;
  - patrons 7a et 7b sans événement ;
  - **0 PID faux**, jamais le lecteur.
- **Après** : le même tableau. La colonne « PID faux » doit rester à **0 avec le lecteur**, patron 7 compris.

## 7. Découpage (un commit chacun, diff soumis avant commit)

1. **Session** : `EtwFileIoSession`, `IKernelFileIoTrace` ; mots-clés, `Flush()` à 100 ms, tampons ; raisons d'échec (droits, slot, démarrage, traitement).
2. **Filtre** : ensemble des canaris remplacé en bloc, callback sans attente, canal de 1 024 places et compteur de pertes.
3. **Corrélation** : clé et fenêtre de 2 s, watcher et ETW fusionnés en une détection, l'alerte part avec `pending`.
4. **Attribution** : règles de la section 2.4, paire (PID, heure de création), complément par la file persistante ; Restart Manager retiré de SENTINEL, `out`/`ref` corrigé.
5. **GRID et console** : l'endpoint cloisonné (section 3), la mise à jour idempotente, l'audit, le test d'isolation entre tenants, l'affichage.
6. **Repli et plafond** : watcher seul ; plafond CPU inactif pendant un incident ; tampons agrandis avant de couper.
7. **État et heartbeat** : `attribution_unavailable`, `attribution_suspended_load`, `etw_events_lost`, `etw_cpu_percent`.
8. **Mesure** : AttributionBench en mode ETW, avec le lecteur et le patron 7, et le tableau avant/après.
