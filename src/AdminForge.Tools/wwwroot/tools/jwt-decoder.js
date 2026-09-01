import { ok, fail, status, keyValues, code, table } from "./blocks.js";

/**
 * Standard registered claims, plus the Microsoft identity platform ones an admin
 * actually needs to read when debugging Entra ID sign-ins.
 */
const CLAIM_NAMES = {
    iss: "Issuer",
    sub: "Subject",
    aud: "Audience",
    exp: "Expires at",
    nbf: "Not valid before",
    iat: "Issued at",
    jti: "Token ID",
    azp: "Authorised party",
    scp: "Scopes",
    roles: "Roles",
    tid: "Tenant ID",
    oid: "Object ID",
    upn: "User principal name",
    preferred_username: "Preferred username",
    name: "Name",
    email: "Email",
    appid: "Application ID",
    idtyp: "Identity type",
    amr: "Authentication methods",
    acr: "Authentication context",
    auth_time: "Authenticated at",
    ver: "Token version"
};

const TIME_CLAIMS = new Set(["exp", "nbf", "iat", "auth_time"]);

/** Decode base64url, tolerating the missing padding JWTs always omit. */
function decodeSegment(segment) {
    const padded = segment.replace(/-/g, "+").replace(/_/g, "/");
    const binary = atob(padded + "=".repeat((4 - (padded.length % 4)) % 4));
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    return new TextDecoder("utf-8").decode(bytes);
}

function formatInstant(seconds) {
    if (typeof seconds !== "number" || !Number.isFinite(seconds)) {
        return String(seconds);
    }

    const when = new Date(seconds * 1000);
    const delta = Math.round((when.getTime() - Date.now()) / 1000);
    const magnitude = Math.abs(delta);

    let relative;
    if (magnitude < 60) { relative = `${magnitude}s`; }
    else if (magnitude < 3600) { relative = `${Math.round(magnitude / 60)}m`; }
    else if (magnitude < 86400) { relative = `${Math.round(magnitude / 3600)}h`; }
    else { relative = `${Math.round(magnitude / 86400)}d`; }

    return `${when.toISOString().replace(".000", "")} (${delta < 0 ? relative + " ago" : "in " + relative})`;
}

function formatValue(key, value) {
    if (TIME_CLAIMS.has(key)) {
        return formatInstant(value);
    }

    if (Array.isArray(value)) {
        return value.join(", ");
    }

    return typeof value === "object" && value !== null ? JSON.stringify(value) : String(value);
}

export function run(input) {
    const raw = (input.Token || "").trim().replace(/^Bearer\s+/i, "");

    if (!raw) {
        return fail("Paste a token to decode.");
    }

    const parts = raw.split(".");

    if (parts.length !== 3) {
        return fail(
            `A JWT has three dot-separated parts. This has ${parts.length}. ` +
            "If you copied it from a header, make sure the whole value came across.");
    }

    let header;
    let payload;

    try {
        header = JSON.parse(decodeSegment(parts[0]));
    } catch {
        return fail("The header is not valid base64url-encoded JSON. This may not be a JWT.");
    }

    try {
        payload = JSON.parse(decodeSegment(parts[1]));
    } catch {
        return fail("The payload is not valid base64url-encoded JSON.");
    }

    const now = Math.floor(Date.now() / 1000);
    const blocks = [];

    // The headline is always the question someone opened this tool to answer.
    if (typeof payload.exp === "number") {
        const remaining = payload.exp - now;
        blocks.push(remaining <= 0
            ? status("Danger", "This token has expired", `It expired ${formatInstant(payload.exp)}.`)
            : status("Ok", "This token is still valid", `It expires ${formatInstant(payload.exp)}.`));
    } else {
        blocks.push(status("Warning", "No expiry claim", "This token carries no exp claim, so it never expires on its own."));
    }

    if (typeof payload.nbf === "number" && payload.nbf > now) {
        blocks.push(status("Warning", "Not valid yet", `This token becomes valid ${formatInstant(payload.nbf)}.`));
    }

    if (header.alg === "none") {
        blocks.push(status(
            "Danger",
            "The algorithm is set to none",
            "An unsigned token proves nothing. Any service that accepts alg=none can be trivially forged."));
    }

    blocks.push(keyValues("Header", Object.entries(header).map(([key, value]) => [
        key === "alg" ? "Algorithm (alg)" : key === "typ" ? "Type (typ)" : key === "kid" ? "Key ID (kid)" : key,
        formatValue(key, value),
        { monospace: true }
    ])));

    const known = [];
    const custom = [];

    for (const [key, value] of Object.entries(payload)) {
        const row = [
            CLAIM_NAMES[key] ? `${CLAIM_NAMES[key]} (${key})` : key,
            formatValue(key, value),
            { monospace: true }
        ];
        (CLAIM_NAMES[key] ? known : custom).push(row);
    }

    if (known.length > 0) {
        blocks.push(keyValues("Standard claims", known));
    }

    if (custom.length > 0) {
        blocks.push(table(
            `Other claims (${custom.length})`,
            ["Claim", "Value"],
            custom.map(([name, value]) => [name, { value, monospace: true }])));
    }

    blocks.push(code(JSON.stringify(payload, null, 2), "json", "Payload"));
    blocks.push(code(parts[2], "base64url", "Signature (not verified)"));

    return ok(...blocks);
}
