import { ok, fail, status, keyValues, code } from "./blocks.js";

const HTML_ESCAPES = {
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
    "'": "&#39;"
};

const HTML_UNESCAPES = {
    amp: "&",
    lt: "<",
    gt: ">",
    quot: "\"",
    apos: "'",
    "#39": "'",
    nbsp: " "
};

const toBytes = (text) => new TextEncoder().encode(text);
const fromBytes = (bytes) => new TextDecoder("utf-8", { fatal: true }).decode(bytes);

/* btoa and atob only speak Latin-1, so anything above U+00FF has to be routed
   through UTF-8 bytes by hand. Skipping this is why so many base64 tools mangle
   accented characters. */
function bytesToBase64(bytes) {
    let binary = "";
    for (const byte of bytes) {
        binary += String.fromCharCode(byte);
    }
    return btoa(binary);
}

function base64ToBytes(value) {
    const binary = atob(value);
    return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

const ENCODERS = {
    Base64: (text) => bytesToBase64(toBytes(text)),
    Base64Url: (text) => bytesToBase64(toBytes(text)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, ""),
    Url: (text) => encodeURIComponent(text),
    Hex: (text) => Array.from(toBytes(text), (b) => b.toString(16).padStart(2, "0")).join(""),
    HtmlEntities: (text) => text.replace(/[&<>"']/g, (character) => HTML_ESCAPES[character])
};

const DECODERS = {
    Base64: (value) => fromBytes(base64ToBytes(value.replace(/\s+/g, ""))),

    Base64Url: (value) => {
        const normalised = value.trim().replace(/-/g, "+").replace(/_/g, "/");
        return fromBytes(base64ToBytes(normalised + "=".repeat((4 - (normalised.length % 4)) % 4)));
    },

    Url: (value) => decodeURIComponent(value),

    Hex: (value) => {
        const clean = value.replace(/(0x|[\s:,-])/gi, "");

        if (clean.length % 2 !== 0) {
            throw new Error("A hex string needs an even number of digits.");
        }

        if (!/^[0-9a-f]*$/i.test(clean)) {
            throw new Error("That contains characters that are not hex digits.");
        }

        return fromBytes(Uint8Array.from(clean.match(/../g) ?? [], (pair) => parseInt(pair, 16)));
    },

    HtmlEntities: (value) => value.replace(/&(#?\w+);/g, (whole, name) => {
        if (name in HTML_UNESCAPES) {
            return HTML_UNESCAPES[name];
        }
        if (/^#\d+$/.test(name)) {
            return String.fromCodePoint(parseInt(name.slice(1), 10));
        }
        if (/^#x[0-9a-f]+$/i.test(name)) {
            return String.fromCodePoint(parseInt(name.slice(2), 16));
        }
        return whole;
    })
};

const LANGUAGE = {
    Base64: "base64",
    Base64Url: "base64url",
    Url: "url-encoded",
    Hex: "hex",
    HtmlEntities: "html"
};

export function run(input) {
    const text = input.Input || "";
    const format = input.Format || "Base64";
    const decoding = input.Direction === "Decode";

    if (text.length === 0) {
        return fail("Enter something to convert.");
    }

    const transform = decoding ? DECODERS[format] : ENCODERS[format];

    if (!transform) {
        return fail(`Unknown format: ${format}.`);
    }

    let output;

    try {
        output = transform(text);
    } catch (error) {
        return fail(decoding
            ? `That does not decode as ${format}. ${error.message || ""}`.trim()
            : `Could not encode that as ${format}. ${error.message || ""}`.trim());
    }

    const inputBytes = toBytes(text).length;
    const outputBytes = toBytes(output).length;

    return ok(
        status(
            "Ok",
            `${decoding ? "Decoded" : "Encoded"} ${format}`,
            `${text.length.toLocaleString()} characters in, ${output.length.toLocaleString()} out.`),
        code(output, decoding ? "text" : LANGUAGE[format], "Output"),
        keyValues("Sizes", [
            ["Input characters", text.length.toLocaleString(), { monospace: true }],
            ["Input UTF-8 bytes", inputBytes.toLocaleString(), { monospace: true }],
            ["Output characters", output.length.toLocaleString(), { monospace: true }],
            ["Output UTF-8 bytes", outputBytes.toLocaleString(), { monospace: true }],
            ["Size change", `${inputBytes === 0 ? 0 : Math.round(((outputBytes - inputBytes) / inputBytes) * 100)}%`, { monospace: true }]
        ])
    );
}
