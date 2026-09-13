# Third-party notices

AnnoTape depends on the following permissively licensed projects. Exact resolved versions are recorded by NuGet restore assets during each build.

| Component | Pinned version | License | Project |
|---|---:|---|---|
| CupriFace | 0.24.0 | MIT | https://github.com/Wixely/CupriFace |
| AngleSharp (through CupriFace) | 1.7.0 | MIT | https://github.com/AngleSharp/AngleSharp |
| SkiaSharp | 3.116.1 | MIT | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp (through CupriFace) | 8.3.0.1 | MIT | https://github.com/mono/SkiaSharp |
| Silk.NET (through CupriFace.Shell) | 2.22.0 | MIT | https://github.com/dotnet/Silk.NET |
| GLFW (through CupriFace.Shell) | 3.4.0 | Zlib | https://github.com/glfw/glfw |
| SDL (through CupriFace.Shell) | 2.30.8 | Zlib | https://github.com/libsdl-org/SDL |
| Tmds.DBus.Protocol (through CupriFace.Shell) | 0.94.2 | MIT | https://github.com/tmds/Tmds.DBus |
| Dapper | 2.1.79 | Apache-2.0 | https://github.com/DapperLib/Dapper |
| DnaX | 10.0.0-alpha.2 / commit `ab1471d` | MIT | https://github.com/Wixely/DnaX |
| Microsoft.Data.Sqlite | 10.0.11 | MIT | https://github.com/dotnet/efcore |
| AndroidX Core | 1.16.0.3 | Apache-2.0 | https://developer.android.com/jetpack/androidx |
| MSTest | 4.3.3 | MIT | https://github.com/microsoft/testfx |

Each dependency remains subject to its own license. Release packaging must retain any license files brought in by those packages and recheck this table after dependency updates.
