const sharp = require('sharp');
const fs = require('fs');
const path = require('path');
const { default: pngToIco } = require('png-to-ico');

const ROOT = __dirname;
const CANVAS = 1024;
const PADDING = 50;
const RECT_SIZE = CANVAS - PADDING * 2;
const OG_W = 1200;
const OG_H = 630;

const OG_TEXT_COLORS = {
  dark: '#FFFFFF',
  light: '#374151',
};

const FONT_CACHE = path.join(ROOT, '.font-cache');
const FONT_CSS_URL = 'https://fonts.googleapis.com/css2?family=Inter:wght@600';

async function ensureInterFont() {
  if (fs.existsSync(FONT_CACHE)) {
    return fs.readFileSync(FONT_CACHE);
  }
  console.log('  ↓ resolving Inter font URL...');
  const cssResp = await fetch(FONT_CSS_URL, {
    headers: { 'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36' },
  });
  if (!cssResp.ok) throw new Error(`CSS fetch failed: ${cssResp.status}`);
  const css = await cssResp.text();
  const match = css.match(/url\(([^)]+)\)/);
  if (!match) throw new Error('Could not find font URL in CSS');
  const fontUrl = match[1];

  console.log('  ↓ downloading Inter font...');
  const fontResp = await fetch(fontUrl);
  if (!fontResp.ok) throw new Error(`Font download failed: ${fontResp.status}`);
  const buf = Buffer.from(await fontResp.arrayBuffer());
  fs.mkdirSync(path.dirname(FONT_CACHE), { recursive: true });
  fs.writeFileSync(FONT_CACHE, buf);
  return buf;
}

const PLATFORMS = {
  apple: { rx: 192, label: 'Apple (rx=192)' },
  android: { rx: 154, label: 'Android (rx=154)' },
};

const THEMES = {
  dark: {
    bg1: '#4B5563',
    bg2: '#374151',
    stroke: 'none',
    strokeWidth: '0',
  },
  light: {
    bg1: '#FAFAFA',
    bg2: '#F3F4F6',
    stroke: '#E5E7EB',
    strokeWidth: '2',
  },
};

const ICONS = [
  { name: 'pwa-48x48', size: 48 },
  { name: 'pwa-72x72', size: 72 },
  { name: 'pwa-96x96', size: 96 },
  { name: 'pwa-128x128', size: 128 },
  { name: 'pwa-144x144', size: 144 },
  { name: 'pwa-152x152', size: 152 },
  { name: 'pwa-192x192', size: 192 },
  { name: 'pwa-384x384', size: 384 },
  { name: 'pwa-512x512', size: 512 },
  { name: 'apple-touch-icon-57x57', size: 57 },
  { name: 'apple-touch-icon-60x60', size: 60 },
  { name: 'apple-touch-icon-72x72', size: 72 },
  { name: 'apple-touch-icon-76x76', size: 76 },
  { name: 'apple-touch-icon-114x114', size: 114 },
  { name: 'apple-touch-icon-120x120', size: 120 },
  { name: 'apple-touch-icon-152x152', size: 152 },
  { name: 'apple-touch-icon-167x167', size: 167 },
  { name: 'apple-touch-icon-180x180', size: 180 },
  { name: 'apple-touch-icon-1024x1024', size: 1024 },
  { name: 'android-36x36', size: 36 },
  { name: 'android-48x48', size: 48 },
  { name: 'android-72x72', size: 72 },
  { name: 'android-96x96', size: 96 },
  { name: 'android-144x144', size: 144 },
  { name: 'android-192x192', size: 192 },
  { name: 'android-512x512', size: 512 },
  { name: 'mstile-70x70', size: 70 },
  { name: 'mstile-144x144', size: 144 },
  { name: 'mstile-150x150', size: 150 },
  { name: 'mstile-310x310', size: 310 },
  { name: 'favicon-16x16', size: 16 },
  { name: 'favicon-32x32', size: 32 },
  { name: 'favicon-48x48', size: 48 },
  { name: 'favicon-64x64', size: 64 },
  { name: 'macos-16x16', size: 16 },
  { name: 'macos-32x32', size: 32 },
  { name: 'macos-64x64', size: 64 },
  { name: 'macos-128x128', size: 128 },
  { name: 'macos-256x256', size: 256 },
  { name: 'macos-512x512', size: 512 },
  { name: 'macos-1024x1024', size: 1024 },
];

