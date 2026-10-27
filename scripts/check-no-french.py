#!/usr/bin/env python3
"""Fail if French can reach a user.

The console is English (owner's decision, 2026-10-25). Nothing French may appear anywhere in the
running system unless somebody deliberately switches language, so this looks at what actually
ships rather than at the source it came from:

  * the built console bundle (dashboard/dist). French translations are not bundled -- they are
    fetched as their own chunk only when French is turned on -- so the chunk that holds fr.json
    is skipped by name and everything else must be clean. A French string anywhere else is a
    string that leaked past i18n;
  * the GRID sources, where a message in an HTTPException reaches the console as-is;
  * the agent and installer, whose output the operator reads on the workstation;
  * the bench scripts, which are read during a demonstration.

It looks for accented letters and for a list of French words that have no English meaning, so
"action", "message" or "important" -- the same word in both languages -- raise nothing.

Run: python scripts/check-no-french.py [--verbose]
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

# Windows consoles default to cp1252, which cannot print the very characters this looks for.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = Path(__file__).resolve().parent.parent

# Letters that only appear in French here. The agent and GRID also carry Cameroonian place and
# person names, so a hit is reported with its line and judged by eye, not assumed.
ACCENTS = re.compile(r"[àâäçéèêëîïôöûùüÿœÀÂÄÇÉÈÊËÎÏÔÖÛÙÜŸŒ]")

# Words that are French and are not also English. Deliberately short: a long list catches
# "application", "service", "instance" and stops anybody trusting the check.
# Words that are unmistakably French. Deliberately short, and deliberately free of anything that
# also occurs in English prose ("plus", "important") or by chance inside minified JavaScript
# ("est", "sur", "qui"): a list that cries wolf is a list nobody reads.
FRENCH_WORDS = re.compile(
    r"\b("
    r"aucun|aucune|veuillez|introuvable|utilisateur|utilisateurs"
    r"|fichier|fichiers|dossier|enregistrement"
    r"|mot de passe|votre|vous|nouveau|nouvelle"
    r"|impossible de|il faut|n'est pas|ne peut pas"
    r"|wotre|poste de travail|en cours|terminé"
    r"|infirmier|infirmiere|accueil|laboratoire|imagerie|medicale|medicaux"
    r")\b",
    re.IGNORECASE,
)

# A chunk whose name says it is the French translation. It exists so the translations can be kept
# without shipping them to everyone, and it is the one place French belongs.
FRENCH_CHUNK = re.compile(r"^fr[-.][A-Za-z0-9_-]+\.js(\.map)?$")


# The one French word allowed anywhere: the French language's own name. A language is called
# what it calls itself, in any interface -- an English console offering "French" in the switcher
# would be the odd one out, not the correct one. It is stripped before a line is judged, so a line
# that also carries real French still fails.
ENDONYM = re.compile(r"Français")

# A line that has to name a French identifier the system is migrating away from -- an old account
# name being renamed, say -- marks itself `french-ok: <why>`. The reason is mandatory and is
# printed by --verbose, so an exemption is an argument somebody made, not a silent hole. It
# exempts the one line it sits on and nothing else.
ALLOWED = re.compile(r"french-ok:\s*\S")


class Target:
    def __init__(self, name: str, root: str, globs: list[str], skip: list[str] | None = None,
                 words: bool = True):
        self.name = name
        self.root = REPO / root
        self.globs = globs
        self.skip = skip or []
        # Minified JavaScript throws up French-looking fragments by chance, so the built bundle is
        # judged on accents alone. Nothing user-visible in it can be French without one.
        self.words = words


TARGETS = [
    # The French translations ship as their own chunk, fetched only when a user switches to
    # French. It is French on purpose and is the one thing here that is allowed to be: what this
    # target protects is the interface an English user is served, not the dictionary behind the
    # switch. Everything else in dist is still judged.
    Target("console bundle", "dashboard/dist", ["**/*.js", "**/*.html"],
           skip=["french-translations"], words=False),
    Target("GRID sources", "grid/src", ["**/*.py"]),
    Target("agent", "agent/src/RansomGuard.Agent.Core", ["**/*.cs"], skip=["/obj/", "/bin/"]),
    Target("agent service", "agent/src/RansomGuard.Agent.Service", ["**/*.cs"], skip=["/obj/", "/bin/"]),
    Target("installer", "agent/src/RansomGuard.Agent.Installer", ["**/*.cs"], skip=["/obj/", "/bin/"]),
    # `credentials.local.md` is the one file on the bench that holds passwords. It is gitignored
    # and never leaves this machine, and a failing line here is printed in full -- in the terminal
    # and in the CI log. Scanning it would publish the passwords it exists to keep.
    Target("bench scripts", "deployment/bench", ["**/*.ps1", "**/*.cmd", "**/*.py", "**/*.md"],
           skip=["credentials.local.md"]),
    # What the owner and the jury actually read. A French line here is French shown to a user just
    # as surely as one in the console; the rest of docs/ is design history and is left alone.
    Target("owner-facing docs", "docs",
           ["OWNER_TEST_GUIDE.md", "SYSTEM_STATUS.md", "testing/BENCH_TEST_RESULTS.md"]),
]


def offending_lines(path: Path, check_words: bool) -> list[tuple[int, str]]:
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []

    hits: list[tuple[int, str]] = []
    for number, raw in enumerate(text.splitlines(), start=1):
        if ALLOWED.search(raw):
            continue
        line = ENDONYM.sub("", raw)
        if ACCENTS.search(line) or (check_words and FRENCH_WORDS.search(line)):
            hits.append((number, raw.strip()[:160]))
    return hits


# A check that cannot fail is not a check. From the day it was written until 2026-10-26 the word
# list compiled to "\x08(...)\x08" -- a literal backspace where \b was meant -- so it matched
# nothing, and the run reported "no French reaches a user" over four French error messages the
# GRID was sending to the console. Nothing noticed, because a green gate looks the same whether it
# is working or dead. These samples run before every scan and cost nothing.
MUST_MATCH = [
    ("Utilisateur introuvable", "a GRID error message"),
    ("detail=\"Mot de passe actuel incorrect\"", "French inside a code line"),
    ("the `infirmier` account", "an account name in a comment"),
    ("Vérification du paquet", "an accent"),
]
MUST_NOT_MATCH = [
    ("the application sends a message", "English that resembles French"),
    ("important action, service instance", "words shared by both languages"),
    ('["dossier_patient"] = GeneratePatientRecord,', "a compatibility key, not prose"),
    ("Français", "the language's own name"),
]


def self_test() -> list[str]:
    """Prove the patterns still match French and still ignore English."""
    broken = []
    for sample, why in MUST_MATCH:
        line = ENDONYM.sub("", sample)
        if not (ACCENTS.search(line) or FRENCH_WORDS.search(line)):
            broken.append(f"should have been caught ({why}): {sample!r}")
    for sample, why in MUST_NOT_MATCH:
        line = ENDONYM.sub("", sample)
        if ACCENTS.search(line) or FRENCH_WORDS.search(line):
            broken.append(f"should have been ignored ({why}): {sample!r}")
    return broken


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verbose", action="store_true", help="list every file scanned")
    args = parser.parse_args()

    broken = self_test()
    if broken:
        print("the check itself is broken; it would pass over French without seeing it:\n")
        for line in broken:
            print(f"  {line}")
        return 2
    if args.verbose:
        print(f"[ok] self-test: {len(MUST_MATCH)} French and "
              f"{len(MUST_NOT_MATCH)} non-French samples judged correctly")

    total_files = 0
    failures: list[str] = []

    for target in TARGETS:
        if not target.root.exists():
            if target.name == "console bundle":
                print(f"[SKIP] {target.name}: {target.root} does not exist "
                      "(run `npm run build` in dashboard/ to check what actually ships)")
                continue
            print(f"[SKIP] {target.name}: {target.root} does not exist")
            continue

        scanned = 0
        for pattern in target.globs:
            for path in sorted(target.root.glob(pattern)):
                if not path.is_file():
                    continue
                posix = path.as_posix()
                if any(part in posix for part in target.skip):
                    continue
                if FRENCH_CHUNK.search(path.name):
                    # The French translation chunk. This is where French is meant to be.
                    continue

                scanned += 1
                total_files += 1
                for number, line in offending_lines(path, target.words):
                    failures.append(f"{path.relative_to(REPO).as_posix()}:{number}: {line}")

        if args.verbose:
            print(f"[ok] {target.name}: {scanned} file(s) scanned")

    print(f"\nscanned {total_files} file(s) across {len(TARGETS)} area(s)")

    if failures:
        print(f"\n{len(failures)} line(s) that would show French to a user:\n")
        for failure in failures[:80]:
            print(f"  {failure}")
        if len(failures) > 80:
            print(f"  ... and {len(failures) - 80} more")
        return 1

    print("no French reaches a user")
    return 0


if __name__ == "__main__":
    sys.exit(main())
