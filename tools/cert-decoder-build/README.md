# Certificate / CSR Decoder

Browser-only X.509 certificate and PKCS#10 CSR decoder for AdminForge.

## Implementation

- `decoder.js` contains the JavaScript source.
- PKI.js and ASN1.js handle certificate and CSR parsing.
- esbuild generates the browser-compatible JavaScript bundle.
- The compiled module is committed so AdminForge does not require Node.js at runtime.

Certificate and CSR contents are processed locally in the browser.
Certificate trust and CA chain validation are not performed.

## Building

From the repository root, with Node.js and npm installed:

    cd tools/cert-decoder-build
    npm ci
    npx esbuild decoder.js --bundle --format=esm --platform=browser --external:./blocks.js --outfile=../../src/AdminForge.Tools/wwwroot/tools/certificate-csr-decoder.bundle.js

Alternatively, from the repository root using Docker:

    docker run --rm -v "$PWD/tools/cert-decoder-build:/app" -v "$PWD/src/AdminForge.Tools/wwwroot/tools:/output" -w /app node:22-alpine sh -c 'npm ci && npx esbuild decoder.js --bundle --format=esm --platform=browser --external:./blocks.js --outfile=/output/certificate-csr-decoder.bundle.js'

After building, remove trailing whitespace from the generated bundle before committing:

    sed -i 's/[[:blank:]]*$//' src/AdminForge.Tools/wwwroot/tools/certificate-csr-decoder.bundle.js

## Testing

From the repository root:

    node --experimental-default-type=module --test tests/js/certificate-decoder.test.mjs

Tests use generic example.com certificate and CSR fixtures.
No private keys are committed.
