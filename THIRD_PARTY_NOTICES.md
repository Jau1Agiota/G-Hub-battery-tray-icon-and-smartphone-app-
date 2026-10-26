# Third-party dependencies and distribution notices

This file applies to third-party components only. Original project code has no approved license yet; MIT is proposed, pending owner approval. Obtain approval before public distribution.

## Application dependencies

| Component | Version | License / attribution |
| --- | --- | --- |
| Microsoft.Data.Sqlite, Microsoft.Data.Sqlite.Core | 10.0.14 | MIT; © Microsoft Corporation. All rights reserved. |
| SQLitePCLRaw.bundle_e_sqlite3, SQLitePCLRaw.core, SQLitePCLRaw.provider.e_sqlite3, SQLitePCLRaw.lib.e_sqlite3 | 2.1.13 | Apache-2.0; Copyright 2014–2024 SourceGear, LLC |
| SQLite native engine, included through SQLitePCLRaw | bundled native package above | SQLite engine is public domain; wrapper/package licenses still apply |
| .NET / Windows Desktop runtime (self-contained build) | 10.0.14 | MIT and included third-party notices; .NET Foundation and contributors / Microsoft |

Microsoft.Data.Sqlite is the one direct runtime NuGet dependency: it supplies the requested SQLite integration, managed provider and native SQLite library. System.Text.Json, Windows Forms, drawing, timers and threading come from .NET. No G HUB integration library is used. Exact transitive versions and integrity hashes are recorded in each project's `packages.lock.json`.

Include this file and the complete `licenses` folder with executable distributions. `docs/licenses/Apache-2.0.txt` contains the Apache license, `Microsoft-MIT.txt` the Microsoft/.NET MIT text, and the versioned runtime package notices are reproduced as `DotNet-Runtime-LICENSE.txt`, `DotNet-Runtime-ThirdPartyNotices.txt`, and `WindowsDesktop-LICENSE.txt`. No dependency source has been modified. These notices are copied beside the published executable automatically.

SQLitePCLRaw package metadata lists Apache-2.0 and the SourceGear copyright above; the NuGet archives contain no separate NOTICE file. SQLite public-domain declaration: https://sqlite.org/copyright.html . NuGet metadata and package hashes can be inspected at https://api.nuget.org/v3/index.json .

License text sources: Microsoft/.NET's official runtime LICENSE.TXT and Apache's official `.github` repository LICENSE, plus the exact runtime NuGet packages used in this build. No existing battery-monitoring project's code or integration reference was consulted.

## Development/test dependencies (not shipped in the application)

Direct dependencies: Microsoft.NET.Test.Sdk 17.14.1 (MIT), xUnit 2.9.3 (Apache-2.0), and xunit.runner.visualstudio 3.1.5 (Apache-2.0). Their transitive test-only dependencies are recorded in the test lockfile. They are restored for development, not included in the executable distribution. The .NET SDK and GitHub Actions used by CI are development tools, not vendored application code.

## Security maintenance

The initial Microsoft.Data.Sqlite 10.0.0 dependency resolved a native SQLite package flagged by NuGet advisory GHSA-2m69-gcr7-jv3q. It was replaced with 10.0.14, which resolves SQLitePCLRaw 2.1.13. No warning or integrity check was suppressed. Re-run `dotnet list PeripheralBatteryCompanion.slnx package --vulnerable --include-transitive` before releases; advisory results can change after this build.
