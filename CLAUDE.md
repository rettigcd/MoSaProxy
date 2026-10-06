# mos

A traffic-logging HTTP/HTTPS proxy in C# (.NET). The main app is in `app/`, tests are in `test/mos.Tests/`.

- Read [docs/plan.md](docs/plan.md) for the roadmap and design decisions, including the testing approach.
- Read [test/test_instructions.md](test/test_instructions.md) before writing or changing any test.
  Tests use xUnit and Shouldly with Given / When / Then comments.
- Long-term functionality gets tests in the test project and is run with `dotnet test` from the repo root.
  Short-term spikes do not need tests.
- Run the proxy with `dotnet run --project app`.
