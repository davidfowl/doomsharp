const saveKey = "doomsharp-save";

export function save(json) {
  localStorage.setItem(saveKey, json);
}

export function load() {
  return localStorage.getItem(saveKey);
}
