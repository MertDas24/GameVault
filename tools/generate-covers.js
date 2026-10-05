// Game Vault hazır kapakları — bu proje için özgün olarak üretilmiş soyut SVG çizimler.
const fs = require('fs');
const path = require('path');
const out = process.argv[2];

function rng(seed) {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
}

const wrap = (defs, body) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 400" width="300" height="400"><defs>${defs}</defs>${body}</svg>\n`;
const lin = (id, a, b, x2 = 0, y2 = 1) =>
  `<linearGradient id="${id}" x1="0" y1="0" x2="${x2}" y2="${y2}"><stop offset="0" stop-color="${a}"/><stop offset="1" stop-color="${b}"/></linearGradient>`;

const covers = {
  nebula() {
    const r = rng(1); let stars = '';
    for (let i = 0; i < 70; i++) stars += `<circle cx="${(r() * 300).toFixed(1)}" cy="${(r() * 400).toFixed(1)}" r="${(r() * 1.6 + .3).toFixed(2)}" fill="#fff" opacity="${(r() * .7 + .2).toFixed(2)}"/>`;
    return wrap(lin('bg', '#0b0826', '#2a0f4f') + `<radialGradient id="n1" cx=".35" cy=".4" r=".55"><stop offset="0" stop-color="#b44cff" stop-opacity=".85"/><stop offset="1" stop-color="#b44cff" stop-opacity="0"/></radialGradient><radialGradient id="n2" cx=".7" cy=".65" r=".5"><stop offset="0" stop-color="#3fb4ff" stop-opacity=".75"/><stop offset="1" stop-color="#3fb4ff" stop-opacity="0"/></radialGradient>`,
      `<rect width="300" height="400" fill="url(#bg)"/><rect width="300" height="400" fill="url(#n1)"/><rect width="300" height="400" fill="url(#n2)"/>${stars}<circle cx="220" cy="90" r="26" fill="#ffd9a8"/><circle cx="212" cy="84" r="26" fill="#1b0f3d" opacity=".55"/>`);
  },
  'neon-grid'() {
    let lines = '';
    for (let i = 0; i <= 12; i++) { const x = i * 25; lines += `<line x1="150" y1="230" x2="${x * 3 - 300}" y2="400" stroke="#ff4fd8" stroke-width="1.5" opacity=".8"/>`; }
    for (let i = 0; i < 9; i++) { const y = 230 + Math.pow(i / 8, 1.8) * 170; lines += `<line x1="0" y1="${y.toFixed(1)}" x2="300" y2="${y.toFixed(1)}" stroke="#ff4fd8" stroke-width="1.5" opacity=".8"/>`; }
    return wrap(lin('sky', '#12002e', '#4b0a5e') + lin('sun', '#ffd23f', '#ff3f8e'),
      `<rect width="300" height="400" fill="url(#sky)"/><circle cx="150" cy="190" r="80" fill="url(#sun)"/>` +
      [0, 1, 2, 3, 4].map(i => `<rect x="60" y="${196 + i * 9}" width="180" height="${2 + i}" fill="#2a0645"/>`).join('') +
      `<rect y="230" width="300" height="170" fill="#16002b"/>${lines}`);
  },
  forest() {
    const r = rng(7); let trees = '';
    const layers = [['#123b2c', 250, 14], ['#0d2d22', 290, 11], ['#082019', 330, 9]];
    for (const [c, base, n] of layers) for (let i = 0; i < n; i++) {
      const x = i * (300 / (n - 1)) + (r() * 16 - 8), h = 90 + r() * 70;
      trees += `<polygon points="${(x - 22).toFixed(1)},${base} ${x.toFixed(1)},${(base - h).toFixed(1)} ${(x + 22).toFixed(1)},${base}" fill="${c}"/>`;
    }
    return wrap(lin('bg', '#8fd3c3', '#1f5e57'),
      `<rect width="300" height="400" fill="url(#bg)"/><circle cx="90" cy="110" r="38" fill="#f4f1c9" opacity=".9"/>${trees}<rect y="328" width="300" height="72" fill="#061611"/>`);
  },
  dungeon() {
    let bricks = '';
    for (let row = 0; row < 16; row++) for (let col = -1; col < 7; col++) {
      const x = col * 50 + (row % 2) * 25, y = row * 25;
      bricks += `<rect x="${x + 1}" y="${y + 1}" width="48" height="23" rx="2" fill="#2a2433"/>`;
    }
    return wrap(`<radialGradient id="glow" cx=".5" cy=".62" r=".45"><stop offset="0" stop-color="#ff9a3c" stop-opacity=".9"/><stop offset="1" stop-color="#ff9a3c" stop-opacity="0"/></radialGradient>`,
      `<rect width="300" height="400" fill="#16121d"/>${bricks}<path d="M90 400V250a60 60 0 0 1 120 0v150Z" fill="#07050a"/><rect width="300" height="400" fill="url(#glow)"/><circle cx="150" cy="300" r="10" fill="#ffd27a"/>`);
  },
  racer() {
    return wrap(lin('sky', '#ff7b39', '#3b1257') + lin('road', '#26263a', '#0f0f18'),
      `<rect width="300" height="400" fill="url(#sky)"/><polygon points="0,400 130,190 170,190 300,400" fill="url(#road)"/>` +
      [0, 1, 2, 3, 4].map(i => { const t = i / 5, y = 200 + Math.pow(t, 1.5) * 200, w = 2 + t * 10, hh = 6 + t * 26; return `<rect x="${(150 - w / 2).toFixed(1)}" y="${y.toFixed(1)}" width="${w.toFixed(1)}" height="${hh.toFixed(1)}" fill="#ffe16b"/>`; }).join('') +
      `<polygon points="0,190 60,150 110,180 170,130 230,175 300,140 300,190" fill="#241036"/>` +
      [0, 1, 2, 3, 4, 5].map(i => `<line x1="0" y1="${60 + i * 18}" x2="300" y2="${50 + i * 18}" stroke="#fff" stroke-opacity=".12" stroke-width="3"/>`).join(''));
  },
  ocean() {
    let waves = '';
    for (let i = 0; i < 7; i++) {
      const y = 170 + i * 34; let d = `M0 ${y}`;
      for (let x = 0; x <= 300; x += 25) d += ` Q${x + 12.5} ${y + (x / 25 % 2 ? 10 : -10)} ${x + 25} ${y}`;
      waves += `<path d="${d} V400 H0Z" fill="#0b4f7a" opacity="${(0.25 + i * 0.08).toFixed(2)}"/>`;
    }
    return wrap(lin('bg', '#6fd6ff', '#062a47'),
      `<rect width="300" height="400" fill="url(#bg)"/>${waves}` +
      [[80, 300, 6], [110, 270, 4], [95, 240, 3], [210, 330, 5]].map(([x, y, rr]) => `<circle cx="${x}" cy="${y}" r="${rr}" fill="none" stroke="#bff1ff" stroke-width="1.5" opacity=".7"/>`).join('') +
      `<path d="M180 220c20-18 50-18 70 0-20 18-50 18-70 0Z" fill="#ffb74a"/><path d="M180 220l-18-12v24Z" fill="#ffb74a"/>`);
  },
  desert() {
    return wrap(lin('sky', '#ffcf8a', '#ff7e5f'),
      `<rect width="300" height="400" fill="url(#sky)"/><circle cx="200" cy="120" r="46" fill="#fff1c9"/>` +
      `<path d="M0 260 Q80 210 160 250 T300 230 V400 H0Z" fill="#e39b52"/>` +
      `<path d="M0 310 Q100 270 190 305 T300 290 V400 H0Z" fill="#c67637"/>` +
      `<path d="M0 360 Q120 330 220 360 T300 350 V400 H0Z" fill="#a35a28"/>` +
      `<polygon points="60,262 95,200 130,262" fill="#8a4a24"/><polygon points="105,262 130,220 155,262" fill="#733d1e"/>`);
  },
  circuit() {
    const r = rng(42); let traces = '';
    for (let i = 0; i < 26; i++) {
      let x = Math.round(r() * 12) * 25, y = Math.round(r() * 16) * 25, d = `M${x} ${y}`;
      for (let k = 0; k < 4; k++) { if (r() > .5) x += (r() > .5 ? 1 : -1) * 50; else y += (r() > .5 ? 1 : -1) * 50; d += ` L${x} ${y}`; }
      traces += `<path d="${d}" fill="none" stroke="#35e0b0" stroke-width="2" opacity=".75"/><circle cx="${x}" cy="${y}" r="4" fill="#0f2a2a" stroke="#35e0b0" stroke-width="2"/>`;
    }
    return wrap(lin('bg', '#0a1f24', '#062019'),
      `<rect width="300" height="400" fill="url(#bg)"/>${traces}<rect x="100" y="150" width="100" height="100" rx="10" fill="#0c3a33" stroke="#7ff5d2" stroke-width="3"/><rect x="125" y="175" width="50" height="50" rx="4" fill="#35e0b0" opacity=".35"/>`);
  }
};

for (const [name, fn] of Object.entries(covers)) {
  fs.writeFileSync(path.join(out, name + '.svg'), fn());
}
console.log('ok');
