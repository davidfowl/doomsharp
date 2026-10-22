# NetWasm browser host

This project compiles the same DoomSharp engine files as the Blazor host using
[NetWasm](https://github.com/zion-sati/NetWasm) SDK 0.10.1 and its
`NetWasm.System.Linq` and `NetWasm.System.Text.Json` 0.10.0 packages. It publishes
a standalone JS-callable WebAssembly module; the browser page uses the existing
Canvas/WebAudio/keyboard adapter. The two browser builds are separate: switching
versions loads a new page, not a second runtime in the same page.

From the repository root, with .NET SDK 10.0.303 or newer and `wasm-tools`:

```sh
dotnet publish Browser/DoomSharp.Browser.csproj -c Release -o artifacts/browser
dotnet publish NetWasm/DoomSharp.NetWasm.csproj -c Release -o artifacts/netwasm
mkdir -p artifacts/browser/wwwroot/netwasm
cp -a artifacts/netwasm/browser artifacts/browser/wwwroot/netwasm/
cp artifacts/netwasm/index.html artifacts/netwasm/app.mjs artifacts/netwasm/host.js artifacts/netwasm/style.css artifacts/browser/wwwroot/netwasm/
dotnet run --project tools/Serve -c Release -- artifacts/browser/wwwroot --urls http://localhost:5187 --path-base /doomsharp
```

Open `http://localhost:5187/doomsharp/netwasm/`. The NetWasm page expects
`../assets/freedoom/freedoom1.wad` (and the credits) from the Blazor publish.
Keep the trailing slash on both URLs. The `NetWasm` project by itself is **not**
a standalone site with bundled game assets. The NetWasm raw Wasm module does not
load Blazor or .NET assemblies in the browser. It runs on the main thread;
long synchronous exports can briefly block page interaction.

NetWasm is a pre-1.0 toolchain with a
[Community License for developer tooling and separate MIT/Apache runtime terms](https://github.com/zion-sati/NetWasm/blob/main/docs/licensing.md).
Review its terms before using the SDK in a commercial build pipeline; generated
applications retain their authors' chosen license subject to included notices.

The `NETWASM` conditional only excludes unused desktop filesystem helpers;
engine/rendering/gameplay sources otherwise remain shared. See
[development and verification](../docs/DEVELOPMENT.md#published-build-comparison)
for benchmark methodology and [playing and publishing](../docs/PLAYING.md#browser-webassembly)
for browser limitations and site deployment instructions.
