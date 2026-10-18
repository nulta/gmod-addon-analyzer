// Proof-of-work client.
// Fetches a challenge, solves SHA-256(seed:addonId:nonce) leading-zero-bits in a Web Worker,
// then enables the submit button which POSTs to /analyze.
"use strict";

(function () {
    const btn = document.getElementById("analyzeBtn");
    const input = document.getElementById("addonId");
    const status = document.getElementById("powStatus");
    if (!btn || !input || !status) return;

    let pendingChallenge = null;
    let worker = null;
    let solving = false;

    function setStatus(msg, cls) {
        status.textContent = msg;
        status.className = "muted " + (cls || "");
    }

    function parseAddonId(raw) {
        if (!raw) return null;
        const trimmed = raw.trim();
        if (!trimmed) return null;
        const m = trimmed.match(/(?:[?&]id=|^)(\d{2,})/);
        if (m) return parseInt(m[1], 10);
        const n = parseInt(trimmed, 10);
        return Number.isFinite(n) && n > 0 ? n : null;
    }

    async function ensureChallenge() {
        const id = parseAddonId(input.value);
        if (id === null) {
            btn.disabled = true;
            setStatus("");
            return;
        }
        if (pendingChallenge && pendingChallenge.addonId === id) {
            btn.disabled = solving;
            return;
        }
        pendingChallenge = null;
        btn.disabled = true;
        setStatus("Requesting challenge…");
        try {
            const r = await fetch(`/pow/challenge?addonId=${id}`, { cache: "no-store" });
            if (!r.ok) { setStatus("Failed to get challenge.", "danger"); return; }
            pendingChallenge = await r.json();
            if (pendingChallenge.difficulty === 0) {
                setStatus("PoW disabled — submit directly.");
                btn.disabled = false;
                return;
            }
            setStatus(`Challenge ready (difficulty ${pendingChallenge.difficulty} bits). Click Analyze.`);
            btn.disabled = false;
        } catch (e) {
            setStatus("Network error.", "danger");
        }
    }

    input.addEventListener("input", () => { pendingChallenge = null; btn.disabled = true; setStatus(""); });
    input.addEventListener("blur", ensureChallenge);
    ensureChallenge();

    btn.addEventListener("click", () => {
        if (solving || !pendingChallenge) return;
        if (pendingChallenge.difficulty === 0) {
            submit(pendingChallenge, 0);
            return;
        }
        solving = true;
        btn.disabled = true;
        setStatus("Solving proof-of-work…");
        if (worker) worker.terminate();
        const blob = new Blob([workerSrc], { type: "text/javascript" });
        worker = new Worker(URL.createObjectURL(blob));
        worker.onmessage = (ev) => {
            // Progress messages ({progress: n}) arrive while solving; only a
            // message carrying a nonce is the final solution.
            if (ev.data && typeof ev.data.progress === "number") {
                setStatus(`Solving proof-of-work… ${ev.data.progress.toLocaleString()} attempts`);
                return;
            }
            const { nonce, attempts } = ev.data;
            solving = false;
            setStatus(`Solved in ${attempts} attempts. Submitting…`, "ok");
            submit(pendingChallenge, nonce);
            worker.terminate();
            worker = null;
        };
        worker.postMessage(pendingChallenge);
    });

    async function submit(challenge, nonce) {
        const body = JSON.stringify({ addonId: challenge.addonId, seed: challenge.seed, nonce: nonce });
        let r;
        try {
            r = await fetch("/analyze", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: body,
                redirect: "follow",
            });
        } catch (e) {
            setStatus("Network error: " + e.message, "danger");
            btn.disabled = false;
            return;
        }
        // With redirect:"follow", a 302 → 200 landing on /report/{id} is the final response.
        // The browser navigated there automatically only for top-level navigation; fetch does not navigate.
        // So we read the final URL from r.url (the resolved location after following redirects).
        if (r.ok || (r.status >= 300 && r.status < 400)) {
            const finalUrl = r.url || `/report/${challenge.addonId}`;
            setStatus("Redirecting…", "ok");
            window.location.href = finalUrl;
            return;
        }
        if (r.status === 429) { setStatus("Rate limited. Wait a minute and try again.", "danger"); btn.disabled = false; return; }
        if (r.status === 503) { setStatus("Server busy — analysis queue is full. Try again in a few minutes.", "danger"); btn.disabled = false; return; }
        if (r.status === 400) { setStatus("Proof rejected. Try again.", "danger"); pendingChallenge = null; ensureChallenge(); return; }
        setStatus("Submission failed (HTTP " + r.status + ").", "danger");
        btn.disabled = false;
    }
})();

