# Update artifacts

Files served by the master's update endpoints. The directory name is the default
for `ArtifactDirectory` in `masterapi.json` and is resolved **relative to the
executable**, so a deployed copy keeps its own artifacts.

```
artifacts/
└── update/
    └── {channel}/                 stable, beta, ...
        └── {kind}/                game | cef | launcher | client
            ├── version            plain version string, e.g. 1.0.0
            └── files.zip          update archive
```

`game/version` is the supported GTA V version. It is deliberately **not** published
here: a guessed value would be worse than none, and the launcher reports
"Failed to check currently supported GTA 5 version" when the endpoint is absent.

`cef/version` only matters when `cef\libcef.dll` is present next to the launcher,
which is not the case for a build from source.

## launcher/version

This file must match the launcher build it serves, otherwise the launcher offers an
update on every start. Read it from the executable after building:

```powershell
$exe = 'F:\GTMP\GTMP-Launcher\src\GTANetworkLauncher.WPF\bin\Debug\GrandTheftMultiplayer.Launcher.exe'
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion

$dir = 'artifacts\update\stable\launcher'
New-Item -ItemType Directory -Path $dir -Force | Out-Null
Set-Content -LiteralPath "$dir\version" -Value $version -NoNewline -Encoding utf8
```

## Hashes

There is no hash file. The master computes SHA-256 from `files.zip` on request, so
the published digest can never disagree with the published archive.

## Note on .gitignore

The shared .NET `.gitignore` ignores `artifacts/`. The files here are tracked with
`git add -f`, because they are deployment data, not build output.
