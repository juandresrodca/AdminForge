<!--
Thanks for contributing to AdminForge.

If this is your first pull request here: CI runs the same checks a maintainer would,
so a green tick means the mechanical review is already done. Anything red will tell
you exactly what to change.
-->

## What this changes

<!-- One or two sentences. If it closes an issue, say "Closes #123". -->

## Adding a tool?

<!-- Delete this whole section if you are not adding one. -->

- [ ] The tool lives in its own folder under `src/AdminForge.Tools/<Category>/`, and nothing else under `src/` or `tests/` changed
- [ ] `Id` is kebab-case and matches the folder name
- [ ] `Description` is one sentence, sentence case, under 110 characters, no trailing period
- [ ] At least three lowercase `Keywords`, including the abbreviations people actually type
- [ ] `Icon` is an existing id from `src/AdminForge.Web/wwwroot/icons/sprite.svg` (or the pull request adds a new `<symbol>`)
- [ ] `Compute` is `ClientSide` if the input could ever be a secret
- [ ] Every `[ToolField]` has a `Label`, and every text field has a `MaxLength`
- [ ] Expected failures return `ToolResult.Fail` with a message written for an admin, not a stack trace
- [ ] There is a test covering both a normal input and a bad one

### Documentation

<!--
The application discovers a new tool at startup, so the gallery, the route and the
generated form need no registration. The four files below are hand-written and each
one says something untrue about the repository until this pull request changes it.
docs/tools/README.md, under "Adding a tool", has the reasoning and the detail.
-->

- [ ] `README.md`: a row in the category table, the tool count in *What is in it*, and the outbound count in *Security* if the tool fetches a target
- [ ] `docs/tools/README.md`: a row in *Where each tool runs*, a section under the tool's category, and the counts in the paragraph after that table
- [ ] `CHANGELOG.md`: an `Added` entry under `[Unreleased]`, and the tool taken out of `Planned`
- [ ] `docs/ROADMAP.md`: the specification row deleted, its category count, and the total in the lede

## Checks

- [ ] `dotnet test` passes
- [ ] `dotnet format` leaves nothing to change
- [ ] `node --test "tests/js/*.test.mjs"` passes (only if you touched a browser module)
- [ ] I ran it and looked at the result in a browser

## Anything a reviewer should know

<!--
Trade-offs, data sources, an RFC that says something surprising, or a decision you
went back and forth on. This is the most useful part of the description.
-->
