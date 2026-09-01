import { ok, fail, status, keyValues, code, table } from "./blocks.js";

const UPPER = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
const LOWER = "abcdefghijklmnopqrstuvwxyz";
const DIGITS = "0123456789";
const SYMBOLS = "!#$%&()*+,-./:;<=>?@[]^_{|}~";
const AMBIGUOUS = "Il1O0";

/**
 * Exactly 256 words, so one random byte selects one word with no modulo bias and each
 * word contributes exactly 8 bits. Short, common and unambiguous to type or dictate.
 *
 * A larger list means more entropy per word — swapping this for the EFF long list
 * (7776 words, 12.9 bits each) is an open good-first-issue.
 */
const WORDS = [
    "acid", "acorn", "actor", "agent", "album", "alert", "alien", "alpha",
    "amber", "angle", "ankle", "apple", "apron", "arena", "armor", "arrow",
    "aside", "atlas", "audio", "aunt", "avoid", "awake", "award", "badge",
    "bagel", "baker", "balmy", "banjo", "barge", "basil", "basin", "batch",
    "beach", "beard", "beast", "bench", "berry", "bison", "black", "blade",
    "blank", "blaze", "blend", "blimp", "block", "blond", "bloom", "blunt",
    "board", "bonus", "boost", "booth", "brace", "braid", "brain", "brand",
    "brass", "brave", "bread", "brick", "brief", "bring", "brisk", "broad",
    "brook", "broom", "brown", "brush", "buddy", "buggy", "build", "bunch",
    "bunny", "cabin", "cable", "cache", "camel", "canal", "candy", "canoe",
    "canyon", "cargo", "carol", "carve", "catch", "cedar", "chain", "chair",
    "chalk", "charm", "chart", "chase", "cheek", "chess", "chest", "chief",
    "chili", "chime", "chirp", "chord", "cider", "cinema", "civic", "claim",
    "clamp", "clash", "clasp", "clean", "clear", "clerk", "cliff", "climb",
    "cloak", "clock", "close", "cloth", "cloud", "clove", "clown", "coach",
    "coast", "cobra", "cocoa", "comet", "coral", "couch", "cough", "count",
    "court", "cover", "crane", "crank", "crate", "crawl", "cream", "creek",
    "crest", "crisp", "cross", "crowd", "crown", "crumb", "crust", "curve",
    "cycle", "daisy", "dance", "dawn", "debut", "decoy", "delta", "dense",
    "depot", "diner", "ditch", "diver", "dizzy", "dodge", "donor", "donut",
    "draft", "drain", "drama", "dream", "dress", "drift", "drill", "drink",
    "drive", "drone", "drums", "dusty", "eagle", "early", "earth", "easel",
    "eight", "elbow", "elder", "elite", "ember", "empty", "entry", "equal",
    "essay", "event", "exact", "extra", "fable", "fancy", "fault", "feast",
    "fence", "ferry", "fiber", "field", "fifth", "final", "finch", "flame",
    "flash", "fleet", "flint", "float", "flock", "flour", "fluid", "flute",
    "focus", "forge", "forum", "found", "frame", "fresh", "front", "frost",
    "fruit", "fudge", "gauge", "ghost", "giant", "given", "glass", "glide",
    "globe", "glove", "grace", "grade", "grain", "grand", "grant", "grape",
    "graph", "grass", "green", "grill", "grind", "group", "grove", "guard",
    "guess", "guest", "guide", "habit", "handy", "happy", "harbor", "hasty",
    "hatch", "haven", "hazel", "heart", "heavy", "hedge", "hello", "hobby"
];

/** A cryptographically random integer in [0, max), rejection-sampled to remove bias. */
function randomBelow(max) {
    const limit = Math.floor(0xffffffff / max) * max;
    const buffer = new Uint32Array(1);

    for (;;) {
        crypto.getRandomValues(buffer);
        if (buffer[0] < limit) {
            return buffer[0] % max;
        }
    }
}

function buildAlphabet(input) {
    let alphabet = "";
    if (input.Uppercase) { alphabet += UPPER; }
    if (input.Lowercase) { alphabet += LOWER; }
    if (input.Digits) { alphabet += DIGITS; }
    if (input.Symbols) { alphabet += SYMBOLS; }

    if (input.ExcludeAmbiguous) {
        alphabet = [...alphabet].filter((character) => !AMBIGUOUS.includes(character)).join("");
    }

    return alphabet;
}

