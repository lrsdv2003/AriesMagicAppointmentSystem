// Propagate an intentionally edited legacy-rule section across role bundles.
// Example: npm run css:sync -- --source site.css --from site.css
// Changes remain local; validate and inspect the resulting Git diff.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dir = path.join(root, 'wwwroot', 'css');
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'docs', 'frontend-cleanup', 'phase2', 'bundle-manifest.json'), 'utf8'));
const bundleMap = manifest.BundleMap;
const args = process.argv.slice(2);
const opt = flag => args.includes(flag) ? args[args.indexOf(flag) + 1] : null;
const source = opt('--source');
const from = opt('--from');
if (!source || !from || !/^[a-z0-9._-]+\.css$/i.test(source) || !bundleMap[from]?.includes(source)) {
  throw new Error('Provide --source name.css and --from bundle.css (which must contain that source)');
}
const marker = /\/\* ========== ORIGINAL: ([\w.-]+\.css) ========== \*\//g;
function locate(text, name) {
  const matches = [...text.matchAll(marker)];
  const index = matches.findIndex(x => x[1] === name);
  if (index < 0) throw new Error(name + ' not found in bundle');
  const start = matches[index].index + matches[index][0].length;
  const end = index + 1 < matches.length ? matches[index + 1].index : text.length;
  return { start, end };
}
const fromPath = path.join(dir, from);
const canonicalText = fs.readFileSync(fromPath, 'utf8');
const canonicalLoc = locate(canonicalText, source);
const canonical = canonicalText.slice(canonicalLoc.start, canonicalLoc.end);

let changed = 0;
for (const [bundle, names] of Object.entries(bundleMap)) {
  if (!names.includes(source) || bundle === from) continue;
  const file = path.join(dir, bundle);
  const text = fs.readFileSync(file, 'utf8');
  const loc = locate(text, source);
  const revised = text.slice(0, loc.start) + canonical + text.slice(loc.end);
  if (revised !== text) {
    fs.writeFileSync(file, revised, 'utf8');
    console.log('SYNCED ' + source + ' to ' + bundle);
    changed++;
  }
}
console.log('Synced ' + source + ' from ' + from + ' into ' + changed + ' other CSS bundle(s).');
console.log('Review the diff and run npm run test:css, then build and visually test.');
