# Contributing to RazorConsole

Thank you for your interest in contributing to RazorConsole! We welcome contributions from the community and appreciate your help in making this project better.

## 📋 Getting Started

### Before You Start

- **For large PRs, refactors, or new features**: Please create an issue first to discuss your proposed changes. This helps ensure your contribution aligns with the project's goals and prevents duplicate work.
- **Join our Discord**: For timely responses and real-time collaboration, [join our Discord server](https://discord.gg/DphHAnJxCM). The maintainers and community are active there and can help guide your contributions.

## 🐛 Reporting Issues

When reporting bugs or requesting features, please use our issue templates:

- **Bug Report**: For reporting bugs or unexpected behavior ([use template](../../issues/new?template=bug-report.yml))
- **Feature Request**: For suggesting new features or enhancements ([use template](../../issues/new?template=feature-request.yml))

The templates will guide you through providing the necessary information:

1. **Search existing issues** to avoid duplicates
2. **Provide a clear description** of the problem or feature request
3. **Include reproduction steps** for bugs
4. **Share your environment details** (.NET version, OS, etc.)
5. **Use appropriate labels**: bug, feature-request, contribution-requested, docs

## 🔧 Development Setup

### Prerequisites

- [.NET 11 RC1 SDK](https://dotnet.microsoft.com/download/dotnet/11.0), using the version in `global.json`
- .NET 8, 9 and 10 runtimes (or SDKs) to run the complete multi-target test suite
- [Git LFS](https://git-lfs.github.io/) for handling large media files

### Clone and Setup

```bash
# Install Git LFS if not already installed
git lfs install

# Clone the repository
git clone https://github.com/RazorConsole/RazorConsole.git
cd RazorConsole

# Build the solution
dotnet build RazorConsole.slnx

# Run tests
dotnet test RazorConsole.slnx
```

## 💻 Making Changes

### Coding Standards

- Follow the rules encoded in `.editorconfig` (four-space indentation, file-scoped namespaces, system usings first)
- Prefer async/await with `ConfigureAwait(false)` when awaiting inside library code
- Keep public APIs nullable-enabled and document exceptions and edge cases
- Treat Spectre.Console renderables as immutable from outside rendering loops

### Before Submitting

1. **Format your code**: Run `dotnet format RazorConsole.slnx` before opening a pull request
2. **Run tests**: Execute `dotnet test RazorConsole.slnx` locally. CI requires a clean test run on Linux and Windows
3. **Update tests**: When touching focus or keyboard handling, add or update tests in `FocusManagerTests` or `KeyboardEventManagerTests`
4. **Update documentation**: Update the README when introducing user-facing features or significant architectural changes

## 📝 Pull Request Process

1. **Create an issue first** for large changes, refactors, or new features
2. **Fork the repository** and create a branch from `main`
3. **Make your changes** following the coding standards
4. **Write or update tests** to cover your changes
5. **Run `dotnet format`** to ensure formatting is correct
6. **Run `dotnet test`** to ensure all tests pass
7. **Submit a pull request** with a clear description of the changes

### Website Preview Deployments

When you submit a pull request that includes changes to the website, an automated workflow will:
- Build the website with your changes
- Upload the built site as a downloadable artifact (30-day retention)
- Optionally deploy to Cloudflare Pages for live preview (if configured)
- Post a comment on your PR with download instructions and/or live preview URL

The workflow can also be manually triggered via the Actions tab. For automatic live preview deployments, see [`.github/PREVIEW_DEPLOYMENT.md`](.github/PREVIEW_DEPLOYMENT.md) for setup instructions.

### Pull Request Guidelines

- Keep PRs focused on a single feature or bug fix
- Write clear, descriptive commit messages
- Reference related issues in your PR description
- Be responsive to feedback and questions

## 🧪 Testing

- Add tests for new features and bug fixes
- Ensure all existing tests pass
- Tests should be placed in the `src/RazorConsole.Tests` project

## 📚 Documentation

- Update the README.md for user-facing changes
- Add or update XML documentation comments for public APIs
- Consider adding examples to the `examples/` directory for new features
- Design notes can be added to `design-doc/` for architectural changes

## 🚀 Release Process

Pushing a version tag (`v*.*.*` or `*.*.*`) triggers `.github/workflows/release.yml` to build, test, pack, publish NuGet packages, and create a GitHub release with platform bundles. The workflow can also be dispatched manually; manual runs publish NuGet packages but only tag runs create a GitHub release. Version numbers should follow semantic versioning.

### NuGet Trusted Publishing Setup

Both the CI nightly NuGet job in `.github/workflows/ci.yml` and the official release NuGet job in `.github/workflows/release.yml` use [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing). They exchange GitHub OIDC tokens for short-lived API keys immediately before pushing packages; neither workflow uses a stored `NUGET_API_KEY`.

Before enabling publishing, a package owner must complete this setup:

1. Sign in to NuGet.org and open **Trusted Publishing**. Create two GitHub policies using these values:

   | Setting | CI nightly | Official release |
   | --- | --- | --- |
   | Repository Owner | `RazorConsole` | `RazorConsole` |
   | Repository | `RazorConsole` | `RazorConsole` |
   | Workflow File | `ci.yml` | `release.yml` |
   | Environment | Leave empty | Leave empty |
   | Package pattern | `RazorConsole.*` | `RazorConsole.*` |

   Choose the individual or organization that owns the packages as the policy owner. Allow publishing new versions of existing packages; allow new packages only if needed. Enter only the workflow filename, not the `.github/workflows/` path. The publishing jobs do not use GitHub Actions environments.

2. Add the repository Actions secret `NUGET_USER` with the NuGet.org **profile username**, not an email address or GitHub username. Use the account associated with the policies; for organization-owned policies, that user must remain an active member with permission to publish the packages.

3. Merge the workflow changes and verify a qualifying push to `main` authenticates and publishes a nightly package. Verify official publishing on the next intended version tag or release dispatch; do not dispatch solely to test authentication, because it publishes packages. CI still publishes nightly packages only when its source-change check passes, and pull requests cannot run the nightly publishing job.

4. After both workflows have successfully published, revoke the old long-lived API key on NuGet.org and remove the repository's `NUGET_API_KEY` secret.

The publishing jobs grant `id-token: write` only at job scope. Login failure stops publishing rather than falling back to the old API key. Temporary keys expire after one hour, so keep login immediately before the push step.

## 💬 Getting Help

- **Discord**: [Join our server](https://discord.gg/DphHAnJxCM) for quick questions and real-time help
- **Issues**: Create an issue on GitHub for bugs or feature requests
- **Discussions**: Use GitHub Discussions for general questions and ideas

## 📜 Code of Conduct

Please be respectful and constructive in all interactions. We aim to maintain a welcoming and inclusive community.

## 🙏 Thank You!

Your contributions help make RazorConsole better for everyone. We appreciate your time and effort!