function makePassword(alphabet, length) {
    let out = "";
    for (let i = 0; i < length; i++) {
        out += alphabet[randomBelow(alphabet.length)];
    }
    return out;
}

function makePassphrase(wordCount) {
    const bytes = new Uint8Array(wordCount);
    crypto.getRandomValues(bytes);
    // The list is exactly 256 long, so a byte maps to a word uniformly.
    return Array.from(bytes, (byte) => WORDS[byte]).join("-");
}

/**
 * How long an exhaustive search takes at a stated rate. The rate is an assumption
 * printed alongside the answer, not a measurement — a real attacker's speed depends
 * entirely on the hash the password is stored under.
 */
function describeCrackTime(entropyBits) {
    const guessesPerSecond = 1e12;
    const seconds = Math.pow(2, entropyBits - 1) / guessesPerSecond;

    const units = [
        [1, "second"],
        [60, "minute"],
        [3600, "hour"],
        [86400, "day"],
        [31557600, "year"],
        [31557600e3, "thousand years"],
        [31557600e6, "million years"],
        [31557600e9, "billion years"]
    ];

    if (seconds < 1) {
        return "under a second";
    }

    let chosen = units[0];
    for (const unit of units) {
        if (seconds >= unit[0]) { chosen = unit; }
    }

    const value = seconds / chosen[0];
    const rendered = value >= 100 ? value.toExponential(1) : value.toFixed(1);
    return `${rendered} ${chosen[1]}${value >= 2 && !chosen[1].includes(" ") ? "s" : ""}`;
}

function rateEntropy(bits) {
    if (bits >= 100) { return ["Ok", "Very strong"]; }
    if (bits >= 75) { return ["Ok", "Strong"]; }
    if (bits >= 60) { return ["Warning", "Adequate for most accounts"]; }
    if (bits >= 45) { return ["Warning", "Weak against an offline attack"]; }
    return ["Danger", "Too weak to use"];
}

export function run(input) {
    const count = Math.min(Math.max(parseInt(input.Count, 10) || 5, 1), 50);
    const isPassphrase = input.Style === "Passphrase";

    let secrets;
    let entropy;
    let alphabetNote;

    if (isPassphrase) {
        const words = Math.min(Math.max(parseInt(input.Words, 10) || 6, 3), 12);
        secrets = Array.from({ length: count }, () => makePassphrase(words));
        entropy = words * Math.log2(WORDS.length);
        alphabetNote = `${words} words from a ${WORDS.length}-word list`;
    } else {
        const alphabet = buildAlphabet(input);

        if (alphabet.length === 0) {
            return fail("Pick at least one character set — with all four switched off there is nothing to choose from.");
        }

        const length = Math.min(Math.max(parseInt(input.Length, 10) || 20, 4), 256);
        secrets = Array.from({ length: count }, () => makePassword(alphabet, length));
        entropy = length * Math.log2(alphabet.length);
        alphabetNote = `${length} characters from a ${alphabet.length}-character alphabet`;
    }

    const [level, verdict] = rateEntropy(entropy);

    return ok(
        status(level, `${entropy.toFixed(1)} bits of entropy — ${verdict.toLowerCase()}`, alphabetNote),
        code(secrets.join("\n"), "text", `Generated (${secrets.length})`),
        keyValues("Strength", [
            ["Entropy", `${entropy.toFixed(1)} bits`, { monospace: true }],
            ["Possible combinations", `2^${entropy.toFixed(0)}`, { monospace: true }],
            ["Exhaustive search", describeCrackTime(entropy), { monospace: true }],
            ["Assumed attack rate", "10^12 guesses per second, offline", { monospace: true }],
            ["Randomness source", "crypto.getRandomValues", { monospace: true }]
        ]),
        table("Where these came from", ["Property", "Value"], [
            ["Generated", "Entirely in your browser"],
            ["Sent to the server", { value: "Nothing", status: "Ok" }],
            ["Stored anywhere", { value: "Nothing", status: "Ok" }]
        ])
    );
}
