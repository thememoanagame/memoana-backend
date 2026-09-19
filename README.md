# memoana-backend

MemoAna Game backend.

## Testing and quality gate

Unit tests are mandatory for backend behavior changes. The repository uses xUnit v3 and Microsoft Testing Platform for the unit-test suite.

The unit-test project exposes the official local quality-gate command:

    dotnet tool restore
    dotnet build ./tests/MemoAna.UnitTests/MemoAna.UnitTests.csproj -t:ValidateQualityGate

`ValidateQualityGate`:

1. builds the unit-test project;
2. executes the unit tests with the existing Coverlet MTP configuration;
3. generates a Cobertura coverage report;
4. uses the repository-local `dotnet-reportgenerator-globaltool`;
5. generates a Markdown summary;
6. extracts the total line and branch coverage percentages from the Markdown report;
7. validates both metrics against the required **80%** threshold;
8. prints the result in a stable, human-readable `en-US` style;
9. fails the build when either metric is below 80%, including a combined message when both are below the threshold.

The generated quality-gate artifacts are written under:

    tests/MemoAna.UnitTests/TestResults/QualityGate/

The quality gate requires all of the following:

- unit tests pass;
- line coverage is at least 80%;
- branch coverage is at least 80%.

Do not consider a test change complete until the quality-gate command passes.

### Tooling

The repository pins ReportGenerator in `dotnet-tools.json`. Restore local tools before running the quality gate:

    dotnet tool restore

The custom MSBuild target is defined in:

    tests/MemoAna.UnitTests/MemoAna.UnitTests.csproj

and is intentionally the single documented entry point for validating the local unit-test quality gate.
