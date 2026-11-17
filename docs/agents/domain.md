# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Layout

This repo uses a **single-context** layout:

- `CONTEXT.md` at the repo root holds the domain glossary and context.
- `docs/adr/` holds architectural decision records.

## Before exploring, read these

- Root `CONTEXT.md`.
- ADRs in `docs/adr/` that touch the area being explored.

If these files do not exist, proceed silently. The `/domain-modeling` skill creates them lazily when terms or decisions actually get resolved.

## Use the glossary's vocabulary

When naming a domain concept in an issue title, refactor proposal, hypothesis, or test name, use the term defined in `CONTEXT.md`.

If a needed concept is missing from the glossary, reconsider whether the project uses that concept or note the gap for `/domain-modeling`.

## Flag ADR conflicts

If a proposal contradicts an existing ADR, surface the conflict explicitly with the ADR reference and the reason for reconsidering it.
