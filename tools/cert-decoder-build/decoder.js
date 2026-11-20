
import * as pkijs from "pkijs";
import * as asn1js from "asn1js";
import { ok, fail, status, keyValues, list } from "./blocks.js";

const OIDS = {
    "2.5.4.3": "Common Name",
    "2.5.4.6": "Country",
    "2.5.4.7": "Locality",
    "2.5.4.8": "State / Province",
    "2.5.4.10": "Organization",
    "2.5.4.11": "Organizational Unit",
    "1.2.840.113549.1.9.1": "Email",
    "1.2.840.113549.1.1.1": "RSA",
    "1.2.840.10045.2.1": "EC",
    "1.3.101.112": "Ed25519",
    "1.2.840.113549.1.1.11": "SHA256 with RSA",
    "1.2.840.113549.1.1.12": "SHA384 with RSA",
    "1.2.840.113549.1.1.13": "SHA512 with RSA",
    "1.2.840.10045.4.3.2": "ECDSA with SHA256",
    "1.2.840.10045.4.3.3": "ECDSA with SHA384",
    "1.2.840.10045.4.3.4": "ECDSA with SHA512"
};

function name(oid) {
    return OIDS[oid] || oid;
}

function subjectRows(subject) {
    return subject.typesAndValues.map(item => [
        name(item.type),
        String(item.value.valueBlock.value ?? item.value.valueBlock.valueHexView ?? ""),
        { monospace: true }
    ]);
}

function decodeBase64(value) {
    const cleaned = value.replace(/\s+/g, "");

    if (!/^[A-Za-z0-9+/]*={0,2}$/.test(cleaned) ||
        cleaned.length === 0 || cleaned.length % 4 !== 0) {
        throw new Error("Invalid Base64 encoding.");
    }

    return Uint8Array.from(atob(cleaned), c => c.charCodeAt(0));
}

function parseInput(raw) {
    const matches = [...raw.matchAll(
        /-----BEGIN (CERTIFICATE|CERTIFICATE REQUEST|NEW CERTIFICATE REQUEST)-----([\s\S]*?)-----END \1-----/g
    )];

    if (matches.length) {
        const remainder = raw.replace(
            /-----BEGIN (CERTIFICATE|CERTIFICATE REQUEST|NEW CERTIFICATE REQUEST)-----[\s\S]*?-----END \1-----/g,
            ""
        ).trim();

        if (remainder) {
            throw new Error("Unexpected content outside PEM blocks.");
        }

        return matches.map(match => ({
            hint: match[1] === "CERTIFICATE" ? "cert" : "csr",
            bytes: decodeBase64(match[2])
        }));
    }

    if (raw.includes("-----BEGIN") || raw.includes("-----END")) {
        throw new Error("Incomplete or unsupported PEM headers.");
    }

    return [{ hint: null, bytes: decodeBase64(raw) }];
}

function parseDer(bytes) {
    const result = asn1js.fromBER(bytes.buffer);

    if (result.offset === -1 || result.offset !== bytes.length) {
        throw new Error("Invalid or incomplete ASN.1 DER data.");
    }

    return result.result;
}

function identify(asn1, hint) {
    if (hint === "cert") {
        return { type: "cert", object: new pkijs.Certificate({ schema: asn1 }) };
    }

    if (hint === "csr") {
        return { type: "csr", object: new pkijs.CertificationRequest({ schema: asn1 }) };
    }

    // Try both parsers. For headerless input, inspect the
    // resulting object and require a plausible structure.
    try {
        const cert = new pkijs.Certificate({ schema: asn1 });
        if (cert.issuer?.typesAndValues && cert.notAfter?.value &&
            cert.subjectPublicKeyInfo?.subjectPublicKey) {
            return { type: "cert", object: cert };
        }
    } catch {}

    try {
        const csr = new pkijs.CertificationRequest({ schema: asn1 });
        if (csr.subject?.typesAndValues &&
            csr.subjectPublicKeyInfo?.subjectPublicKey) {
            return { type: "csr", object: csr };
        }
    } catch {}

    throw new Error("The data is not a recognized X.509 certificate or PKCS#10 CSR.");
}

function getExtensions(item, type) {
    if (type === "cert") return item.extensions || [];

    const extensions = [];
    for (const attr of item.attributes || []) {
        if (attr.type !== "1.2.840.113549.1.9.14") continue;

        for (const value of attr.values || []) {
            try {
                const ext = new pkijs.Extensions({ schema: value });
                extensions.push(...ext.extensions);
            } catch {}
        }
    }
    return extensions;
}

function getSans(extensions) {
    const san = extensions.find(e => e.extnID === "2.5.29.17");
    if (!san) return [];

    let parsed = san.parsedValue;

    if (!parsed) {
        try {
            const bytes = new Uint8Array(san.extnValue.valueBlock.valueHexView);
            parsed = new pkijs.GeneralNames({ schema: parseDer(bytes) });
        } catch {
            return ["SAN extension could not be decoded"];
        }
    }

    return (parsed.altNames || []).map(entry => {
        const kinds = {
            1: "Email",
            2: "DNS",
            6: "URI",
            7: "IP"
        };

        const kind = kinds[entry.type] || `Type ${entry.type}`;
        let value = entry.value;

        if (entry.type === 7 && value?.valueBlock?.valueHexView) {
            const bytes = new Uint8Array(value.valueBlock.valueHexView);

            if (bytes.length === 4) {
                value = [...bytes].join(".");
            } else if (bytes.length === 16) {
                value = Array.from({ length: 8 }, (_, i) =>
                    ((bytes[i * 2] << 8) | bytes[i * 2 + 1]).toString(16)
                ).join(":");
            }
        }

        return `${kind}: ${String(value)}`;
    });
}

