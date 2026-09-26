#!/usr/bin/env python3
"""Every form that POSTs from a view must carry an anti-forgery token.

    ./tools/check-antiforgery.py

A form that POSTs without one is a CSRF hole: any third-party page can submit it with the
visitor's cookies attached, and nothing about it looks wrong in the browser or the server log.
The site already had the pieces to prevent that - Html.PostLink emits the token itself, and
[ValidateAntiForgeryToken] rejects a request without it - but nothing checked that the two ends
were actually connected, and several PlanetWars forms had the attribute on neither end.

**Html.PostLink and Ajax.BeginForm are not inspected.** PostLink builds the token into the markup
it generates, and Ajax.BeginForm is MVC's own helper; this looks only at forms written by hand in
a view, because those are the ones where the token is a thing somebody has to remember.

The token is looked for anywhere inside the form element. Razor makes exact parsing unreasonable -
a form can open in one @if branch and close in another - so a form is taken to run to its next
</form>, and an unclosed one is reported rather than guessed at.

**There is a baseline, and it is not an endorsement.** Writing this check turned up 25 such forms
outside PlanetWars. They are recorded in check-antiforgery-baseline.txt as a count per file, so
the check fails on anything NEW while the existing ones stay visible. They are NOT triaged: some
are search and filter forms that POST a query and change nothing, where a forged request does
nothing worth doing, and others are real. Working out which is which means reading each form and
its action, and belongs with whoever fixes them. A count rather than a line number because line
numbers move for unrelated reasons; the point is that the number can only go down.
"""
import re
import subprocess
import sys

FORM_OPEN = re.compile(r"<form\b[^>]*>", re.IGNORECASE | re.DOTALL)
TOKEN = re.compile(r"AntiForgeryToken\s*\(", re.IGNORECASE)
METHOD_POST = re.compile(r"""method\s*=\s*["']?\s*post""", re.IGNORECASE)


def views():
    listed = subprocess.run(["git", "ls-files", "*.cshtml"], capture_output=True, text=True, check=True)
    return [line for line in listed.stdout.splitlines() if line]


def failures_in(path):
    with open(path, encoding="utf-8-sig") as handle:
        text = handle.read()

    found = []
    for match in FORM_OPEN.finditer(text):
        if not METHOD_POST.search(match.group(0)):
            continue
        line = text.count("\n", 0, match.start()) + 1
        close = text.find("</form>", match.end())
        if close < 0:
            found.append((line, "form opens with method=post and never closes; cannot tell whether it has a token"))
            continue
        if not TOKEN.search(text[match.end():close]):
            found.append((line, "form posts without @Html.AntiForgeryToken()"))
    return found


BASELINE = "tools/check-antiforgery-baseline.txt"


def baseline():
    allowed = {}
    try:
        with open(BASELINE, encoding="utf-8") as handle:
            for line in handle:
                line = line.split("#", 1)[0].strip()
                if not line:
                    continue
                path, count = line.rsplit(" ", 1)
                allowed[path] = int(count)
    except FileNotFoundError:
        pass
    return allowed


def main():
    update = "--update" in sys.argv[1:]
    allowed = baseline()
    counts = {}
    detail = {}
    for path in views():
        found = failures_in(path)
        if found:
            counts[path] = len(found)
            detail[path] = found

    if update:
        with open(BASELINE, "w", encoding="utf-8") as handle:
            handle.write("# Forms that POST without an anti-forgery token, per file, as of when\n")
            handle.write("# tools/check-antiforgery.py was written. See that file: this records what\n")
            handle.write("# was already there so new ones fail, and is not a statement that any of\n")
            handle.write("# them is fine. Regenerate with --update only when REMOVING entries.\n")
            for path in sorted(counts):
                handle.write("%s %d\n" % (path, counts[path]))
        print("recorded %d file(s)" % len(counts))
        return 0

    bad = 0
    for path in sorted(counts):
        if counts[path] <= allowed.get(path, 0):
            continue
        for line, why in detail[path]:
            print("%s:%d: %s" % (path, line, why))
        print("  %s: %d untokened post form(s), baseline allows %d"
              % (path, counts[path], allowed.get(path, 0)))
        bad += 1

    fixed = [p for p in allowed if counts.get(p, 0) < allowed[p]]
    if fixed and not bad:
        print("these files improved on the baseline - rerun with --update to record it:")
        for path in sorted(fixed):
            print("  %s: %d, baseline says %d" % (path, counts.get(path, 0), allowed[path]))

    if bad:
        print("\n%d file(s) gained a form that POSTs without an anti-forgery token." % bad)
        print("Add @Html.AntiForgeryToken() inside the form, and [ValidateAntiForgeryToken] on the action.")
        return 1
    print("no view POSTs without an anti-forgery token beyond the recorded baseline "
          "(%d form(s) in %d file(s))" % (sum(allowed.values()), len(allowed)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
