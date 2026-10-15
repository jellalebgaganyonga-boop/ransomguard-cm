# ADR-025 — Attribution SENTINEL par ETW FileIO

**Statut** : proposé — gate de conception (`SPRINT_8_ARBITRAGE_LOT3.md`, B.3). Aucune ligne de code ETW avant lecture.
**Date** : 2026-10-15. **Mesures** : `agent/tools/EtwFileIoProbe`, run CI 38805679885 (Windows Server 2025, 4 vCPU, administrateur), 60 s par phase ; la copie est synthétique et volontairement lourde (≈ 1 300 fichiers de 64 Ko par seconde) : c'est un plafond, pas un poste typique.

## Ce que disent les chiffres (B.2)

| Mots-clés | Repos (év./s) | Copie (év./s) | CPU de la session, copie | Perdus | Livraison p50 / p95 repos | Copie |
|---|---|---|---|---|---|---|
| FileIOInit | 5 525 | 42 981 | 1,7 % d'un cœur | 0 | 378 / 998 ms | 77 / 168 ms |
| **FileIOInit + DiskFileIO** | 5 660 | 44 084 | 1,7 % | 0 | 415 / 1 008 ms | 76 / 159 ms |
| + FileIO (fin d'opération) | 9 225 | 88 189 | 3,0 % | 0 | 187 / 942 ms | 65 / 178 ms |

L'agent actuel sur la même copie, dans un dossier qu'il surveille : **46,6 % d'un cœur** (3,3 % au repos). La session ETW ajoute donc environ 1/25 de ce que l'agent coûte déjà. Analyse d'un événement par TraceEvent : ≈ 0,4 µs. Filtre canari (préfixe de dossier + recherche exacte) : **10 ns**.

**PID** (opérations du rédacteur sur les canaris) : Write, Rename et Delete portent le PID du rédacteur **dans 100 % des cas, 0 PID faux**. Mais avec DiskFileIO, des Write (≈ 1 sur 3) et tous les SetInfo d'un canari arrivent avec le **PID 4 (System)** : c'est l'écriture différée du cache. Les prendre serait un PID faux. **Nom de fichier** : FileIOInit seul ne nomme que 38 % des Write, et **0 sur 409** écritures sur un handle ouvert avant la session. Avec DiskFileIO, TraceEvent résout les noms (événements FileIO/Name et rundown) : 100 %, et 220 sur 221 pour ce handle préexistant.
**Sans droits administrateur** (poste de développement, non élevé) : la session noyau est refusée (`UnauthorizedAccessException`), et le `FileSystemWatcher` voit la modification d'un canari en 10 à 16 ms.

## Décisions proposées

1. **Mots-clés : FileIOInit + DiskFileIO**. DiskFileIO est indispensable pour le nom des fichiers déjà ouverts. FileIO (OperationEnd) double le volume pour un statut NT dont l'attribution n'a pas besoin.
2. **Session séparée** de celle d'EXFIL WATCH. Un volume dix fois supérieur demande des tampons dimensionnés à part. Un retard de lecture FileIO ne doit pas retarder les événements réseau. Et l'échec de l'une ne doit pas éteindre l'autre module : chaque module publie son propre état. Coût : une session « system logger » de plus, sur 8 disponibles par machine.
3. **Filtrage dans le callback, sans attente.** Le callback ne fait qu'un test de préfixe, puis une recherche dans un ensemble immuable des chemins de canaris, remplacé en bloc à chaque régénération. Seuls les succès quittent le fil ETW, vers un canal dédié de 1 024 places en `DropNewest`, dont les pertes sont comptées et envoyées dans le heartbeat. Le fil ETW n'attend jamais : AGT-AUD-002 a montré ce que coûte une attente sur ce chemin. Le canal actuel de SENTINEL (10 000, `DropOldest`) n'en reçoit plus que des événements de canari. La clé du déduplicateur devient `chemin | opération | PID` : deux processus sur le même canari ne fusionnent jamais.
4. **Règle d'attribution : un PID faux est pire qu'un PID absent.** On attribue seulement à partir des événements Create, Write, SetInfo, Delete et Rename dont le PID n'est ni 0 ni 4. Un événement attribué à System n'attribue rien : l'alerte part avec « attribution indisponible ». On ne fait plus de requête Restart Manager, puisque le PID est dans l'événement.
5. **`FileSystemWatcher` conservé**, comme repli et comme contrôle. Sans droits administrateur, ou si la session échoue : détection par le watcher, sans attribution, et SENTINEL publié en `degraded / attribution_unavailable` (nouveau code, GRID et console ; la posture devient orange). Avec l'ETW : un canari vu par le watcher sans événement ETW dans les 2 s compte comme une perte ETW, envoyée dans le heartbeat.
6. **Plafond CPU sur un vieux poste.** Le consommateur mesure son temps CPU sur des fenêtres de 10 s. Au-delà de 5 % d'un cœur pendant 60 s, ou si des événements sont perdus : la session est arrêtée, SENTINEL repasse en repli watcher, en `degraded / attribution_suspended_load`, et une nouvelle tentative a lieu 10 min plus tard. Projection : un poste deux à quatre fois plus lent que le runner reste sous 1 % au repos ; la copie synthétique lourde serait à 3,5–7 %, ce qui déclencherait le repli.

## Point ouvert, à arbitrer : déclencheur ou source d'attribution ?

A.1 a fait de l'ETW le déclencheur, au motif qu'une requête **après** le watcher reproduirait le retard. Ce motif tenait pour le Restart Manager, interrogé après la libération du handle. Il ne tient plus pour l'ETW : le PID est dans l'événement, même lu tard. En revanche, la mesure montre une **livraison ETW de 378 ms en médiane et d'environ 1 s au p95 au repos** : les tampons temps réel se vident environ une fois par seconde. Le watcher, lui, met 10 à 16 ms. Faire de l'ETW le seul déclencheur ralentirait donc la détection.

Je recommande ceci : l'alerte part au **premier** des deux, watcher ou ETW, et l'attribution est complétée par l'ETW (attente de 1,5 s au plus, sinon « indisponible »). `TraceEventSession.Flush()` à cadence courte pourrait réduire la latence ; c'est à mesurer, avec son coût.

## Mesure du résultat

AttributionBench est rejoué **à l'identique** : six patrons, 20 itérations, mêmes arguments, et une colonne « PID correct » séparée de « PID faux ». Mesure d'avant : **0/120**, et 0/60 sur les patrons 3, 5 et 6. Rejoué ce soir à 0 ms de délai consommateur : de nouveau **0/120**, patron 3 compris. Mesure d'après : le même tableau, plus le taux sur 3, 5 et 6, plus le nombre de PID faux (cible : 0).

Avant la mesure d'après, les contrôles 1 et 2 de l'instrument sont reconstruits (AGT-BENCH-001) ; sinon les deux chiffres ne sont pas comparables. EtwFileIoProbe est rejoué sur un vrai poste ancien dès qu'on en a un. Le run CI n'est pas un poste d'hôpital : pas d'antivirus tiers, disque rapide, aucun utilisateur.
