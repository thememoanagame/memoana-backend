# Localization Skill

## Purpose

All manually authored strings exposed through logs, outputs, responses and other runtime-facing text must come from the repository's RESX resources.

This skill applies to:
- logs;
- gRPC response/diagnostic text;
- REST responses;
- validation/rejection messages;
- user/operator-facing output;
- manually authored runtime error messages;
- other natural-language strings introduced by application code.

Do not hard-code a new natural-language string in C# when it belongs in localization resources.

## Resource files

The repository uses:

- `src/MemoAna/Resources/Localization/Strings.resx` — **neutral/default resource**.
- `src/MemoAna/Resources/Localization/Strings.pt-BR.resx` — **pt-BR culture resource**.
- `src/MemoAna/Resources/Localization/Strings.en-US.resx` — **en-US secondary resource**.

Follow the existing `ExampleString` entry as the canonical shape.

## Key naming

Every new resource key must follow:

`<Key>String`

where `<Key>` is a descriptive identifier written in **en-US**.

Examples:
- `MatchCreatedString`
- `SessionDisconnectedString`
- `InvalidCommandString`

Do not use Portuguese words in resource keys.

## Resource content

### Strings.resx

The neutral resource is the source of truth for the default value.

For every new string:
- add the key to `Strings.resx`;
- provide its neutral/default value;
- add a **neutral comment** describing the purpose/usage of the string.

The comment must be written in the neutral/default language used by the repository.

### Strings.pt-BR.resx

Add the same key with the **pt-BR translation**.

### Strings.en-US.resx

Add the same key with the **en-US translation**.

The three resource files must remain key-compatible.

## Example pattern

Follow the existing `ExampleString` XML structure:

```xml
<data name="ExampleString" xml:space="preserve">
  <value>Example</value>
  <comment>...</comment>
</data>
```

Culture-specific files may omit the comment when consistent with the existing convention.

## Localizer

Use the existing localization abstraction:

```csharp
MemoAna.Application.Localization.ILocalizer
```

and its infrastructure implementation rather than creating a second localization mechanism.

When a localized message requires formatting parameters, use the repository's localization abstraction/implementation or extend it minimally so formatting remains resource-driven.

Do not use resource keys directly as user-facing output unless that is explicitly the intended contract.

## Exceptions

Do not force framework/system exception messages into RESX resources.

System/SDK exceptions are translated by the SDK/framework and are excluded from this rule.

However, manually authored exception messages created by application code are runtime strings and should be localized when they are intended for output/diagnostics.

Never replace an exception type's standard system message merely to satisfy localization.

## gRPC/REST considerations

Keep protocol enum/code values stable and language-neutral.

Human-readable `reason`, `message`, response diagnostics and similar text should be localized.

Do not localize:
- protobuf field names;
- enum identifiers;
- machine-readable error codes;
- command IDs;
- session/match/player IDs;
- log event IDs.

## Review checklist

Before finishing a task:
1. Search changed code for newly introduced string literals.
2. Classify each literal as code/config/protocol/system exception or runtime-facing text.
3. Move runtime-facing text to RESX.
4. Add the key to all three resource files.
5. Ensure the key follows `<Key>String` and uses an en-US identifier.
6. Add a meaningful neutral comment in `Strings.resx`.
7. Use `ILocalizer` at the consuming boundary.
8. Verify no new hard-coded natural-language runtime strings remain.
9. Keep translations semantically equivalent across neutral, pt-BR and en-US resources.
