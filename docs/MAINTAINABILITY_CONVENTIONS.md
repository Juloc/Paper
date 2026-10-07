# Paper Maintainability Conventions

Paper adopts the canonical Juloc rules:

- [Agent Control engineering rules](https://github.com/Juloc/agent-control/blob/main/docs/ENGINEERING_RULES.md)
- [Agent Control maintainability rules](https://github.com/Juloc/agent-control/blob/main/docs/MAINTAINABILITY_CONVENTIONS.md)
- [Agent Control database conventions](https://github.com/Juloc/agent-control/blob/main/docs/DATABASE_CONVENTIONS.md)
- [Agent Control C# conventions](https://github.com/Juloc/agent-control/blob/main/docs/CSHARP_CONVENTIONS.md)

They apply to all new and intentionally touched code. This document records only Paper-specific ownership decisions; it does not replace or weaken the canonical rules.

## Architecture

Paper is a modular monolith. Features own their domain behavior and persistence access. Razor Pages validate transport input and translate results into UI; they do not query `AppDbContext` directly when a feature store or query owner is appropriate.

The storage feature owns the active runtime storage configuration, provider selection, connection testing, secret protection and storage-change policy. PostgreSQL is the canonical runtime source after the one-time bootstrap from deployment configuration. Environment/AppSettings provide bootstrap values for a new database, not a competing runtime override.

Shelf owns the physical folder tree and document filing operations. Folder changes and document moves must update the filesystem through the storage provider and the database through one visible mutation flow, with rollback/error semantics preserved.

Shared templates and styles are organized by real UI responsibility. Settings pages have one page model per settings area, and feature-specific styles belong in the corresponding stylesheet rather than a growing global dumping ground.

## Completion review

Before completion, review touched code for one canonical owner, no hidden writes, no duplicate configuration path, explicit errors/cancellation, safe secrets, bounded queries, focused behavior tests and the required build/test/migration/Compose/demo gates.
