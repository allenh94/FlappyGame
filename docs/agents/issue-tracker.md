# Issue Tracker

## Provider

Local markdown files under `.scratch/<feature>/` in this repo.

## Convention

Each feature or work item gets its own directory:

```
.scratch/
  feature-name/
    issue.md          # The issue description
    notes.md          # Working notes (optional)
```

## Issue file format

```markdown
# [Title]

**Status:** open | in-progress | done | wontfix
**Priority:** high | medium | low
**Created:** YYYY-MM-DD

## Description

[What needs to happen]

## Acceptance criteria

- [ ] Criterion 1
- [ ] Criterion 2
```

## Rules

- One directory per feature/work item
- `issue.md` is the canonical issue file
- Status transitions: `open` → `in-progress` → `done` (or `wontfix`)
- Skills that create issues (`to-tickets`, `triage`, `qa`) write here by default
