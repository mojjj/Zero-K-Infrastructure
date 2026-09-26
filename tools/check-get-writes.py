#!/usr/bin/env python3
"""An action that writes to the database must not be reachable by GET.

    ./tools/check-get-writes.py

The link-shaped half of tools/check-antiforgery.py. That one looks at forms; this one looks at
the other end, because a link cannot carry a token and never could - an <a href> is a GET, so the
only defence is that the action refuses one. Nothing about a state-changing GET looks wrong in a
view: it is an ordinary link, and a third-party page can fire it with the visitor's cookies by
being loaded.

**What counts as a write is the EF/LINQ-to-SQL call, not a guess**: SaveChanges, SubmitChanges,
InsertOnSubmit, InsertAllOnSubmit, DeleteOnSubmit, DeleteAllOnSubmit. That is deliberately narrow
and checkable. It means this does NOT see an action whose only effect is on the lobby server -
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
                   r"|DeleteOnSubmit|DeleteAllOnSubmit)\s*\(")
BY_DESIGN = re.compile(r'\[WritesOnGetByDesign\("([^"]*)"\)\]')
NOT_YET = re.compile(r'\[WritesOnGetNotYetFixed\("([^"]*)"\)\]')


def controllers():
    listed = subprocess.run(["git", "ls-files", "Zero-K.info/Controllers/*.cs"],
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


def scan():
    writing, by_design, not_yet = [], [], []
    for path in controllers():
        with open(path, encoding="utf-8-sig") as handle:
            text = handle.read()
        for match in SIGNATURE.finditer(text):
            body = body_of(text, match.end())
            if body is None or not WRITE.search(body):
                continue
            attributes = attributes_above(text, match.start())
            if any("HttpPost" in attribute for attribute in attributes):
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


def main():
    unmarked, by_design, not_yet = scan()

    for path, line, name, in unmarked:
        print("%s:%d: %s writes to the database and a GET can reach it" % (path, line, name))
    if unmarked:
        print("\n%d action(s) change state on a GET with nothing said about it." % len(unmarked))
        print("Add [HttpPost] and [ValidateAntiForgeryToken], and make the view use Html.PostLink")
        print("- or say why it is right, with [WritesOnGetByDesign(\"...\")] on the method.")
        return 1

    print("no action writes on a GET without saying why (%d by design, %d not yet fixed)"
          % (len(by_design), len(not_yet)))
    if not_yet:
        print("\nstill reachable by GET, and should not be:")
        for path, line, name, reason in not_yet:
            print("  %s:%d  %s - %s" % (path, line, name, reason))
    return 0


if __name__ == "__main__":
    sys.exit(main())
