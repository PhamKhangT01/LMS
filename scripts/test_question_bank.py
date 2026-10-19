"""Question-bank and quiz-shuffle integration tests in a disposable PostgreSQL schema."""
import concurrent.futures
import datetime as dt
import io
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zipfile
from xml.sax.saxutils import escape
import database
import seed_test_data as seed

ROOT=Path(__file__).resolve().parents[1]
PORT=int(os.environ.get('LMS_BANK_TEST_PORT','5169'))
BASE=f'http://127.0.0.1:{PORT}/api'
checks=[]

def req(path,token=None,method='GET',body=None,status=200):
    headers={'Content-Type':'application/json'}
    if token:headers['Authorization']='Bearer '+token
    r=urllib.request.Request(BASE+path,method=method,headers=headers,data=None if body is None else json.dumps(body).encode())
    try:
        with urllib.request.urlopen(r,timeout=20) as response:code,data=response.status,response.read()
    except urllib.error.HTTPError as e:code,data=e.code,e.read()
    if code!=status:raise AssertionError(f'{method} {path}: expected {status}, got {code}: {data[:700]!r}')
    return json.loads(data) if data else None

def upload(course,token,content,status=200):
    boundary='bank-'+uuid.uuid4().hex
    fields={'courseId':course,'topic':'DOCX kiểm thử','difficulty':'0'}
    parts=[]
    for key,value in fields.items():parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode())
    parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="questions.docx"\r\nContent-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document\r\n\r\n'.encode()+content+b'\r\n')
    parts.append(f'--{boundary}--\r\n'.encode())
    r=urllib.request.Request(BASE+'/question-bank/import-docx',data=b''.join(parts),headers={'Authorization':'Bearer '+token,'Content-Type':'multipart/form-data; boundary='+boundary},method='POST')
    try:
        with urllib.request.urlopen(r,timeout=20) as response:code,data=response.status,response.read()
    except urllib.error.HTTPError as e:code,data=e.code,e.read()
    if code!=status:raise AssertionError(f'DOCX import expected {status}, got {code}: {data[:700]!r}')
    return json.loads(data) if data else None

def ok(name):checks.append(name);print('PASS',name,flush=True)

