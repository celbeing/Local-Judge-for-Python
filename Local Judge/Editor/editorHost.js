let editor = null;
let isEditorReady = false;
let currentTheme = "localJudgeDark";
let themeCatalogPromise = null;
const loadedExternalThemes = new Map();

const defaultPythonCode =
    `import sys

def main():
    pass

if __name__ == "__main__":
    main()
`;

require(["vs/editor/editor.main"], function () {
    monaco.editor.defineTheme("localJudgeDark", {
        base: "vs-dark",
        inherit: true,
        rules: [],
        colors: {
            "editor.background": "#1E1E1E"
        }
    });

    monaco.editor.defineTheme("localJudgeLight", {
        base: "vs",
        inherit: true,
        rules: [],
        colors: {
            "editor.background": "#FFFFFF"
        }
    });

    editor = monaco.editor.create(document.getElementById("editor"), {
        value: defaultPythonCode,
        language: "python",
        theme: currentTheme,
        automaticLayout: true,
        fontFamily: "Consolas, 'Courier New', monospace",
        fontSize: 15,
        lineNumbers: "on",
        minimap: {
            enabled: true
        },
        scrollBeyondLastLine: false,
        tabSize: 4,
        insertSpaces: true,
        detectIndentation: false,
        wordWrap: "off",
        bracketPairColorization: {
            enabled: true
        }
    });

    isEditorReady = true;

    postToHost({
        type: "editorReady"
    });

    editor.onDidChangeModelContent(function () {
        postToHost({
            type: "codeChanged",
            code: editor.getValue()
        });
    });
});

window.addEventListener("resize", function () {
    if (editor) {
        editor.layout();
    }
});

window.chrome.webview.addEventListener("message", function (event) {
    const message = event.data;

    if (!message || !message.type) {
        return;
    }

    switch (message.type) {
        case "setCode":
            setCode(message.code || "");
            break;

        case "getCode":
            postToHost({
                type: "currentCode",
                requestId: message.requestId || "",
                code: getCode()
            });
            break;

        case "focus":
            if (editor) {
                editor.focus();
            }
            break;

        case "setTheme":
            setTheme(message.theme || "");
            break;

        case "setReadOnly":
            setReadOnly(message.readOnly === true);
            break;
    }
});

function setCode(code) {
    if (!editor) {
        return;
    }

    editor.setValue(code);
}

function getCode() {
    if (!editor) {
        return "";
    }

    return editor.getValue();
}

function setReadOnly(readOnly) {
    if (!editor) {
        return;
    }

    editor.updateOptions({
        readOnly: readOnly,
        domReadOnly: readOnly
    });
}

async function setTheme(theme) {
    currentTheme = normalizeTheme(theme);
    const themeData = await loadThemeData(currentTheme);
    const background = getThemeBackground(currentTheme, themeData);
    document.documentElement.style.background = background;
    document.body.style.background = background;

    if (editor) {
        monaco.editor.setTheme(getMonacoThemeName(currentTheme));
    }
}

function normalizeTheme(theme) {
    switch (theme) {
        case "localJudgeLight":
        case "vs":
            return "localJudgeLight";

        case "localJudgeDark":
        case "vs-dark":
            return "localJudgeDark";

        case "hc-black":
        case "hc-light":
            return theme;

        default:
            if (theme.startsWith("monaco-themes:")) {
                return theme;
            }

            return "localJudgeDark";
    }
}

async function loadThemeData(theme) {
    if (!theme.startsWith("monaco-themes:")) {
        return null;
    }

    if (loadedExternalThemes.has(theme)) {
        return loadedExternalThemes.get(theme);
    }

    try {
        const catalog = await loadThemeCatalog();
        const themeInfo = catalog.find(function (item) {
            return item && item.id === theme && item.file;
        });

        if (!themeInfo) {
            currentTheme = "localJudgeDark";
            return null;
        }

        const response = await fetch("./themes/" + encodeThemeFileName(themeInfo.file));
        if (!response.ok) {
            currentTheme = "localJudgeDark";
            return null;
        }

        const themeData = await response.json();
        ensureEditorBackground(themeData);
        monaco.editor.defineTheme(getMonacoThemeName(theme), themeData);
        loadedExternalThemes.set(theme, themeData);
        return themeData;
    }
    catch {
        currentTheme = "localJudgeDark";
        return null;
    }
}

async function loadThemeCatalog() {
    if (!themeCatalogPromise) {
        themeCatalogPromise = fetch("./themes/themeCatalog.json")
            .then(function (response) {
                return response.ok ? response.json() : [];
            })
            .catch(function () {
                return [];
            });
    }

    return themeCatalogPromise;
}

function encodeThemeFileName(fileName) {
    return String(fileName || "")
        .split("/")
        .map(encodeURIComponent)
        .join("/");
}

function getMonacoThemeName(theme) {
    if (!theme.startsWith("monaco-themes:")) {
        return theme;
    }

    return "monacoThemes-" + theme.substring("monaco-themes:".length).replace(/[^a-zA-Z0-9_-]/g, "-");
}

function getThemeBackground(theme, themeData) {
    const themeBackground = getThemeDataBackground(themeData);
    if (themeBackground) {
        return themeBackground;
    }

    switch (theme) {
        case "localJudgeLight":
        case "hc-light":
            return "#ffffff";

        case "hc-black":
            return "#000000";

        default:
            return "#1e1e1e";
    }
}

function ensureEditorBackground(themeData) {
    if (!themeData) {
        return;
    }

    if (!themeData.colors) {
        themeData.colors = {};
    }
    if (themeData.colors["editor.background"]) {
        themeData.colors["editor.background"] = normalizeColor(themeData.colors["editor.background"]);
        return;
    }

    const background = getThemeDataBackground(themeData);
    if (background) {
        themeData.colors["editor.background"] = background;
    }
}


function getThemeDataBackground(themeData) {
    if (!themeData) {
        return "";
    }

    if (themeData.colors && themeData.colors["editor.background"]) {
        return normalizeColor(themeData.colors["editor.background"]);
    }

    if (Array.isArray(themeData.rules)) {
        const rootRule = themeData.rules.find(function (rule) {
            return rule && rule.token === "" && rule.background;
        });

        if (rootRule) {
            return normalizeColor(rootRule.background);
        }
    }

    return "";
}

function normalizeColor(color) {
    if (!color) {
        return "";
    }

    const text = String(color).trim();
    return text.startsWith("#") ? text : "#" + text;
}

function postToHost(message) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(message);
    }
}
