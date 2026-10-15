# ADR-025 — Attribution SENTINEL par ETW FileIO

**Statut** : proposé, révision 2 (revue du 2026-10-15 : A.1 révisé, corrections 2a à 2d). Aucune ligne de code ETW avant feu vert.
**Mesures** : `agent/tools/EtwFileIoProbe`, sur un runner Windows de la CI (Windows Server 2025, 4 vCPU, administrateur), 60 s par phase. Runs 38805679885 (jeux de mots-clés) et 38809979940 (Flush, Process, patron 7). La copie est synthétique et volontairement lourde : environ 1 300 fichiers de 64 Ko par seconde. C'est un plafond, pas un poste typique.

## 1. Les chiffres

| FileIOInit + DiskFileIO | Repos (év./s) | Copie (év./s) | CPU consommateur (repos / copie) | Perdus | Livraison p50 / p95, repos | Copie |
|---|---|---|---|---|---|---|
| sans Flush | 7 832 | 47 658 | 0,5 % / 2,2 % d'un cœur | 0 | 577 / 1 067 ms | 65 / 154 ms |
| Flush 250 ms | 6 427 | 45 936 | 0,5 % / 1,9 % | 0 | 204 / 497 ms | 57 / 141 ms |
| **Flush 100 ms** | 7 475 | 45 829 | 0,3 % / 2,4 % | 0 | **97 / 203 ms** | 44 / 92 ms |

**CPU de l'agent actuel** sur la même copie, dans un dossier qu'il surveille : 42,7 à 49,4 % d'un cœur. **Débit du rédacteur** : 70 000 à 85 000 copies par minute avec ou sans session, sans écart attribuable à la session. **Analyse d'un événement** par TraceEvent : environ 0,4 µs. **Filtre canari** : 7 à 10 ns.

**PID** (patrons 3 à 5 : réécriture, renommage, suppression) :
- Write, Rename et Delete portent le PID du rédacteur 100 % du temps, avec **0 PID faux**.
- Mais environ un Write sur trois, et tous les SetInfo, arrivent avec le **PID 4** : c'est l'écriture différée du cache.

**Patron 7** (réécriture par vue mappée, `FlushViewOfFile` explicite) : Create, Write, Cleanup et Close portent le PID du rédacteur, 170 sur 170.

**Nom de fichier** : avec DiskFileIO, 100 % des événements sont nommés, et 235 sur 237 pour un handle ouvert avant la session. Sans DiskFileIO : 0 sur 409.

**Heure de démarrage** : Process/Start et `GetProcessTimes` diffèrent de 2,6 ms.

**Sans droits administrateur** (poste de développement) : la session est refusée, et le watcher détecte en 10 à 16 ms.

## 2. Décisions

1. **Mots-clés FileIOInit + DiskFileIO + Process, avec `Flush()` toutes les 100 ms.** C'est la seule cadence qui ramène le p95 de livraison au repos sous l'attente d'attribution (1,5 s) avec une marge large, et son coût ne se distingue pas du bruit.
2. **Session séparée d'EXFIL WATCH.** Les tampons se dimensionnent à part. Un retard FileIO ne retarde pas le réseau. Chaque module publie son propre état.
3. **Filtre dans le callback, sans attente.**
   - Le callback fait un test de préfixe, puis une recherche dans l'ensemble immuable des chemins de canaris, remplacé en bloc à chaque régénération.
   - Les succès partent dans un canal de 1 024 places en `DropNewest`. Ses pertes sont comptées et envoyées dans le heartbeat.
   - Le fil ETW n'attend jamais (leçon d'AGT-AUD-002).
4. **Un PID faux est pire qu'un PID absent.**
   - On attribue seulement à partir de Create, Write, SetInfo, Delete et Rename, et jamais à partir d'un PID 0 ou 4.
   - Si deux PID différents touchent le même canari dans la fenêtre : l'attribution est `ambiguous`, ce qui compte comme non résolu pour le moteur de réponse.
