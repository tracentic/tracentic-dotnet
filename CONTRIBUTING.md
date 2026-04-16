# Contributing

Thanks for your interest in improving the Tracentic .NET SDK. This guide covers how to get set up, the conventions we follow, and how to land a change.

## Getting started

Prerequisites:

- .NET SDK 6.0, 8.0, and 10.0 installed (the project multi-targets all three)
- git

Clone and build:

```bash
git clone https://github.com/tracentic/tracentic-dotnet
cd tracentic-dotnet
dotnet restore
dotnet build
dotnet test
```

## Reporting bugs & requesting features

Open an issue on GitHub. For bugs, include:

- SDK version
- Target framework (`net6.0` / `net8.0` / `net10.0`)
- Minimal reproduction
- What you expected vs. what happened

For features, describe the use case first - the shape of the API usually follows from the problem.

## Making a change

1. Fork and create a branch off `main`.
2. Keep the change focused - one logical change per PR.
3. Add or update tests under `tests/Tracentic.Sdk.Tests/`. New public behavior needs a test.
4. Run the full test suite: `dotnet test`.
5. Update `CHANGELOG.md` under `[Unreleased]` if the change is user-visible.
6. Update `README.md` if you add or change a public API surface.
7. Open a PR with a short description of **what** and **why** - the diff shows the how.

## Code style

- Follow existing conventions in the file you're editing.
- Nullable reference types are enabled - honor them.
- Public types and members need XML doc comments (the project generates a doc file).
- Keep types `internal` unless they're part of the public API.
- Don't add comments that restate what the code does. Comments should explain non-obvious **why**.

## Public API stability

Until we ship 1.0, minor versions may include breaking changes, but we try to avoid them. If your PR changes a public signature, call it out in the description.

## License & contributor agreement

By submitting a contribution, you agree that it is licensed under the Apache License 2.0 (see [LICENSE](LICENSE)), consistent with section 5 of the license.
