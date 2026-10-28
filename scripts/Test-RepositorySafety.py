"""Fail on private artifacts in the Git index; never print contents or secrets."""
import json, re, subprocess, sys
files = [p for p in subprocess.check_output(['git','ls-files','-z']).decode().split('\0') if p]
blocked = re.compile(r'(?i)(^|/)(App_Data|node_modules|bin|obj|logs|backups?|TestResults|artifacts|\.tmp[^/]*|publish(?:-[^/]*)?)(/|$)|\.(bak|bkp|backup|trn|bacpac|mdf|ldf|pfx|p12|pem|key|log|dmp|dll|exe|pdb|zip|7z|db|sqlite3?|pubxml\.user|publishsettings)$|(^|/)appsettings\.(Development|Production|Staging)\.json$|(^|/)\.env(?:\.|$)|(^|/)(key|revocation)-[^/]+\.xml$|^wwwroot/uploads/(?!\.gitkeep$)')
failures = []
for name in files:
    if blocked.search(name) and name != '.env.example':
        failures.append({'path':name,'rule':'private-or-generated-artifact'})
    if name.startswith('wwwroot/') and re.search(r'(?i)(^|/)(appsettings[^/]*\.json|web\.config|\.env[^/]*)$',name):
        failures.append({'path':name,'rule':'public-configuration'})
    if re.fullmatch(r'appsettings\.json|Properties/Deployment/appsettings\.[^/]+\.json\.template',name):
        try:
            data=json.loads(subprocess.check_output(['git','show',':'+name]))
            def inspect(value):
                if isinstance(value,dict):
                    for key,child in value.items():
                        if re.search(r'(?i)(password|pwd|secret|token|apikey|username)$',key) and child:
                            failures.append({'path':name,'rule':'nonempty-credential-setting'})
                        inspect(child)
                elif isinstance(value,list):
                    for item in value: inspect(item)
                elif isinstance(value,str) and re.search(r'(?i)(?:^|;)\s*(password|pwd|user\s*id|uid)\s*=',value):
                    failures.append({'path':name,'rule':'sql-credential-setting'})
            inspect(data)
        except (ValueError,subprocess.CalledProcessError):
            failures.append({'path':name,'rule':'invalid-settings-template'})
print(json.dumps({'indexedFiles':len(files),'failures':failures},indent=2))
sys.exit(bool(failures))
