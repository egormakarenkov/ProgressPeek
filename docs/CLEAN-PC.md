# Real clean-PC / VM verification checklist

Isolated test folders on the development host are not a clean machine. Windows 11 Home on that host has no Windows Sandbox. These steps remain required on another clean Windows machine or disposable VM before a public release.

1. Start with a standard non-admin user, no prior widget or data, and test without Codex CLI installed. Install per user without elevation; verify shortcuts and optional desktop task, dark/light wizard behavior and no automatic app launch.
2. Launch manually. Cancel setup, observe a task and press Refresh: no estimate request or allowance increment may occur. Reopen setup. Read disclosure, finish unchecked, verify local observation only. Then explicitly opt in and check status.
3. Use official instructions to install/login manually. Test missing CLI, wrong executable, other login, signed-in plan, unavailable model and entitlement failures. Never record credentials or raw status output. No API fallback should occur.
4. In the observed desktop Codex / Work UI, start a request, submit an addition, scroll to history, return, and finish. Latest scope survives; drafts are not submitted additions; response completion is distinct from success.
5. Verify minimum card width at maximum text scale, themed wrapped tooltip, settings, other-monitor focus, same-monitor takeover, minimized/closed app and native drag/resize. Unsupported standard/web chats must fail conservatively.
6. With explicit opt-in, perform a bounded estimate and verify usage/freshness/retry behavior. Test ETA default-off and saved-on state without extra estimate calls.
7. Repeat installation as upgrade with widget closed; uninstall. Verify executable/shortcuts removed while data and unrelated sentinels remain. Repeat portable operation without installation.
8. Test legacy settings/ledger migration, existing new-setting conflict, interrupted/failing copy and preserved originals. Never copy auth, transcripts or old pending executable stages.
9. For a configured publisher fixture only, verify signed tampering rejection and all seven local lifecycle scenarios. Check retained previous/failed executable recovery and no forced process kill.

Local repeatable commands: `Test-Distribution.ps1 -ExecutablePath <candidate>`, `Test-Installer.ps1 -ExecutablePath <candidate> -CompilerPath <Inno>\ISCC.exe`, and `Test-UpdateLifecycle.ps1 -SourceRoot <source> -Case <case>` for manual, stable-beta-off, beta-off, scheduled, blocked, parent-timeout, start-failure. Installer testing compiles a unique disposable AppId, disables shortcuts, uses a temporary install folder and never launches the candidate. These are isolated fixtures, not fresh-user or VM certification.
