# Juloc Agent Bootstrap

Before substantial work in Paper:

1. Read `.agent/project.yaml` and any notes it references.
2. Read the canonical rules from `Juloc/agent-control/AGENTS.md` and the applicable documents in `Juloc/agent-control/docs/`.
3. Resolve the active coordination ledger through `Juloc/agent-control` issue #1 before creating or changing a claim.
4. Inspect the affected owner, existing pattern and tests before adding a new type, store, provider or configuration path.
5. Run the configured validation and the maintainability completion gate before reporting completion.

Paper follows the same engineering discipline as Jularr:

- Paper is a modular monolith organized by feature/domain ownership.
- Razor Pages are transport/UI boundaries; persistence belongs to stores or query owners.
- Every durable setting, state transition and business rule has one canonical owner.
- Do not create parallel configuration paths, helper/manager/service soup, artificial three-line wrappers or speculative abstractions.
- Keep existing working ownership when extending a feature.
- Comments explain intent, invariants, constraints or trade-offs; do not add AI-generated commentary.
- Keep UI copy concise and purposeful; avoid unnecessary headings, explanations and generic card nesting.
- Keep frontend assets structured by real responsibility instead of growing global dumping grounds.
- New and intentionally touched C# follows the canonical Microsoft/.NET and Juloc line-width/Allman rules.

GitHub Issues are the durable backlog and GitHub issue #1 is the coordination index. Never commit credentials, tokens, plaintext secrets or agent attribution.
