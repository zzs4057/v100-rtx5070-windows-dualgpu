# Contributing

Contributions are welcome through issues and pull requests.

## Before opening an issue

1. Read [docs/troubleshooting.md](docs/troubleshooting.md).
2. Remove local paths, usernames, device UUIDs, serial numbers, IP addresses
   and model tokens from logs.
3. State whether the problem occurs with the released runtime or a local build.

## Pull requests

- Keep changes focused and documented.
- Do not commit model weights, NVIDIA installers, generated logs or telemetry.
- Do not commit credentials or machine-specific configuration.
- Update documentation when behavior changes.
- Run the PowerShell parser checks described by CI.

## Driver-related changes

Do not submit unsigned kernel drivers, modified driver packages, test-signing
workarounds or instructions that disable Secure Boot. Driver notes must refer
to vendor-signed binaries and clearly describe rollback steps.

## License

By contributing, you agree that your contribution is licensed under the MIT
License in [LICENSE](LICENSE).