5. **Le watcher reste** comme déclencheur parallèle, comme repli et comme contrôle.
   - Un canari vu par le watcher sans événement ETW dans les 2 s compte comme une perte ETW, envoyée dans le heartbeat.

## 3. Détection et attribution : le contrat (A.1 révisé)

L'alerte part au **premier** signal, watcher ou ETW. L'ETW complète l'attribution. A.1 visait le Restart Manager, interrogé après la libération du handle ; avec l'ETW, le PID est dans l'événement.

- **Une seule détection par opération.**
  - Clé de corrélation : `(chemin du canari, normalisé en minuscules ; classe d'opération : modification | renommage | suppression)`. Pour un renommage, c'est l'ancien chemin.
  - Correspondances watcher → ETW : `Changed` ↔ Write/SetInfo, `Renamed` ↔ Rename, `Deleted` ↔ Delete ou SetInfo (disposition).
  - Fenêtre : 2 s à partir du premier signal. Le second signal rejoint la détection existante et ne crée pas d'alerte. Après 2 s, une nouvelle opération crée une nouvelle détection.
  - Cette clé remplace celle du déduplicateur actuel (`changeType|chemin`, 500 ms) pour les canaris.
- **L'alerte n'attend jamais l'attribution.**
  - Elle part tout de suite avec `attribution: pending` et son `client_message_id`.
  - L'agent envoie ensuite un **événement de complément** : `POST /agents/{id}/alerts/{client_message_id}/attribution`, qui porte l'état (`resolved` | `unavailable` | `ambiguous`), le PID, l'heure de création du processus, le nom, le chemin de l'image, la raison si non résolu, et la source `etw`.
  - Ce complément passe par la même file persistante que les alertes, avec les mêmes reprises.
  - Le GRID met à jour les champs d'attribution de l'alerte (une seule fois, de façon idempotente) et journalise le complément dans l'audit.
  - Console : « Attribution en cours… » tant que l'état est `pending`. Puis le processus, ou « indisponible » avec sa raison. Au-delà de 10 min sans complément : « attribution non reçue ».
  - Le complément est `unavailable` si rien n'est résolu 1,5 s après le premier signal.
- **Le moteur de réponse attend l'attribution, la détection non.**
  - Une suspension de processus ne part qu'avec une attribution `resolved`, et après vérification de la paire (PID, heure de création), voir 4b.
  - Le chemin « attribution échouée + rafale de canaris → isolement » de la table A1 (LOT 5) reste disponible sans PID.

## 4. Corrections de la revue

