"""Explicit, additive QA dataset for the configured local PostgreSQL LMS database.
Run from the repository root after dotnet build LMS.sln --no-restore.
--verify-only seeds and verifies an isolated temporary schema, never public rows.
"""
import argparse
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import uuid

import database

ROOT = Path(__file__).resolve().parents[1]
TABLES = ['users','courses','lessons','teaching_classes','enrollments','offering_enrollments','lesson_completions','quizzes','quiz_questions','quiz_attempts','quiz_answers','grade_appeals','quiz_allowances','schedule_events','schedule_changes','attendance_records','forum_topics','forum_replies','forum_subscriptions','library_resources','notifications','audit_entries','bank_questions']

def query(statement, pg_env):
    return subprocess.run([str(database.bin_dir/'psql'), '-X', '-v', 'ON_ERROR_STOP=1', '-At', '-c', statement], env=pg_env, check=True, capture_output=True, text=True).stdout.strip()

def existing_rows(pg_env):
    return {table: set(query('SELECT row_to_json(t)::text FROM '+table+' t', pg_env).splitlines()) for table in TABLES}

def fingerprint(pg_env):
    # Includes content, not only counts, so unchanged rows can be verified on a second run.
    return {table: query('SELECT count(*)::text || \':\' || md5(coalesce(string_agg(row_to_json(t)::text, \'\' ORDER BY row_to_json(t)::text), \'\')) FROM '+table+' t', pg_env) for table in TABLES}

def run_seed(connection, storage, seed_password):
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', DOTNET_ENVIRONMENT='Development', ConnectionStrings__LmsDatabase=connection, Database__SeedDemoData='false', Testing__SeedPassword=seed_password, Storage__RootPath=str(storage), Logging__LogLevel__Default='Warning')
    result = subprocess.run(['dotnet', str(ROOT/'backend/bin/Debug/net9.0/LawLms.Api.dll'), '--seed-test-data'], cwd=ROOT/'backend', env=env, text=True, capture_output=True, timeout=180)
    if result.returncode:
        # Configuration isn't logged; credentials are never printed.
        print(result.stdout[-4000:], file=sys.stderr)
        print(result.stderr[-4000:], file=sys.stderr)
        raise SystemExit('Seed failed; transaction was rolled back.')
    marker = 'TEST_SEED_REPORT='
    line = next((line[len(marker):] for line in result.stdout.splitlines() if line.startswith(marker)), None)
    if line is None:
        raise SystemExit('Seeder returned no report.')
    return json.loads(line)

def check_integrity(pg_env):
    checks = {
        'ready roster': "SELECT count(*) FROM teaching_classes t WHERE t.status=1 AND NOT EXISTS (SELECT 1 FROM offering_enrollments e WHERE e.teaching_class_id=t.id AND e.status=0)",
        'three distinct lecturers': "SELECT count(*) FROM teaching_classes WHERE primary_lecturer_id=assistant_lecturer_one_id OR primary_lecturer_id=assistant_lecturer_two_id OR assistant_lecturer_one_id=assistant_lecturer_two_id",
        'published flag': "SELECT count(*) FROM courses WHERE is_published<>(status=2)",
        'lesson scope': "SELECT count(*) FROM lesson_completions c JOIN offering_enrollments e ON e.id=c.enrollment_id JOIN teaching_classes t ON t.id=e.teaching_class_id JOIN lessons l ON l.id=c.lesson_id WHERE l.course_id<>t.course_id",
        'attempt membership': "SELECT count(*) FROM quiz_attempts a JOIN quizzes q ON q.id=a.quiz_id WHERE NOT EXISTS (SELECT 1 FROM offering_enrollments e WHERE e.teaching_class_id=q.teaching_class_id AND e.user_id=a.student_id AND e.status<>1)",
        'answer scope': "SELECT count(*) FROM quiz_answers a JOIN quiz_attempts x ON x.id=a.attempt_id JOIN quiz_questions q ON q.id=a.question_id WHERE x.quiz_id<>q.quiz_id OR a.score<0 OR a.score>q.points",
        'attempt state': "SELECT count(*) FROM quiz_attempts WHERE (status=0 AND (submitted_at IS NOT NULL OR final_score IS NOT NULL)) OR (status=1 AND (submitted_at IS NULL OR final_score IS NOT NULL)) OR (status=2 AND (submitted_at IS NULL OR final_score IS NULL)) OR expires_at<=started_at OR submitted_at>expires_at",
        'closed weights': "SELECT count(*) FROM teaching_classes t WHERE t.status>=3 AND (SELECT coalesce(sum(weight),0) FROM quizzes q WHERE q.teaching_class_id=t.id AND q.status=3)<>100",
        'grade lifecycle': "SELECT count(*) FROM teaching_classes WHERE (grade_status>=3 AND grades_published_at IS NULL) OR (status>=4 AND grade_status<>4)",
        'schedule conflicts': "SELECT count(*) FROM schedule_events a JOIN schedule_events b ON a.id<b.id AND a.starts_at<b.ends_at AND b.starts_at<a.ends_at JOIN teaching_classes x ON x.id=a.teaching_class_id JOIN teaching_classes y ON y.id=b.teaching_class_id WHERE NOT a.is_cancelled AND NOT b.is_cancelled AND (a.room=b.room OR x.code=y.code OR ARRAY[x.primary_lecturer_id,x.assistant_lecturer_one_id,x.assistant_lecturer_two_id] && ARRAY[y.primary_lecturer_id,y.assistant_lecturer_one_id,y.assistant_lecturer_two_id])",
        'attendance membership': "SELECT count(*) FROM attendance_records a JOIN schedule_events s ON s.id=a.schedule_event_id WHERE s.type<>0 OR s.is_cancelled OR NOT EXISTS (SELECT 1 FROM offering_enrollments e WHERE e.teaching_class_id=s.teaching_class_id AND e.user_id=a.student_id AND e.status<>1)",
        'appeal scope': "SELECT count(*) FROM grade_appeals g JOIN quiz_attempts a ON a.id=g.attempt_id JOIN quizzes q ON q.id=a.quiz_id WHERE a.student_id<>g.student_id OR q.teaching_class_id<>g.teaching_class_id OR a.status<>2",
        'independent reviewer': "SELECT count(*) FROM grade_appeals g JOIN audit_entries a ON a.entity_id=g.teaching_class_id AND a.action='attempt.grade' AND a.actor_id=g.reviewer_id WHERE a.detail LIKE '%' || g.attempt_id::text || '%'",
    }
    for name, statement in checks.items():
        value = query(statement, pg_env)
        if value != '0':
            raise AssertionError(f'{name}: {value} invalid rows')
    counts = {table: int(query('SELECT count(*) FROM '+table, pg_env)) for table in TABLES}
    if not all(counts.values()):
        raise AssertionError('Some feature tables have no test data: '+str(counts))
    if query('SELECT count(DISTINCT role) FROM users', pg_env) != '4':
        raise AssertionError('Missing account roles')
    return counts

