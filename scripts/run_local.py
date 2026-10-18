"""Start the built API with local config; --test uses the last isolated integration schema."""
import json,os,secrets,sys,subprocess,signal,socket,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=ROOT/'backend/appsettings.Local.json'
config=json.loads(p.read_text())
if '--test' in sys.argv:
 schema=json.loads(Path('/private/tmp/lms-integration-report.json').read_text())['schema']
 os.environ['ConnectionStrings__LmsDatabase']=config['ConnectionStrings']['LmsDatabase']+';Search Path='+schema
 os.environ['Jwt__Key']=secrets.token_urlsafe(48)
 os.environ['Database__SeedDemoData']='false'
 port='5166'
else:
 if not config.get('Jwt',{}).get('Key'):
  config['Jwt']={**config.get('Jwt',{}),'Key':secrets.token_urlsafe(48)}
 config.setdefault('Database',{})['SeedDemoData']=False
 p.write_text(json.dumps(config,ensure_ascii=False,indent=2));p.chmod(0o600)
 port='5155'
if '--restart' in sys.argv:
 listeners=subprocess.run(['lsof','-tiTCP:'+port,'-sTCP:LISTEN'],capture_output=True,text=True).stdout.split()
 for pid in listeners:
  cwd=subprocess.run(['lsof','-a','-p',pid,'-d','cwd','-Fn'],capture_output=True,text=True).stdout.splitlines()
  if 'n'+str(ROOT/'backend') not in cwd:raise SystemExit('Refusing to stop a process outside the LMS backend')
  os.kill(int(pid),signal.SIGTERM)
 for _ in range(50):
  try:
   with socket.create_connection(('localhost',int(port)),timeout=.1):pass
  except OSError:break
  time.sleep(.1)

os.environ['Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command']='Warning'
os.chdir(ROOT/'backend')
os.execvp('dotnet',['dotnet',str(ROOT/'backend/bin/Debug/net9.0/LawLms.Api.dll'),'--urls','http://localhost:'+port])
