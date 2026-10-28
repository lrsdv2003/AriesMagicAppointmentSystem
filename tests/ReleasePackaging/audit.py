"""Local inventory generator; no network, SQL, certificate or key-provider access."""
import collections
import csv
import hashlib
import json
import pathlib
import re
import subprocess
import zipfile

root = pathlib.Path.cwd().resolve()
state = json.loads((root / '.tmp-owner-build/unified-alpha-state.json').read_text(encoding='utf-8-sig'))
work, package = pathlib.Path(state['Work']).resolve(), pathlib.Path(state['Package']).resolve()
assert work.parent == root / '.tmp-owner-build'
assert package.parent == root and package.name.startswith('publish-net10-alpha-beta-')
out = work / 'delivery'
out.mkdir(exist_ok=True)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


files = []
for path in sorted(package.rglob('*')):
    if not path.is_file():
        continue
    relative = path.relative_to(package).as_posix()
    assert not any(part.lower() in {'app_data', 'uploads', 'tests', 'logs', '.git', '.tmp-owner-build'} for part in path.relative_to(package).parts)
    assert path.suffix.lower() not in {'.pfx', '.p12', '.pem', '.key', '.pdb', '.cs', '.cshtml', '.sql', '.mdf', '.ldf', '.log', '.pubxml', '.ps1', '.bak', '.zip'}
    assert path.name not in {'global.json', 'package.json', 'package-lock.json', 'npm-shrinkwrap.json', 'launchSettings.json', 'appsettings.Development.json', 'appsettings.Staging.json'}
    assert not re.search(r'(^|/)(key-|revocation-).*\.xml$', relative, re.I)
    assert not any(word in relative.lower() for word in ('fixture', 'synthetic', 'securityprobe', 'iis-diagnostic'))
    action = 'TRANSFER_AFTER_APPROVED_MAINTENANCE'
    if path.name == 'appsettings.Production.json':
        action = 'PROTECTED_SKIP_EXISTING_PRIVATE_CONFIG'
    elif path.name in {'web.config', 'appsettings.json'}:
        action = 'MANUAL_PRIVATE_CONFIGURATION_REVIEW_BEFORE_TRANSFER'
    files.append(dict(Path=relative, Bytes=path.stat().st_size, SHA256=sha(path), Action=action))

assert not (package / 'coreclr.dll').exists() and not (package / 'AriesMagicAppointmentSystem.exe').exists()
assert json.loads((package / 'appsettings.Production.json').read_text(encoding='utf-8-sig')) == {}
settings = json.loads((package / 'appsettings.json').read_text(encoding='utf-8-sig'))
assert settings['Database']['ValidateOnStartup'] is True
assert settings['Database']['InitializeOnStartup'] is False and 'DataProtection' not in settings
assets = []
for path in (root / 'wwwroot').rglob('*'):
    if not path.is_file():
        continue
    relative = path.relative_to(root).as_posix()
    if relative.startswith('wwwroot/uploads/') or path.name.startswith('test-contract'):
        continue
    assert (package / relative).is_file() and sha(path) == sha(package / relative), 'Source asset mismatch: ' + relative
    assets.append(relative)
compiled = json.loads((work / 'packaging-evidence-final.json').read_text(encoding='utf-8'))
assert compiled['H4Configured'] is False and compiled['ServicesRegisteredByDisabledH4'] == 0

(out / 'SHA256_MANIFEST.json').write_text(json.dumps(files, indent=2), encoding='utf-8')
with (out / 'DEPLOYMENT_INVENTORY.csv').open('w', encoding='utf-8-sig', newline='') as stream:
    writer = csv.DictWriter(stream, fieldnames=['Path', 'Bytes', 'SHA256', 'Action'])
    writer.writeheader()
    writer.writerows(files)
(out / 'TRANSFER_AFTER_APPROVAL.txt').write_text('\n'.join(x['Path'] for x in files if x['Action'] == 'TRANSFER_AFTER_APPROVED_MAINTENANCE') + '\n', encoding='utf-8')
(out / 'REVIEW_OR_SKIP_CONFIG.txt').write_text('\n'.join(x['Path'] + ' : ' + x['Action'] for x in files if x['Action'] != 'TRANSFER_AFTER_APPROVED_MAINTENANCE') + '\n', encoding='utf-8')

