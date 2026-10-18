"""Local PostgreSQL inspection, backup and versioned SQL upgrade. Never prints credentials."""
import os, json, subprocess, sys, datetime, tarfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
config = json.loads((ROOT/'backend/appsettings.Local.json').read_text())
parts = dict(item.split('=',1) for item in config['ConnectionStrings']['LmsDatabase'].split(';') if '=' in item)
lookup = {k.lower():v for k,v in parts.items()}
env = dict(os.environ, PGHOST=lookup.get('host','localhost'), PGPORT=lookup.get('port','5432'), PGDATABASE=lookup.get('database','postgres'), PGUSER=lookup.get('username','postgres'), PGPASSWORD=lookup.get('password',''), PGCONNECT_TIMEOUT='10')
bin_dir = Path('/Library/PostgreSQL/18/bin')
def run(tool, args, **kw): return subprocess.run([str(bin_dir/tool),*args],env=env,check=True,**kw)
def sql(query): return run('psql',['-X','-v','ON_ERROR_STOP=1','-At','-c',query],capture_output=True,text=True).stdout.strip()
if __name__ == '__main__':
 mode=sys.argv[1] if len(sys.argv)>1 else 'inspect'
 if mode=='inspect':
  print(sql("select current_database(), version(); select tablename from pg_tables where schemaname='public' order by tablename;"))
  print(sql("select 'users',count(*) from users union all select 'courses',count(*) from courses union all select 'teaching_classes',count(*) from teaching_classes union all select 'quiz_attempts',count(*) from quiz_attempts;"))
 elif mode=='backup':
  folder=ROOT/'.backups';folder.mkdir(exist_ok=True);folder.chmod(0o700)
  target=folder/('lms-backup-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'.dump')
  run('pg_dump',['-Fc','-f',str(target)])
  target.chmod(0o600)
  run('pg_restore',['--list',str(target)],stdout=subprocess.DEVNULL)
  files=target.with_suffix('.files.tar.gz')
  storage=config.get('Storage',{}).get('RootPath','uploads')
  storage=Path(storage) if Path(storage).is_absolute() else ROOT/'backend'/storage
  with tarfile.open(files,'w:gz') as archive:
   if storage.exists():archive.add(storage,arcname='uploads')
  files.chmod(0o600)
  print('Verified backup archive:', target, 'bytes:',target.stat().st_size)
  print('Companion storage archive:',files)
 elif mode=='upgrade':
  run('psql',['-X','-v','ON_ERROR_STOP=1','-c','CREATE TABLE IF NOT EXISTS lms_schema_versions (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());'])
  for name in sys.argv[2:]:
   path=ROOT/'backend/database'/name
   if path.parent != ROOT/'backend/database' or not path.is_file(): raise SystemExit('Invalid migration')
   if sql("select count(*) from lms_schema_versions where name='"+name.replace("'","''")+"'")=='1':
    print('Already applied:',name); continue
   run('psql',['-X','-v','ON_ERROR_STOP=1','-f',str(path)])
   sql("insert into lms_schema_versions(name) values ('"+name.replace("'","''")+"')")
   print('Applied:',name)
 else: raise SystemExit('Unknown mode')
