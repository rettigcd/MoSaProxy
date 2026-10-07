# Test Instructions

Instructions for AI assistants (and humans) writing tests in this repo.
Add new rules to this file as they come up; follow everything here when writing or changing a test.

## When to write tests

- Long-term functionality gets tests in the test project, and they are run from there with `dotnet test`.
  Add the tests as part of implementing the feature, not afterward.
- Short-term spikes and throwaway experiments do not need tests in the test project.
  If spike code is kept, add tests for it then.
- See "Testing approach" in [docs/plan.md](../docs/plan.md).

## Stack

- xUnit for the test framework.
- Shouldly for assertions (`result.ShouldBe(...)`). Do not use `Assert.*`.
- Tests live in `test/MoSaProxy.Tests/`. Mirror the structure of the code under test where practical.

## Structure: Given / When / Then

Every test is divided into three sections, in this order, each introduced by a comment:

```csharp
[Fact]
public void Method_Scenario_ExpectedResult() {

	// Given: <what state / inputs are set up, in plain English>
	...setup code...

	// When: <the single action being tested, in plain English>
	...act code...

	// Then: <what is expected to be true afterward, in plain English>
	...assertions...
}
```

### The comments are the documentation

A human should be able to read only the `// Given`, `// When` and `// Then` comments and understand the test
without reading any code. Therefore:

- Write each comment as a complete plain-English statement of intent, not a restatement of the code.
  - Good: `// Given: a client that sends a GET with an absolute http:// URL`
  - Bad: `// Given: create a new HttpRequestMessage`
- Describe behavior and business meaning, not implementation details.
- If a section has several steps, the comment covers all of them. Use `// And: ...` lines only when a
  section genuinely has distinct parts that deserve their own sentence.
- Keep comments accurate. When you change the code in a section, update its comment in the same edit.
- Never omit a section. If there is no setup, write `// Given: nothing special` (or similar).

### Rules for each section

- **Given:** arrange only. No assertions.
- **When:** exactly one action on the code under test, usually one line. Capture its result in a variable.
- **Then:** assertions only. No further calls to the code under test except to read results.
  Prefer one logical assertion per test; several `ShouldBe` lines are fine if they describe one outcome.

## Naming

- Test class: `<ClassUnderTest>_Tests` (e.g. `HeaderFilter_Tests`).
- Test method: `Method_Scenario_ExpectedResult` (e.g. `Filter_ConnectionHeader_IsRemoved`).
- One file per test class, file name matches the class name.

## General rules

- Tests must be independent and runnable in any order; no shared mutable state between tests.
- No real network or external services. Use in-process fakes (for example a local listener on port 0)
  and loopback only.
- Use `[Theory]` with `[InlineData]` for the same behavior over several inputs. The Given / When / Then
  comments still apply and should describe the general case.
- Use `async Task` for async tests, never `.Result` or `.Wait()`.
- Do not use `Thread.Sleep` or fixed delays to wait for things. Await the actual event, or use a timeout.
- Use tabs for indentation and K&R braces to match the rest of the code.
- Keep tests small. If setup gets long, extract a helper with a descriptive name and mention what it does in
  the `// Given` comment.

## Before finishing

- Run `dotnet test` from the repo root and make sure everything passes.
- Confirm every test has all three comments and that each comment still matches its code.

## Project-specific notes

<!-- Add more instructions below as the project grows. -->
