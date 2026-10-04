#!/usr/bin/env python3
"""An action that writes to the database must not be reachable by GET.

    ./tools/check-get-writes.py

The link-shaped half of tools/check-antiforgery.py. That one looks at forms; this one looks at
the other end, because a link cannot carry a token and never could - an <a href> is a GET, so the
only defence is that the action refuses one. Nothing about a state-changing GET looks wrong in a
view: it is an ordinary link, and a third-party page can fire it with the visitor's cookies by
being loaded.

**What counts as a write is the EF/LINQ-to-SQL call, not a guess**: SaveChanges, SubmitChanges,
InsertOnSubmit, InsertAllOnSubmit, DeleteOnSubmit, DeleteAllOnSubmit - and EntityFramework
.Extensions' bulk pair, `.Update(x => new ...)` and `.Delete()`, which issue SQL straight at the
server and never go near SaveChanges. Those two were missing at first, and what they hid was
PlanetwarsAdmin's ResetRatings: a GET that rewrote every PlanetWars battle and rating in the
database. `Update` is matched with a word boundary, so UpdateLastRead and UpdateMission are not
mistaken for it. That is deliberately narrow and checkable. It means this does NOT see an action whose only effect is on the lobby server -
Tourney's JoinBattle and RemoveBattle were of that kind, found by reading rather than by this -
and it does not see writes made through a helper it calls. A check that is honest about its edges
is worth more than one that guesses at them.

An action is reachable by GET unless it carries [HttpPost]. Every action that writes and is
reachable must say which it is, on the method:

    [WritesOnGetByDesign("records that the thread was read")]
    [WritesOnGetNotYetFixed("the vote links are raw HTML built in a helper")]

The second kind is counted and printed every run. It is a list that shrinks, not a decision.
"""
import re
import subprocess
import sys

SIGNATURE = re.compile(
    r"^[ \t]*public[ \t]+(?:async[ \t]+)?"
    r"(?:ActionResult|Task<ActionResult>|IActionResult|Task<IActionResult>)[ \t]+(\w+)[ \t]*\(",
    re.MULTILINE)
WRITE = re.compile(r"\b(?:SaveChanges|SubmitChanges|InsertOnSubmit|InsertAllOnSubmit"
                   r"|DeleteOnSubmit|DeleteAllOnSubmit|Update|Delete)\s*\(")
# Any method at all, not only actions - the helpers an action can call. Used to answer
# "does this name, called from an action, end in a write?"
ANY_METHOD = re.compile(
    r"^[ \t]*(?:public|private|protected|internal)[ \t]+(?:static[ \t]+)?(?:async[ \t]+)?"
    r"[\w<>,\[\]\.\?]+[ \t]+(\w+)[ \t]*\(",
    re.MULTILINE)

# A call to something by name. Deliberately crude: it matches `Foo(` and `Bar.Foo(`, and the
# caller only follows names it has actually seen defined in these files, so the crudeness costs
# false FOLLOWS rather than false positives.
CALL = re.compile(r"\b(\w+)\s*\(")

HTTP_POST = re.compile(r"\[\s*HttpPost\s*[\]\(]")
BY_DESIGN = re.compile(r'\[WritesOnGetByDesign\("([^"]*)"\)\]')
NOT_YET = re.compile(r'\[WritesOnGetNotYetFixed\("([^"]*)"\)\]')


def controllers():
    # BOTH controller directories, because both are deployed. ZeroKWeb.Host ships
    # HarnessController alongside every linked site controller, so a GET that writes in it is as
    # live as one in Zero-K.info/Controllers - and this glob did not look at it. The limit was a
    # glob rather than a decision; it finds nothing new today, and now it would.
    listed = subprocess.run(["git", "ls-files",
                             "Zero-K.info/Controllers/*.cs", "ZeroKWeb.Host/Controllers/*.cs"],
                            capture_output=True, text=True, check=True)
    return [line for line in listed.stdout.splitlines() if line]


