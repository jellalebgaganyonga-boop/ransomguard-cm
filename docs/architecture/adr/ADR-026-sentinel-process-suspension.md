# ADR-026 — SENTINEL suspend le processus qui a écrit un canari

**Statut** : proposé, 2026-10-21, à lire avant tout code (plan de finition, J1 § 1.2). Court par choix : il répond aux deux questions posées dans `SPRINT_8.md` (LOT 5, A1) et fixe la règle d'action.
**Dépend de** : ADR-025, commit 4 (`9de3762`, branche `review/etw-4-attribution`, en relecture). Précision honnête : le commit 3 ne nomme aucun processus, il corrèle watcher et ETW ; le PID vient du `CanaryAttributor` du commit 4. Sans le commit 4 fusionné, ce moteur n'a rien à suspendre.

## 1. Contexte

Un canari est un fichier que personne de légitime n'écrit. Quand l'ETW voit une écriture dessus et qu'un seul processus l'a faite, on sait qui chiffre. L'agent alerte aujourd'hui ; il n'agit pas. Suspendre plutôt que tuer : les preuves et les clés éventuelles restent en mémoire, et un logiciel d'imagerie tué en plein examen est un incident en soi.

## 2. Décisions

**2a. La seule condition d'action : une attribution `resolved`.** Le moteur suspend un processus si, et seulement si :
- l'attribution de l'alerte est `resolved` (un seul écrivain dans la fenêtre de 2 s, ADR-025 § 3) ;
- le PID n'est ni 0, ni 4, ni celui de l'agent (`Environment.ProcessId`) ;
- la paire (PID, heure de création) est **relue** juste avant d'agir (`Process.StartTime`) et ne diffère pas de plus de 50 ms de celle de l'attribution ; sinon rien n'est fait, raison `pid_reused` ;
- le processus peut être ouvert avec `PROCESS_SUSPEND_RESUME` ; sinon rien n'est fait, raison `access_denied` (processus protégé).

`unavailable` et `ambiguous` ne déclenchent jamais une suspension : on ne frappe pas au hasard. Le lecteur qui ouvre un canari n'est jamais concerné : le commit 4 n'attribue que les écritures.

**2b. Le mécanisme : `NtSuspendProcess`, pas `SuspendThread` par thread.** Réponse à la question 1 de `SPRINT_8.md`. `NtSuspendProcess` (ntdll, P/Invoke, droit `PROCESS_SUSPEND_RESUME`) suspend tous les threads du processus en un appel noyau, y compris ceux créés entre l'énumération et l'action ; `SuspendThread` thread par thread laisse cette fenêtre ouverte, exige un handle par thread, et un thread qui naît pendant la boucle continue de chiffrer. `NtSuspendProcess` n'est pas documentée mais elle est stable depuis Windows XP ; c'est ce qu'utilise Process Explorer. La reprise est `NtResumeProcess`, symétrique.

