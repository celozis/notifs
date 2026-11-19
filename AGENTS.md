## Agent skills

### Issue tracker

Track issues and specs in GitHub Issues for `celozis/notifs`. Before creating, reading, or updating tickets, read `docs/agents/issue-tracker.md`.

### Triage labels

Use the five standard triage labels. Before triaging an issue, read `docs/agents/triage-labels.md`.

### Domain docs

Use a single-context layout: root `CONTEXT.md` and `docs/adr/`. Before exploring the codebase or recording domain decisions, read `docs/agents/domain.md`.

## Product requirements

Before changing product behavior, read the approved interview summary in `docs/product/interview-summary.md`.

Before UI or task/reminder/calendar changes, also read `docs/product/design-spec-2026-11-18.md` and `design-system/notifs/MASTER.md`. Behavior Q1–Q46 and the extended graphite/amber prototype are the implementation target approved on 2026-11-19. Open the relevant prototype states before implementation and compare the WPF result with their screenshots before completion; use `design-system/notifs/reference/README.md` for preview and verification instructions. Earlier white/teal designs and temporary prototype rules are historical. Do not copy prototype JavaScript into the production WPF app.

For planning or updating implementation tickets, read `docs/product/implementation-map-2026-11-19.md`. For prototype edits, use its source additions and builder; generated HTML and vendored Lucide are preview outputs/dependencies, not files to read in full or hand-edit.
