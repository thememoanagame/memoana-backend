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
5. generates a Markdown summary and a text summary;
6. applies the configured assembly filters so test-only/external assemblies such as FluentValidation and Mediator are not included in the quality-gate coverage scope;
7. extracts total line, branch and method coverage from `Summary.txt`;
8. normalizes branch coverage into `Summary.txt` when ReportGenerator omits the branch-percentage line (for example when the report has zero total branches), using the covered/total branch counts;
9. validates line coverage, branch coverage and method coverage against the required **80%** threshold;
10. prints the three coverage results in a stable, human-readable `en-US` style;
11. fails the build when any required metric is below 80%.

The method-coverage gate is intentionally tied to the text-report contract: `Summary.txt` must contain a `Method coverage: N%` line. If that line is not produced, the quality gate aborts instead of silently skipping method coverage.

The generated quality-gate artifacts are written under:

    tests/MemoAna.UnitTests/TestResults/QualityGate/

The quality gate requires all of the following:

- unit tests pass;
- line coverage is at least 80%;
- branch coverage is at least 80%;
- method coverage is at least 80%.

Do not consider a test change complete until the quality-gate command passes.

### Tooling

The repository pins ReportGenerator in `dotnet-tools.json`. Restore local tools before running the quality gate:

    dotnet tool restore

The custom MSBuild target is defined in:

    tests/MemoAna.UnitTests/MemoAna.UnitTests.csproj

and is intentionally the single documented entry point for validating the local unit-test quality gate.