function generateSvg({ platform, theme }) {
  const { rx } = PLATFORMS[platform];
  const { bg1, bg2, stroke, strokeWidth } = THEMES[theme];

  const rect = `<rect x="${PADDING}" y="${PADDING}" width="${RECT_SIZE}" height="${RECT_SIZE}" rx="${rx}" fill="url(#bg)" stroke="${stroke}" stroke-width="${strokeWidth}"/>`;

  return `<svg width="${CANVAS}" height="${CANVAS}" viewBox="0 0 ${CANVAS} ${CANVAS}" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="${bg1}"/>
      <stop offset="100%" stop-color="${bg2}"/>
    </linearGradient>
  </defs>
  ${rect}
  <g transform="translate(276, 185) scale(1.6)" fill="#EE7221">
    <path d="M222.289 345.677C221.196 436.829 58.655 424.572 73.6932 333.434H222.289V345.677ZM147.5 0.0861511C274.44 -4.33261 343.954 162.31 254.839 249.867C242.121 263.282 230.872 278.848 225.248 296.039H157.975V224.113C195.197 217.281 209.435 182.846 205.62 147.572C218.285 147.519 218.298 128.229 205.62 128.19H186.242V99.1174C186.242 93.7543 181.916 89.4271 176.554 89.427C171.191 89.427 166.864 93.7542 166.864 99.1174V128.19H128.121V99.1174C128.121 93.7543 123.795 89.427 118.432 89.427C113.07 89.427 108.743 93.7542 108.743 99.1174V128.19H89.3651C76.7002 128.242 76.6871 147.519 89.3651 147.572C85.563 182.846 99.7624 217.255 137.011 224.113V296.039H69.7381C64.1922 278.861 52.8645 263.282 40.16 249.88C-48.942 162.31 20.5331 -4.30678 147.5 0.0861511Z"/>
  </g>
</svg>`;
}

function generateForegroundSvg() {
  return `<svg width="${CANVAS}" height="${CANVAS}" viewBox="0 0 ${CANVAS} ${CANVAS}" xmlns="http://www.w3.org/2000/svg">
  <g transform="translate(276, 185) scale(1.6)" fill="#EE7221">
    <path d="M222.289 345.677C221.196 436.829 58.655 424.572 73.6932 333.434H222.289V345.677ZM147.5 0.0861511C274.44 -4.33261 343.954 162.31 254.839 249.867C242.121 263.282 230.872 278.848 225.248 296.039H157.975V224.113C195.197 217.281 209.435 182.846 205.62 147.572C218.285 147.519 218.298 128.229 205.62 128.19H186.242V99.1174C186.242 93.7543 181.916 89.4271 176.554 89.427C171.191 89.427 166.864 93.7542 166.864 99.1174V128.19H128.121V99.1174C128.121 93.7543 123.795 89.427 118.432 89.427C113.07 89.427 108.743 93.7542 108.743 99.1174V128.19H89.3651C76.7002 128.242 76.6871 147.519 89.3651 147.572C85.563 182.846 99.7624 217.255 137.011 224.113V296.039H69.7381C64.1922 278.861 52.8645 263.282 40.16 249.88C-48.942 162.31 20.5331 -4.30678 147.5 0.0861511Z"/>
  </g>
</svg>`;
}

function generateBackgroundSvg({ platform, theme }) {
  const { rx } = PLATFORMS[platform];
  const { bg1, bg2, stroke, strokeWidth } = THEMES[theme];

  return `<svg width="${CANVAS}" height="${CANVAS}" viewBox="0 0 ${CANVAS} ${CANVAS}" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="${bg1}"/>
      <stop offset="100%" stop-color="${bg2}"/>
    </linearGradient>
  </defs>
  <rect x="${PADDING}" y="${PADDING}" width="${RECT_SIZE}" height="${RECT_SIZE}" rx="${rx}" fill="url(#bg)" stroke="${stroke}" stroke-width="${strokeWidth}"/>
</svg>`;
}

async function generateIcon(svgBuffer, size) {
  return sharp(svgBuffer, { density: Math.min(Math.max(size * 2, 144), 300) })
    .resize(size, size)
    .png()
    .toBuffer();
}

