# MoSaProxy

A traffic-logging HTTP/HTTPS proxy in C# (.NET). The proxy engine is the `core/` library (`MoSaProxy.Core`); the console app is in `app/` and references it. Tests for Core are in `test/MoSaProxy.Core.Tests/` (it references only `MoSaProxy.Core`); tests for the console app's UI types are in `test/MoSaProxy.App.Tests/`.

- Read [docs/plan.md](docs/plan.md) for the roadmap and design decisions, including the testing approach.
- [docs/landscape.md](docs/landscape.md) lists the competing proxy servers, the features they offer and the save formats wanted.
- [docs/class-classification.md](docs/class-classification.md) classifies every class in `core/` and `app/` as Core, UI, Mix or Other. Add new types to it.
- Read [test/test_instructions.md](test/test_instructions.md) before writing or changing any test.
  Tests use xUnit and Shouldly with Given / When / Then comments.
- Long-term functionality gets tests in the test project and is run with `dotnet test` from the repo root.
  Short-term spikes do not need tests.
- Run the proxy with `dotnet run --project app`.
