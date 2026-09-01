/*
 * AdminForge — progressive enhancement.
 *
 * Everything here is optional. With JavaScript disabled, server-side tools still work
 * through a plain form post and the page renders identically; only client-side tools
 * need this file, and they say so on the page.
 *
 * No inline script, no eval, no third-party code — the app runs under a strict CSP.
 */
(function () {
    "use strict";

    var STATUS_CLASS = {
        Ok: "s-ok",
        Warning: "s-warn",
        Danger: "s-danger",
        Info: "s-info",
        Neutral: "s-neutral"
    };

    var TEXT_CLASS = {
        Ok: "t-ok",
        Warning: "t-warn",
        Danger: "t-danger",
        Info: "t-info"
    };

    var SPRITE = "/icons/sprite.svg#";

    // ---------------------------------------------------------------- helpers

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) { node.className = className; }
        if (text !== undefined && text !== null) { node.textContent = String(text); }
        return node;
    }

    function icon(name, size) {
        var svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("width", size);
        svg.setAttribute("height", size);
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("focusable", "false");
        var use = document.createElementNS("http://www.w3.org/2000/svg", "use");
        use.setAttribute("href", SPRITE + name);
        svg.appendChild(use);
        return svg;
    }

    function cellOf(raw) {
        return typeof raw === "string" ? { value: raw } : (raw || { value: "" });
    }

    // --------------------------------------------------- client-side renderer

    /* Mirrors Views/Shared/_ResultBlock.cshtml so a client-side tool and a
       server-side tool produce visually identical output. If you change one,
       change the other — the block contract is shared on purpose. */
    function renderBlock(block) {
        var wrap = el("div", "block");

        if (block.title) {
            wrap.appendChild(el("h3", "block-title", block.title));
        }

        switch (block.type) {
            case "status": {
                var line = el("div", "status-line " + (STATUS_CLASS[block.status] || "s-neutral"));
                var dot = el("span", "status-dot");
                dot.setAttribute("aria-hidden", "true");
                line.appendChild(dot);
                var body = el("span");
                body.appendChild(el("span", "status-message", block.message));
                if (block.detail) {
                    body.appendChild(document.createElement("br"));
                    body.appendChild(el("span", "status-detail", block.detail));
                }
                line.appendChild(body);
                wrap.appendChild(line);
                break;
            }

            case "keyValue": {
                var dl = el("dl", "kv");
                (block.rows || []).forEach(function (row) {
                    dl.appendChild(el("dt", null, row.label));
                    var classes = [];
                    if (TEXT_CLASS[row.status]) { classes.push(TEXT_CLASS[row.status]); }
                    if (row.monospace) { classes.push("mono"); }
                    dl.appendChild(el("dd", classes.join(" ") || null, row.value));
                });
                wrap.appendChild(dl);
                break;
            }

            case "table": {
                var scroll = el("div", "table-scroll");
                var rows = block.rows || [];

                if (rows.length === 0) {
                    scroll.appendChild(el("p", "table-empty", block.emptyMessage || "No results."));
                } else {
                    var table = el("table");
                    var thead = el("thead");
                    var headRow = el("tr");
                    (block.headers || []).forEach(function (header) {
                        var th = el("th", null, header);
                        th.setAttribute("scope", "col");
                        headRow.appendChild(th);
                    });
                    thead.appendChild(headRow);
                    table.appendChild(thead);

                    var tbody = el("tbody");
                    rows.forEach(function (row) {
                        var tr = el("tr");
                        row.forEach(function (raw) {
                            var cell = cellOf(raw);
                            var classes = [];
                            if (TEXT_CLASS[cell.status]) { classes.push(TEXT_CLASS[cell.status]); }
                            if (cell.monospace) { classes.push("mono"); }
                            tr.appendChild(el("td", classes.join(" ") || null, cell.value));
                        });
                        tbody.appendChild(tr);
                    });
                    table.appendChild(tbody);
                    scroll.appendChild(table);
                }

                wrap.appendChild(scroll);
                break;
            }

            case "code": {
                var codeWrap = el("div", "code-wrap");
                var copyable = block.copyable !== false;

                if (block.language || copyable) {
                    var bar = el("div", "code-bar");
                    bar.appendChild(el("span", null, block.language || "output"));
                    if (copyable) {
                        var button = el("button", "copy-btn");
                        button.type = "button";
                        button.setAttribute("data-copy", "");
                        button.appendChild(icon("copy", 12));
                        var label = el("span", null, "Copy");
                        label.setAttribute("data-copy-label", "");
                        button.appendChild(label);
                        bar.appendChild(button);
                    }
                    codeWrap.appendChild(bar);
                }

                var pre = el("pre");
                pre.appendChild(el("code", null, block.content));
                codeWrap.appendChild(pre);
                wrap.appendChild(codeWrap);
                break;
            }

            case "list": {
                var list = el(block.ordered ? "ol" : "ul", "list-block");
                (block.items || []).forEach(function (item) {
                    list.appendChild(el("li", null, item));
                });
                wrap.appendChild(list);
                break;
            }

            case "text":
                wrap.appendChild(el("p", "tool-lede", block.text));
                break;

            default:
                wrap.appendChild(el("p", "field-help", "Unsupported block type: " + block.type));
        }

        return wrap;
    }

    function renderOutcome(target, result, toolId, elapsedMs) {
        var outcome = el("div", "outcome");

        if (!result || result.ok === false) {
            var error = el("p", "result-error");
            error.appendChild(icon("alert", 17));
            error.appendChild(el("span", null, (result && result.error) || "The tool failed."));
            outcome.appendChild(error);
        } else {
            var blocks = el("div");
            (result.blocks || []).forEach(function (block) {
                blocks.appendChild(renderBlock(block));
            });
            outcome.appendChild(blocks);

            var foot = el("p", "result-foot");
            foot.appendChild(el("span", null, toolId));
            foot.appendChild(el("span", null, Math.round(elapsedMs) + " ms"));
            outcome.appendChild(foot);
        }

        target.replaceChildren(outcome);
    }

    // -------------------------------------------------------- form submission

    function collectValues(form) {
        var values = {};
        new FormData(form).forEach(function (value, key) {
            if (key !== "__RequestVerificationToken") {
                values[key] = value;
            }
        });

        // An unchecked box posts nothing, so absent checkboxes are filled in as false.
        form.querySelectorAll('input[type="checkbox"]').forEach(function (box) {
            values[box.name] = box.checked;
        });

        return values;
    }

    function setBusy(form, busy) {
        var button = form.querySelector("[data-run-button]");
        var label = form.querySelector("[data-run-label]");

        if (!button || !label) { return; }

        button.disabled = busy;

        if (busy) {
            label.textContent = "Running";
            if (!button.querySelector(".spinner")) {
                button.insertBefore(el("span", "spinner"), button.firstChild);
            }
        } else {
            label.textContent = "Run";
            var spinner = button.querySelector(".spinner");
            if (spinner) { spinner.remove(); }
        }
    }

    function runClientTool(form, output) {
        var modulePath = form.getAttribute("data-module");
        var toolId = form.getAttribute("data-tool-form");

        if (!modulePath) {
            renderOutcome(output, { ok: false, error: "This tool has no browser module." }, toolId, 0);
            return;
        }

        setBusy(form, true);
        var started = performance.now();

        import(modulePath)
            .then(function (module) {
                if (typeof module.run !== "function") {
                    throw new Error("The module for " + toolId + " does not export a run function.");
                }
                return module.run(collectValues(form));
            })
            .then(function (result) {
                renderOutcome(output, result, toolId, performance.now() - started);
            })
            .catch(function (error) {
                renderOutcome(
                    output,
                    { ok: false, error: error && error.message ? error.message : "The tool failed to run." },
                    toolId,
                    performance.now() - started);
            })
            .finally(function () {
                setBusy(form, false);
            });
    }

    function runServerTool(form, output) {
        setBusy(form, true);

        fetch(form.action, {
            method: "POST",
            body: new FormData(form),
            headers: { "X-AdminForge-Partial": "1" },
            credentials: "same-origin"
        })
            .then(function (response) {
                if (response.status === 429) {
                    throw new Error("This instance is rate limiting you. Wait a moment and try again.");
                }
                if (!response.ok) {
                    throw new Error("The server returned HTTP " + response.status + ".");
                }
                return response.text();
            })
            .then(function (html) {
                // The fragment is this application's own rendered markup, requested
                // same-origin with credentials — the same trust boundary as the page.
                output.innerHTML = html;
                syncFieldErrors(form, output);
            })
            .catch(function (error) {
                renderOutcome(
                    output,
                    { ok: false, error: error.message || "Could not reach the server." },
                    form.getAttribute("data-tool-form"),
                    0);
            })
            .finally(function () {
                setBusy(form, false);
            });
    }

    /* A server round trip re-renders the outcome pane but not the form, so any
       field-level errors it reported have to be reflected back onto the inputs. */
    function syncFieldErrors(form, output) {
        var failed = output.querySelector(".result-error") !== null
            && output.querySelector(".status-line") === null;

        form.querySelectorAll(".field").forEach(function (field) {
            field.classList.toggle("field-invalid", false);
        });

        if (!failed) { return; }

        form.querySelectorAll("[required]").forEach(function (input) {
            if (!input.value) {
                var field = input.closest(".field");
                if (field) { field.classList.add("field-invalid"); }
            }
        });
    }

    function wireForms() {
        document.querySelectorAll("[data-tool-form]").forEach(function (form) {
            var output = document.getElementById("tool-output");
            if (!output) { return; }

            var isClient = form.getAttribute("data-compute") === "client";

            form.addEventListener("submit", function (event) {
                if (!form.reportValidity()) { return; }

                event.preventDefault();

                if (isClient) {
                    runClientTool(form, output);
                } else {
                    runServerTool(form, output);
                }
            });

            form.addEventListener("reset", function () {
                window.setTimeout(function () {
                    renderPlaceholder(output, form);
                }, 0);
            });

            // A client-side tool with JavaScript off would post to a server route that
            // refuses it, so the button only becomes a real submit once we are wired up.
            if (isClient) {
                form.setAttribute("data-ready", "1");
            }
        });
    }

    function renderPlaceholder(output, form) {
        var placeholder = el("div", "placeholder");
        placeholder.appendChild(icon("tool", 26));
        placeholder.appendChild(el("p", null, "Fill in the form and run the tool. Results appear here."));
        output.replaceChildren(el("div", "outcome"));
        output.firstChild.appendChild(placeholder);
        form.querySelectorAll(".field").forEach(function (field) {
            field.classList.remove("field-invalid");
        });
    }

    // ------------------------------------------------------------- copy button

    function wireCopy() {
        document.addEventListener("click", function (event) {
            var button = event.target.closest("[data-copy]");
            if (!button) { return; }

            var wrap = button.closest(".code-wrap");
            var code = wrap && wrap.querySelector("pre code");
            if (!code || !navigator.clipboard) { return; }

            navigator.clipboard.writeText(code.textContent).then(function () {
                var label = button.querySelector("[data-copy-label]");
                if (!label) { return; }
                label.textContent = "Copied";
                button.classList.add("is-done");
                window.setTimeout(function () {
                    label.textContent = "Copy";
                    button.classList.remove("is-done");
                }, 1600);
            });
        });
    }

    // ---------------------------------------------------------- search hotkey

    function wireSearchHotkey() {
        var search = document.getElementById("masthead-q");
        if (!search) { return; }

        document.addEventListener("keydown", function (event) {
            if (event.key !== "/" || event.metaKey || event.ctrlKey || event.altKey) { return; }

            var active = document.activeElement;
            var tag = active && active.tagName;
            if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || (active && active.isContentEditable)) {
                return;
            }

            event.preventDefault();
            search.focus();
            search.select();
        });
    }

    // -------------------------------------------------------------------- boot

    function boot() {
        wireForms();
        wireCopy();
        wireSearchHotkey();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", boot);
    } else {
        boot();
    }
})();
