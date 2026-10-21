# MessagingToolbox — DevOps Guidelines

> Guidelines only — no pipeline code here. Companion to
> `messagingtoolbox-architecture-guidelines.md`. Records the Phase 1 decisions and leaves
> documented slots for everything deferred.

---

## 1. Phase 1 decisions (locked)

| # | Topic | Decision | Rationale |
|---|---|---|---|
| 1 | Container registry | ghcr.io only | Simplest path: built-in `GITHUB_TOKEN`, no extra secrets, package lives next to the repo. Docker Hub reconsidered when discoverability matters. |
| 2 | Publish gate | Tag push publishes immediately | Solo dev: manual approval adds friction, not safety. A `release` environment is still used to scope secrets and bind the NuGet policy — just without required reviewers. |
| 3 | Abstractions | Separate `MessagingToolbox.Abstractions` project from day one, not published in Phase 1 | Provider contracts (`IMessagingProvider`, `ProviderCapabilities`, model types) physically isolated so community providers stay possible; flipping `IsPackable` later is a one-line change. |
| 4 | Prerelease channel | Yes — every merge to `main` publishes prerelease NuGets and an `edge` container image | Early feedback loop, dogfoodable in real Aspire solutions before stable releases. |
| 5 | Supply-chain extras | Deferred | Cosign signing, provenance attestations, SBOM slots are documented (§7) and designed to be additive steps, not redesigns. |

**Architecture amendment:** the solution layout gains `src/MessagingToolbox.Abstractions/`
(Domain stays for app-internal model; Abstractions holds the public provider contract surface).
Architecture tests assert that Providers reference Abstractions, never the other way —
implemented in `DependencyRuleTests`.

---

## 2. Repository layout (DevOps-relevant)

- `.github/workflows/` — three workflow files, following convention:
  `_build.yml` (reusable, `workflow_call`, parameterized: dotnet version, pack toggle),
  `ci.yml` (thin caller: PRs + main pushes, build & test only),
  `publish.yml` (thin caller: main pushes → prerelease, `v*` tags → stable).
- `Dockerfile` + `.dockerignore` at repo root.
- `Directory.Build.props` — shared package metadata and `IsPackable=false` default.
- `Directory.Packages.props` — central versions; Dependabot must target this.
- Root `.dockerignore` excludes `bin/`, `obj/`, `tests/`, `docs/`, `*.md`, `.git/`, `.github/`,
  and IDE folders — keeps the build context small and restores cacheable.

---

## 3. Container image guidelines

- Multi-stage Dockerfile: SDK image restores from project files only (restore layer caches
  independently of source changes), then publishes the Web host; runtime stage is the Alpine
  ASP.NET base image (`mcr.microsoft.com/dotnet/aspnet:10.0-alpine` — chosen for size; revisit
  only if a dependency ever forces glibc).
- Run non-root using the base image's built-in user; expose a single HTTP port; configuration
  purely via environment variables so the same image serves Aspire injection and standalone
  `docker run` scenarios.
- One image only in Phase 1 — the Web host. Do not publish separate images for AppHost or
  test projects.
- Build for `linux/amd64` and `linux/arm64` from the start; Apple Silicon dev machines are the
  core audience, and adding a platform later is painless when done early.
- Tags per release: full semver, `major.minor`, immutable `sha-<commit>`, and `latest`
  (releases only). Prerelease builds get `edge` plus their `sha` tag — never `latest`.
- The hosting NuGet references the image by tag: a package built from a `v*` tag pins the image
  tag of the same version (stamped at build time from `GITHUB_REF_TYPE`/MinVer), so the semver
  tag pushed by the same run must stay `{{version}}`. Main-branch prerelease packages use `edge`.

---

## 4. CI pipeline guidelines

- PRs and main pushes run restore → build → test; nothing is packed or published on PRs.
- The emulator-based integration tests run in CI on the standard Linux runner (Docker is
  available there), in their own `emulator` job of `ci.yml` so a flaky emulator pull never
  blocks the unit-test signal and failures are attributable. The suite lives in
  `MessagingToolbox.Providers.AzureServiceBus.Tests/Integration/`: tests use `[EmulatorFact]`
  and `[Trait("Category", "Emulator")]`, and are skipped unless
  `MESSAGINGTOOLBOX_EMULATOR_TESTS=true`, so a plain `dotnet test` never starts containers.
  Run them locally with
  `MESSAGINGTOOLBOX_EMULATOR_TESTS=true dotnet test tests/MessagingToolbox.Providers.AzureServiceBus.Tests --filter Category=Emulator`.
  Once enabled, a container that fails to start fails the tests; they never pass silently.