async function generateSet(platform, theme, fontB64, fontFormat) {
  const { label: platformLabel } = PLATFORMS[platform];
  const themeLabel = theme === 'dark' ? 'Dark' : 'Light';
  const baseDir = path.join(ROOT, platform, theme);
  const sourceDir = path.join(baseDir, 'source');
  const iconsDir = path.join(baseDir, 'icons');

  fs.mkdirSync(sourceDir, { recursive: true });
  fs.mkdirSync(iconsDir, { recursive: true });

  console.log(`\n=== ${platformLabel} / ${themeLabel} ===`);

  const masterSvg = generateSvg({ platform, theme });
  const foregroundSvg = generateForegroundSvg();
  const backgroundSvg = generateBackgroundSvg({ platform, theme });

  fs.writeFileSync(path.join(sourceDir, 'master-icon.svg'), masterSvg);
  fs.writeFileSync(path.join(sourceDir, 'foreground.svg'), foregroundSvg);
  fs.writeFileSync(path.join(sourceDir, 'background.svg'), backgroundSvg);
  console.log('  ✓ source SVGs');

  const masterBuf = Buffer.from(masterSvg);
  const fgBuf = Buffer.from(foregroundSvg);
  const bgBuf = Buffer.from(backgroundSvg);

  for (const icon of ICONS) {
    const png = await generateIcon(masterBuf, icon.size);
    fs.writeFileSync(path.join(iconsDir, `${icon.name}.png`), png);
  }
  console.log(`  ✓ ${ICONS.length} PNG icons`);

  // MS Tile wide (310x150)
  const widePng = await sharp(masterBuf, { density: 300 })
    .resize(310, 150, { fit: 'contain', background: theme === 'dark'
      ? { r: 55, g: 65, b: 81, alpha: 0 }
      : { r: 243, g: 244, b: 246, alpha: 0 } })
    .png()
    .toBuffer();
  fs.writeFileSync(path.join(iconsDir, 'mstile-310x150.png'), widePng);
  console.log('  ✓ mstile-310x150.png');

  // Android adaptive layers
  const fg1024 = await generateIcon(fgBuf, 1024);
  fs.writeFileSync(path.join(iconsDir, 'android-foreground.png'), fg1024);
  const bg1024 = await generateIcon(bgBuf, 1024);
  fs.writeFileSync(path.join(iconsDir, 'android-background.png'), bg1024);
  console.log('  ✓ android adaptive layers');

  // favicon.ico
  const icoBuf = await pngToIco([
    path.join(iconsDir, 'favicon-16x16.png'),
    path.join(iconsDir, 'favicon-32x32.png'),
    path.join(iconsDir, 'favicon-48x48.png'),
  ]);
  fs.writeFileSync(path.join(iconsDir, 'favicon.ico'), icoBuf);
  console.log('  ✓ favicon.ico');

  // OG image (1200x630) for social preview
  const skColor = OG_TEXT_COLORS[theme];
  const ogBgSvg = `<svg width="${OG_W}" height="${OG_H}" viewBox="0 0 ${OG_W} ${OG_H}" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="ogbg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="${THEMES[theme].bg1}"/>
      <stop offset="100%" stop-color="${THEMES[theme].bg2}"/>
    </linearGradient>
    <style>
      @font-face {
        font-family: 'Inter';
        src: url(data:${fontFormat};base64,${fontB64}) format('truetype');
        font-weight: 600;
      }
    </style>
  </defs>
  <rect width="${OG_W}" height="${OG_H}" fill="url(#ogbg)"/>
  <text x="600" y="520" text-anchor="middle" font-family="Inter, system-ui, -apple-system, sans-serif, Segoe UI" font-size="44" font-weight="600">
    <tspan fill="#EE7221">Svitlo</tspan><tspan fill="${skColor}">Sk</tspan>
  </text>
</svg>`;
  const ogBgBuf = Buffer.from(ogBgSvg);
  const ogBgPng = await sharp(ogBgBuf, { density: 300 })
    .resize(OG_W, OG_H)
    .png()
    .toBuffer();

  const ogIconSize = 360;
  const ogIconPng = await generateIcon(masterBuf, ogIconSize);
  const ogLeft = Math.round((OG_W - ogIconSize) / 2);
  const ogTop = 95;

  const ogPng = await sharp(ogBgPng)
    .composite([{ input: ogIconPng, top: ogTop, left: ogLeft }])
    .png()
    .toBuffer();
  fs.writeFileSync(path.join(iconsDir, 'og-image.png'), ogPng);
  console.log(`  ✓ og-image.png (${OG_W}x${OG_H})`);
}

async function main() {
  console.log('SvitloSk — All Platform Icon Generator\n');

  const fontBuf = await ensureInterFont();
  const fontB64 = fontBuf.toString('base64');
  const fontFormat = 'font/ttf';

  for (const platform of ['apple', 'android']) {
    for (const theme of ['dark', 'light']) {
      await generateSet(platform, theme, fontB64, fontFormat);
    }
  }

  console.log('\n✓ All 4 sets generated successfully!');
}

main().catch(err => {
  console.error('Error:', err);
  process.exit(1);
});