source_names = subprocess.run(['git', 'ls-files', '-c', '-o', '--exclude-standard'], capture_output=True, text=True, check=True).stdout.splitlines()
baseline = zipfile.ZipFile(root / '.tmp-owner-build/security-baseline-20261025/HEAD.zip')
old = {x.filename: baseline.read(x) for x in baseline.infolist() if not x.is_dir()}
audit, endings = [], []
text_types = {'.cs', '.cshtml', '.json', '.md', '.css', '.js', '.mjs', '.ps1', '.sql', '.xml', '.yml', '.txt', '.pubxml', '.csproj', '.sln'}
for relative in sorted(set(source_names)):
    path = root / relative
    if relative in {'index.html', 'package-lock.json'} or not path.is_file():
        continue
    before, current = old.get(relative), path.read_bytes()
    if before == current:
        continue
    if before is not None and (path.suffix in text_types or path.name.startswith('.')) and before.replace(b'\r\n', b'\n') == current.replace(b'\r\n', b'\n'):
        endings.append(relative)
        continue
    group = 'Documentation/test/release tooling'
    if relative.startswith('Views/'):
        group = 'Client/shared Razor presentation and asset references'
    elif relative.startswith('wwwroot/'):
        group = 'Static UI asset'
    elif relative.startswith(('Data/', 'Models/', 'Migrations/')):
        raise AssertionError('Schema/business-model change requires independent review: ' + relative)
    elif relative.startswith(('Controllers/', 'Services/', 'ViewComponents/', 'ViewModels/')):
        group = 'Runtime code: UI read-model, storage or security change'
    elif relative in {'Program.cs', 'AriesMagicAppointmentSystem.csproj', 'appsettings.json', 'global.json'}:
        group = 'Startup/framework/configuration'
    audit.append(dict(Path=relative, Status='Added' if before is None else 'Modified', BaselineSHA256=hashlib.sha256(before).hexdigest().upper() if before is not None else None, CandidateSHA256=sha(path), Group=group))
(out / 'SOURCE_CHANGE_AUDIT.json').write_text(json.dumps(dict(Comparison='Local pre-hardening committed baseline; exact live binary not available', LiveBinaryComparisonVerified=False, LineEndingOnly=endings, Changes=audit), indent=2), encoding='utf-8')

photos = {
    'about/illusion.jpeg': '689f032a-d611-40ae-8c2d-2a246e8fd1f5.jpeg',
    'gallery/fire.jpeg': '6bafb3da-8238-4b95-975a-3215590a9e2d.jpeg',
    'wonder-studio/audience-wonder.jpeg': '5cf23c8a-0d8c-4c7e-bbd1-b3ec54e30098.jpeg',
    'about/participation.jpeg': '5ba9378a-a34f-4c4e-9a44-b03fc5a95f25.jpeg',
    'gallery/silks.jpeg': '1c1f0f0d-0941-4093-a66f-9166355c0da0.jpeg',
    'gallery/bear.jpeg': '0c174c76-b09d-44ed-a548-6b261f79b1ac.jpeg',
    'wonder-studio/celebration.jpeg': '2cfdd8b6-e79e-484b-a9e7-e0474902c1d4.jpeg',
}
for relative, original in photos.items():
    assert sha(package / 'wwwroot/images/client' / relative) == sha(pathlib.Path.home() / 'Downloads' / original)
ids = sorted(set(re.findall(r'\[Migration\("([^"]+)"\)\]', '\n'.join(p.read_text(encoding='utf-8-sig') for p in (root / 'Migrations').glob('*.Designer.cs')))))
(out / 'SCHEMA_REQUIREMENTS.json').write_text(json.dumps(dict(ExpectedMigrationIds=ids, NoNewMigrations=True, ProductionSchemaVerified=False), indent=2), encoding='utf-8')
summary = dict(Files=len(files), Bytes=sum(x['Bytes'] for x in files), SourceStaticAssetsVerified=len(assets), CompiledRazorItems=len(compiled['Views']), PackageMetadataChecks=compiled['Checks'], H4Configured=False, OriginalGalleryPhotosMatched=len(photos), MigrationCount=len(ids), SourceChangeCounts=dict(collections.Counter(x['Group'] for x in audit)), ManifestSHA256=sha(out / 'SHA256_MANIFEST.json'), PackageSHA256=sha(package / 'AriesMagicAppointmentSystem.dll'), RuntimeConfigSHA256=sha(package / 'AriesMagicAppointmentSystem.runtimeconfig.json'), WebConfigSHA256=sha(package / 'web.config'), ProductionApproval='NO-GO pending manual hosting gates')
(out / 'RELEASE_SUMMARY.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
print(json.dumps(summary, indent=2))