function hex(bytes) {
    return [...bytes].map(b => b.toString(16).padStart(2, "0")).join(":").toUpperCase();
}

function publicKeyRows(info) {
    const oid = info.algorithm.algorithmId;
    const rows = [["Algorithm", name(oid)]];

    if (oid === "1.2.840.113549.1.1.1") {
        try {
            const rsa = new pkijs.RSAPublicKey({
                schema: parseDer(
                    new Uint8Array(info.subjectPublicKey.valueBlock.valueHexView)
                )
            });

            const modulus = new Uint8Array(rsa.modulus.valueBlock.valueHexView);

            let first = 0;
            while (first < modulus.length - 1 && modulus[first] === 0) first++;

            const significant = modulus[first];
            const bits = (modulus.length - first - 1) * 8 +
                (8 - Math.clz32(significant) + 24);

            rows.push(["Key size", `${bits} bits`]);
        } catch {
            rows.push(["Key size", "Unable to determine"]);
        }
    } else if (oid === "1.2.840.10045.2.1") {
        const curves = {
            "1.2.840.10045.3.1.7": "P-256",
            "1.3.132.0.34": "P-384",
            "1.3.132.0.35": "P-521"
        };

        const curve = info.algorithm.algorithmParams?.valueBlock?.toString?.();
        rows.push(["Curve", curves[curve] || "EC (curve details unavailable)"]);
    }

    return rows;
}

async function fingerprint(bytes) {
    return hex(new Uint8Array(
        await crypto.subtle.digest("SHA-256", bytes)
    ));
}

async function decodeOne(entry, index) {
    const asn1 = parseDer(entry.bytes);
    const { type, object: item } = identify(asn1, entry.hint);
    const cert = type === "cert";
    const title = `${cert ? "Certificate" : "CSR"} ${index + 1}`;
    const blocks = [];

    blocks.push(status("Info", title,
        cert ? "X.509 certificate decoded" : "PKCS#10 certificate signing request decoded"));

    blocks.push(keyValues(`${title} — Subject`, subjectRows(item.subject)));

    if (cert) {
        const now = Date.now();
        const start = item.notBefore.value;
        const end = item.notAfter.value;

        blocks.push(status(
            now > end.getTime() ? "Danger" :
            now < start.getTime() ? "Warning" : "Ok",
            now > end.getTime() ? "Certificate expired" :
            now < start.getTime() ? "Certificate not yet valid" :
            "Certificate dates are current",
            `Valid from ${start.toISOString()} to ${end.toISOString()}`
        ));

        blocks.push(keyValues("Issuer", subjectRows(item.issuer)));

        blocks.push(keyValues("Certificate details", [
            ["Serial number", hex(new Uint8Array(item.serialNumber.valueBlock.valueHexView))],
            ["Signature algorithm", name(item.signatureAlgorithm.algorithmId)],
            ["SHA-256 fingerprint", await fingerprint(entry.bytes)]
        ]));
    } else {
        blocks.push(keyValues("CSR details", [
            ["Signature algorithm", name(item.signatureAlgorithm.algorithmId)],
            ["SHA-256 fingerprint of CSR", await fingerprint(entry.bytes)]
        ]));

        try {
            const valid = await item.verify();
            blocks.push(status(
                valid ? "Ok" : "Danger",
                valid ? "CSR signature verified" : "CSR signature invalid",
                "Verification uses the public key included in the CSR."
            ));
        } catch (error) {
            blocks.push(status("Warning", "CSR signature could not be verified",
                error.message));
        }
    }

    blocks.push(keyValues("Public key", publicKeyRows(item.subjectPublicKeyInfo)));

    const extensions = getExtensions(item, type);
    const sans = getSans(extensions);

    if (sans.length) {
        blocks.push(list("Subject Alternative Names", sans));
    }

    if (extensions.length) {
        blocks.push(keyValues("Extensions", extensions.map(ext => [
            ext.extnID,
            ext.critical ? "Critical" : "Non-critical"
        ])));
    }

    return blocks;
}

export async function run(input) {
    const raw = (input.Value || "").trim();

    if (!raw) return fail("Paste a certificate or CSR first.");

    try {
        const entries = parseInput(raw);
        const blocks = [];

        for (let i = 0; i < entries.length; i++) {
            blocks.push(...await decodeOne(entries[i], i));
        }

        blocks.push(status("Info", "Decoding complete",
            `${entries.length} item(s) decoded locally. Certificate trust and chain validation were not performed.`));

        return ok(...blocks);
    } catch (error) {
        return fail(`Unable to decode input: ${error.message}`);
    }
}
