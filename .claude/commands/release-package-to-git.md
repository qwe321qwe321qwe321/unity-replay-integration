---
description: Bump package.json version, update CHANGELOG.md, check README.md for needed doc updates, then commit and push
argument-hint: [patch|minor|major|x.y.z] [optional release summary]
---

You are cutting a release for this Unity package (`com.pedev.unity-replay-integration`). Follow these steps in order. Do not skip steps or reorder them.

## 0. Gather context

- Read `package.json` to get the current version.
- Run `git log --oneline -20` and `git diff` / `git status` to see what's changed since the last commit (uncommitted working-tree changes are the release payload — there is no separate "last release tag", the last CHANGELOG entry marks the last release).
- Read `CHANGELOG.md` to see the latest documented version and its formatting conventions (Traditional Chinese, `### Added` / `### Changed` / `### Fixed` / `### Removed` sections, Keep a Changelog style).
- Note: if `package.json`'s version is already ahead of the latest `CHANGELOG.md` entry (i.e. earlier releases were committed without a changelog update), do not try to backfill history — just proceed from the current package.json version forward, and mention the gap to the user once.

## 1. Determine the new version number

- Parse `$ARGUMENTS`:
  - If it looks like a full semver (`x.y.z`), use it directly.
  - If it's one of `patch` / `minor` / `major`, bump accordingly from the current `package.json` version.
  - If empty, infer the bump type from the size/nature of the change (new feature → minor, fix/tweak → patch, breaking change → major) and briefly state your reasoning.
- If genuinely ambiguous, ask the user with `AskUserQuestion` rather than guessing.

## 2. Update `package.json`

- Edit the `"version"` field to the new version. Nothing else in this file should change.

## 3. Update `CHANGELOG.md`

- Insert a new section directly below the header/preamble and above the previous latest version, following the exact structure of existing entries:
  ```
  ## [x.y.z] - YYYY-MM-DD
  ### Added
  - ...
  ### Changed
  - ...
  ### Fixed
  - ...
  ```
  Omit any subsection (`Added`/`Changed`/`Fixed`/`Removed`) that has no entries for this release. Use today's date (check the current date rather than assuming).
- Write entries in Traditional Chinese to match the existing entries' language and tone, one bullet per notable change. Base the content on the actual diff/`git log`/`git status`, not on guesses — describe what the code changes actually do, at the same level of detail as existing entries (mentions specific window names, APIs, defines, etc. where relevant).
- Do not restate trivial/internal-only changes (formatting, comments) as changelog entries unless the user's request specifically calls them out.

## 4. Check whether `README.md` needs updating

- Read `README.md` and compare it against the changes being released.
- Update it only if the changes are user-facing and not already reflected — e.g. a new feature, a new required dependency, a changed setup step, a new Inspector field, a new menu item. Match the existing README's section structure and language (Traditional Chinese, same heading style).
- If it's a pure bug fix or internal refactor with no user-facing surface change, leave `README.md` untouched — do not make busywork edits.
- If you're unsure whether something is README-worthy, ask the user rather than silently skipping or silently adding.

## 5. Commit and push

- Show the user a short summary of every file you're about to stage (`git status`) before committing.
- Stage exactly the files relevant to this release (the version bump, `CHANGELOG.md`, and `README.md` if touched, plus any other already-modified source files that are part of this release). Do not sweep in unrelated stray files (e.g. crash dumps, editor scratch files) with a blanket `git add -A` — check `git status` and add by name.
- Commit message: follow this repo's existing convention exactly — `vX.Y.Z: <short imperative summary>`, e.g. `v0.1.9: add update checks for InstantReplay dependencies in Settings window`. Use `$ARGUMENTS`' trailing text (after any version/bump keyword) as the summary if the user supplied one; otherwise write a concise one-line summary yourself from the actual diff. End the commit body with the standard `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>` trailer per this repo's commit instructions.
- Before pushing, confirm with the user (this pushes to `origin/main`, a shared branch) unless they've explicitly told you in this conversation to push without asking.
- Push, then report the pushed commit hash and version.
