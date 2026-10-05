# Contributing to Game Library

Help is welcome with bugs, documentation, accessibility, and code.
For larger changes, open an issue first to discuss scope.

## Get started

Read [development](docs/development.md) and [architecture](docs/architecture.md).
Create a branch, keep changes focused, and run:

```powershell
.\scripts\build.ps1 -Test -Publish
```

For UI changes, exit the normal app, run `.\scripts\verify-ui.ps1`, and check
behavior manually. Report checks performed and anything you could not run.
Documentation changes need a link and command review.

## Report a problem

Search existing issues first. Include the version or commit, Windows version,
reproduction steps, expected result, and actual result. For visual problems,
include display scaling and input method.

Share relevant log excerpts only. Redact personal paths, account IDs, game
library locations, and private information in screenshots.

## Code and comments

Follow the surrounding file's style. Use descriptive names and respect the
Core, Infrastructure, and App boundaries.

Comments should explain intent, constraints, or surprising decisions. Existing
examples include carousel virtualization alignment, Steam manifest rewrite
handling, and bounded process detection.

- Explain why platform exceptions and workarounds exist.
- Document format assumptions where they are enforced.
- Use XML summaries when public contracts need explanation.
- Update comments when behavior changes.
- Avoid narrating obvious assignments or leaving commented-out code.
- Put explanations spanning several components in architecture docs.

Preserve behavior in structural changes. Keep bulk formatting, dependency
updates, and unrelated refactors out of the same pull request.

## Pull requests

Explain the problem, resulting behavior, and validation. Link relevant issues.
Include screenshots for visual changes and update affected docs. Add meaningful
regression coverage for behavior changes.

Treat contributors respectfully. Give specific, constructive feedback and keep
discussions focused on the work.
