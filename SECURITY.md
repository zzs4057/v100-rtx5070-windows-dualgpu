# Security policy

## Supported versions

Security fixes are applied to the current `main` branch and the latest tagged
release.

## Report a vulnerability

Do not open a public issue for a credential leak, arbitrary code execution,
unsafe command construction or privacy exposure. Use GitHub private
vulnerability reporting if it is available for this repository, or contact the
maintainer through the profile listed on the repository page.

## Privacy rules

Issue reports and pull requests must not include:

- usernames, hostnames, email addresses or account tokens
- GPU UUIDs, serial numbers or stable device instance IDs
- public or private IP addresses and MAC addresses
- complete local paths
- raw logs containing any of the above

Use placeholders such as `<MODEL_PATH>`, `<USER>` and `<GPU_UUID>`.

## Driver safety

This project documents an unsupported dual-driver coexistence workflow. It
does not distribute modified kernel drivers and does not recommend disabling
Secure Boot or test signing.
