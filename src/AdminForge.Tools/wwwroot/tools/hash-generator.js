import { ok, fail, status, keyValues, table } from "./blocks.js";

/*
 * SHA-1 through SHA-512 come from WebCrypto. MD5 does not — no browser exposes it,
 * deliberately, because it is broken. It is implemented here anyway because
 * checksums published a decade ago are still MD5, and verifying one is a real job.
 */

const ROTATIONS = [
    7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
    5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
    4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
    6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21
];

// K[i] = floor(2^32 * abs(sin(i + 1))), per RFC 1321.
const K = Array.from({ length: 64 }, (_, i) => Math.floor(Math.abs(Math.sin(i + 1)) * 4294967296));

function rotateLeft(value, shift) {
    return (value << shift) | (value >>> (32 - shift));
}

/** RFC 1321 MD5 over a byte array, returning lowercase hex. */
function md5(bytes) {
    const originalBitLength = bytes.length * 8;

    // Append 0x80, pad with zeros to 56 mod 64, then the little-endian bit length.
    const paddedLength = (((bytes.length + 8) >>> 6) + 1) << 6;
    const buffer = new Uint8Array(paddedLength);
    buffer.set(bytes);
    buffer[bytes.length] = 0x80;

    const view = new DataView(buffer.buffer);
    view.setUint32(paddedLength - 8, originalBitLength >>> 0, true);
    view.setUint32(paddedLength - 4, Math.floor(originalBitLength / 4294967296), true);

    let a0 = 0x67452301;
    let b0 = 0xefcdab89;
    let c0 = 0x98badcfe;
    let d0 = 0x10325476;

    const M = new Uint32Array(16);

    for (let offset = 0; offset < paddedLength; offset += 64) {
        for (let i = 0; i < 16; i++) {
            M[i] = view.getUint32(offset + i * 4, true);
        }

        let A = a0;
        let B = b0;
        let C = c0;
        let D = d0;

        for (let i = 0; i < 64; i++) {
            let F;
            let g;

            if (i < 16) {
                F = (B & C) | (~B & D);
                g = i;
            } else if (i < 32) {
                F = (D & B) | (~D & C);
                g = (5 * i + 1) % 16;
            } else if (i < 48) {
                F = B ^ C ^ D;
                g = (3 * i + 5) % 16;
            } else {
                F = C ^ (B | ~D);
                g = (7 * i) % 16;
            }

            F = (F + A + K[i] + M[g]) >>> 0;
            A = D;
            D = C;
            C = B;
            B = (B + rotateLeft(F, ROTATIONS[i])) >>> 0;
        }

        a0 = (a0 + A) >>> 0;
        b0 = (b0 + B) >>> 0;
        c0 = (c0 + C) >>> 0;
        d0 = (d0 + D) >>> 0;
    }

    const out = new Uint8Array(16);
    const outView = new DataView(out.buffer);
    outView.setUint32(0, a0, true);
    outView.setUint32(4, b0, true);
    outView.setUint32(8, c0, true);
    outView.setUint32(12, d0, true);

    return toHex(out);
}

function toHex(bytes) {
    return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

async function webCrypto(algorithm, bytes) {
    const digest = await crypto.subtle.digest(algorithm, bytes);
    return toHex(new Uint8Array(digest));
}

export async function run(input) {
    const text = input.Text || "";

    if (text.length === 0) {
        return fail("Enter some text to hash.");
    }

    const bytes = new TextEncoder().encode(text);
    const expected = (input.Expected || "").trim().toLowerCase().replace(/^(sha\d*|md5)[:=\s]+/i, "");

    const digests = [
        ["MD5", md5(bytes), "Broken. Collisions are trivial — use only to verify an old published checksum."],
        ["SHA-1", await webCrypto("SHA-1", bytes), "Broken for collisions. Still seen in Git and older certificates."],
        ["SHA-256", await webCrypto("SHA-256", bytes), "The current default. Use this unless something requires otherwise."],
        ["SHA-384", await webCrypto("SHA-384", bytes), "SHA-2 family, truncated SHA-512."],
        ["SHA-512", await webCrypto("SHA-512", bytes), "SHA-2 family, faster than SHA-256 on 64-bit hardware."]
    ];

    const matched = expected ? digests.find(([, digest]) => digest === expected) : null;
    const blocks = [];

    if (expected) {
        blocks.push(matched
            ? status("Ok", `The digest matches — ${matched[0]}`, "The input produces exactly the value you pasted.")
            : status(
                "Danger",
                "No algorithm produces that digest",
                "Either the input differs from the original, or the digest came from something else. " +
                "Check for a trailing newline — hashing a file is not the same as hashing its text."));
    } else {
        blocks.push(status(
            "Info",
            `${bytes.length.toLocaleString()} bytes hashed`,
            "Computed in your browser. Nothing was sent anywhere."));
    }

    blocks.push(table(
        "Digests",
        ["Algorithm", "Digest", "Notes"],
        digests.map(([name, digest, note]) => [
            { value: name, status: matched && matched[0] === name ? "Ok" : "Neutral" },
            { value: input.Uppercase ? digest.toUpperCase() : digest, monospace: true, status: matched && matched[0] === name ? "Ok" : "Neutral" },
            note
        ])));

    blocks.push(keyValues("Input", [
        ["Characters", text.length.toLocaleString(), { monospace: true }],
        ["UTF-8 bytes", bytes.length.toLocaleString(), { monospace: true }],
        ["Ends with a newline", text.endsWith("\n") ? "yes" : "no", { monospace: true }]
    ]));

    return ok(...blocks);
}
