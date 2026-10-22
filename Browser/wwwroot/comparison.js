const container = document.querySelector("#comparison");
try {
    const response = await fetch(new URL("./comparison.json", import.meta.url));
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const stats = await response.json();
    const runs = Object.fromEntries(stats.render.map(run => [run.runtime, run]));
    const heading = document.createElement("h2");
    heading.textContent = "Published build comparison";
    const description = document.createElement("p");
    description.textContent = "Shared Freedoom WAD excluded. Raw runtime bytes; 120 paused initial-scene software renders in headless Chromium (median of five runs, lower is faster). Your device may differ.";
    const table = document.createElement("table");
    const header = table.insertRow();
    for (const text of ["Runtime", "Download (raw)", "120 renders"])
        header.insertCell().textContent = text;
    for (const [runtime, bytes, label] of [
        ["blazor-aot", stats.blazorBytes, ".NET WASM (Blazor AOT)"],
        ["netwasm", stats.netwasmBytes, "NetWasm"]]) {
        const row = table.insertRow();
        row.insertCell().textContent = label;
        row.insertCell().textContent = `${(bytes / 1024 / 1024).toFixed(2)} MiB`;
        row.insertCell().textContent = `${runs[runtime].renderMilliseconds.toFixed(1)} ms`;
    }
    container.replaceChildren(heading, description, table);
} catch (error) {
    container.textContent = `Build comparison unavailable: ${error.message}`;
}
