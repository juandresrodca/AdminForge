/*
 * Tests for the client-side tool modules.
 *
 * Run with:  node --test tests/js
 *
 * These run in Node rather than a browser, which works because the modules use only
 * platform APIs Node also implements: TextEncoder, crypto.subtle, btoa and atob. A
 * tool that reaches for a DOM API will fail here, which is the intended signal — tool
 * modules compute, they do not render.
 */
import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";

import { run as hash } from "../../src/AdminForge.Tools/wwwroot/tools/hash-generator.js";
import { run as encode } from "../../src/AdminForge.Tools/wwwroot/tools/text-encoder.js";
import { run as jwt } from "../../src/AdminForge.Tools/wwwroot/tools/jwt-decoder.js";
import { run as password } from "../../src/AdminForge.Tools/wwwroot/tools/password-generator.js";

const codeOf = (result, index = 0) => result.blocks.filter((b) => b.type === "code")[index].content;
const tableOf = (result, title) => result.blocks.find((b) => b.type === "table" && b.title.startsWith(title));

test("hash-generator matches Node's crypto for every algorithm", async () => {
    // Lengths chosen to straddle MD5's 64-byte block and its 56-byte padding boundary.
    const inputs = [
        "a",
        "abc",
        "message digest",
        "The quick brown fox jumps over the lazy dog",
        "café ☕ 日本語 🔐",
        "x".repeat(55),
        "x".repeat(56),
        "x".repeat(57),
        "x".repeat(63),
        "x".repeat(64),
        "x".repeat(65),
        "x".repeat(119),
        "x".repeat(120),
    ];

    for (const input of inputs) {
        const result = await hash({ Text: input, Expected: "", Uppercase: false });
        assert.ok(result.ok, `hashing ${input.length} chars failed`);

        const rows = tableOf(result, "Digests").rows;
        const digests = Object.fromEntries(rows.map((row) => [row[0].value, row[1].value]));

        for (const [label, nodeName] of [
            ["MD5", "md5"],
            ["SHA-1", "sha1"],
            ["SHA-256", "sha256"],
            ["SHA-384", "sha384"],
            ["SHA-512", "sha512"],
        ]) {
            assert.equal(
                digests[label],
                createHash(nodeName).update(input, "utf8").digest("hex"),
                `${label} of a ${input.length}-character input`);
        }
    }
});

test("hash-generator confirms a matching digest and rejects a wrong one", async () => {
    const expected = createHash("sha256").update("hello", "utf8").digest("hex");

    const match = await hash({ Text: "hello", Expected: expected, Uppercase: false });
    assert.equal(match.blocks[0].status, "Ok");
    assert.match(match.blocks[0].message, /SHA-256/);

    const mismatch = await hash({ Text: "hello world", Expected: expected, Uppercase: false });
    assert.equal(mismatch.blocks[0].status, "Danger");
});

test("hash-generator rejects empty input", async () => {
    assert.equal((await hash({ Text: "" })).ok, false);
});

test("text-encoder round-trips every format, including non-ASCII", () => {
    const samples = [
        "hello world",
        "café ☕ 日本語 🔐",
        "a",
        "line1\nline2\ttab",
        "<script>alert(\"x\")&'</script>",
        "x".repeat(500),
    ];

    for (const sample of samples) {
        for (const format of ["Base64", "Base64Url", "Url", "Hex", "HtmlEntities"]) {
            const encoded = encode({ Input: sample, Format: format, Direction: "Encode" });
            assert.ok(encoded.ok, `encoding ${format} failed`);

            const decoded = encode({ Input: codeOf(encoded), Format: format, Direction: "Decode" });
            assert.ok(decoded.ok, `decoding ${format} failed: ${decoded.error}`);
            assert.equal(codeOf(decoded), sample, `${format} did not round-trip`);
        }
    }
});

