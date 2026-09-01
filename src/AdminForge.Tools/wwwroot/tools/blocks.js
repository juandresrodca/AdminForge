/*
 * Result-block builders for client-side tools.
 *
 * These produce exactly the shapes AdminForge.Core.Results emits on the server, so a
 * browser tool and a server tool render through the same components and look
 * identical. Import them from a tool module:
 *
 *   import { ok, fail, status, keyValues, table, code, list, text } from "./blocks.js";
 *
 *   export function run(input) {
 *       if (!input.Value) { return fail("Paste a value first."); }
 *       return ok(status("Ok", "Looks good"), keyValues(null, [["Length", input.Value.length]]));
 *   }
 */

/** A successful result carrying the given blocks. */
export function ok(...blocks) {
    return { ok: true, blocks: blocks.filter(Boolean) };
}

/** An expected, user-facing failure. Write the message for an admin, not a developer. */
export function fail(error) {
    return { ok: false, error };
}

/**
 * A colour-coded verdict line — the headline answer.
 * @param {"Ok"|"Warning"|"Danger"|"Info"|"Neutral"} level severity colouring
 * @param {string} message the verdict
 * @param {string} [detail] optional supporting line
 * @param {string} [title] optional heading above the block
 */
export function status(level, message, detail, title) {
    return { type: "status", status: level, message, detail, title };
}

/**
 * A definition list. Rows are `[label, value]` pairs, or
 * `[label, value, { status, monospace }]` when a row needs colouring.
 * @param {string|null} title optional heading
 * @param {Array} rows the rows
 */
export function keyValues(title, rows) {
    return {
        type: "keyValue",
        title,
        rows: rows.filter(Boolean).map(([label, value, options = {}]) => ({
            label,
            value: value === null || value === undefined || value === "" ? "—" : String(value),
            status: options.status || "Neutral",
            monospace: options.monospace === true
        }))
    };
}

/**
 * A table. Cells may be plain strings or `{ value, status, monospace }`.
 * @param {string|null} title optional heading
 * @param {string[]} headers column headings
 * @param {Array} rows row data
 * @param {string} [emptyMessage] shown instead of the table when there are no rows
 */
export function table(title, headers, rows, emptyMessage) {
    return { type: "table", title, headers, rows, emptyMessage };
}

/**
 * A copyable monospace payload block.
 * @param {string} content the payload
 * @param {string} [language] informational tag shown on the block
 * @param {string} [title] optional heading
 */
export function code(content, language, title) {
    return { type: "code", content, language, title, copyable: true };
}

/**
 * A bulleted or numbered list.
 * @param {string|null} title optional heading
 * @param {string[]} items the items
 * @param {boolean} [ordered] render numbered
 */
export function list(title, items, ordered = false) {
    return { type: "list", title, items, ordered };
}

/** A paragraph of prose. Use sparingly — prefer structured blocks. */
export function text(body, title) {
    return { type: "text", text: body, title };
}

/** Marks a value as monospace inside a table cell. */
export function mono(value, level = "Neutral") {
    return { value: String(value), monospace: true, status: level };
}
