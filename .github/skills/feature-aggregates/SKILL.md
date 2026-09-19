
# Feature Aggregates Skill

## Goal

Implement each feature as a self-contained aggregate. Organize by feature first and technical role second.

## Rules

- Find the aggregate before adding a file.
- Keep domain rules in Domain.
- Keep use-case orchestration in Application.
- Keep protobuf/HTTP adapters in Grpc/Http or equivalent presentation folders.
- Keep feature-specific infrastructure inside the feature.
- Avoid cross-feature dependencies on concrete services.
- Prefer interfaces at application boundaries and concrete implementations in infrastructure.
- Do not introduce repository-wide technical folders solely to reduce path depth.

## Review checklist

- Does every new file have a clear feature owner?
- Is transport code free of domain decisions?
- Can the domain/application be tested without ASP.NET or gRPC?
- Is a shared abstraction actually shared by more than one feature?
- Is the dependency direction toward the domain/application rather than toward transport?
