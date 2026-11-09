import { run as decode } from "./certificate-csr-decoder.bundle.js";

export async function run(input) {
    return decode(input);
}
