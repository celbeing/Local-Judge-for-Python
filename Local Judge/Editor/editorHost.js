let editor = null;
let isEditorReady = false;

const defaultPythonCode =
    `# Python 코드를 작성하세요.
import sys

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

    editor = monaco.editor.create(document.getElementById("editor"), {
        value: defaultPythonCode,
        language: "python",
        theme: "localJudgeDark",
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

function postToHost(message) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(message);
    }
}