const root = document.getElementById("problem-root");
let assetBaseUrl = "https://localjudge.problem-assets/";

if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", function (event) {
        const message = event.data;

        if (!message || !message.type) {
            return;
        }

        if (message.type === "renderProblem") {
            assetBaseUrl = message.assetBaseUrl || assetBaseUrl;
            renderProblem(message.problem || null);
        }
    });
}

postToHost({ type: "viewerReady" });

function renderProblem(problem) {
    if (!problem || problem.emptyState) {
        root.innerHTML = `<section class="empty-state">${escapeHtml(problem?.message || "문제를 불러와주세요.")}</section>`;
        return;
    }

    const title = problem.id ? `[${problem.id}] ${problem.title || "제목 없는 문제"}` : (problem.title || "제목 없는 문제");
    const meta = [
        `시간 제한: ${problem.timeLimitMs || "-"} ms`,
        `메모리 제한: ${problem.memoryLimitMb || "-"} MB`,
        `예제: ${(problem.samples || []).length}개`,
        `채점 테스트: ${problem.testCaseCount || 0}개`,
        `제작자: ${problem.authorName || "-"}`,
        `출처: ${problem.source || "-"}`
    ];

    const html = [
        `<h1 class="problem-title">${escapeHtml(title)}</h1>`,
        `<div class="problem-meta">${meta.map(item => `<span class="meta-item">${escapeHtml(item)}</span>`).join("")}</div>`,
        renderSection("문제 설명", problem.description, problem.statementFormat),
        renderSection("입력", problem.inputFormat, problem.statementFormat),
        renderSection("출력", problem.outputFormat, problem.statementFormat),
        renderSamples(problem.samples || [])
    ].join("");

    root.innerHTML = html;
    attachImageFallbacks();
}

function renderSection(title, text, format) {
    const content = (text || "").trim();
    const rendered = content
        ? renderStatement(content, format)
        : `<p class="empty-state">${escapeHtml(title)}이 비어 있습니다.</p>`;

    return `<section class="problem-section"><h2>${escapeHtml(title)}</h2><div class="statement">${rendered}</div></section>`;
}

function renderSamples(samples) {
    if (!samples.length) {
        return `<section class="problem-section"><h2>예제</h2><p class="empty-state">등록된 예제가 없습니다.</p></section>`;
    }

    const sections = samples.map((sample, index) => {
        const number = index + 1;
        return [
            `<section class="problem-section sample-section">`,
            `<h2>예제 입력 ${number}</h2>`,
            `<pre class="sample-box"><code>${escapeHtml(sample.input || "")}</code></pre>`,
            `<h2>예제 출력 ${number}</h2>`,
            `<pre class="sample-box"><code>${escapeHtml(sample.output || "")}</code></pre>`,
            `</section>`
        ].join("");
    });

    return sections.join("");
}

function renderStatement(text, format) {
    if ((format || "").toLowerCase() !== "markdown-latex") {
        return `<p>${escapeHtml(text).replace(/\n/g, "<br>")}</p>`;
    }

    return renderMarkdown(text);
}