**2c. Réversible, et jamais de reprise automatique.** Réponse à la question 2. Si la reprise échoue, le processus reste suspendu : la suspension est un compteur sur ses threads, elle ne dépend pas d'un handle que l'agent tiendrait ; elle ne survit pas à un redémarrage du poste. Je ne propose **pas** de minuteur qui reprend tout seul : reprendre automatiquement un chiffreur parce que personne n'a répondu est le pire résultat possible. Le minuteur de sûreté que je propose est un **minuteur d'escalade** :
- chaque suspension est **persistée** (table `SuspendedProcesses` : PID, heure de création, nom, image, alerte d'origine, horodatage) ; au redémarrage de l'agent, chaque entrée est relue : si le processus existe encore avec la même heure de création, il reste enregistré et déclaré ; sinon l'entrée passe `ended` ;
- le heartbeat transmet `suspended_processes` (nombre, âge du plus ancien) ; la console l'affiche dans la posture ;
- sans décision humaine après `Sentinel:Response:EscalationMinutes` (valeur explicite dans `appsettings.json`, proposée à 60 ; pas de défaut silencieux dans le code), l'agent lève une nouvelle alerte Critical « processus suspendu depuis N min sans décision », et la répète à chaque échéance ;
- une reprise qui échoue est retentée 3 fois sur 30 s, puis journalisée Critical + audit `ProcessResumeFailed`, et la console l'affiche : « reprise impossible, intervention manuelle (redémarrage du poste) ».

**2d. Deux chemins, et ce que le second ne fait pas.**

| Situation | Réponse |
|---|---|
| Attribution `resolved`, 1 canari | Suspendre, audit, alerte « processus suspendu » |
| Attribution `resolved`, plusieurs canaris | Suspendre, alerte Critical |
| Attribution `unavailable` / `ambiguous`, 1 canari | Alerte High, isolement **proposé** (texte dans l'alerte), aucune action |
| Sans attribution, rafale : ≥ 3 canaris distincts en 60 s | Alerte Critical `isolation_recommended` ; **pas d'isolement automatique** |

La dernière ligne s'écarte du tableau de `SPRINT_8.md` (isolement automatique) parce que l'isolement réseau n'existe pas et n'entre pas dans les 4 jours (plan de finition, « Ce qui ne sera pas fini »). L'écrire autrement serait mentir. Seuil de rafale proposé : 3 canaris distincts en 60 s, mesurés sur le corrélateur existant ; à ajuster au banc.

**2e. Ce que la console voit, sans la route du commit 5.** L'alerte de réponse est une alerte ordinaire (type `SENTINEL_PROCESS_SUSPENDED`, sévérité Critical, détails : `pid`, `process_name`, `image_path`, `process_created_at`, `response_to` = `client_message_id` de l'alerte d'origine), envoyée par la file d'alertes existante. La route de complément d'attribution (commit 5) reste en pause. La reprise est une commande de la console (`resume_process`, J1 § 1.3) ; l'agent ne reprend que si la paire (PID, heure de création) correspond encore.

**2f. Audit.** `ProcessSuspendIntent` avant l'appel (AGT-AUD-002 : l'intention est écrite avant l'action), puis `ProcessSuspended` ou `ProcessSuspendFailed` avec la raison ; `ProcessResumed` / `ProcessResumeFailed` ; chaque entrée porte la paire (PID, heure de création) et l'alerte d'origine.

## 3. Tests prévus

- Unitaires (sans ETW réel) : le test lance un vrai processus enfant (`cmd /c ping -n 60 127.0.0.1`), fabrique les `FileIoRecord` d'écriture avec son PID, le moteur le suspend (ses threads sont en `Wait` / `Suspended`), puis le reprend. Un enregistrement de lecture seule ne suspend jamais. Le PID de l'agent n'est jamais suspendu. Une heure de création antérieure au vrai départ du processus donne `pid_reused` et aucune action. Un processus terminé entre l'attribution et l'action donne `process_exited` et aucune action.
- `agent-full` (service installé, ETW réel, administrateur) : un enfant lancé par l'étape écrit un canari ; en 5 s au plus il est suspendu ; l'étape le constate, le tue, et lit l'entrée d'audit.
- Mutation : sans la relecture de la paire, le test `pid_reused` passe au rouge ; sans l'exclusion du PID de l'agent, le test correspondant passe au rouge.

## 4. Honnêteté, à reprendre dans la doc

La suspension est fiable quand, et seulement quand, l'ETW a vu l'écriture et qu'un seul processus l'a faite. Sans ETW (droits manquants, pas de slot de session), ou pour une écriture différée portée par le PID 4, ou quand deux processus écrivent dans la même fenêtre, l'agent alerte et **n'agit pas**. Elle n'est jamais présentée comme garantie. Limite connue : un client de synchronisation ou une restauration qui réécrit un canari serait suspendu ; la reprise existe pour cela, et aucune liste d'exclusion n'est prévue pour l'instant (une exclusion par chemin d'image serait un contournement pour un attaquant qui se nomme comme le client de synchronisation).

## 5. Dettes ouvertes par cet ADR

- AGT-ISO-001 : isolement réseau du poste (ligne 4 du tableau), non implémenté.
- AGT-RESP-001 : la reprise d'un processus que `NtResumeProcess` ne peut pas reprendre exige un redémarrage du poste.
