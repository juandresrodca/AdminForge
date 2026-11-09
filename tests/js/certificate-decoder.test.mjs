import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

import { run } from "../../src/AdminForge.Tools/wwwroot/tools/certificate-csr-decoder.js";

const cert = readFileSync(new URL("./fixtures/certificate-decoder.crt", import.meta.url), "utf8");
const csr = readFileSync(new URL("./fixtures/certificate-decoder.csr", import.meta.url), "utf8");

const findBlock = (result, title) => result.blocks.find(block => block.title === title);

test("decodes PEM certificates", async () => {
    const result = await run({ Value: cert });
    assert.equal(result.ok, true);
    assert.ok(findBlock(result, "Certificate 1 — Subject"));
    assert.ok(findBlock(result, "Issuer"));
});

test("extracts certificate SANs", async () => {
    const result = await run({ Value: cert });
    const sans = findBlock(result, "Subject Alternative Names");
    assert.ok(sans.items.includes("DNS: example.com"));
    assert.ok(sans.items.includes("DNS: www.example.com"));
});

test("reports RSA key size", async () => {
    const result = await run({ Value: cert });
    const key = findBlock(result, "Public key");
    assert.ok(key.rows.some(row => row.label === "Key size" && row.value === "2048 bits"));
});

test("decodes and verifies a CSR", async () => {
    const result = await run({ Value: csr });
    assert.equal(result.ok, true);
    assert.ok(result.blocks.some(block =>
        block.message === "CSR signature verified" && block.status === "Ok"
    ));
});

test("extracts CSR SANs", async () => {
    const result = await run({ Value: csr });
    const sans = findBlock(result, "Subject Alternative Names");
    assert.ok(sans.items.includes("DNS: example.com"));
    assert.ok(sans.items.includes("DNS: www.example.com"));
});

test("accepts raw Base64 DER", async () => {
    const base64 = cert.replace(/-----BEGIN CERTIFICATE-----/g, "")
        .replace(/-----END CERTIFICATE-----/g, "").replace(/\s+/g, "");
    const result = await run({ Value: base64 });
    assert.equal(result.ok, true);
    assert.ok(findBlock(result, "Certificate 1 — Subject"));
});

test("decodes concatenated certificates", async () => {
    const result = await run({ Value: cert + "\n" + cert });
    assert.equal(result.ok, true);
    assert.ok(findBlock(result, "Certificate 1 — Subject"));
    assert.ok(findBlock(result, "Certificate 2 — Subject"));
});

test("rejects invalid input", async () => {
    const result = await run({ Value: "not-a-certificate" });
    assert.equal(result.ok, false);
});

test("rejects a CSR with a modified signature", async () => {
    const der = Buffer.from(
        csr.replace(/-----BEGIN CERTIFICATE REQUEST-----|-----END CERTIFICATE REQUEST-----|\s+/g, ""),
        "base64"
    );

    // Modify the final byte of the signature, preserving ASN.1 structure.
    der[der.length - 1] ^= 0x01;

    const result = await run({ Value: der.toString("base64") });

    assert.equal(result.ok, true);
    assert.ok(result.blocks.some(block =>
        block.message === "CSR signature invalid" &&
        block.status === "Danger"
    ));
});
