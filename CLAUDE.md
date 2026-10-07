# MoSaProxy

A traffic-logging HTTP/HTTPS proxy in C# (.NET). The main app is in `app/`, tests are in `test/MoSaProxy.Tests/`.

- Read [docs/plan.md](docs/plan.md) for the roadmap and design decisions, including the testing approach.
- [docs/landscape.md](docs/landscape.md) lists the competing proxy servers, the features they offer and the save formats wanted.
- [docs/class-classification.md](docs/class-classification.md) classifies every class in `app/` as Core, UI, Mix or Other. Add new types to it.
- Read [test/test_instructions.md](test/test_instructions.md) before writing or changing any test.
  Tests use xUnit and Shouldly with Given / When / Then comments.
- Long-term functionality gets tests in the test project and is run with `dotnet test` from the repo root.
  Short-term spikes do not need tests.
- Run the proxy with `dotnet run --project app`.