def verify():
    schema = 'lms_seed_check_'+uuid.uuid4().hex[:12]
    pg_env = dict(database.env, PGOPTIONS='-c search_path='+schema)
    query('CREATE SCHEMA '+schema, database.env)
    try:
        for migration in sorted((ROOT/'backend/database').glob('[0-9]*.sql')):
            subprocess.run([str(database.bin_dir/'psql'), '-X','-v','ON_ERROR_STOP=1','-f',str(migration)], env=pg_env, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        with tempfile.TemporaryDirectory(prefix='lms-seed-check-') as storage:
            connection = database.config['ConnectionStrings']['LmsDatabase']+';Search Path='+schema
            first = run_seed(connection, storage, secrets.token_urlsafe(24))
            counts = check_integrity(pg_env)
            before = fingerprint(pg_env)
            # A changed password input on a repeat run must not reset existing account credentials.
            second = run_seed(connection, storage, secrets.token_urlsafe(24))
            if second['added'] or before != fingerprint(pg_env):
                raise AssertionError('Second seed modified the dataset')
            import zipfile
            for path in Path(storage).glob('test-seed-*.zip'):
                with zipfile.ZipFile(path) as archive:
                    if archive.testzip() is not None or not archive.namelist():
                        raise AssertionError('Invalid library fixture '+str(path))
            print(json.dumps({'verified':True,'schema':schema,'counts':counts,'idempotent':True,'filesValid':True},ensure_ascii=False,indent=2))
    finally:
        query('DROP SCHEMA '+schema+' CASCADE',database.env)

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--verify-only',action='store_true')
    args=parser.parse_args()
    if args.verify_only:
        verify()
    else:
        if database.lookup.get('host','localhost') not in ('localhost','127.0.0.1','::1'):
            raise SystemExit('Seed is restricted to local PostgreSQL.')
        subprocess.run([sys.executable,str(ROOT/'scripts/database.py'),'backup'],check=True)
        storage=Path(database.config.get('Storage',{}).get('RootPath','uploads'))
        if not storage.is_absolute(): storage=ROOT/'backend'/storage
        existing=existing_rows(database.env)
        report=run_seed(database.config['ConnectionStrings']['LmsDatabase'],storage,os.environ.get('Testing__SeedPassword','LmsTest@2026!'))
        after=existing_rows(database.env)
        for table, rows in existing.items():
            if not rows.issubset(after[table]):
                raise AssertionError('Existing data changed in '+table)
        report['existingRowsPreserved']=True
        output=ROOT/'docs/SEED_KIEM_THU_KET_QUA.json'
        output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
        print(json.dumps(report,ensure_ascii=False,indent=2))
        print('Saved report:',output)
