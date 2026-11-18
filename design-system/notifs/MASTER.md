# Notifs — desktop design system

Designed first for issue #2 using UI/UX Pro Max. Personal, offline Windows productivity tool, Russian UI, WPF (.NET 10), mouse and keyboard.

## Verified guidance and adaptations

- `personal productivity desktop minimal --design-system`: verified Flat Design and teal productivity palette. Marketing pattern is inapplicable; the narrower `task management desktop` retry also returned a marketing pattern. Neither marketing output is persisted here.
- `minimal productivity --domain style`: Minimalism & Swiss Style; clear hierarchy, spacious functional grid, no decorative gradients or shadows.
- `keyboard focus forms --stack wpf`: keyboard navigation, commands/input bindings, view-only code-behind.
- `error summary validation --domain ux`: inline errors, retained input, focus invalid field. Web ARIA examples translate to WPF AutomationProperties.
- Font search did not verify an offline Cyrillic pairing. Platform fallback: installed Segoe UI throughout; Consolas for code. No web fonts or network content.

## Visual language

Quiet light workspace. Native title bar and resizing. Left navigation (tasks/history), middle task list, right reading pane. Editing is an owned dialog with explicit Save and Cancel. No inactive future-feature controls. One primary action per context. Empty states offer a useful next action; never insert example tasks into the user's database.

Use semantic resources from `tokens.json` in WPF. Primary teal is darkened to #0F766E for white text. Neutral background #F5F7F8, white surfaces, ink #163632, muted ink #52645F, borders #D9E2DF, selected #E4F2EE, danger #B42318. Selected controls also use weight/border, not color alone. Focus outline #0F766E, 2 DIP. No shadows. Corner radii 8–12 DIP; 4/8 spacing rhythm, 24–32 DIP page gutters.

Body 16 DIP, labels 14 DIP, list titles 17 DIP semibold, pane title 26 DIP, page title 32 DIP semibold. Wrap user text. Primary and secondary text meet 4.5:1 on their backgrounds. Minimum action height 44 DIP. Vector line icons share 1.7 DIP stroke. Tooltips and automation names describe icon controls.

## Interaction and sizing

- Ctrl+N creates, Ctrl+S saves in editor; native Tab, Enter, Space, Escape behavior. Focus first input on open, invalid title on failure. Owned modal traps navigation and returns focus to its trigger.
- Dirty editor asks before discarding, including title-bar close and Escape. Errors preserve both fields. Storage failures show a readable retry path; never report success before storage succeeds.
- List virtualization and asynchronous storage work. Busy actions are disabled, with a visible status. Completion remains recoverable from history.
- Default window 1240×800; minimum 760×560. Below 1040 DIP, navigation becomes a compact top row. Below 900 DIP, list/details share one pane with an explicit return-to-list action. Each content region scrolls; actions remain visible. Long titles, Markdown, empty lists, 150%/200% scaling are QA cases.
- Optional hover color interpolation: 160 ms, only when Windows client-area animation is enabled. Press and focus remain immediate. No movement or decorative entrance animations.
- This iteration provides a light theme. Dark/mobile-specific checklist items do not apply to this Windows-only release; no untested dark variant is claimed.

## Required states

Active list / empty active list; history / empty history; selected task / no selection; new/edit dialog; invalid title; saving; storage error; Markdown source and local formatted preview. Created timestamp is always retained through edit/complete/restore.
