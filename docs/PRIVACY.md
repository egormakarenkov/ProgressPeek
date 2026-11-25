# Privacy

ProgressPeek reads accessible text from the selected observed desktop Codex / Work chat. Relevant prompt, additions and bounded response evidence are processed locally; when AI estimates are explicitly enabled, that evidence is sent through Codex CLI to the signed-in provider and consumes plan allowance. Provider processing and the CLI's own storage follow their respective policies. This app does not read authentication files, automate login, or use a paid API fallback.

Preferences, daily/per-task check counters, update metadata and a last-check metadata file are local. Diagnostic reports use bounded counts and classifications rather than chat content; manually supplied bug descriptions may contain information you choose to enter. Task estimates/history are held in memory. This utility does not save transcripts. The CLI can maintain its own state independently.

New installs cannot request an AI estimate until setup is finished with explicit consent. Turn AI estimates off or pause in Settings. CLI login status checks and account-usage reads do not request a model response. Updates are disabled at the network boundary until a publisher repository and public key are compiled in. Official setup links open only when clicked.

Legacy migration copies settings.json and checks.json only. Uninstall leaves data intact. Remove only `%LOCALAPPDATA%\ProgressPeek` yourself when you intend to erase the app's state; legacy `%LOCALAPPDATA%\TaskProgress` remains separately. Public release contact is pending publisher choice.