def body_of(text, start):
    """The method body, by brace matching. Returns None if it does not close."""
    open_brace = text.find("{", start)
    if open_brace < 0:
        return None
    depth = 0
    for index in range(open_brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[open_brace:index]
    return None


def attributes_above(text, start):
    """The attribute lines immediately above a signature, stopping at a blank line or code."""
    head = text[:start].rstrip("\n")
    found = []
    for line in reversed(head.split("\n")):
        stripped = line.strip()
        if stripped.startswith("["):
            found.append(stripped)
        elif stripped.startswith("//") or stripped.startswith("/*") or stripped.startswith("*"):
            continue
        else:
            break
    return found


def writing_methods(paths):
    """Every method in these files that writes, directly or through another one of them.

    The original check read only the ACTION's own body, and that is how
    /Factions/LeaveFaction sat unnoticed: it is three lines, none of them a write, and the
    SaveChanges is one call away in PerformLeaveFaction. A GET that changes state is the
    whole subject of this check, so not seeing one call deep was not a detail.

    Transitive, not one level, because PerformLeaveFaction in turn calls
    ClansController.PerformLeaveClan. Only names DEFINED in these files are followed, so an
    unrelated framework method that happens to share a name is not chased.
    """
    bodies = {}
    for path in paths:
        with open(path, encoding="utf-8-sig") as handle:
            text = handle.read()
        for match in ANY_METHOD.finditer(text):
            body = body_of(text, match.end())
            if body is not None:
                bodies.setdefault(match.group(1), []).append(body)

    writes = {name for name, found in bodies.items()
              if any(WRITE.search(body) for body in found)}

    # Closure: a method that calls a writer is a writer. Repeat until nothing new appears -
    # the graph is small and this is clearer than ordering it by hand.
    changed = True
    while changed:
        changed = False
        for name, found in bodies.items():
            if name in writes:
                continue
            called = {c for body in found for c in CALL.findall(body)}
            if called & writes:
                writes.add(name)
                changed = True
    return writes


def scan(paths):
    writing, by_design, not_yet = [], [], []
    writers = writing_methods(paths)
    for path in paths:
        with open(path, encoding="utf-8-sig") as handle:
            text = handle.read()
        for match in SIGNATURE.finditer(text):
            body = body_of(text, match.end())
            if body is None:
                continue
            # Its own body, or a call to something that ends in a write.
            if not WRITE.search(body) and not ({c for c in CALL.findall(body)} & writers):
                continue
            attributes = attributes_above(text, match.start())
            # The attribute itself, not the word anywhere inside one. A reason that mentions
            # HttpPost - "needs HttpPost and a token" is the obvious thing to write - used to
            # read as the action HAVING it, and the action was then skipped entirely. Found by
            # writing exactly that sentence.
            if any(HTTP_POST.search(attribute) for attribute in attributes):
                continue
            line = text.count("\n", 0, match.start()) + 1
            where = (path, line, match.group(1))
            design = next((BY_DESIGN.search(a) for a in attributes if BY_DESIGN.search(a)), None)
            pending = next((NOT_YET.search(a) for a in attributes if NOT_YET.search(a)), None)
            if design:
                by_design.append(where + (design.group(1),))
            elif pending:
                not_yet.append(where + (pending.group(1),))
            else:
                writing.append(where)
    return writing, by_design, not_yet


# A check that looks at nothing passes. These scripts find their subjects through
# git ls-files, so a directory rename, a project move or a glob that stops matching
# leaves them scanning an empty list and reporting success - which is how a 314-item
# guard in tools/build-website.sh ran as a 0-item guard, green, for one commit.
def main():
    paths = controllers()
    if not paths:
        print("found no controllers - the check would pass by seeing nothing", file=sys.stderr)
        return 2

    unmarked, by_design, not_yet = scan(paths)

    for path, line, name, in unmarked:
        print("%s:%d: %s writes to the database and a GET can reach it" % (path, line, name))
    if unmarked:
        print("\n%d action(s) change state on a GET with nothing said about it." % len(unmarked))
        print("Add [HttpPost] and [ValidateAntiForgeryToken], and make the view use Html.PostLink")
        print("- or say why it is right, with [WritesOnGetByDesign(\"...\")] on the method.")
        return 1

    print("no action writes on a GET without saying why in %d controller(s)"
          " (%d by design, %d not yet fixed)" % (len(paths), len(by_design), len(not_yet)))
    if not_yet:
        print("\nstill reachable by GET, and should not be:")
        for path, line, name, reason in not_yet:
            print("  %s:%d  %s - %s" % (path, line, name, reason))
    return 0


if __name__ == "__main__":
    sys.exit(main())
