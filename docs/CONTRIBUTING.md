# Contributing locally

License, public repository and contribution policy remain pending publisher decisions. This source candidate grants no automatic rights to redistribute or modify and does not invite public contributions under an assumed license.

For authorized local maintenance, use C# 5 / .NET Framework WPF and Windows PowerShell 5.1. Keep dependencies and model calls out of tests; use disposable folders, no credential inspection or live profile edits. Run build.ps1, Test-Distribution.ps1, all seven Test-UpdateLifecycle.ps1 cases and the disposable Test-Installer.ps1 fixture. Tests use ephemeral RSA keys, which are not release signing keys. Never create a production key as a test side effect.

Keep progress anchoring, historical-scroll rejection, retry limits, ETA uncertainty and updater trust protections covered by behavior tests. Public CI builds and tests only; no upload, release secret or publisher deployment is configured.
