// Render documentation diagrams with the same colors and SVGs as the plugin.
// Run: npm install --prefix tools && node tools/render-images.cjs
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const palette = read('src/Herdr.Core/AgentPalette.cs');
const rgb = name => {
  const match = palette.match(new RegExp(`AgentStatus\\.${name}\\s*=>\\s*new\\((\\d+),\\s*(\\d+),\\s*(\\d+)\\)`));
  if (!match) throw new Error(`Missing color ${name}`);
  return `rgb(${match[1]},${match[2]},${match[3]})`;
};
const colors = { blocked: rgb('Blocked'), working: rgb('Working'), done: rgb('Done'), idle: rgb('Idle'), none: '#6e6e6e', offline: '#822323' };
const icons = {};
const svgBody = name => {
  if (!icons[name]) {
    const svg = read(`src/HerdrKeypad/Icons/${name}.svg`);
    const viewBox = svg.match(/viewBox="([^"]+)"/)[1];
    const content = svg.replace(/^[\s\S]*?<svg\b[^>]*>/, '').replace(/<\/svg>\s*$/, '');
    icons[name] = { viewBox, content };
  }
  return icons[name];
};
const escape = text => text.replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[ch]));
const icon = (name, x, y, size, color = '#fff') => {
  const item = svgBody(name);
  return `<svg x="${x}" y="${y}" width="${size}" height="${size}" viewBox="${item.viewBox}" fill="${color}">${item.content.replace(/currentColor/g, color)}</svg>`;
};
const state = (status, label, remote = false) => ({ type: 'state', status, label, remote });
const action = (name, label) => ({ type: 'action', name, label });
const key = (item, x, y, side) => {
  const scale = side / 132;
  let content;
  if (item.type === 'state') {
    const inner = item.status === 'none' || item.status === 'offline'
      ? '<circle cx="66" cy="49" r="20" fill="#000"/>'
      : icon('herdr', 44, 27, 44, '#000');
    content = `<circle cx="66" cy="49" r="28" fill="${colors[item.status]}"/>${inner}${item.remote ? '<circle cx="91" cy="25" r="14" fill="#000"/><circle cx="91" cy="25" r="11" fill="#fff"/><text x="91" y="29" text-anchor="middle" font-family="Arial,Helvetica,sans-serif" font-size="14" font-weight="600" fill="#000">R</text>' : ''}`;
  } else if (item.type === 'blank') {
    content = '';
  } else if (item.type === 'confirm') {
    content = '<rect x="0" y="0" width="132" height="94" fill="#c81e1e"/><text x="66" y="65" font-size="52" font-weight="600" text-anchor="middle" fill="#fff">?</text>';
  } else {
    content = icon(item.name, 43, 27, 46);
  }
  return `<g transform="translate(${x} ${y}) scale(${scale})"><rect width="132" height="132" fill="#000"/>${content}<text x="66" y="117" font-family="Arial,Helvetica,sans-serif" font-size="15" text-anchor="middle" fill="#e6e6e6">${escape(item.label)}</text></g>`;
};
const rows = (values, threshold) => {
  const bands = []; let start = -1;
  values.forEach((value, i) => {
    if (value > threshold && start < 0) start = i;
    else if (value <= threshold && start >= 0) { bands.push([start, i - 1]); start = -1; }
  });
  if (start >= 0) bands.push([start, values.length - 1]);
  return bands;
};
async function frameWindows(frame) {
  const { data, info } = await sharp(frame).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width, height } = info, holes = new Uint8Array(width * height);
  for (let y = 0; y < height; y++) {
    let first = -1, last = -1;
    for (let x = 0; x < width; x++) if (data[(y * width + x) * 4 + 3] > 128) { if (first < 0) first = x; last = x; }
    for (let x = first + 1; x < last; x++) holes[y * width + x] = data[(y * width + x) * 4 + 3] <= 128 ? 1 : 0;
  }
  const rowCounts = Array.from({ length: height }, (_, y) => {
    let count = 0; for (let x = 0; x < width; x++) count += holes[y * width + x]; return count;
  });
  const windows = [];
  for (const [top, bottom] of rows(rowCounts, 30)) {
    const columnCounts = Array.from({ length: width }, (_, x) => {
      let count = 0; for (let y = top; y <= bottom; y++) count += holes[y * width + x]; return count;
    });
    for (const [left, right] of rows(columnCounts, 10)) windows.push({ x: left, y: top, width: right - left + 1, height: bottom - top + 1 });
  }
  if (windows.length !== 9) throw new Error(`Expected 9 transparent key windows; found ${windows.length}`);
  return { width, height, windows };
}
async function renderDeck(items, output) {
  if (items.length !== 9) throw new Error('A Keypad diagram must have nine slots');
  const frame = path.join(root, 'assets/keypad-frame.png');
  const { width, height, windows } = await frameWindows(frame);
  const faces = items.map((item, i) => {
    const w = windows[i];
    return key(item, w.x, w.y + (w.height - w.width) / 2, w.width);
  }).join('');
  const svg = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">${faces}</svg>`);
  const frameData = await sharp(frame).png().toBuffer();
  await sharp(svg).composite([{ input: frameData }]).png().toFile(path.join(root, 'docs/images', output));
}
async function renderStates() {
  const states = [state('blocked','Blocked'), state('done','Unread'), state('working','Working'), state('idle','Idle'), state('none','Empty'), state('offline','Offline')];
  const svg = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="852" height="150"><rect width="100%" height="100%" fill="#141518"/>${states.map((item, i) => key(item, 4 + i * 141, 9, 132)).join('')}</svg>`);
  await sharp(svg).png().toFile(path.join(root, 'docs/images/states.png'));
}
async function main() {
  fs.mkdirSync(path.join(root, 'docs/images'), { recursive: true });
  await renderDeck([
    state('blocked','fix-login'),state('working','add-tests'),state('done','review-pr'),
    state('idle','docs'),state('working','refactor'),state('idle','release'),
    state('none','Recent 7'),state('none','Recent 8'),state('none','Recent 9'),
  ], 'keypad.png');
  await renderDeck([
    action('chevron-left','Back'), action('layout','Shortcuts'), action('sync','Cycle panes'),
    state('blocked','fix-login', true), state('done','review-pr'), state('working','add-tests', true),
    state('idle','docs'), state('working','refactor'), state('idle','release'),
  ], 'agents-folder.png');
  await renderDeck([
    action('chevron-left','Back'),{ type: 'blank', label: '' },action('screen-full','Zoom pane'),
    action('sync','Cycle panes'),action('multiple-windows','Cycle tabs'),action('arrow-circle-right','Go to'),
    action('layout','Layout'),action('multiple-windows','Tabs'),action('folder','Workspaces'),
  ], 'shortcuts-folder.png');
  await renderDeck([
    action('chevron-left','Back'),action('split-horizontal','Split right'),action('split-vertical','Split down'),
    action('arrow-left','Previous pane'),action('arrow-right','Next pane'),action('close','Close pane'),
    { type: 'blank', label: '' },{ type: 'blank', label: '' },{ type: 'blank', label: '' },
  ], 'layout-folder.png');
  await renderStates();
  console.log('Rendered Keypad and status diagrams in docs/images');
}
main().catch(error => { console.error(error); process.exitCode = 1; });
