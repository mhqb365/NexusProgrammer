# Contributing

Thanks for your interest in improving XGecu T48 SDK.

This project talks directly to programmer hardware and can erase or write flash
chips, so please keep changes small, testable, and explicit about hardware
impact.

## Development Setup

Requirements:

- Windows
- XGecu WinUSB driver
- .NET SDK with support for the target frameworks in the projects

Build:

```powershell
dotnet build .\XGecuT48SDK.sln
```

## Pull Requests

- Describe the hardware, chip model, and workflow used for testing.
- Include the exact CLI command or API path exercised.
- Keep destructive-operation behavior conservative by default.
- Update `README.md` or `PROTOCOL_NOTES.md` when protocol behavior changes.
- Avoid committing captures, binary dumps, logs, or generated build output.

## Protocol Notes

Reverse-engineering notes live in `PROTOCOL_NOTES.md`. If you add a new command
or decode a frame, include enough evidence for another contributor to reproduce
the observation without needing your local machine.
