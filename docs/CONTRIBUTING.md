# Contributing locally

The original project is offered under the custom ProgressPeek Source-Available License 1.0; see ../LICENSE and ../NOTICE. Workplace use and free modifications/distribution with attribution are permitted. Commercial distribution requires separate permission.

A public contribution-acceptance policy is not finalized. Contributors retain their own rights; submitting changes does not transfer ownership or automatically grant permission to include them in a future commercial edition. Agree on appropriate contribution terms before accepting external patches.

For authorized local maintenance, use C# 5 / .NET Framework WPF and Windows PowerShell 5.1. Keep dependencies and model calls out of tests; use disposable folders, no credential inspection or live profile edits. Run build.ps1, Test-Distribution.ps1, all seven Test-UpdateLifecycle.ps1 cases and the disposable Test-Installer.ps1 fixture. Tests use ephemeral RSA keys, which are not release signing keys. Never create a production key as a test side effect.

Keep progress anchoring, historical-scroll rejection, retry limits, ETA uncertainty and updater trust protections covered by behavior tests. Public CI builds and tests only; no upload, release secret or publisher deployment is configured.