- Treat warnings as errors in CI builds (configure via `Directory.Build.props`), but keep local
  builds warning-tolerant during early scaffolding if preferred.
- Keep the pipeline fast: single build with `--no-restore` / `--no-build` chaining between
  steps; upload test results and coverage as artifacts, not gates, in Phase 1.

---

## 5. Publishing guidelines

### NuGet

- Trusted publishing (OIDC) only — no long-lived API keys anywhere. The nuget.org policy binds
  repo + workflow filename + `release` environment; since the policy is bound per workflow
  file, all NuGet pushes (stable and prerelease) go through `publish.yml` so a single policy
  covers both.
- Only `MessagingToolbox.Aspire.Hosting` is packable-and-published in Phase 1.
  `MessagingToolbox.Abstractions` stays `IsPackable=false` but is versioned in lockstep, so
  publishing it later never creates a version mismatch with the hosting package.
- Push with `--skip-duplicate` so rerunning a failed workflow is safe.
- Package quality bar before the first push: license expression, authors, repository URL,
  readme, icon, description, and tags set in shared props — nuget.org surfaces all of these,
  and Aspire users discover the package by them.

### Container

- ghcr.io only; image name mirrors the repository path. After the first push, link the package
  to the repo and mark it public.
- Both publish jobs depend on a green build job in the same workflow run; packages and images
  from one tag always come from the same commit.

### Release records

- Every `v*` tag creates a GitHub Release with auto-generated notes (PR titles); keep PR
  titles meaningful — they become the changelog. Prerelease tags (`v0.1.0-preview.1`) create a
  GitHub prerelease, so they never become the repository's latest release.

---

## 6. Versioning guidelines

- Tag-driven versioning via git tags (`v0.3.0` → package/image `0.3.0`); MinVer (or equivalent)
  derives prerelease versions from tag height for main builds, so no version numbers are ever
  edited by hand in source. Untagged builds use the `preview` identifier and never go below
  0.1 (`Directory.Build.props`): before the first tag a main build is `0.1.0-preview.0.N`,
  after `v0.3.0` it is `0.3.1-preview.0.N`, and after `v0.4.0-preview.1` it is
  `0.4.0-preview.1.N`.
- Pre-1.0 SemVer: minor bumps for features, patch for fixes, breaking changes allowed in
  minors but called out in release notes. Revisit at 1.0.
- The git tag is the single source of truth — CI never computes versions from anything else.

---

## 7. Deferred slots (do not redesign later)

Leave these as clearly-marked, additive extension points:

- **Signing & provenance:** the container job ends with the push step; cosign keyless signing
  and `attest-build-provenance` append after it (require `id-token: write` and
  `attestations: write` permissions — pre-add the permissions now so enabling later is
  steps-only).
- **SBOM:** attachable in the same job via an SBOM action; no structural change needed.
- **Docker Hub mirror:** a second login + tag namespace in the existing container job;
  requires two secrets (`DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`) — document the secret names
  now so nothing else changes.
- **Abstractions package publish:** flip `IsPackable`, add to the pack step's existing glob —
  the publish job needs no changes.

---

## 8. Secrets & access inventory (Phase 1)

| Name | Where | Purpose |
|---|---|---|
| `GITHUB_TOKEN` | built-in | ghcr.io push, release creation — no setup |
| `NUGET_USER` | `release` environment secret | nuget.org username for trusted publishing login |

That is the complete list — Phase 1 deliberately needs one secret. Every deferred feature above
adds secrets only to the `release` environment, never to the repository level.

---

## 9. Housekeeping guidelines

- Dependabot: three ecosystems — `nuget` (root, understands central package management),
  `github-actions`, `docker` — weekly, grouped into one PR per ecosystem to keep noise low.
- Branch protection on `main`: require the CI workflow green + up-to-date branch; solo dev
  can still self-merge.
- Artifact retention: keep published `.nupkg` artifacts from `publish.yml` runs for traceability;
  default retention is fine otherwise.
- Once public: add CodeQL (or equivalent) analysis — free for public repos — as a separate
  scheduled workflow so it never gates PR velocity.
