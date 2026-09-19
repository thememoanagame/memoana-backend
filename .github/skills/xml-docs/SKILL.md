# XML Documentation Skill

## Purpose

All public APIs introduced or modified in this repository must have complete XML documentation in **en-US**.

This skill applies to public:
- classes, records, structs, interfaces, enums and delegates;
- constructors;
- methods, including async methods;
- properties, fields and events when publicly exposed;
- generic type parameters;
- public extension methods.

## Required documentation

Use the applicable XML tags and do not add meaningless boilerplate.

At minimum, when applicable:
- `<summary>` — concise purpose and observable behavior.
- `<param>` — every parameter.
- `<typeparam>` — every generic parameter.
- `<returns>` — non-`void` return semantics, including important nullability/empty-result behavior.
- `<remarks>` — architectural constraints, lifecycle, concurrency, ownership, ordering, cancellation, idempotency or other behavior a caller must understand.
- `<exception>` — every exception the public API can reasonably throw as part of its documented contract.

All prose inside XML documentation must be written in **en-US**. Do not use Portuguese in XML documentation.

## Exception investigation

Do not guess the exception list.

Before documenting a public method's `<exception>` tags:
1. Inspect the method body.
2. Inspect every method/property/constructor it directly invokes when those calls can throw.
3. Follow relevant internal call paths far enough to identify exceptions that can escape the public method.
4. Inspect framework/library calls whose documented contracts can throw (for example argument validation, cancellation, channel operations, stream operations, collection lookups and disposal).
5. Distinguish exceptions that are intentionally translated/caught from exceptions that can actually escape.
6. Document only exceptions that can reasonably escape the public API under its supported usage; do not list arbitrary implementation exceptions.

For async APIs, account for exceptions surfaced through the returned `Task`/async state machine and for `OperationCanceledException` when cancellation is part of the contract.

When an exception is intentionally converted to a domain/application result, document the resulting behavior rather than documenting the swallowed exception.

## Method-level implementation comments

Inside methods, comments may document only important logic or architectural decisions that are not obvious from the code.

Use en-US.

Good examples include:
- why a particular ordering is required;
- why a lock is deliberately not held across an await;
- why cancellation is propagated at a specific boundary;
- why an operation is idempotent;
- why a fallback or compatibility behavior exists.

Do not narrate obvious statements or restate the code line-by-line.

## Style

- Write documentation for the caller, not for the implementation author.
- Prefer precise behavioral language over implementation trivia.
- Describe observable guarantees and constraints.
- Keep `<remarks>` useful and concise.
- Use `<see cref="..."/>` and `<paramref name="..."/>` where they improve navigation.
- Escape XML-sensitive characters correctly.
- Preserve existing documentation when editing an API and update it when behavior changes.

## Enforcement

When adding or modifying public APIs, treat missing or incomplete XML documentation as a defect.

Before finishing a task:
1. Search the changed files for public APIs.
2. Verify every applicable XML tag.
3. Re-check documented exceptions against the actual call graph.
4. Build with XML documentation/analyzer warnings enabled where configured.
5. Fix documentation warnings rather than suppressing them.