def run(password,pg_env):
    def login(email):return req('/auth/login',method='POST',body={'email':email,'password':password})
    head=login('seed.truongkhoa@lms.test')['token'];teacher=login('seed.giangvien3@lms.test')['token'];other=login('seed.giangvien2@lms.test')['token'];helper=login('seed.giangvien1@lms.test')['token']
    students=[login(f'seed.hocvien{i:02}@lms.test')['token'] for i in range(1,5)]
    offerings=req('/operations/offerings',teacher);offering=next(x for x in offerings if x['code']=='TEST-K49A1' and x['status']==2);oid=offering['id'];cid=offering['courseId']
    base='/question-bank';query='?'+urllib.parse.urlencode({'courseId':cid})
    assert len(req(base+'/courses',teacher))==4
    req(base+'/courses',students[0],status=403);req(base+query,other,status=403)
    assert len(req(base+query,teacher)['items'])==5
    assert req(base+query+'&type=3&difficulty=1&topic='+urllib.parse.quote('Tình huống'),teacher)['total']==2
    ok('bank access follows assigned course; students and unassigned lecturers are denied')
    command={'courseId':cid,'content':'Phân tích tình huống ngân hàng mới','gradingGuide':'Nêu đủ ba bước và phân biệt giả định.','points':3.5,'topic':'Hợp đồng','difficulty':2}
    qid=req(base+'/essay',teacher,'POST',command)['id']
    req(base+'/essay',teacher,'POST',command,status=409);req(base+'/essay',head,'POST',command,status=403);req(base+'/essay',other,'POST',command,status=403)
    def current(id):return next(x for x in req(base+query+'&includeArchived=true',teacher)['items'] if x['id']==id)
    row=current(qid)
    edited={**row,'content':row['content']+' đã chỉnh sửa'}
    req(base+'/'+qid,helper,'PUT',edited,status=403)
    req(base+'/'+qid,teacher,'PUT',edited,status=204)
    req(base+'/'+qid,teacher,'PUT',edited,status=409)
    row=current(qid);assert row['revision']==2
    req(base+'/'+qid+'/archive',teacher,'PUT',{'archived':True,'revision':row['revision']},status=204)
    assert not any(x['id']==qid for x in req(base+query,teacher)['items'])
    req(base+'/draw',teacher,'POST',{'courseId':cid,'count':1,'difficulty':2},status=409)
    row=current(qid);req(base+'/'+qid+'/archive',teacher,'PUT',{'archived':False,'revision':row['revision']},status=204)
    row=current(qid)
    ok('essay creation, duplicate protection, ownership, revision conflict, archive and restore')
    lines=[]
    for i in range(6):lines.extend([f'Câu {i+1}: Câu trắc nghiệm DOCX độc lập số {i+1}?','A. Phân tích dữ kiện','B. Đối chiếu tài liệu','C. Nêu giả định','D. Trình bày lập luận','Đáp án: '+('A' if i%2==0 else 'C')])
    doc=io.BytesIO()
    with zipfile.ZipFile(doc,'w',zipfile.ZIP_DEFLATED) as z:z.writestr('word/document.xml','<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>'+''.join('<w:p><w:r><w:t>'+escape(line)+'</w:t></w:r></w:p>' for line in lines)+'</w:body></w:document>')
    payload=doc.getvalue()
    assert upload(cid,teacher,payload)=={'added':6,'skipped':0}
    assert upload(cid,teacher,payload)=={'added':0,'skipped':6}
    upload(cid,students[0],payload,403);upload(cid,other,payload,403);upload(cid,teacher,b'not a DOCX',400)
    invalid=io.BytesIO()
    with zipfile.ZipFile(invalid,'w') as z:z.writestr('word/document.xml','<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>'+''.join('<w:p><w:r><w:t>'+escape(line)+'</w:t></w:r></w:p>' for line in [*lines,'Câu 7: Câu thiếu đáp án','A. Một','B. Hai'])+'</w:body></w:document>')
    upload(cid,teacher,invalid.getvalue(),400)
    choices=req(base+query+'&type=0&difficulty=0&topic=DOCX',teacher)['items'];assert len(choices)==6
    drawn=req(base+'/draw',teacher,'POST',{'courseId':cid,'count':6,'type':0,'topic':'DOCX','difficulty':0})
    assert len({x['id'] for x in drawn})==6
    matching=req(base+'/draw',teacher,'POST',{'courseId':cid,'count':1,'query':'độc lập số 3','type':0,'topic':'DOCX','difficulty':0})
    assert len(matching)==1 and 'độc lập số 3' in matching[0]['content']
    req(base+'/draw',teacher,'POST',{'courseId':cid,'count':1,'query':'khong-co-tu-khoa-nay'},status=409)
    req(base+'/draw',teacher,'POST',{'courseId':cid,'count':1,'type':0,'topic':'DOCX','excludeIds':[x['id'] for x in drawn]},status=409)
    source=next(x for x in req('/operations/offerings/'+oid+'/quizzes',teacher) if x['status']==1)
    assert req(base+'/from-quiz/'+source['id'],teacher,'POST',{'topic':'','difficulty':1})['skipped']==2
    ok('DOCX-only objective creation, atomic duplicate import, filtered random draw and reuse from existing quizzes')
    now=dt.datetime.now(dt.timezone.utc)
    questions=[{k:x[k] for k in ['content','type','options','correctAnswer','points']} for x in drawn]
    setup={'teachingClassId':oid,'title':'Ngân hàng và trộn đề tích hợp','description':'Schema kiểm thử riêng','durationMinutes':45,'attemptsAllowed':2,'passingScore':3,'opensAt':(now-dt.timedelta(hours=1)).isoformat(),'closesAt':(now+dt.timedelta(days=1)).isoformat(),'weight':100,'multipleChoiceWeight':100,'essayWeight':0,'shuffleQuestions':True,'shuffleOptions':True,'questions':questions}
    quiz_id=req('/operations/quizzes',teacher,'POST',setup)['id']
    draft=req('/operations/quizzes/'+quiz_id+'/draft',teacher);assert draft['setup']['shuffleQuestions'] and draft['setup']['shuffleOptions']
    # Bank content may change after selection, but the quiz keeps its own immutable copy.
    original=drawn[0];changed={**original,'content':'Nội dung ngân hàng đã đổi','correctAnswer':next(x for x in original['options'] if x!=original['correctAnswer'])}
    req(base+'/'+original['id'],teacher,'PUT',changed,status=204)
    draft_again=req('/operations/quizzes/'+quiz_id+'/draft',teacher)
    assert draft_again['setup']['questions']==draft['setup']['questions']
    settings={**draft_again['setup'],'shuffleOptions':False}
    req('/operations/quizzes/'+quiz_id,teacher,'PUT',{'setup':settings,'revision':draft_again['revision']},status=204)
    draft_again=req('/operations/quizzes/'+quiz_id+'/draft',teacher);assert not draft_again['setup']['shuffleOptions']
    req('/operations/quizzes/'+quiz_id,teacher,'PUT',{'setup':setup,'revision':draft_again['revision']},status=204)
    def quiz():return next(x for x in req('/operations/offerings/'+oid+'/quizzes',teacher) if x['id']==quiz_id)
    for action,token in [('submit',teacher),('publish',head)]:req('/operations/quizzes/'+quiz_id+'/review',token,'POST',{'action':action,'reason':'Kiểm thử trộn đề','revision':quiz()['revision']},status=204)
    assert quiz()['shuffleQuestions'] and quiz()['shuffleOptions']
    ok('quiz snapshots survive bank edits; shuffle settings persist through draft revision and review')
    with concurrent.futures.ThreadPoolExecutor(2) as pool:concurrent_starts=list(pool.map(lambda _:req('/operations/quizzes/'+quiz_id+'/start',students[0],'POST',{}),range(2)))
    attempt=concurrent_starts[0];assert concurrent_starts[0]==concurrent_starts[1] or concurrent_starts[0]['id']==concurrent_starts[1]['id']
    def signature(a):return [(q['id'],q['options']) for q in a['questions']]
    assert signature(concurrent_starts[0])==signature(concurrent_starts[1])
    assert not any('correctAnswer' in q or 'gradingGuide' in q for q in attempt['questions'])
    reference={x['content']:x for x in questions}
    answers=[{'questionId':q['id'],'answer':reference[q['content']]['correctAnswer']} for q in attempt['questions']]
    saved=req('/operations/attempts/'+attempt['id']+'/draft',students[0],'PUT',{'revision':attempt['revision'],'answers':answers})
    resumed=req('/operations/quizzes/'+quiz_id+'/start',students[0],'POST',{})
    assert signature(attempt)==signature(saved)==signature(resumed) and len(resumed['answers'])==6
    submitted=req('/operations/attempts/'+attempt['id']+'/submit',students[0],'POST',{'revision':saved['revision'],'answers':answers})
    assert signature(submitted)==signature(attempt) and submitted['status']==2
    assert seed.query("SELECT final_score FROM quiz_attempts WHERE id='"+attempt['id']+"'",pg_env)=='6.00'
    papers=req('/operations/offerings/'+oid+'/grading',teacher);paper=next(x for x in papers if x['id']==attempt['id'])
    assert [x['questionId'] for x in paper['answers']]==[q['id'] for q in attempt['questions']]
    forms=[signature(attempt)]
    for token in students[1:]:forms.append(signature(req('/operations/quizzes/'+quiz_id+'/start',token,'POST',{})))
    assert len({json.dumps(x) for x in forms})>1
    req('/operations/quizzes/'+quiz_id,teacher,'PUT',{'setup':setup,'revision':quiz()['revision']},status=409)
    export=req('/operations/offerings/'+oid+'/export',head);exported=next(x for x in export['attempts'] if x['id']==attempt['id'])
    assert exported['questionOrderJson'] and exported['optionOrderJson']
    ok('concurrent starts, resume/draft/submit retain order; independent forms score against original answer text')
    expiring=req('/operations/quizzes/'+quiz_id+'/start',students[1],'POST',{})
    seed.query("UPDATE quiz_attempts SET expires_at=now()-interval '1 second' WHERE id='"+expiring['id']+"'",pg_env)
    expired=req('/operations/quizzes/'+quiz_id+'/start',students[1],'POST',{})
    assert expired['status']==2 and signature(expired)==signature(expiring)
    legacy=req('/operations/quizzes/'+source['id']+'/start',students[1],'POST',{})
    assert [q['position'] for q in legacy['questions']]==list(range(1,len(legacy['questions'])+1))
    ok('automatic expiry preserves presentation; legacy attempts remain compatible')

