#!/usr/bin/env python3
"""Every form that POSTs from a view must carry an anti-forgery token, or say why not.

    ./tools/check-antiforgery.py

A form that POSTs without one is a CSRF hole: any third-party page can submit it with the
visitor's cookies attached, and nothing about it looks wrong in the browser or the server log.
The site already had the pieces - Html.PostLink emits the token itself, [ValidateAntiForgeryToken]
rejects a request without it - but nothing checked that the two ends were connected, and a dozen
forms had neither.

**Html.PostLink is not inspected**, because it builds the token into the markup it generates.

**Ajax.BeginForm emits a plain <form> with nothing in it**, unlike PostLink. An earlier version
of this comment put the two side by side as though being MVC's own helper meant it handled the
token, which it does not. It then said that teaching this script to read `@using (...)` blocks
would flag fifteen paging forms to no purpose - true of the rule above, and it stopped the
question there for longer than it should have, because there is a second rule that needs no
judgement at all.

**A form must carry a token exactly when the action it posts to validates one.** That is decidable
from the two ends: the view says whether a token is sent, the controller says whether one is
checked. Neither end is asked to guess whether a request "changes something", so there is nothing
to exempt and no list to keep. Every form is checked this way - hand-written and Ajax.BeginForm
alike, twenty of the latter that nothing looked at before.

The direction that bites is a form posting to a validating action WITHOUT a token: that feature is
simply broken, answering 400 to its own users, and no check anywhere said so. It is the failure
somebody causes by adding [ValidateAntiForgeryToken] to an action - which is exactly what closing
ForumController.SubmitPost did, and the two forms that post to it were confirmed by hand because
nothing could answer it.

The opposite direction - a token sent to an action that ignores it - is the SubmitPost bug seen
from the view, and it is left to the other two scripts on purpose. They already require every
action that writes to validate or to say why its caller cannot, so a decorative token can only
survive on an action that writes nothing. It is reported below rather than failed, because
removing a harmless hidden field is not worth a rule, and today the list is empty.

That list being empty is worth one sentence, because the first draft of this comment said
otherwise. A survey written before the check claimed ContributionsIndex's paging form carried
one; it does not. The @Html.AntiForgeryToken() eleven lines below that form belongs to a
different, hand-written one, and the survey had looked at a fixed window of characters rather
than at the form's actual extent. Writing the report and running it is what said so.

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

# Who a form posts to. Url.Action("Action", "Controller") in a hand-written form, and
# Ajax.BeginForm("Action", "Controller") - whose controller argument is optional, defaulting to
# the controller that owns the view, which is the directory the view sits in.
URL_ACTION = re.compile(r'Url\.Action\(\s*"(\w+)"\s*(?:,\s*"(\w+)")?')
AJAX_FORM = re.compile(r'Ajax\.BeginForm\(\s*"(\w+)"\s*(?:,\s*"(\w+)")?')
SIGNATURE = re.compile(
    r"^[ \t]*public[ \t]+(?:async[ \t]+)?"
    r"(?:ActionResult|Task<ActionResult>|IActionResult|Task<IActionResult>)[ \t]+(\w+)[ \t]*\(",
    re.MULTILINE)
VALIDATES = re.compile(r"\[\s*ValidateAntiForgeryToken\s*\]")


def validating_actions():
    """(controller, action) pairs whose method carries [ValidateAntiForgeryToken].

    The controller name is the file's, minus "Controller" - which is how a view names it too.
    """
    listed = subprocess.run(["git", "ls-files",
                             "Zero-K.info/Controllers/*.cs", "ZeroKWeb.Host/Controllers/*.cs"],
                            capture_output=True, text=True, check=True)
    paths = [line for line in listed.stdout.splitlines() if line]

    found, every = set(), set()
    for path in paths:
        with open(path, encoding="utf-8-sig") as handle:
            text = handle.read()
        controller = path.split("/")[-1][:-len("Controller.cs")]
        for match in SIGNATURE.finditer(text):
            # The attributes immediately above, stopping at a blank line or code. A multi-line
            # attribute's continuations are folded in, as in tools/check-get-writes.py.
            head = text[:match.start()].rstrip("\n")
            attributes, pending = [], []
            for line in reversed(head.split("\n")):
                stripped = line.strip()
                if stripped.startswith("["):
                    attributes.append(" ".join([stripped] + pending))
                    pending = []
                elif stripped.startswith("//") or stripped.startswith("/*") or stripped.startswith("*"):
                    continue
                elif pending or stripped.endswith("]"):
                    pending.insert(0, stripped)
                else:
                    break
            every.add((controller, match.group(1)))
            if any(VALIDATES.search(a) for a in attributes):
                found.add((controller, match.group(1)))
    return found, every


def targets_in(text, start, end, own_controller):
    """Which (controller, action) a form posts to, from the markup between start and end."""
    out = set()
    for match in URL_ACTION.finditer(text, start, end):
        out.add((match.group(2) or own_controller, match.group(1)))
    return out


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


def mismatches_in(path, validating):
    """Forms that post to a validating action and send no token.

    A form's extent is taken to its next </form> for hand-written markup, and for an
    Ajax.BeginForm - a Razor `@using` block - to the end of the braces it opens. Both are
    approximations that Razor makes unavoidable; both err by reading MORE text, so the mistake
    they can make is missing a fault rather than inventing one.
    """
    with open(path, encoding="utf-8-sig") as handle:
        text = handle.read()
    parts = path.split("/")
    own = parts[-2] if len(parts) > 1 else ""

    found = []

    for match in FORM_OPEN.finditer(text):
        if not METHOD_POST.search(match.group(0)):
            continue
        close = text.find("</form>", match.end())
        if close < 0:
            continue                      # already reported by failures_in
        # The target is named in the opening tag, not the body.
        for target in targets_in(text, match.start(), match.end(), own):
            if target in validating and not TOKEN.search(text[match.end():close]):
                found.append((text.count("\n", 0, match.start()) + 1, target))

    for match in AJAX_FORM.finditer(text):
        target = (match.group(2) or own, match.group(1))
        if target not in validating:
            continue
        # From the END of the BeginForm call, not from the match: the remaining arguments
        # routinely contain braces - `new { id = "form" }` is the usual last one - and starting
        # the brace walk inside those reads the anonymous object as the form body. Every one of
        # the five forms that actually carry a token was flagged that way.
        body = block_after(text, call_end(text, match.start()))
        if not TOKEN.search(body):
            found.append((text.count("\n", 0, match.start()) + 1, target))

    return found


def call_end(text, start):
    """The index just past the closing paren of the call beginning at or after `start`.

    String literals are skipped, so a paren or brace inside one is not counted.
    """
    index = text.find("(", start)
    if index < 0:
        return start
    depth, quote = 0, None
    while index < len(text):
        char = text[index]
        if quote:
            if char == "\\":
                index += 2
                continue
            if char == quote:
                quote = None
        elif char in "\"'":
            quote = char
        elif char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return index + 1
        index += 1
    return start


def decorative_in(path, validating, every_action):
    """Ajax forms that send a token to an action which does not validate it."""
    with open(path, encoding="utf-8-sig") as handle:
        text = handle.read()
    parts = path.split("/")
    own = parts[-2] if len(parts) > 1 else ""

    out = []
    for match in AJAX_FORM.finditer(text):
        target = (match.group(2) or own, match.group(1))
        # Unknown targets are skipped rather than guessed at: a view can name a controller this
        # glob does not read.
        if target not in every_action or target in validating:
            continue
        if TOKEN.search(block_after(text, call_end(text, match.start()))):
            out.append((path, text.count("\n", 0, match.start()) + 1) + target)
    return out


def block_after(text, start):
    """The `@using (...) { ... }` body that follows an Ajax.BeginForm call, by brace matching."""
    open_brace = text.find("{", start)
    if open_brace < 0:
        return ""
    depth = 0
    for index in range(open_brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[open_brace:index]
    return text[open_brace:]


def exemptions():
    out = []
    for path in views():
        with open(path, encoding="utf-8-sig") as handle:
            for number, line in enumerate(handle, 1):
                reason = EXEMPT.search(line)
                if reason:
                    out.append((path, number, reason.group(1)))
    return out


# A check that looks at nothing passes. These scripts find their subjects through
# git ls-files, so a directory rename, a project move or a glob that stops matching
# leaves them scanning an empty list and reporting success - which is how a 314-item
# guard in tools/build-website.sh ran as a 0-item guard, green, for one commit.
def main():
    scanned = views()
    if not scanned:
        print("found no .cshtml files - the check would pass by seeing nothing", file=sys.stderr)
        return 2

    validating, every_action = validating_actions()
    if not every_action:
        print("found no controller actions - the second rule would pass by seeing nothing",
              file=sys.stderr)
        return 2

    bad = 0
    for path in scanned:
        for line, why in failures_in(path):
            print("%s:%d: %s" % (path, line, why))
            bad += 1

    if bad:
        print("\n%d form(s) POST without an anti-forgery token." % bad)
        print("Add @Html.AntiForgeryToken() inside the form and [ValidateAntiForgeryToken] on the")
        print("action - or, if it changes nothing, @* csrf-exempt: <why> *@ inside the form.")
        return 1

    broken = 0
    for path in scanned:
        for line, (controller, action) in mismatches_in(path, validating):
            print("%s:%d: this form posts to %s/%s, which validates an anti-forgery token,"
                  " and sends none" % (path, line, controller, action))
            broken += 1

    if broken:
        print("\n%d form(s) will answer 400 to their own users." % broken)
        print("Add @Html.AntiForgeryToken() inside the form - or, if the action should not be")
        print("validating, take [ValidateAntiForgeryToken] off it and say why on the method.")
        return 1

    decorative = []
    for path in scanned:
        decorative.extend(decorative_in(path, validating, every_action))

    waived = exemptions()
    print("every hand-written form that POSTs carries an anti-forgery token, and every form"
          " posting to one of the %d validating action(s) sends one (%d views read)"
          % (len(validating), len(scanned)))
    if waived:
        print("\n%d exempted as changing nothing:" % len(waived))
        for path, line, reason in waived:
            print("  %s:%d  %s" % (path, line, reason))
    if decorative:
        # Reported, not failed. A token sent to an action that ignores it is the shape of the
        # SubmitPost bug - but the other two scripts already require every action that WRITES to
        # validate or to say why its caller cannot, so what can still appear here writes nothing
        # and the hidden field is harmless.
        print("\n%d form(s) send a token the action does not validate:" % len(decorative))
        for path, line, controller, action in decorative:
            print("  %s:%d  -> %s/%s" % (path, line, controller, action))
    return 0


if __name__ == "__main__":
    sys.exit(main())