const workerSrc = `
self.onmessage = function (e) {
    const { seed, addonId, difficulty } = e.data;
    const enc = new TextEncoder();
    let nonce = 0;
    const start = performance.now();
    const batch = 100000;
    while (true) {
        let found = -1;
        for (let i = 0; i < batch; i++) {
            const n = nonce + i;
            const data = enc.encode(seed + ":" + addonId + ":" + n);
            // crypto.subtle is async; use a sync pure-JS sha256 fallback instead.
            const h = syncSha256(data);
            if (leadingZeroBits(h) >= difficulty) { found = n; break; }
        }
        if (found >= 0) {
            self.postMessage({ nonce: found, attempts: found + 1 });
            return;
        }
        nonce += batch;
        if (nonce % 1000000 === 0) self.postMessage({ progress: nonce });
    }
};

function leadingZeroBits(bytes) {
    let bits = 0;
    for (let i = 0; i < bytes.length; i++) {
        const b = bytes[i];
        if (b === 0) { bits += 8; continue; }
        let z = 0;
        for (let mask = 0x80; mask > 0 && (b & mask) === 0; mask >>= 1) z++;
        bits += z;
        break;
    }
    return bits;
}

// Synchronous SHA-256 (needed because crypto.subtle is async and slow in a tight loop).
// Standard implementation. Returns Uint8Array(32).
function syncSha256(data) {
    const k = new Uint32Array([
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2
    ]);
    const H = new Uint32Array([
        0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19
    ]);
    const len = data.length;
    const bitLen = len * 8;
    const padLen = (((len + 9) + 63) >> 6) << 6;
    const buf = new Uint8Array(padLen);
    buf.set(data);
    buf[len] = 0x80;
    // 64-bit big-endian length at the end
    const dv = new DataView(buf.buffer);
    dv.setUint32(padLen - 4, bitLen >>> 0, false);
    dv.setUint32(padLen - 8, Math.floor(bitLen / 0x100000000), false);

    for (let off = 0; off < padLen; off += 64) {
        const W = new Uint32Array(64);
        for (let i = 0; i < 16; i++) W[i] = dv.getUint32(off + i * 4, false);
        for (let i = 16; i < 64; i++) {
            const s0 = rotr(W[i-15],7) ^ rotr(W[i-15],18) ^ (W[i-15] >>> 3);
            const s1 = rotr(W[i-2],17) ^ rotr(W[i-2],19) ^ (W[i-2] >>> 10);
            W[i] = (W[i-16] + s0 + W[i-7] + s1) >>> 0;
        }
        let a=H[0],b=H[1],c=H[2],d=H[3],e=H[4],f=H[5],g=H[6],h=H[7];
        for (let i = 0; i < 64; i++) {
            const S1 = rotr(e,6) ^ rotr(e,11) ^ rotr(e,25);
            const ch = (e & f) ^ (~e & g);
            const t1 = (h + S1 + ch + k[i] + W[i]) >>> 0;
            const S0 = rotr(a,2) ^ rotr(a,13) ^ rotr(a,22);
            const mj = (a & b) ^ (a & c) ^ (b & c);
            const t2 = (S0 + mj) >>> 0;
            h=g; g=f; f=e; e=(d+t1)>>>0; d=c; c=b; b=a; a=(t1+t2)>>>0;
        }
        H[0]=(H[0]+a)>>>0; H[1]=(H[1]+b)>>>0; H[2]=(H[2]+c)>>>0; H[3]=(H[3]+d)>>>0;
        H[4]=(H[4]+e)>>>0; H[5]=(H[5]+f)>>>0; H[6]=(H[6]+g)>>>0; H[7]=(H[7]+h)>>>0;
    }
    const out = new Uint8Array(32);
    const ov = new DataView(out.buffer);
    for (let i = 0; i < 8; i++) ov.setUint32(i*4, H[i], false);
    return out;
}

function rotr(x, n) { return ((x >>> n) | (x << (32 - n))) >>> 0; }
`;