// Monaco editor loaded lazily from the CDN (no build pipeline). Falls back gracefully when the
// CDN is unreachable (offline container) — initialize() reports success: false and the
// component renders a plain textarea instead.
const monacoVersion = "0.52.2";
const monacoBase = `https://cdn.jsdelivr.net/npm/monaco-editor@${monacoVersion}/min/vs`;

let monacoLoaderPromise = null;

function loadMonaco() {
    if (monacoLoaderPromise) {
        return monacoLoaderPromise;
    }

    monacoLoaderPromise = new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = `${monacoBase}/loader.min.js`;
        script.onload = () => {
            window.require.config({ paths: { vs: monacoBase } });
            window.require(["vs/editor/editor.main"], resolve, reject);
        };
        script.onerror = reject;
        document.head.appendChild(script);
    });

    return monacoLoaderPromise;
}

// The editor follows the system light/dark preference, like the rest of the UI.
const darkQuery = window.matchMedia("(prefers-color-scheme: dark)");
const preferredTheme = () => (darkQuery.matches ? "vs-dark" : "vs");
darkQuery.addEventListener("change", () => window.monaco?.editor.setTheme(preferredTheme()));

export async function initialize(element, initialValue, language, dotNetRef) {
    try {
        await loadMonaco();

        const editor = monaco.editor.create(element, {
            value: initialValue,
            language: language || "plaintext",
            theme: preferredTheme(),
            automaticLayout: true,
            minimap: { enabled: false },
            fontSize: 13,
            scrollBeyondLastLine: false,
        });

        // Debounce change notifications — one round trip per pause, not per keystroke.
        let changeTimer = null;
        editor.onDidChangeModelContent(() => {
            clearTimeout(changeTimer);
            changeTimer = setTimeout(() => dotNetRef.invokeMethodAsync("OnContentChanged", editor.getValue()), 300);
        });

        element.monacoEditorInstance = editor;
        return { success: true };
    } catch {
        return { success: false };
    }
}

export function dispose(element) {
    if (element.monacoEditorInstance) {
        element.monacoEditorInstance.dispose();
        element.monacoEditorInstance = null;
    }
}
