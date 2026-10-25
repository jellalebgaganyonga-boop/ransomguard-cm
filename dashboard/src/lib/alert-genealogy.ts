/**
 * Reading order for the chain of processes behind a write (GENEALOGY).
 *
 * The agent sends the ancestors nearest-first -- parent, then grandparent -- because that is the
 * order a process tree is walked in. A human reads the chain the other way: explorer started cmd,
 * cmd started powershell, powershell wrote the file. Printing it as it arrives says the attacker
 * started Explorer, which is the opposite of what happened.
 */

import type { AlertGenealogy, GenealogyProcess } from '@/api/alerts';

/** Oldest ancestor first, the writer last. */
export function chainOrder(genealogy: AlertGenealogy): GenealogyProcess[] {
  return [...genealogy.ancestors].reverse().concat(genealogy.writer);
}