test("text-encoder produces the canonical encodings", () => {
    assert.equal(codeOf(encode({ Input: "hello", Format: "Base64", Direction: "Encode" })), "aGVsbG8=");
    assert.equal(codeOf(encode({ Input: "abc", Format: "Hex", Direction: "Encode" })), "616263");
    assert.equal(
        codeOf(encode({ Input: "a+b/c", Format: "Base64Url", Direction: "Encode" })).includes("="),
        false,
        "base64url output must not be padded");
});

test("text-encoder reports bad input rather than throwing", () => {
    assert.equal(encode({ Input: "zzz", Format: "Hex", Direction: "Decode" }).ok, false);
    assert.equal(encode({ Input: "abc", Format: "Hex", Direction: "Decode" }).ok, false, "odd digit count");
    assert.equal(encode({ Input: "", Format: "Base64", Direction: "Encode" }).ok, false);
});

const segment = (value) => Buffer.from(JSON.stringify(value)).toString("base64url");
const token = (payload, header = { alg: "RS256", typ: "JWT" }) =>
    `${segment(header)}.${segment(payload)}.signature`;

test("jwt-decoder reads claims and reports validity", () => {
    const soon = Math.floor(Date.now() / 1000) + 3600;
    const result = jwt({ Token: token({ sub: "1234", exp: soon, tid: "contoso", widget: 42 }) });

    assert.ok(result.ok);
    assert.equal(result.blocks[0].status, "Ok");
    assert.ok(tableOf(result, "Other claims"), "unrecognised claims belong in their own table");
});

test("jwt-decoder flags an expired token", () => {
    const result = jwt({ Token: token({ sub: "1", exp: Math.floor(Date.now() / 1000) - 3600 }) });
    assert.equal(result.blocks[0].status, "Danger");
});

test("jwt-decoder flags alg=none", () => {
    const result = jwt({ Token: token({ sub: "1" }, { alg: "none", typ: "JWT" }) });
    assert.ok(result.blocks.some((b) => b.status === "Danger" && /none/.test(b.message ?? "")));
});

test("jwt-decoder tolerates a Bearer prefix and rejects malformed tokens", () => {
    const valid = token({ sub: "1", exp: Math.floor(Date.now() / 1000) + 60 });
    assert.equal(jwt({ Token: `Bearer ${valid}` }).ok, true);
    assert.equal(jwt({ Token: "not.a.jwt.at.all" }).ok, false);
    assert.equal(jwt({ Token: "   " }).ok, false);
});

test("password-generator honours length, count and character set", () => {
    const result = password({
        Style: "Password",
        Count: 5,
        Length: 20,
        Uppercase: true,
        Lowercase: true,
        Digits: true,
        Symbols: true,
        ExcludeAmbiguous: false,
    });

    const secrets = codeOf(result).split("\n");
    assert.equal(secrets.length, 5);
    assert.ok(secrets.every((s) => s.length === 20));
    assert.equal(new Set(secrets).size, 5, "generated secrets must not repeat");
});

test("password-generator excludes the character sets that are switched off", () => {
    const result = password({
        Style: "Password",
        Count: 20,
        Length: 24,
        Uppercase: false,
        Lowercase: true,
        Digits: false,
        Symbols: false,
        ExcludeAmbiguous: true,
    });

    // Lowercase only, minus the ambiguous 'l'.
    for (const secret of codeOf(result).split("\n")) {
        assert.match(secret, /^[a-km-z]+$/);
    }
});

test("password-generator refuses to run with no character set", () => {
    const result = password({
        Style: "Password",
        Count: 1,
        Length: 10,
        Uppercase: false,
        Lowercase: false,
        Digits: false,
        Symbols: false,
    });

    assert.equal(result.ok, false);
});

test("passphrase entropy is exactly eight bits per word", () => {
    const result = password({ Style: "Passphrase", Count: 4, Words: 8 });
    const secrets = codeOf(result).split("\n");

    assert.ok(secrets.every((s) => s.split("-").length === 8));

    const entropy = result.blocks
        .find((b) => b.type === "keyValue")
        .rows.find((r) => r.label === "Entropy").value;

    assert.equal(entropy, "64.0 bits", "256 words means 8 bits each");
});
