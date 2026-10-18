"""Read-only API smoke checks for the explicit law-lms-qa-v1 local dataset.
Uses seed.*@lms.test accounts only. Does not submit work or change marks.
"""
import datetime as dt
import io
import json
import os
from pathlib import Path
import urllib.error
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BASE = os.environ.get('LMS_SEED_API_URL', 'http://localhost:5155/api').rstrip('/')
PASSWORD = os.environ.get('Testing__SeedPassword', 'LmsTest@2026!')
checks = []

def request(path, token=None, payload=None, expected=200, raw=False):
    headers = {'Content-Type': 'application/json'}
    if token: headers['Authorization'] = 'Bearer '+token
    req = urllib.request.Request(BASE+path, headers=headers, data=None if payload is None else json.dumps(payload).encode(), method='GET' if payload is None else 'POST')
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            status, body = response.status, response.read()
    except urllib.error.HTTPError as error:
        status, body = error.code, error.read()
    if status != expected: raise AssertionError(f'{path}: expected {expected}, got {status}')
    return body if raw else json.loads(body) if body else None

def login(email, expected=200):
    return request('/auth/login', payload={'email':email, 'password':PASSWORD}, expected=expected)

def ok(name):
    checks.append(name)
    print('PASS',name,flush=True)

if __name__ == '__main__':
    admin = login('seed.admin@lms.test')
    head = login('seed.truongkhoa@lms.test')
    teacher = login('seed.giangvien3@lms.test')
    student = login('seed.hocvien01@lms.test')
    outsider = login('seed.ngoailop@lms.test')
    assert {admin['user']['role'],head['user']['role'],teacher['user']['role'],student['user']['role']} == {'Admin','DepartmentHead','Lecturer','Student'}
    login('seed.bikhoa@lms.test',403)
    first = login('seed.doimatkhau@lms.test')
    assert first['user']['mustChangePassword']
    request('/operations/offerings',first['token'],expected=403)
    ok('four roles, locked account and forced password-change gate')
    staff = request('/operations/offerings',head['token'])
    seeded = [x for x in staff if x['code'].startswith('TEST-K49A')]
    assert len(seeded)==8 and set(x['status'] for x in seeded)==set(range(6))
    own = request('/operations/offerings',student['token'])
    assert len(own)==3 and all(x['code']=='TEST-K49A1' for x in own)
    assert request('/operations/offerings',outsider['token'])==[]
    assigned = request('/operations/offerings',teacher['token'])
    assert len(assigned)==6
    unassigned = next(x for x in seeded if x['id'] not in {row['id'] for row in assigned})
    request('/operations/offerings/'+unassigned['id']+'/roster',teacher['token'],expected=403)
    ok('all offering states, learner roster and lecturer assignment boundaries')
    active = next(x for x in own if x['status']==2)
    published = next(x for x in own if x['gradeStatus']==3)
    pending = next(x for x in own if x['gradeStatus']==1)
    for offering in [active,pending]:
        grades = request('/operations/offerings/'+offering['id']+'/grades',student['token'])
        assert not grades['visible'] and grades['rows']==[]
    visible = request('/operations/offerings/'+published['id']+'/grades',student['token'])
    assert visible['visible'] and len(visible['rows'])==1 and visible['rows'][0]['studentId']==student['user']['id']
    request('/operations/offerings/'+published['id']+'/grades',outsider['token'],expected=403)
    paper = request('/operations/offerings/'+active['id']+'/grading',teacher['token'])
    assert any(x['status']==1 and x['finalScore'] is None for x in paper)
    appeals = request('/operations/offerings/'+published['id']+'/appeals',head['token'])
    assert len(appeals)==4 and {x['status'] for x in appeals}==set(range(4))
    ok('pending grading, hidden/published grades and all appeal states')
    report = request('/operations/reports?state=all',head['token'])
    totals = report['totals']
    assert totals['offerings']>=8 and totals['missingSubmissions']>0 and totals['pendingGrading']>0 and totals['pendingAppeals']>0
    assert 0 < totals['publishedPassed'] < totals['publishedEnrollments']
    request('/operations/reports?state=all',student['token'],expected=403)
    personal = request('/student/reports/learning',student['token'])
    assert len(personal['offerings'])==3 and any(x['attendance']['recordedSessions']>0 for x in personal['offerings'])
    assert any(x['gradesPublished'] and x['totalGrade'] is not None for x in personal['offerings'])
    ok('academic risk reports and personal progress/attendance/results')
    topics = request('/forum/topics?teachingClassId='+active['id'],student['token'])
    # API wraps paginated or context data depending on its response contract.
    topic_rows = topics if isinstance(topics,list) else topics.get('topics',topics.get('items',[]))
    assert len(topic_rows)>=2
    thread = request('/forum/topics/'+topic_rows[0]['id'],student['token'])
    assert len(thread['replies'])==3
    request('/forum/topics?teachingClassId='+active['id'],outsider['token'],expected=403)
    now = dt.datetime.now(dt.timezone.utc)
    params = urllib.parse.urlencode({'from':(now-dt.timedelta(days=7)).isoformat(),'to':(now+dt.timedelta(days=14)).isoformat()})
    events = request('/schedule?'+params,student['token'])
    assert events
    resources = [x for x in request('/library-resources',student['token']) if x['title'].startswith('[TEST]')]
    assert len(resources)==4
    for item in resources:
        payload = request('/library-resources/'+item['id']+'/download',student['token'],raw=True)
        with zipfile.ZipFile(io.BytesIO(payload)) as archive:
            assert archive.testzip() is None and archive.read('NOI-DUNG-KIEM-THU.md')
    notices = request('/operations/notifications',student['token'])
    assert notices
    ok('class forum, timetable, notifications and all four downloadable ZIP resources')
    export = request('/operations/offerings/'+published['id']+'/export',head['token'])
    assert export['attempts'] and export['audit'] and export['appeals']
    ok('offering archive export includes attempts, audit and appeals')
    result = {'passed':True,'checks':checks,'totals':totals}
    (ROOT/'docs/SEED_KIEM_THU_API.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print('API seed smoke checks passed.')
