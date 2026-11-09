# ADR-XXXX: [Short decision title]

<!--
Must Have = required in every ADR.
Optional = include only if it adds useful information.
Replace all [instructions] and remove unused optional sections before merging.
-->

**Related:** [Optional] [Issue, PR, or design document]

**Superseded by:** [Optional] [Link to the replacing ADR]

## 1. Summary [Must Have]

**Decision:** [Which option was selected? Use "Pending" while discussing in a PR.]

**Reason:** [The main reason for the choice, in one sentence.]

## 2. Problem [Must Have]

**Current situation:** [How does it work today? Explain unfamiliar terms.]

**Problem:** [What needs to change, and why does it matter?]

**Constraints:** [What must remain true? Write "None known" if applicable.]

## 3. Decision drivers [Must Have]

[What matters most when comparing options? Put the most important first.]

1. [A required behavior or constraint]
2. [An important quality, such as simplicity, performance, or maintainability]

## 4. Options [Must Have]

Describe the **real** approaches using the same structure. Include the current approach if keeping it is viable. **Do not invent alternatives:** if only one option is feasible, explain why.

### Option A: [Name]

**Approach:** [What changes, in one or two sentences?]

**How it works:** [Describe the main flow or behavior in plain language.]

<!-- Optional, recommended: Add a Mermaid diagram showing how this option works and what distinguishes it. Explain it in the text above. -->

### Option B: [Name]

<!-- Optional: Remove this option if there is no real alternative. -->

**Approach:** [What changes, in one or two sentences?]

**How it works:** [Describe the main flow or behavior in plain language.]

<!-- Optional, recommended: Add a Mermaid diagram showing how this option works and what distinguishes it. Explain it in the text above. -->

<!-- Optional: Repeat the same structure for Option C, D, etc. -->

## 5. Evidence and trade-offs [Must Have]

- **Fact:** A verified statement supported by evidence.
- **Assumption:** An unverified statement with a clear way to validate it.
- **Applies to:** `*` (All) or an option label (`A`, `B`, etc.).
- **Impact:** Explain how the statement affects
the relevant decision drivers.
Use `+` for a benefit, `-` for a drawback,
and `/` for a neutral observation.

| Type | Applies to | Statement | Evidence / How to validate | Impact |
| --- | --- | --- | --- | --- |
| Fact | * | [A relevant verified statement] | [Code, test, documentation, or measurement] | / |
| Fact | A | [A verified property of option A] | [Evidence] | + [Benefit tied to a driver] |
| Assumption | B | [An expected property of option B] | [Test, prototype, or benchmark] | - [Potential drawback tied to a driver] |

<!-- Remove sample rows. Include only statements that matter to the decision. -->

## 6. Decision [Must Have]

**Selected:** [Option A / B / ...; do not merge while the decision is "Pending".]

**Why:** [Compare the options using decision drivers and the evidence above. Explain which trade-offs are acceptable.]

<!-- Optional: **Out of scope:** [What this decision does not attempt to solve.] -->