**4a. Le plafond CPU ne coupe jamais l'attribution pendant une attaque.**
- Le consommateur mesure son CPU sur des fenêtres de 10 s.
- Le repli pour charge (au-delà de 5 % d'un cœur pendant 60 s) n'a lieu **que si aucun signal de détection n'est actif** : aucun canari touché et aucune alerte ENTROPY dans les 10 dernières minutes. Pendant un incident, la session reste ouverte quel qu'en soit le coût ; ce coût est compté et envoyé dans le heartbeat (`etw_cpu_percent`, `etw_events_lost`).
- Les événements perdus suivent la même règle : comptés, jamais une raison de couper pendant un incident.
- Avant de couper : on double les tampons (`BufferSizeMB` de 64 à 128, puis 256), et on recompte sur une fenêtre.
- Le filtre de dossier ne peut pas être poussé côté noyau : le fournisseur FileIO n'accepte pas de filtre par chemin. Côté agent, il coûte déjà 7 à 10 ns contre environ 0,4 µs d'analyse. Il n'y a donc pas de levier de ce côté, et je l'écris plutôt que de le promettre.
- Le repli publie `degraded / attribution_suspended_load`, avec une nouvelle tentative 10 min plus tard.

**4b. La réutilisation des PID.**
- L'attribution enregistre la paire **(PID, heure de création)**.
- L'heure vient de l'événement Process/Start si le processus est né pendant la session (écart mesuré : 2,6 ms). Sinon, d'un `GetProcessTimes` lu dans le callback de succès.
- Si l'heure lue est **postérieure** à l'événement FileIO, le PID a déjà été réattribué : attribution `unavailable`, raison `pid_reused`.
- Avant d'agir, le moteur de réponse relit `GetProcessTimes`. S'il ne trouve pas la même heure (à 50 ms près), il n'agit pas et journalise pourquoi.
- Test prévu (horloge et fournisseur de processus injectés) : un processus qui se termine, son PID repris par un autre ; la suspension est refusée et la raison journalisée.

**4c. Slots de session et cohabitation (R12).**
- Une machine a 8 sessions « system logger » ; Sysmon, Defender ou un EDR tiers peuvent en occuper.
- Un échec de démarrage faute de slot (`ERROR_NO_SYSTEM_RESOURCES`, « no more sessions ») donne le repli watcher, l'état `degraded / attribution_unavailable`, et dans le log la raison distincte `etw_no_session_slot`, avec la liste des sessions actives (`logman query -ets`).
- C'est ajouté au test de cohabitation du risque R12, aux côtés de Kaspersky, Bitdefender et Defender.

**4d. Écritures par mappage mémoire.**
- Mesuré : avec un `FlushViewOfFile` explicite, Create et Write portent le bon PID (170 sur 170).
- Non mesuré, et c'est la limite écrite : sans flush explicite, les pages partent plus tard par l'écrivain de pages mappées, avec le **PID 4**. Pour ces écritures, l'attribution est `unavailable`.
- Filets : le Create/Open du processus sur le canari porte son PID (mesuré, 100 %) et sert d'attribution s'il est dans la fenêtre. Sinon, la rafale de canaris sans attribution mène à l'isolement.
- Le banc reçoit un **patron 7** « écriture par fichier mappé », en deux variantes : avec et sans flush.

## 5. Condition de sortie avant le pilote (AGT-ETW-001)

EtwFileIoProbe est rejoué sur un **vieux poste réel** représentatif, avec Bitdefender actif, dans le cadre du test R12. Le run CI suffit pour concevoir, pas pour déployer : pas d'antivirus tiers, un disque rapide, aucun utilisateur. Sur le runner, le CPU de l'agent au repos a varié de 3,8 à 12,2 % selon les phases, sans lien démontré avec la session. Ce poste dira si le coût noyau, payé par les processus qui font des entrées-sorties, est visible. `etw-probe.yml` garde `workflow_dispatch` : une fois fusionné, il se lance à la main depuis `sprint8/recovery`.

## 6. Mesure du résultat

AttributionBench est rejoué **à l'identique** (`-- 20 10`), avec ses contrôles d'instrument 1 et 2 reconstruits (AGT-BENCH-001).
- **Avant**, sur l'instrument contrôlé (2026-10-15) : sonde 5/5, 0 écart avec la requête de référence, **0/120** et 0/60 sur les patrons 3, 5 et 6.
- **Après** : le même tableau, plus le patron 7, le taux sur 3, 5 et 6, la colonne « PID correct », et le nombre de PID faux, dont la cible est **0**.

## 7. Découpage proposé (un commit chacun, diff soumis avant commit)

1. **Session** : `EtwFileIoSession` (mots-clés, `Flush()` à 100 ms, tampons), démarrage et arrêt, raisons d'échec (droits, slot), état publié.
2. **Filtre** : ensemble des canaris remplacé en bloc, callback sans attente, canal de 1 024 places et compteur de pertes.
3. **Corrélation** : clé et fenêtre de 2 s, watcher et ETW fusionnés en une détection, l'alerte part avec `pending`.
4. **Attribution** : règles PID 0/4, `ambiguous`, paire (PID, heure de création), complément envoyé par la file persistante.
5. **GRID et console** : l'endpoint de complément, la mise à jour idempotente, l'audit, l'affichage « en cours / processus / indisponible ».
6. **Repli et plafond** : watcher seul, plafond CPU inactif pendant un incident, tampons agrandis avant de couper.
7. **État et heartbeat** : `attribution_unavailable`, `attribution_suspended_load`, `etw_events_lost`, `etw_cpu_percent`.
8. **Mesure** : AttributionBench en mode ETW avec le patron 7, puis le tableau avant/après.
