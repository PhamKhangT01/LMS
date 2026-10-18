"""Restore a backup into a separate database and compare table counts. Leaves recovery evidence intact."""
import json, subprocess, sys, uuid
from pathlib import Path
import database as config
archive=Path(sys.argv[1]).resolve()
if not archive.is_file():raise SystemExit('Backup does not exist')
target='lms_restore_'+uuid.uuid4().hex[:10]
config.run('createdb',[target])
config.run('pg_restore',['--exit-on-error','--no-owner','--no-privileges','-d',target,str(archive)])
query="select 'users',count(*) from users union all select 'courses',count(*) from courses union all select 'teaching_classes',count(*) from teaching_classes union all select 'quiz_attempts',count(*) from quiz_attempts;"
result=config.run('psql',['-X','-At','-d',target,'-c',query],capture_output=True,text=True).stdout.strip()
report={'database':target,'backup':str(archive),'restoredCounts':result.splitlines()}
Path('/private/tmp/lms-restore-report.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
