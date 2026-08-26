# Contributing

Thanks for helping improve RT809F .NET SDK.

## Development

- Install the .NET 8 SDK on Windows.
- Install the FTDI D2XX driver.
- Keep `ftd2xx64.dll` or `ftd2xx.dll` available to the process when running hardware tests.
- Build with:

```powershell
dotnet build src/RT809F.SDK.csproj
dotnet build src/samples/RT809F.Cli/RT809F.Cli.csproj
```

## Pull Requests

- Keep changes focused and explain hardware assumptions in the PR description.
- Do not commit generated `bin/` or `obj/` output.
- Do not commit proprietary driver binaries or firmware dumps.
- Include the RT809F programmer model, flash chip part number, and Windows/.NET version when reporting hardware behavior.
