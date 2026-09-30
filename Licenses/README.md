# Direct plugin support dependencies

The plugin feature adds these runtime packages. Their upstream license notices
are copied beside the published application in `Licenses/`.

| Package | Version | Source / license |
| --- | --- | --- |
| SharpZipLib | 1.4.2 | [upstream MIT notice](https://github.com/icsharpcode/SharpZipLib/blob/v1.4.2/LICENSE.txt) |
| K4os.Compression.LZ4 and K4os.Compression.LZ4.Streams | 1.3.8 | [upstream MIT notice](https://github.com/MiloszKrajewski/K4os.Compression.LZ4/blob/1.3.8/LICENSE) |
| K4os.Hash.xxHash (transitive) | 1.0.8 | [upstream MIT notice](https://github.com/MiloszKrajewski/K4os.Hash.xxHash/blob/master/LICENSE) |
| System.IO.Pipelines (transitive) | 6.0.3 | [upstream MIT notice](https://github.com/dotnet/runtime/blob/v6.0.3/LICENSE.TXT) |

The exact SharpZipLib NuGet package also declares copyright 2000–2022 SharpZipLib
Contributors; its version-tagged source notice states 2000–2018. Both notices are
retained here and in `SharpZipLib-LICENSE.txt` respectively.

xEdit, xTranslator and Mutagen were used as independent tools or references for
format behavior. They are not bundled with the application. This directory
documents the dependencies introduced by direct plugin support; the application
also uses the pre-existing .NET, SQLite and MVVM dependencies listed in its project files.
