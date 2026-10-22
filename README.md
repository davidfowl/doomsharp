# DoomSharp

A playable, clean-room **classic WAD game engine built in C#**. DoomSharp loads
maps, runs single-player gameplay and draws each frame in software, without
using the original DOOM source or a managed game engine. The same engine runs
as a native desktop app and in the browser: .NET WebAssembly or the experimental
NetWasm toolchain, with JavaScript only bridging browser input, Canvas and audio.

**Playable, not a complete DOOM replacement.** The bundled, freely licensed
Freedoom Phase 1 starts automatically in the browser or with `--demo` on desktop.
No proprietary assets are included. Expect gaps in original-game compatibility;
see [supported features and limitations](docs/ENGINE.md#compatibility) before
trying another WAD.

## Play in your browser

| Version | What runs |
|---|---|
| [Play .NET WASM (Blazor AOT)](https://davidfowl.github.io/doomsharp/) | The C# engine compiled ahead of time for the browser. |
| [Play NetWasm](https://davidfowl.github.io/doomsharp/netwasm/) | The same engine compiled with the experimental [NetWasm toolchain](NetWasm/README.md). |

These links point to GitHub Pages and require a successful deployment. Both
versions use the same bundled Freedoom WAD, support a local WAD (up to 64 MiB),
and keep selected WADs on your device. Click the game to enable sound and mouse
capture; see [controls](docs/PLAYING.md#controls). Mobile/touch controls and
gamepads are not implemented.

## Quick start

Install the **.NET 10 SDK** and run from the repository root. To play on desktop:

```sh
dotnet run -c Release -- --demo --no-audio
```

Omit `--no-audio` when an audio device is available. To run the .NET WASM host
locally (without publishing), install the browser workload and open
<http://localhost:5187>:

```sh
dotnet workload install wasm-tools
dotnet run --project Browser/DoomSharp.Browser.csproj -c Release --urls http://localhost:5187
```

For publishing and other ways to run it, see [desktop and browser instructions](docs/PLAYING.md)
or the [NetWasm host instructions](NetWasm/README.md).

## Explore the project

Follow [Wad.cs](Wad.cs) for classic WAD parsing, [Game.cs](Game.cs) for movement,
combat and map actions, and [Renderer.cs](Renderer.cs) for BSP-based software
rendering. [Platform.cs](Platform.cs) and [Program.cs](Program.cs) connect the
engine to native windows and audio; [Browser/](Browser/) and [NetWasm/](NetWasm/)
host the same C# sources on the web. From textures and automap to switches,
music and save games, this is a compact place to explore how a playable game
crosses native and WebAssembly boundaries.

Native AOT targets Linux, Windows and macOS. For precise feature boundaries,
read [engine scope](docs/ENGINE.md); for reproducible checks, published-build
measurements and historical qualification, see
[development and verification](docs/DEVELOPMENT.md) and
[progress](PROGRESS.md). The engine is [MIT licensed](LICENSE); bundled
Freedoom assets retain their [own license and credits](assets/freedoom/COPYING.txt).
