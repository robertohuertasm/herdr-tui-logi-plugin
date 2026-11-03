// Run after npm install --prefix tools.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');

const root = path.resolve(__dirname, '..');
const logo = fs.readFileSync(path.join(root, 'assets/herdr-logo.svg'));
const mask = Buffer.from('<svg width="206" height="206"><rect width="206" height="206" rx="42" fill="white"/></svg>');

async function main() {
  const rounded = await sharp(logo).resize(206, 206).composite([{ input: mask, blend: 'dest-in' }]).png().toBuffer();
  await sharp({ create: { width: 256, height: 256, channels: 4, background: '#00000000' } })
    .composite([{ input: rounded, left: 25, top: 25 }])
    .png().toFile(path.join(root, 'package/metadata/Icon256x256.png'));
}
main().catch(error => { console.error(error); process.exitCode = 1; });
