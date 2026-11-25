# Security and reporting

Repository and publisher update key are unset in this local candidate. Configured updates require an exact repository identity, signed canonical manifest, trusted compiled RSA public key, channel/version checks and matching SHA256 payload. Linked/network/unsupported paths fail closed. The updater uses a validated helper and normal process exit, never elevation or forced termination. A previous executable and a failed-start candidate are retained for recovery; there is no post-launch health watchdog.

The utility is not a sandbox against another process running as the same user or an administrator. Credentials, private signing keys, local data and transcripts must never be included in reports or archives. Verify local package SHA256 checksums through an independently trusted channel; a checksum alone does not authenticate a publisher. This candidate is not Authenticode signed and may trigger Windows reputation warnings.

Private security contact and supported release policy are publisher release gates. Do not post sensitive details publicly until the publisher provides an appropriate reporting route. No fabricated contact is supplied.
