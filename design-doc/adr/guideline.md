# ADR Writing Guide

Use [the ADR template](template.md) for the required sections and fields. This guide covers **how to make a sound decision record**, not how to fill in each placeholder.

## When to write an ADR

Write one when a technical choice will meaningfully affect architecture, compatibility, performance, maintenance, or future development. Minor implementation details and routine refactoring usually do not need an ADR.

Record **one decision per ADR**. An ADR preserves the reasoning behind a choice; a separate design document can explain implementation details, APIs, and algorithms.

## Make the reasoning trustworthy

- **Separate observation from expectation.** Link to code, tests, documentation, or measurements for verified claims. If a result has not been tested, treat it as an assumption—even if it seems obvious.
- **Make important assumptions testable.** Describe a practical way to check them. If an unverified assumption could reverse the decision, consider validating it before merge.
- **Compare fairly.** Assess viable alternatives against the same decision drivers. Consider retaining the current approach where it is a genuine option. Do not invent alternatives to meet a quota.
- **Distinguish evidence from judgment.** A measurement may be factual; whether it is an advantage depends on the project's priorities. Explain the connection rather than assigning a positive or negative impact without reasoning.
- **Make trade-offs explicit.** The selected approach does not need to win on every criterion. Readers should understand what the team deliberately accepts or gives up.

Prefer concrete statements over vague claims such as “faster,” “cleaner,” or “more scalable.” Include conditions, a comparison baseline, or a way to verify them where relevant.

## Use diagrams only when they help

Mermaid diagrams are **recommended, not required**. Use a `flowchart` to show a processing path, `sequenceDiagram` for interactions, `stateDiagram-v2` for transitions, or `classDiagram` for important structural relationships.

Show what distinguishes an option rather than reproducing the entire system. Keep diagrams small, use meaningful names, and explain any non-obvious behavior in prose. If the alternatives differ only in a small detail, one focused comparison may be clearer than nearly identical diagrams. For extensive diagrams, link to a separate design document.

## GitHub workflow and history

1. Propose and discuss the ADR in a pull request. During discussion, the decision may remain pending.
2. Before merging, resolve the choice, remove placeholders, and check that the summary agrees with the final decision. **The merge records acceptance**; a separate status or date is unnecessary for this workflow.
3. Keep accepted ADRs in version control as historical records. If a later decision replaces one, create a new ADR and add a visible **Superseded by** link to the original. Do not silently rewrite the old rationale.

Links to issues, PRs, benchmarks, and technical documents provide evidence and detail, but the ADR should still be understandable without opening all of them.

## Review checklist

Before merging, ask:

- [ ] **Self-contained:** Can a new contributor understand the problem without past discussions?
- [ ] **Unambiguous:** Is the chosen option clear and consistent throughout?
- [ ] **Reasoned:** Does the choice follow from the stated priorities and accepted trade-offs?
- [ ] **Verifiable:** Are facts backed by evidence and key assumptions testable?
- [ ] **Visual:** Is the behavior understandable, with an informative diagram where useful?
- [ ] **Comparable:** Were viable options assessed by the same standards?
- [ ] **Concise:** Can anything be removed without losing an important reason or risk?
- [ ] **Maintainable:** Could a future contributor understand when and why the choice should be reconsidered or replaced?
- [ ] **Juniour frienly:** Can a developer that has no much explirience in project / coding understand ADR?
