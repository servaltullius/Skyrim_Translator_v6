# Third-party notices

TulliusTranslator is licensed under GPL-3.0-or-later (see `LICENSE`). The
release EXE is a self-contained single-file build: it carries the .NET runtime,
WPF and the NuGet packages below. Their license texts and notices are copied
beside the published application in `Licenses/`.

## .NET runtime and WPF

| Component | Version | License | Files |
| --- | --- | --- | --- |
| .NET runtime (Microsoft.NETCore.App) | 10.0.x | MIT | `dotnet-runtime-LICENSE.txt`, `dotnet-runtime-THIRD-PARTY-NOTICES.txt` |
| Windows Desktop runtime and WPF (Microsoft.WindowsDesktop.App) | 10.0.x | MIT | `dotnet-windowsdesktop-LICENSE.txt`, `dotnet-wpf-THIRD-PARTY-NOTICES.txt`, `dotnet-winforms-THIRD-PARTY-NOTICES.txt` |

The runtime files come from the 10.0.14 runtime packs. A release carries the
10.0 patch release its build used. The WPF and Windows Forms notices are
[dotnet/wpf THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/wpf/blob/v10.0.0/THIRD-PARTY-NOTICES.TXT)
and [dotnet/winforms THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/winforms/blob/v10.0.0/THIRD-PARTY-NOTICES.TXT)
at v10.0.0. The Windows Desktop pack ships System.Drawing.Common and other
libraries built in dotnet/winforms.

## Application packages

| Package | Version | License | Files |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.3.2 | MIT | `CommunityToolkit.Mvvm-LICENSE.txt`, `CommunityToolkit.Mvvm-THIRD-PARTY-NOTICES.txt` (both from the NuGet package) |
| Microsoft.Data.Sqlite and Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | `Microsoft.Data.Sqlite-LICENSE.txt` ([dotnet/efcore LICENSE.txt](https://github.com/dotnet/efcore/blob/v10.0.0/LICENSE.txt)) |
| SQLitePCLRaw.bundle_e_sqlite3, .core, .provider.e_sqlite3, .lib.e_sqlite3 | 2.1.12 | Apache-2.0 | `SQLitePCLRaw-LICENSE.txt`, `SQLitePCLRaw-NOTICE.txt` ([SQLitePCL.raw v2.1.12](https://github.com/ericsink/SQLitePCL.raw/tree/v2.1.12)) |

The native `e_sqlite3` library inside SQLitePCLRaw.lib.e_sqlite3 is built from
SQLite, which is in the public domain. Its notice is part of
`SQLitePCLRaw-NOTICE.txt`. That NOTICE also has sections for MS Open Tech code
and for SQLCipher and OpenSSL. The application does not use SQLCipher or OpenSSL.

## Direct plugin support

The plugin feature adds these runtime packages.

| Package | Version | Source / license |
| --- | --- | --- |
| SharpZipLib | 1.4.2 | [upstream MIT notice](https://github.com/icsharpcode/SharpZipLib/blob/v1.4.2/LICENSE.txt) |
| K4os.Compression.LZ4 and K4os.Compression.LZ4.Streams | 1.3.8 | [upstream MIT notice](https://github.com/MiloszKrajewski/K4os.Compression.LZ4/blob/1.3.8/LICENSE) |
| K4os.Hash.xxHash (transitive) | 1.0.8 | [upstream MIT notice](https://github.com/MiloszKrajewski/K4os.Hash.xxHash/blob/master/LICENSE) |
| System.IO.Pipelines (transitive) | from the .NET 10.0.x runtime | MIT. The shipped copy comes from the .NET runtime and is covered by `dotnet-runtime-LICENSE.txt`. `System.IO.Pipelines-LICENSE.txt` is the package's [6.0.3 notice](https://github.com/dotnet/runtime/blob/v6.0.3/LICENSE.TXT) |

The exact SharpZipLib NuGet package also declares copyright 2000–2022 SharpZipLib
Contributors. Its version-tagged source notice states 2000–2018. Both notices are
kept: the 2000–2022 claim here, and the source notice in `SharpZipLib-LICENSE.txt`.

xEdit, xTranslator and Mutagen were used as independent tools or references for
format behavior. They are not bundled with the application.
