// Verifies the original-rule provenance and consistency of CSS bundles.
// Run: node scripts/verify-css-bundles.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const cssDir = path.join(root, 'wwwroot', 'css');
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'docs', 'frontend-cleanup', 'phase2', 'bundle-manifest.json'), 'utf8'));
const bundles = manifest.BundleMap;
const expected = Object.keys(bundles).sort();
const actual = fs.readdirSync(cssDir).filter(x => x.endsWith('.css')).sort();
const fail = msg => { throw new Error('CSS bundle validation: ' + msg); };
if (expected.length !== 13 || manifest.OptimizedCount !== 13) fail('unexpected manifest file count');
if (JSON.stringify(expected) !== JSON.stringify(actual)) fail('actual CSS list does not match manifest: ' + JSON.stringify(actual));

const provenance = new Map();
let sourceSections = 0;
const marker = /\/\* ========== ORIGINAL: ([\w.-]+\.css) ========== \*\//g;
for (const bundle of expected) {
  const content = fs.readFileSync(path.join(cssDir, bundle), 'utf8').replace(/\r\n/g, '\n');
  const markers = [...content.matchAll(marker)];
  const sources = bundles[bundle];
  if (markers.length !== sources.length) fail(bundle + ' should have ' + sources.length + ' sections, found ' + markers.length);
  for (let i = 0; i < markers.length; i++) {
    const current = markers[i];
    const source = current[1];
    if (source !== sources[i]) fail(bundle + ' section ' + (i+1) + ' is ' + source + ', expected ' + sources[i]);
    const beginning = current.index + current[0].length;
    const ending = i + 1 < markers.length ? markers[i + 1].index : content.length;
    const section = content.slice(beginning, ending).trim();
    if (!section) fail(bundle + '/' + source + ': empty section');
    const prior = provenance.get(source);
    if (prior && prior.section !== section) fail(source + ' differs between ' + prior.bundle + ' and ' + bundle);
    provenance.set(source, { bundle, section });
    sourceSections++;
  }
}

const viewDir = path.join(root, 'Views');
let viewFiles = 0;
const unavailable = [];
function scan(dir) {
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, ent.name);
    if (ent.isDirectory()) scan(full);
    if (!ent.isFile() || !ent.name.endsWith('.cshtml')) continue;
    viewFiles++;
    const txt = fs.readFileSync(full, 'utf8');
    const refs = [...txt.matchAll(/~\/css\/([\w-]+\.css)/g)].map(x => x[1]);
    for (const ref of refs) if (!bundles[ref]) unavailable.push(path.relative(root, full) + ' => ' + ref);
  }
}
scan(viewDir);
if (unavailable.length) fail('views reference deleted CSS files:\n' + unavailable.join('\n'));
console.log('PASS: ' + expected.length + ' CSS bundles, ' + sourceSections + ' original-rule sections, ' + provenance.size + ' unique legacy sections, and ' + viewFiles + ' Razor files verified.');
