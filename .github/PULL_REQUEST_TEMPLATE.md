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

- [ ] The tool lives in its own folder under `src/AdminForge.Tools/<Category>/`, and no file outside that folder and its test changed
- [ ] `Id` is kebab-case and matches the folder name
- [ ] `Description` is one sentence, sentence case, under 110 characters, no trailing period
- [ ] At least three lowercase `Keywords`, including the abbreviations people actually type
- [ ] `Icon` is an existing id from `src/AdminForge.Web/wwwroot/icons/sprite.svg` (or the pull request adds a new `<symbol>`)
- [ ] `Compute` is `ClientSide` if the input could ever be a secret
- [ ] Every `[ToolField]` has a `Label`, and every text field has a `MaxLength`
- [ ] Expected failures return `ToolResult.Fail` with a message written for an admin, not a stack trace
- [ ] There is a test covering both a normal input and a bad one

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
