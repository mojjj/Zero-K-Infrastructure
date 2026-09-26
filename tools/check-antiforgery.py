#!/usr/bin/env python3
"""Every form that POSTs from a view must carry an anti-forgery token, or say why not.

    ./tools/check-antiforgery.py

A form that POSTs without one is a CSRF hole: any third-party page can submit it with the
visitor's cookies attached, and nothing about it looks wrong in the browser or the server log.
The site already had the pieces - Html.PostLink emits the token itself, [ValidateAntiForgeryToken]
rejects a request without it - but nothing checked that the two ends were connected, and a dozen
forms had neither.

**Html.PostLink and Ajax.BeginForm are not inspected.** PostLink builds the token into the markup
it generates, and Ajax.BeginForm is MVC's own helper; this looks only at forms written by hand in
a view, because those are the ones where the token is a thing somebody has to remember.

Not every POST is a state change. A search or a paging form posts a query and alters nothing, so a
forged request achieves nothing worth having, and a token there is ceremony. Those say so at the
form:

    @* csrf-exempt: paging only, Index writes nothing *@

A reason is required, and it is checked where the decision is visible rather than recorded in a
list somewhere else. This replaced a baseline file that carried 25 entries: a count far from the
code, which said nothing about which of them mattered.

The token is looked for anywhere inside the form element. Razor makes exact parsing unreasonable -
a form can open in one @if branch and close in another - so a form is taken to run to its next
</form>, and an unclosed one is reported rather than guessed at.
"""
import re
import subprocess
import sys

FORM_OPEN = re.compile(r"<form\b[^>]*>", re.IGNORECASE | re.DOTALL)
TOKEN = re.compile(r"AntiForgeryToken\s*\(", re.IGNORECASE)
EXEMPT = re.compile(r"csrf-exempt:\s*(\S.*?)\s*(?:\*@|-->|$)", re.IGNORECASE | re.MULTILINE)
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
        body = text[match.end():close]
        if TOKEN.search(body) or EXEMPT.search(body):
            continue
        found.append((line, "form posts without @Html.AntiForgeryToken()"))
    return found


def exemptions():
    out = []
    for path in views():
        with open(path, encoding="utf-8-sig") as handle:
            for number, line in enumerate(handle, 1):
                reason = EXEMPT.search(line)
                if reason:
                    out.append((path, number, reason.group(1)))
    return out


def main():
    bad = 0
    for path in views():
        for line, why in failures_in(path):
            print("%s:%d: %s" % (path, line, why))
            bad += 1

    if bad:
        print("\n%d form(s) POST without an anti-forgery token." % bad)
        print("Add @Html.AntiForgeryToken() inside the form and [ValidateAntiForgeryToken] on the")
        print("action - or, if it changes nothing, @* csrf-exempt: <why> *@ inside the form.")
        return 1

    waived = exemptions()
    print("every hand-written form that POSTs carries an anti-forgery token")
    if waived:
        print("\n%d exempted as changing nothing:" % len(waived))
        for path, line, reason in waived:
            print("  %s:%d  %s" % (path, line, reason))
    return 0


if __name__ == "__main__":
    sys.exit(main())
