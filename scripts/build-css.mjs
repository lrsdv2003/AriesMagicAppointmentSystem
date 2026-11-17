// Aries canonical CSS build. No third-party dependencies; Node >=24.
// npm run css:build: regenerate the 13 existing browser-served bundles.
// npm run css:check: verify all generated bundles are up to date, without writes.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sourceDir = path.join(root, 'assets', 'styles');
const outDir = path.join(root, 'wwwroot', 'css');
const config = JSON.parse(fs.readFileSync(path.join(sourceDir, 'bundles.json'), 'utf8'));
const args = new Set(process.argv.slice(2));
const writing = args.has('--write');
const checking = args.has('--check');
if (writing === checking || [...args].some(x => !['--write', '--check'].includes(x))) {
  throw new Error('Usage: node scripts/build-css.mjs --write OR --check');
}
if (config.formatVersion !== 1 || config.generatedDirectory !== 'wwwroot/css') {
  throw new Error('Unsupported CSS build recipe; refusing to generate output');
}
const validCssName = s => typeof s === 'string' && /^[a-z0-9][\w.-]*\.css$/.test(s);
const canonicalMarker = /\/\* ========== CANONICAL: ([\w.-]+\.css) ========== \*\//g;
const sourceSections = new Map();
const usedModules = new Set();
for (const [moduleFile, sourceNames] of Object.entries(config.modules)) {
  if (!validCssName(moduleFile) || !Array.isArray(sourceNames) || !sourceNames.length) {
    throw new Error('Invalid source module declaration: ' + moduleFile);
  }
  usedModules.add(moduleFile);
  const text = fs.readFileSync(path.join(sourceDir, 'modules', moduleFile), 'utf8');
  const matches = [...text.matchAll(canonicalMarker)];
  const foundNames = matches.map(x => x[1]);
  if (JSON.stringify(foundNames) !== JSON.stringify(sourceNames)) {
    throw new Error('Canonical source list differs for ' + moduleFile + ': ' + JSON.stringify(foundNames));
  }
  for (let i = 0; i < matches.length; i++) {
    const m = matches[i];
    const next = i + 1 < matches.length ? matches[i + 1].index : text.length;
    const content = text.slice(m.index + m[0].length, next).trim();
    if (!content || sourceSections.has(m[1])) {
      throw new Error('Empty or duplicated canonical source: ' + m[1]);
    }
    sourceSections.set(m[1], content);
  }
}
const actualModules = fs.readdirSync(path.join(sourceDir, 'modules')).filter(x => x.endsWith('.css'));
if (actualModules.length !== usedModules.size || actualModules.some(x => !usedModules.has(x))) {
  throw new Error('Unexpected canonical source module: ' + actualModules.join(', '));
}

const allBundles = Object.entries(config.bundles);
const expected = allBundles.map(([name]) => name).sort();
const actual = fs.readdirSync(outDir).filter(x => x.endsWith('.css')).sort();
if (expected.length !== 13 || JSON.stringify(expected) !== JSON.stringify(actual)) {
  throw new Error('Unexpected deployed bundle set; need exactly 13: ' + JSON.stringify(actual));
}
let renderedSources = 0;
let modified = 0;
let stale = 0;
for (const [name, recipe] of allBundles) {
  if (!validCssName(name) || typeof recipe.preamble !== 'string' || !Array.isArray(recipe.sections)) {
    throw new Error('Invalid bundle definition: ' + name);
  }
  let generated = recipe.preamble;
  for (const section of recipe.sections) {
    const css = sourceSections.get(section.source);
    if (!css || typeof section.before !== 'string' || typeof section.after !== 'string') {
      throw new Error('Invalid or missing source used in ' + name + ': ' + section.source);
    }
    generated += '/* ========== ORIGINAL: ' + section.source + ' ========== */' + section.before + css + section.after;
    renderedSources++;
  }
  const dest = path.join(outDir, name);
  const existing = fs.readFileSync(dest, 'utf8');
  if (existing === generated) continue;
  stale++;
  const hash = text => crypto.createHash('sha256').update(text, 'utf8').digest('hex').slice(0, 12);
  if (checking) {
    console.error('STALE ' + name + ': expected ' + hash(generated) + ', existing ' + hash(existing));
    continue;
  }
  // The content is already complete in memory. Replace each generated artifact
  // only after confirming the recipe and all source modules are valid.
  const temp = dest + '.phase3-tmp';
  if (fs.existsSync(temp)) throw new Error('Stale temporary output; refusing to overwrite ' + temp);
  fs.writeFileSync(temp, generated, { encoding: 'utf8', flag: 'wx' });
  fs.renameSync(temp, dest);
  modified++;
  console.log('GENERATED ' + name + ' (' + Buffer.byteLength(generated, 'utf8') + ' bytes)');
}
if (renderedSources !== 79 || sourceSections.size !== 41) {
  throw new Error('Unexpected source section count: ' + sourceSections.size + ' / ' + renderedSources);
}
if (stale && checking) {
  console.error('FAIL: ' + stale + ' outdated CSS bundle(s). Run npm run css:build.');
  process.exitCode = 1;
} else {
  console.log('PASS: ' + sourceSections.size + ' canonical source sections in ' + usedModules.size
    + ' modules -> ' + expected.length + ' browser bundles, ' + renderedSources
    + ' ordered placements. Updated: ' + modified + '.');
}