function renderMarkdown(text) {
    const lines = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
    const blocks = [];
    let index = 0;

    while (index < lines.length) {
        const line = lines[index];

        if (!line.trim()) {
            index++;
            continue;
        }

        if (line.trimStart().startsWith("```")) {
            const codeLines = [];
            index++;

            while (index < lines.length && !lines[index].trimStart().startsWith("```")) {
                codeLines.push(lines[index]);
                index++;
            }

            if (index < lines.length) {
                index++;
            }

            blocks.push(`<pre><code>${escapeHtml(codeLines.join("\n"))}</code></pre>`);
            continue;
        }

        if (line.trim() === "$$") {
            const mathLines = [];
            index++;

            while (index < lines.length && lines[index].trim() !== "$$") {
                mathLines.push(lines[index]);
                index++;
            }

            if (index < lines.length) {
                index++;
            }

            blocks.push(renderMath(mathLines.join("\n"), true));
            continue;
        }

        const heading = /^(#{1,3})\s+(.+)$/.exec(line);
        if (heading) {
            const level = heading[1].length;
            blocks.push(`<h${level}>${renderInline(heading[2])}</h${level}>`);
            index++;
            continue;
        }

        if (isTableStart(lines, index)) {
            const tableLines = [lines[index]];
            index += 2;

            while (index < lines.length && lines[index].includes("|") && lines[index].trim()) {
                tableLines.push(lines[index]);
                index++;
            }

            blocks.push(renderTable(tableLines));
            continue;
        }

        const unordered = /^[-*+]\s+/.test(line);
        const ordered = /^\d+\.\s+/.test(line);
        if (unordered || ordered) {
            const tag = ordered ? "ol" : "ul";
            const itemRegex = ordered ? /^\d+\.\s+/ : /^[-*+]\s+/;
            const items = [];

            while (index < lines.length && itemRegex.test(lines[index])) {
                items.push(`<li>${renderInline(lines[index].replace(itemRegex, ""))}</li>`);
                index++;
            }

            blocks.push(`<${tag}>${items.join("")}</${tag}>`);
            continue;
        }

        const paragraphLines = [line];
        index++;

        while (index < lines.length && lines[index].trim() && !startsNewBlock(lines, index)) {
            paragraphLines.push(lines[index]);
            index++;
        }

        blocks.push(`<p>${renderInline(paragraphLines.join("\n")).replace(/\n/g, "<br>")}</p>`);
    }

    return blocks.join("");
}

function startsNewBlock(lines, index) {
    const line = lines[index];
    return line.trimStart().startsWith("```")
        || line.trim() === "$$"
        || /^(#{1,3})\s+/.test(line)
        || /^[-*+]\s+/.test(line)
        || /^\d+\.\s+/.test(line)
        || isTableStart(lines, index);
}

function isTableStart(lines, index) {
    if (index + 1 >= lines.length) {
        return false;
    }

    return lines[index].includes("|") && /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$/.test(lines[index + 1]);
}

function renderTable(tableLines) {
    const rows = tableLines.map(splitTableRow);
    const header = rows[0] || [];
    const body = rows.slice(1);

    return [
        "<table>",
        "<thead><tr>",
        header.map(cell => `<th>${renderInline(cell)}</th>`).join(""),
        "</tr></thead>",
        "<tbody>",
        body.map(row => `<tr>${row.map(cell => `<td>${renderInline(cell)}</td>`).join("")}</tr>`).join(""),
        "</tbody>",
        "</table>"
    ].join("");
}

function splitTableRow(line) {
    return line
        .trim()
        .replace(/^\|/, "")
        .replace(/\|$/, "")
        .split("|")
        .map(cell => cell.trim());
}

function renderInline(text) {
    let result = "";
    let index = 0;

    while (index < text.length) {
        if (text[index] === "`") {
            const end = text.indexOf("`", index + 1);
            if (end > index) {
                result += `<code>${escapeHtml(text.slice(index + 1, end))}</code>`;
                index = end + 1;
                continue;
            }
        }

        if (text[index] === "!" && text[index + 1] === "[") {
            const image = parseImage(text, index);
            if (image) {
                result += image.html;
                index = image.nextIndex;
                continue;
            }
        }

        if (text[index] === "$") {
            const end = text.indexOf("$", index + 1);
            if (end > index) {
                result += renderMath(text.slice(index + 1, end), false);
                index = end + 1;
                continue;
            }
        }

        if (text.startsWith("**", index)) {
            const end = text.indexOf("**", index + 2);
            if (end > index) {
                result += `<strong>${renderInline(text.slice(index + 2, end))}</strong>`;
                index = end + 2;
                continue;
            }
        }

        if (text[index] === "*") {
            const end = text.indexOf("*", index + 1);
            if (end > index) {
                result += `<em>${renderInline(text.slice(index + 1, end))}</em>`;
                index = end + 1;
                continue;
            }
        }

        if (text[index] === "[") {
            const link = parseLink(text, index);
            if (link) {
                result += `<span class="disabled-link">${renderInline(link.label)}</span>`;
                index = link.nextIndex;
                continue;
            }
        }

        result += escapeHtml(text[index]);
        index++;
    }

    return result;
}

function parseImage(text, startIndex) {
    const labelEnd = text.indexOf("]", startIndex + 2);
    if (labelEnd < 0 || text[labelEnd + 1] !== "(") {
        return null;
    }

    const pathEnd = text.indexOf(")", labelEnd + 2);
    if (pathEnd < 0) {
        return null;
    }

    const alt = text.slice(startIndex + 2, labelEnd);
    const rawPath = text.slice(labelEnd + 2, pathEnd).trim().split(/\s+/)[0] || "";
    const resolved = resolveAssetUrl(rawPath);

    if (!resolved) {
        return {
            html: `<span class="missing-image">이미지 경로를 사용할 수 없습니다: ${escapeHtml(rawPath)}</span>`,
            nextIndex: pathEnd + 1
        };
    }

    return {
        html: `<img class="problem-image" src="${resolved}" alt="${escapeHtml(alt)}" data-source="${escapeHtml(rawPath)}">`,
        nextIndex: pathEnd + 1
    };
}

function parseLink(text, startIndex) {
    const labelEnd = text.indexOf("]", startIndex + 1);
    if (labelEnd < 0 || text[labelEnd + 1] !== "(") {
        return null;
    }

    const pathEnd = text.indexOf(")", labelEnd + 2);
    if (pathEnd < 0) {
        return null;
    }

    return {
        label: text.slice(startIndex + 1, labelEnd),
        nextIndex: pathEnd + 1
    };
}

function resolveAssetUrl(rawPath) {
    const normalized = rawPath.replace(/\\/g, "/");

    if (!normalized.toLowerCase().startsWith("assets/") || normalized.includes("..")) {
        return null;
    }

    const fileName = normalized.slice("assets/".length);
    if (!/\.(png|jpe?g|gif|webp)$/i.test(fileName)) {
        return null;
    }

    return assetBaseUrl + encodeURI(fileName);
}

function renderMath(tex, block) {
    let html = escapeHtml(tex.trim());
    const replacements = new Map([
        ["\\\\leq", "≤"], ["\\\\le", "≤"], ["\\\\geq", "≥"], ["\\\\ge", "≥"],
        ["\\\\neq", "≠"], ["\\\\ne", "≠"], ["\\\\times", "×"], ["\\\\cdot", "·"],
        ["\\\\pm", "±"], ["\\\\infty", "∞"], ["\\\\ldots", "…"], ["\\\\dots", "…"],
        ["\\\\sum", "∑"], ["\\\\prod", "∏"], ["\\\\alpha", "α"], ["\\\\beta", "β"],
        ["\\\\gamma", "γ"], ["\\\\delta", "δ"], ["\\\\epsilon", "ε"], ["\\\\theta", "θ"],
        ["\\\\lambda", "λ"], ["\\\\mu", "μ"], ["\\\\pi", "π"], ["\\\\sigma", "σ"],
        ["\\\\phi", "φ"], ["\\\\omega", "ω"], ["\\\\rightarrow", "→"], ["\\\\to", "→"]
    ]);

    for (const [pattern, value] of replacements) {
        html = html.replace(new RegExp(pattern, "g"), value);
    }

    html = html.replace(/\\frac\{([^{}]+)\}\{([^{}]+)\}/g, `<span class="fraction"><span>$1</span><span>$2</span></span>`);
    html = html.replace(/\\sqrt\{([^{}]+)\}/g, `√($1)`);
    html = html.replace(/([A-Za-z0-9)\]])\^\{([^{}]+)\}/g, `$1<sup>$2</sup>`);
    html = html.replace(/([A-Za-z0-9)\]])_\{([^{}]+)\}/g, `$1<sub>$2</sub>`);
    html = html.replace(/([A-Za-z0-9)\]])\^([A-Za-z0-9+\-]+)/g, `$1<sup>$2</sup>`);
    html = html.replace(/([A-Za-z0-9)\]])_([A-Za-z0-9+\-]+)/g, `$1<sub>$2</sub>`);
    html = html.replace(/[{}]/g, "");

    return block
        ? `<div class="math-block">${html}</div>`
        : `<span class="math-inline">${html}</span>`;
}

function attachImageFallbacks() {
    for (const image of document.querySelectorAll("img.problem-image")) {
        image.addEventListener("error", function () {
            const source = image.getAttribute("data-source") || image.getAttribute("src") || "";
            const placeholder = document.createElement("div");
            placeholder.className = "missing-image";
            placeholder.textContent = `이미지를 찾을 수 없습니다: ${source}`;
            image.replaceWith(placeholder);
        }, { once: true });
    }
}

function escapeHtml(value) {
    return String(value ?? "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

function postToHost(message) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(message);
    }
}
