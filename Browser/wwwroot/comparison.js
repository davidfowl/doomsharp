const container = document.querySelector("#comparison");
try {
    const response = await fetch(new URL("./comparison.json", import.meta.url));
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const stats = await response.json();
    const runs = Object.fromEntries(stats.render.map(run => [run.runtime, run]));
    const heading = document.createElement("h2");
    heading.textContent = "Published build comparison";
    const description = document.createElement("p");
    description.textContent = `Shared Freedoom WAD excluded. Uncompressed first-load response bytes in a fresh headless Chromium context; published runtime footprint includes deployable JS/WASM/HTML/CSS, raw and compressed with Brotli quality 11. 120 paused initial-scene software renders (median of five runs; lower is faster). ${stats.host.os}, ${stats.host.architecture}, ${stats.host.processors} logical CPUs; ${stats.measuredAtUtc}. Your device may differ.`;
    const table = document.createElement("table");
    const header = table.insertRow();
    for (const text of ["Runtime", "First load (raw)", "Published files (raw)", "Published files (Brotli)", "120 renders"])
        header.insertCell().textContent = text;
    for (const [runtime, bytes, brotliBytes, label] of [
        ["blazor-aot", stats.blazorBytes, stats.blazorBrotliBytes, ".NET WASM (Blazor AOT)"],
        ["netwasm", stats.netwasmBytes, stats.netwasmBrotliBytes, "NetWasm"]]) {
        const row = table.insertRow();
        row.insertCell().textContent = label;
        row.insertCell().textContent = `${(runs[runtime].firstLoad.rawBytes / 1024 / 1024).toFixed(2)} MiB`;
        row.insertCell().textContent = `${(bytes / 1024 / 1024).toFixed(2)} MiB`;
        row.insertCell().textContent = `${(brotliBytes / 1024 / 1024).toFixed(2)} MiB`;
        row.insertCell().textContent = `${runs[runtime].renderMilliseconds.toFixed(1)} ms`;
    }
    container.replaceChildren(heading, description, table);
} catch (error) {
    container.textContent = `Build comparison unavailable: ${error.message}`;
}
