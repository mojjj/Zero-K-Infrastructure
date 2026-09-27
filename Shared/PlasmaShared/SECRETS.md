# Secrets in this repository

## How it is supposed to work, and mostly does

Secrets live in the database, in `MiscVar`, and are read through `ZkData/Secrets.cs`:

```csharp
public string GetNightwatchPassword()     => MiscVar.GetValue("NightwatchPassword");
public string GetSteamWebApiKey()         => MiscVar.GetValue("SteamWebApiKey");
public string GetGithubHookKey()          => MiscVar.GetValue("GithubHookKey");
public string GetSteamBuildPassword()     => MiscVar.GetValue("SteamBuildPassword");
public string GetGlacierSecretKey()       => MiscVar.GetValue("GlacierSecretKey");
public string GetNightwatchDiscordToken() => MiscVar.GetValue("NightwatchDiscordToken");
```

Nothing there is in source control, and a value can be changed without a deploy. **New secrets go
here.** Note `GetGithubHookKey` in particular: a GitHub credential, handled correctly. The repo
already knows how to do this.

## The one exception: `GlobalConst.CrashReportGithubToken`

`Shared/PlasmaShared/GlobalConst.cs` declares a live GitHub token as a compiled-in constant. It has
one consumer, `ChobbyLauncher/CrashReportHelper.cs`, which files crash reports with it.

It is assembled from two string literals rather than written as one, which is what a secret scanner
looks for. That is probably why it has survived: the scanners have not flagged it, so nothing has
forced the issue.

### Do not "fix" this by deleting the line

This is the trap, and it is worth being explicit because the change looks like a fix and is worse
than leaving it alone:

- The value stays in **git history**. `git log -p` on one file recovers it.
- Both this fork and upstream `ZeroK-RTS/Zero-K-Infrastructure` are **public**. Every fork and every
  clone carries its own copy, and rewriting history here would reach none of them.
- So you would end up with a token that is still valid, still published, and no longer visible to
  anyone reading the code — which removes the only thing currently prompting anyone to deal with it.

**The step that closes this is revoking the token at GitHub.** Nothing done in this repository
substitutes for that.

### It cannot be closed from this fork

The token belongs to a GitHub account this repository does not control. Rotation is upstream's to
do — `ZeroK-RTS/Zero-K-Infrastructure` — and this fork can only carry the note.

If you are upstream, the order that avoids a window where crash reporting is broken:

1. **Issue a replacement**, scoped as narrowly as filing crash reports needs. If the current one is a
   classic PAT with broad scopes, make the replacement fine-grained and limited to the one repository.
2. **Put the new value in `MiscVar`**, behind a `Secrets.cs` accessor like every other secret.
   `ChobbyLauncher` is a desktop client and may have no database access — if so, the right answer is
   probably that the client should not hold a token at all and should post crash reports through the
   site, which already has one.
3. **Ship it**, so the consumer uses the new value.
4. **Revoke the old token.** This is the step that matters; everything before it is preparation.

Check the old token's *last used* date before revoking — if it has been used from somewhere
unexpected, that is worth knowing. Revoking first and accepting broken crash reports in the meantime
is also a defensible choice; it is a trade, not a mistake.

### What becomes possible here once it is rotated

Small and mechanical, because there is one declaration and one consumer:

- move that consumer onto the `Secrets`/`MiscVar` pattern;
- add a CI check that fails when a token-shaped literal appears in source, so this cannot recur.

Both are deliberately **not** done yet. Moving the code first would make the problem look solved
while the published value stayed live, and the check would sit red against the current constant and
teach everyone to ignore it.

## A note on handling

While writing this, the token's value was deliberately not read, printed or moved; two attempts to
inspect it were refused, which was correct. Documenting an exposure does not require reproducing it,
and this file contains no part of the value.

## See also

- `ZkData/Secrets.cs` — the pattern to follow.
- `Zero-K.info/HOSTING.md` — the two `Web.config` input-filtering settings, and what is known about
  whether they can be restored.