if __name__=='__main__':
    schema='lms_bank_'+uuid.uuid4().hex[:12]
    pg_env=dict(database.env,PGOPTIONS='-c search_path='+schema)
    seed.query('CREATE SCHEMA '+schema,database.env)
    process=None
    try:
        for migration in sorted((ROOT/'backend/database').glob('[0-9]*.sql')):subprocess.run([str(database.bin_dir/'psql'),'-X','-v','ON_ERROR_STOP=1','-f',str(migration)],env=pg_env,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        with tempfile.TemporaryDirectory(prefix='lms-bank-') as storage:
            password=secrets.token_urlsafe(24);connection=database.config['ConnectionStrings']['LmsDatabase']+';Search Path='+schema
            seed.run_seed(connection,storage,password)
            api_env=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',ConnectionStrings__LmsDatabase=connection,Jwt__Key=secrets.token_urlsafe(48),Storage__RootPath=storage,Database__SeedDemoData='false',Logging__LogLevel__Default='Warning')
            with open('/private/tmp/lms-question-bank-api.log','w') as log:
                process=subprocess.Popen(['dotnet',str(ROOT/'backend/bin/Debug/net9.0/LawLms.Api.dll'),'--urls',f'http://127.0.0.1:{PORT}'],cwd=ROOT/'backend',env=api_env,stdout=log,stderr=subprocess.STDOUT)
                for _ in range(100):
                    try:
                        urllib.request.urlopen(f'http://127.0.0.1:{PORT}/health/live',timeout=1).close();break
                    except (OSError,urllib.error.URLError):time.sleep(.1)
                else:raise RuntimeError('API did not start; see /private/tmp/lms-question-bank-api.log')
                run(password,pg_env)
            result={'passed':True,'checks':checks,'schema':schema,'publicDatabaseUntouched':True}
            (ROOT/'docs/NGAN_HANG_CAU_HOI_KIEM_THU.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
            print('All question-bank/shuffle integration checks passed.')
    finally:
        if process:
            process.terminate()
            try:process.wait(timeout=10)
            except subprocess.TimeoutExpired:process.kill();process.wait()
        seed.query('DROP SCHEMA '+schema+' CASCADE',database.env)
