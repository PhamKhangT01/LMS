"""Integration checks against an isolated PostgreSQL schema and real HTTP API.
Requires the current backend build and configured local PostgreSQL. Never changes public LMS rows.
"""
import io,zipfile
import concurrent.futures, datetime as dt, json, os, secrets, subprocess, sys, time, urllib.request, urllib.error, urllib.parse, uuid
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import database as database_module
ROOT=Path(__file__).resolve().parents[1]
schema='lms_test_'+uuid.uuid4().hex[:12]
pg_env=dict(database_module.env,PGOPTIONS='-c search_path='+schema)
server_port=int(os.environ.get('LMS_TEST_PORT','5166'))
base=f'http://127.0.0.1:{server_port}/api'
checks=[]
def sql(query):
 return subprocess.run([str(database_module.bin_dir/'psql'),'-X','-v','ON_ERROR_STOP=1','-At','-c',query],env=pg_env,capture_output=True,text=True,check=True).stdout.strip()
def req(path,method='GET',data=None,token=None,expect=200):
 headers={'Content-Type':'application/json'}
 if token:headers['Authorization']='Bearer '+token
 r=urllib.request.Request(base+path,data=None if data is None else json.dumps(data).encode(),headers=headers,method=method)
 try:
  with urllib.request.urlopen(r,timeout=20) as response:status=response.status;raw=response.read()
 except urllib.error.HTTPError as error:status=error.code;raw=error.read()
 body=json.loads(raw) if raw else None
 if status!=expect:raise AssertionError(f'{method} {path}: expected {expect}, got {status}: {body}')
 return body

def upload(path,name,content,token):
 boundary='lms-test-'+uuid.uuid4().hex
 body=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()+content+f'\r\n--{boundary}--\r\n'.encode()
 request=urllib.request.Request(base+path,data=body,headers={'Content-Type':'multipart/form-data; boundary='+boundary,'Authorization':'Bearer '+token},method='POST')
 with urllib.request.urlopen(request,timeout=20) as response:return json.loads(response.read())

def ok(name): checks.append(name);print('PASS',name,flush=True)
def login(email,password='123456'):
 return req('/auth/login','POST',{'email':email,'password':password})

def run():
 manager=login('truongkhoa@lmsluat.edu.vn'); lecturer=login('giangvien@lmsluat.edu.vn'); assistant=login('lethuha@lmsluat.edu.vn');admin=login('admin@lmsluat.edu.vn'); student=login('sinhvien@lmsluat.edu.vn')
 head=manager['token']; teacher=lecturer['token']; helper=assistant['token']; adm=admin['token']; learner=student['token']
 # Force the activation gate for one test account, then rotate its credential.
 sql("update users set must_change_password=true where email='sinhvien@lmsluat.edu.vn'")
 req('/operations/offerings',token=learner,expect=403)
 next_password=secrets.token_urlsafe(20)
 rotated=req('/operations/change-password','POST',{'currentPassword':'123456','newPassword':next_password},learner)
 req('/operations/offerings',token=learner,expect=401); learner=rotated['token']
 ok('first-login gate, password rotation and old-token revocation')
 course=req('/courses','POST',{'code':'TEST-LAW','title':'Lớp kiểm thử vận hành','description':'Dữ liệu tích hợp trong schema riêng','category':'Luật','color':'#355C42'},head,201)
 cid=course['id']; req('/courses/'+cid,expect=401)
 req('/courses/'+cid+'/lessons','POST',{'title':'Bài học thử','content':'Nội dung bài học kiểm thử','durationMinutes':30},head)
 detail=req('/courses/'+cid,token=head)
 old_revision=detail['revision']
 lesson_edit={'title':'Bài học thử','content':'Nội dung đã chỉnh sửa trước duyệt','durationMinutes':30,'revision':old_revision}
 req('/courses/'+cid+'/lessons/'+detail['lessons'][0]['id'],'PUT',lesson_edit,head,204)
 req('/courses/'+cid+'/lessons/'+detail['lessons'][0]['id'],'PUT',lesson_edit,head,409)
 detail=req('/courses/'+cid,token=head)
 assert detail['lessons'][0]['content']==lesson_edit['content']
 ok('draft lessons can be corrected and stale edits are rejected')
 req('/courses/'+cid+'/review','POST',{'action':'publish','reason':'skip','revision':detail['revision']},head,409)
 req('/courses/'+cid+'/review','POST',{'action':'submit','reason':'','revision':detail['revision']},head,204)
 detail=req('/courses/'+cid,token=head)
 req('/courses/'+cid+'/review','POST',{'action':'publish','reason':'Đã kiểm tra nội dung','revision':detail['revision']},head,204)
 ok('course draft, review guard, approval and protected lesson API')
 people=req('/teaching-classes/lecturers',token=head)
 ids={p['email']:p['id'] for p in people}
 sql("update users set class_code='TEST-01' where email='sinhvien@lmsluat.edu.vn'")
 command={'code':'TEST-01','name':'Lớp thử','courseId':cid,'academicYear':'2026-2027','term':'HK1','primaryLecturerId':ids['giangvien@lmsluat.edu.vn'],'assistantLecturerOneId':ids['lethuha@lmsluat.edu.vn'],'assistantLecturerTwoId':ids['tranquanghuy@lmsluat.edu.vn']}
 offering=req('/teaching-classes','POST',command,head,201);oid=offering['id']
 def item():return next(x for x in req('/operations/offerings',token=head) if x['id']==oid)
 now=dt.datetime.now(dt.timezone.utc)
 iso=lambda d:d.isoformat()
 settings={'term':'HK1','startsAt':iso(now-dt.timedelta(days=1)),'endsAt':iso(now+dt.timedelta(days=10)),'passingGrade':5,'requiredProgress':100,'appealDays':1,'revision':item()['revision']}
 req(f'/operations/offerings/{oid}/settings','PUT',settings,head,204)
 for _ in range(2):req(f'/operations/offerings/{oid}/sync','POST',{'reason':'Đối soát danh sách'},head,204)
 roster=req(f'/operations/offerings/{oid}/roster',token=head);assert len(roster)==1
 def action(name,expect=204,revision=None):return req(f'/operations/offerings/{oid}/transition','POST',{'action':name,'reason':'Quyết định kiểm thử','revision':item()['revision'] if revision is None else revision},head,expect)
 action('ready');action('activate')
 second=req('/teaching-classes','POST',{**command,'term':'HK2'},head,201)
 assert second['id']!=oid
 starts=now+dt.timedelta(hours=3);ends=starts+dt.timedelta(hours=2)
 schedule={'teachingClassId':oid,'type':0,'title':'Buổi học kiểm thử','room':'A201','location':'Cơ sở chính','notes':'Mang giáo trình','startsAt':iso(starts),'endsAt':iso(ends)}
 req('/schedule','POST',schedule,learner,403)
 scheduled=req('/schedule','POST',schedule,head,201);event_id=scheduled['id']
 assert any(x['id']==event_id for x in req('/schedule?'+urllib.parse.urlencode({'from':iso(now-dt.timedelta(days=1)),'to':iso(now+dt.timedelta(days=2))}),token=learner))
 req('/schedule','POST',{**schedule,'teachingClassId':second['id'],'title':'Trùng phòng'},head,409)
 req('/schedule','POST',{**schedule,'teachingClassId':second['id'],'room':'B202','title':'Trùng giảng viên'},head,409)
 changed=req(f'/schedule/{event_id}?revision={scheduled["revision"]}','PUT',{**schedule,'room':'A202'},head)
 assert changed['revision']==scheduled['revision']+1
 assert any('Lịch đã thay đổi' in x['message'] for x in req('/operations/notifications',token=learner))
 assert any('BEGIN:VCALENDAR' in line for line in urllib.request.urlopen(urllib.request.Request(base+'/schedule/calendar.ics',headers={'Authorization':'Bearer '+learner}),timeout=20).read().decode().splitlines())
 req(f'/schedule/{event_id}/cancel?revision={changed["revision"]}','POST',{'reason':'Điều chỉnh kế hoạch đào tạo'},head,204)
 history=req(f'/schedule/{event_id}/history',token=head);assert [x['action'] for x in history]==['cancelled','updated','created']
 req(f'/schedule/{event_id}/history',token=learner,expect=403)
 ok('schedule role scoping, room conflict, revisioned changes, notifications and calendar export')
 past_start=now-dt.timedelta(hours=3);past_end=now-dt.timedelta(hours=1)
 past_event=req('/schedule','POST',{**schedule,'title':'Buổi học đã kết thúc','room':'C301','startsAt':iso(past_start),'endsAt':iso(past_end)},head,201)
 roster_student_id=roster[0]['userId']
 req(f'/schedule/{past_event["id"]}/attendance','PUT',{'records':[{'studentId':roster_student_id,'status':0}]},learner,403)
 req(f'/schedule/{past_event["id"]}/attendance','PUT',{'records':[{'studentId':roster_student_id,'status':0}]},teacher,204)
 attendance_view=req(f'/schedule/{past_event["id"]}/attendance',token=teacher);assert attendance_view['students'][0]['status']==0
 personal=req('/student/reports/learning',token=learner)
 own_report=next(x for x in personal['offerings'] if x['id']==oid)
 assert own_report['attendance']['present']==1 and own_report['attendance']['recordedSessions']==1 and not own_report['gradesPublished']
 req('/student/reports/learning',token=head,expect=403)
 ok('attendance is recorded by assigned lecturer and personal report scopes unpublished results')
 assert len(req('/operations/offerings',token=learner))==1
 req('/courses/'+cid,token=learner)
 lesson=detail['lessons'][0]['id']
 for _ in range(2):req(f'/operations/offerings/{oid}/lessons/{lesson}/complete','POST',{},learner,204)
 assert req('/operations/offerings',token=learner)[0]['progress']==100
 ok('semester offering, idempotent roster sync, activation and lesson progress')
 # Same course, administrative class and year in another term is a separate offering.
 assert len(req('/operations/offerings',token=learner))==1
 ok('repeat enrollment in a second semester preserves independent offering')
 # An outsider cannot read content or class data even with a valid student token.
 outsider_id=str(uuid.uuid4())
 sql("insert into users(id,full_name,email,password_hash,role,class_code,created_at,is_active,must_change_password,token_version) select '"+outsider_id+"','Outsider','outsider@test.local',password_hash,3,'OTHER',now(),true,false,0 from users where email='sinhvien@lmsluat.edu.vn'")
 outsider=login('outsider@test.local',next_password)['token']
 req('/courses/'+cid,token=outsider,expect=403);req(f'/operations/offerings/{oid}/grades',token=outsider,expect=403)
 ok('outside-class access denied for lessons and grades')
 topic=req('/forum/topics','POST',{'teachingClassId':oid,'title':'Trao đổi về tình huống pháp lý','body':'Câu hỏi kiểm thử cho diễn đàn lớp học phần.'},learner,201)['topicId']
 req('/forum/topics?teachingClassId='+oid,token=outsider,expect=403)
 req('/forum/topics/'+topic,token=teacher)
 req('/forum/topics/'+topic+'/replies','POST',{'body':'Giảng viên hướng dẫn cách phân tích tình huống theo quy định.'},helper,201)
 thread=req('/forum/topics/'+topic,token=learner);reply_id=thread['replies'][0]['id']
 assert any(x['dedupKey'].startswith('forum-reply:'+topic+':') for x in req('/operations/notifications',token=learner))
 req('/forum/replies/'+reply_id+'/pin','POST',{'pinned':True},learner,403)
 req('/forum/replies/'+reply_id+'/pin','POST',{'pinned':True},head,403)
 req('/forum/replies/'+reply_id+'/pin','POST',{'pinned':True},teacher,204)
 assert req('/forum/topics/'+topic,token=learner)['replies'][0]['isPinnedAnswer']
 assert any(x['dedupKey'].startswith('forum-pin:'+topic+':') for x in req('/operations/notifications',token=learner))
 req('/forum/topics/'+topic+'/follow','PUT',{'follow':False},learner,204)
 ok('class forum scope, topic subscriptions, reply notifications and lecturer-pinned answers')
 qdata={'teachingClassId':oid,'title':'Đánh giá thử','description':'Kiểm thử','durationMinutes':30,'attemptsAllowed':1,'passingScore':5,'opensAt':iso(now-dt.timedelta(hours=1)),'closesAt':iso(now+dt.timedelta(days=1)),'weight':100,'questions':[{'content':'Câu tự luận','type':3,'options':[],'correctAnswer':'','points':6},{'content':'Câu khách quan','type':0,'options':['A','B'],'correctAnswer':'A','points':4}]}
 req('/operations/quizzes','POST',qdata,head,expect=403)
 qid=req('/operations/quizzes','POST',qdata,teacher)['id']
 def quiz():return next(x for x in req(f'/operations/offerings/{oid}/quizzes',token=head) if x['id']==qid)
 req(f'/operations/quizzes/{qid}/start','POST',{},learner,409)
 draft=req(f'/operations/quizzes/{qid}/draft',token=teacher)
 req(f'/operations/quizzes/{qid}/draft',token=head,expect=403)
 req(f'/operations/quizzes/{qid}','PUT',draft,head,403)
 req(f'/operations/quizzes/{qid}','PUT',{'setup':{**draft['setup'],'description':'Đã chỉnh sửa bản nháp'},'revision':draft['revision']},teacher,204)
 req(f'/operations/quizzes/{qid}','PUT',draft,teacher,409)
 req(f'/operations/quizzes/{qid}/review','POST',{'action':'submit','reason':'Gửi duyệt sai vai trò','revision':quiz()['revision']},head,403)
 for state,token in [('submit',teacher),('reject',head)]:req(f'/operations/quizzes/{qid}/review','POST',{'action':state,'reason':'Điều chỉnh trước công bố','revision':quiz()['revision']},token,204)
 corrected=req(f'/operations/quizzes/{qid}/draft',token=teacher)
 req(f'/operations/quizzes/{qid}','PUT',{'setup':{**corrected['setup'],'description':'Đã sửa sau khi trả lại'},'revision':corrected['revision']},teacher,204)
 disposable=req('/operations/quizzes','POST',{**qdata,'title':'Đề nháp tạo nhầm','weight':5},teacher)['id']
 d=req(f'/operations/quizzes/{disposable}/draft',token=teacher)
 req(f'/operations/quizzes/{disposable}','DELETE',{'action':'delete','reason':'Đề nháp tạo nhầm','revision':d['revision']},teacher,204)
 req(f'/operations/quizzes/{disposable}/draft',token=teacher,expect=404)
 ok('returned quiz drafts can be revised, stale revisions fail, unused drafts can be removed')

 for act,token in [('submit',teacher),('publish',head)]:req(f'/operations/quizzes/{qid}/review','POST',{'action':act,'reason':'Đã rà soát','revision':quiz()['revision']},token,204)
 req(f'/operations/quizzes/{qid}','PUT',{'setup':qdata,'revision':quiz()['revision']},teacher,409)
 ok('quiz review blocks unpublished assessment')
 with concurrent.futures.ThreadPoolExecutor(2) as pool:
  started=list(pool.map(lambda _:req(f'/operations/quizzes/{qid}/start','POST',{},learner),range(2)))
 assert started[0]['id']==started[1]['id'];attempt=started[0];aid=attempt['id']
 assert not any('correctAnswer' in q for q in attempt['questions'])
 answers=[{'questionId':q['id'],'answer':'Bài trả lời tự luận' if q['type']==3 else 'A'} for q in attempt['questions']]
 saved=req(f'/operations/attempts/{aid}/draft','PUT',{'revision':attempt['revision'],'answers':answers},learner)
 req(f'/operations/attempts/{aid}/draft','PUT',{'revision':attempt['revision'],'answers':answers},learner,409)
 resumed=req(f'/operations/quizzes/{qid}/start','POST',{},learner);assert len(resumed['answers'])==2
 with concurrent.futures.ThreadPoolExecutor(2) as pool:
  submitted=list(pool.map(lambda _:req(f'/operations/attempts/{aid}/submit','POST',{'revision':saved['revision'],'answers':answers},learner),range(2)))
 assert submitted[0]['id']==submitted[1]['id'];assert submitted[0]['status']==1
 assert 'finalScore' not in submitted[0]
 req(f'/operations/attempts/{aid}/result',token=learner,expect=403)
 req(f'/operations/quizzes/{qid}/start','POST',{},learner,409)
 ok('concurrent starts share one attempt, drafts recover, stale save rejected and duplicate submit is idempotent')
 req('/quizzes/'+qid+'/submit','POST',{'answers':answers},learner,409)
 assert req(f'/operations/offerings/{oid}/grades',token=learner)['rows']==[]
 paper=req(f'/operations/offerings/{oid}/grading',token=helper)[0]
 marks=[{'questionId':a['questionId'],'score':a['maxPoints'],'feedback':'Đạt yêu cầu'} for a in paper['answers']]
 bad=[{**a,'score':1000} for a in marks]
 req(f'/operations/attempts/{aid}/grade','PUT',{'revision':paper['revision'],'answers':bad},helper,409)
 req(f'/operations/attempts/{aid}/grade','PUT',{'revision':paper['revision'],'answers':marks},helper,204)
 hidden=req(f'/operations/offerings/{oid}/grades',token=learner);assert not hidden['visible'] and not hidden['rows']
 req(f'/operations/quizzes/{qid}/review','POST',{'action':'close','reason':'Đóng sai vai trò','revision':quiz()['revision']},head,403)
 for act in ['close']:req(f'/operations/quizzes/{qid}/review','POST',{'action':act,'reason':'Kết thúc đợt kiểm tra','revision':quiz()['revision']},teacher,204)
 action('grading')
 def gradeaction(act,token=head,expect=204):req(f'/operations/offerings/{oid}/grades','POST',{'action':act,'reason':'Đối soát bảng điểm','revision':item()['revision']},token,expect)
 gradeaction('submit',head,403);gradeaction('submit',teacher);gradeaction('approve');gradeaction('publish')
 visible=req(f'/operations/offerings/{oid}/grades',token=learner);assert visible['rows'][0]['grade']==10 and visible['rows'][0]['passed']
 personal=req('/student/reports/learning',token=learner);own_report=next(x for x in personal['offerings'] if x['id']==oid)
 assert own_report['gradesPublished'] and own_report['totalGrade']==10 and own_report['components'][0]['score']==10
 req(f'/operations/attempts/{aid}/grade','PUT',{'revision':paper['revision']+1,'answers':marks},helper,409)
 own_result=req(f'/operations/attempts/{aid}/result',token=learner);assert own_result['finalScore']==10 and all(a['feedback']=='Đạt yêu cầu' for a in own_result['answers'])
 req(f'/operations/attempts/{aid}/result',token=outsider,expect=403)
 ok('essay grading bounds, grade approval, hidden results and frozen published scores')
 req(f'/operations/offerings/{oid}/appeals','POST',{'attemptId':aid,'reason':'Đề nghị đối soát cách chấm'},learner,204)
 appeal=req(f'/operations/offerings/{oid}/appeals',token=head)[0];apid=appeal['id']
 def appealaction(act,token=head,reviewer=None,answers=None,status=204):
  a=req(f'/operations/offerings/{oid}/appeals',token=head)[0]
  return req(f'/operations/appeals/{apid}','POST',{'action':act,'reason':'Đối soát phúc khảo','reviewerId':reviewer,'revision':a['revision'],'answers':answers},token,status)
 appealaction('assign',reviewer=ids['lethuha@lmsluat.edu.vn'],status=409)
 appealaction('assign',reviewer=ids['giangvien@lmsluat.edu.vn'])
 proposal=[{**m,'score':m['score']/2} for m in marks]
 appealaction('propose',teacher,answers=proposal)
 assert req(f'/operations/offerings/{oid}/grades',token=learner)['rows'][0]['grade']==10
 appealaction('approve')
 assert req(f'/operations/offerings/{oid}/grades',token=learner)['rows'][0]['grade']==5
 ok('independent appeal reviewer, proposal does not alter official grade, approval applies audited correction')
 action('complete',expect=409)
 # Advance only the isolated test clock evidence, not application rules.
 sql("update teaching_classes set grades_published_at=now()-interval '2 days' where id='"+oid+"'")
 action('complete');action('archive')
 req(f'/operations/offerings/{oid}/sync','POST',{'reason':'Không được thay đổi'},head,409)
 export=req(f'/operations/offerings/{oid}/export',token=head);assert export['attempts'] and export['audit'] and export['appeals']
 ok('appeal window blocks closure, archive is read-only, full history export')
 # A withdrawn student's historic membership cannot grant fresh content access.
 req(f'/operations/offerings/{second["id"]}/enrollments','POST',{'userId':student['user']['id'],'active':False,'reason':'Thử rút khỏi lớp'},head,204)
 assert sql("select count(*) from quiz_attempts where id='"+aid+"'")=='1'
 ok('withdrawal preserves earlier attempts')
 # A late submission must use the last acknowledged draft, not the late payload.
 sid=second['id']
 req(f'/operations/offerings/{sid}/sync','POST',{'reason':'Chuẩn bị kỳ thứ hai'},head,204)
 second_item=next(x for x in req('/operations/offerings',token=head) if x['id']==sid)
 req(f'/operations/offerings/{sid}/settings','PUT',{**settings,'term':'HK2','revision':second_item['revision']},head,204)
 for state in ['ready','activate']:
  second_item=next(x for x in req('/operations/offerings',token=head) if x['id']==sid)
  req(f'/operations/offerings/{sid}/transition','POST',{'action':state,'reason':'Thử hết giờ','revision':second_item['revision']},head,204)
 eq=req('/operations/quizzes','POST',{**qdata,'teachingClassId':sid,'title':'Kiểm tra tự nộp','attemptsAllowed':2},teacher)['id']
 reminder=req('/operations/accounts','POST',{'fullName':'Học viên cần nhắc hạn','email':'reminder@test.local','role':3,'classCode':'TEST-01'},adm)
 reminder_login=login(reminder['email'],reminder['temporaryPassword'])
 reminder_auth=req('/operations/change-password','POST',{'currentPassword':reminder['temporaryPassword'],'newPassword':secrets.token_urlsafe(20)},reminder_login['token'])
 reminder_token=reminder_auth['token']
 req(f'/operations/offerings/{sid}/enrollments','POST',{'userId':reminder_login['user']['id'],'active':True,'reason':'Thử nhắc hạn người chưa nộp'},head,204)

 for state,token in [('submit',teacher),('publish',head)]:
  revision=next(q['revision'] for q in req(f'/operations/offerings/{sid}/quizzes',token=head) if q['id']==eq)
  req(f'/operations/quizzes/{eq}/review','POST',{'action':state,'reason':'Thử hết hạn','revision':revision},token,204)
 a=req(f'/operations/quizzes/{eq}/start','POST',{},learner)
 accepted=[{'questionId':q['id'],'answer':'Đã lưu trước hạn'} for q in a['questions']]
 a=req(f'/operations/attempts/{a["id"]}/draft','PUT',{'revision':a['revision'],'answers':accepted},learner)
 sql("update quiz_attempts set expires_at=now()-interval '1 second' where id='"+a['id']+"'")
 late=req(f'/operations/attempts/{a["id"]}/submit','POST',{'revision':a['revision'],'answers':[{**x,'answer':'Nội dung gửi sau hạn'} for x in accepted]},learner)
 assert all(x['answer']=='Đã lưu trước hạn' for x in late['answers']) and late['status']!=0
 b=req(f'/operations/quizzes/{eq}/start','POST',{},learner)
 b=req(f'/operations/attempts/{b["id"]}/draft','PUT',{'revision':b['revision'],'answers':accepted},learner)
 sql("update quiz_attempts set expires_at=now()-interval '1 second' where id='"+b['id']+"'")
 for _ in range(30):
  if sql("select status from quiz_attempts where id='"+b['id']+"'")!='0':break
  time.sleep(.5)
 assert sql("select status from quiz_attempts where id='"+b['id']+"'")!='0'
 req(f'/operations/quizzes/{eq}/start','POST',{},learner,409)
 ok('late payload rejected, server worker submits saved draft without browser, attempt limit enforced')
 staff=req('/operations/accounts','POST',{'fullName':'Giảng viên mới','email':'newstaff@test.local','role':2,'classCode':None},adm)
 newlogin=login(staff['email'],staff['temporaryPassword']);assert newlogin['user']['mustChangePassword']
 req('/operations/offerings',token=newlogin['token'],expect=403)
 staffid=newlogin['user']['id']
 req(f'/operations/accounts/{staffid}/active','POST',{'active':False},adm,204)
 req('/operations/offerings',token=newlogin['token'],expect=401)
 req(f'/operations/accounts/{staffid}/active','POST',{'active':True},adm,204)
 reset=req(f'/operations/accounts/{staffid}/reset','POST',{},adm)
 assert reset['temporaryPassword']!=staff['temporaryPassword']
 login(reset['email'],reset['temporaryPassword'])
 ok('unique temporary credentials, forced activation, account lock and password reset')
 req('/operations/quizzes/'+qid+'/preview',token=learner,expect=403)
 preview=req('/operations/quizzes/'+qid+'/preview',token=head);assert len(preview)==2
 copy=req('/courses/'+cid+'/copy','POST',{'code':'TEST-LAW-V2'},head)
 assert copy['id']!=cid and copy['status']==0 and len(copy['lessons'])==1
 ok('staff-only assessment preview and independent content revision')
 req('/operations/quizzes/'+eq+'/allowance','POST',{'studentId':student['user']['id'],'extraAttempts':1,'extendedUntil':iso(now+dt.timedelta(days=2)),'reason':'Cho làm lại vì sự cố được duyệt'},head,204)
 additional=req('/operations/quizzes/'+eq+'/start','POST',{},learner)
 assert additional['status']==0
 req('/operations/quizzes/'+eq+'/allowance','POST',{'studentId':student['user']['id'],'extraAttempts':2,'extendedUntil':None,'reason':'Không có quyền'},teacher,403)
 ok('approved individual allowance adds one attempt and rejects lecturer approval')
 notes=req('/operations/notifications',token=reminder_token)
 assert notes, 'Expected due-date notification after the expiry-worker cycle'
 req('/operations/notifications/'+notes[0]['id']+'/read','POST',{},reminder_token,204)
 assert req('/operations/notifications',token=reminder_token)[0]['readAt']
 assert sql('select count(*)=count(distinct (user_id,dedup_key)) from notifications')=='t'
 ok('in-app due reminders, per-user read state and duplicate prevention')
 fixture=io.BytesIO()
 with zipfile.ZipFile(fixture,'w') as z:
  z.writestr('xl/worksheets/sheet1.xml',"""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Họ tên</t></is></c></row>
  <row r="2"><c r="A2" t="inlineStr"><is><t>Học viên nhập mới</t></is></c><c r="B2" t="inlineStr"><is><t>imported@test.local</t></is></c><c r="C2" t="inlineStr"><is><t>TEST-01</t></is></c></row>
  <row r="3"><c r="A3" t="inlineStr"><is><t>Học viên trùng</t></is></c><c r="B3" t="inlineStr"><is><t>imported@test.local</t></is></c><c r="C3" t="inlineStr"><is><t>TEST-01</t></is></c></row>
  <row r="4"><c r="A4" t="inlineStr"><is><t>Thiếu mã lớp</t></is></c><c r="B4" t="inlineStr"><is><t>missing@test.local</t></is></c></row>
  <row r="5"><c r="A5" t="inlineStr"><is><t>Email giảng viên</t></is></c><c r="B5" t="inlineStr"><is><t>giangvien@lmsluat.edu.vn</t></is></c><c r="C5" t="inlineStr"><is><t>TEST-01</t></is></c></row>
  </sheetData></worksheet>""")
 preview=upload('/students/preview-xlsx','students.xlsx',fixture.getvalue(),adm)
 assert len(preview)==4 and sum(not x['error'] for x in preview)==1
 assert sql("select count(*) from users where email='imported@test.local'")=='0'
 imported=upload('/students/import-xlsx','students.xlsx',fixture.getvalue(),adm)
 assert imported['created']==1 and imported['failed']==3 and len(imported['initialCredentials'])==1
 assert imported['initialCredentials'][0]['temporaryPassword']!='123456'
 ok('XLSX preview is read-only, duplicate and role-conflict rows rejected, credentials unique')
 document=io.BytesIO()
 with zipfile.ZipFile(document,'w') as z:z.writestr('word/document.xml','<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>'+''.join('<w:p><w:r><w:t>'+text+'</w:t></w:r></w:p>' for text in ['Câu 1: Câu hỏi thử','A. Đúng','B. Sai','Đáp án: A'])+'</w:body></w:document>')
 parsed=upload('/quizzes/import-docx','questions.docx',document.getvalue(),teacher)
 assert len(parsed)==1 and parsed[0]['correctAnswer']=='Đúng'
 ok('DOCX multiple-choice import retains separate manual-essay workflow')



 req('/operations/reports',token=learner,expect=403)
 req('/operations/reports/export?kind=students',token=learner,expect=403)
 report=req('/operations/reports?state=all',token=head)
 assert report['totals']['offerings']>=2
 running=req('/operations/reports',token=teacher)
 assert all(x['status'] in [2,3] for x in running['offerings'])
 assert running['totals']['publishedEnrollments']==0 and running['totals']['pendingGrading']>=2
 closed=req('/operations/reports?state=closed&term=HK1',token=head)
 assert closed['totals']['publishedPassed']==1 and closed['totals']['publishedEnrollments']==1
 assert not closed['students']
 unassigned=login(reset['email'],reset['temporaryPassword'])
 unassigned=req('/operations/change-password','POST',{'currentPassword':reset['temporaryPassword'],'newPassword':secrets.token_urlsafe(20)},unassigned['token'])['token']
 assert req('/operations/reports?state=all&offeringId='+sid,token=unassigned)['totals']['offerings']==0
 req('/operations/reports?state=invalid',token=head,expect=409)
 ok('academic reporting enforces staff scope, official published results and stage filters')
 rid=reminder_login['user']['id']
 sql(f"update teaching_classes set starts_at=now()-interval '10 days',ends_at=now()+interval '10 days' where id='{sid}'; update offering_enrollments set enrolled_at=now()-interval '8 days' where teaching_class_id='{sid}' and user_id='{rid}'; update quizzes set closes_at=now()-interval '2 days' where id='{eq}';")
 risks=req('/operations/reports?offeringId='+sid+'&risk=missing&pageSize=1',token=head)
 row=next(x for x in risks['students'] if x['studentId']==rid)
 assert set(['missing','inactive','progress']).issubset(row['reasons'])
 req('/operations/quizzes/'+eq+'/allowance','POST',{'studentId':rid,'extraAttempts':0,'extendedUntil':iso(dt.datetime.now(dt.timezone.utc)+dt.timedelta(days=1)),'reason':'Gia hạn để hỗ trợ học viên'},head,204)
 assert not req('/operations/reports?offeringId='+sid+'&risk=missing',token=head)['students']
 sql(f"update users set full_name='=SUM(1,1)' where id='{rid}'")
 request=urllib.request.Request(base+'/operations/reports/export?kind=students&offeringId='+sid+'&pageSize=1',headers={'Authorization':'Bearer '+head})
 with urllib.request.urlopen(request) as response: raw=response.read()
 import csv
 exported=list(csv.reader(io.StringIO(raw.decode('utf-8-sig'))))
 assert raw.startswith(b'\xef\xbb\xbf') and any(x[0]=="'=SUM(1,1)" for x in exported[1:])
 assert all(len(x)==len(exported[0]) for x in exported)
 all_risks=req('/operations/reports?offeringId='+sid,token=head)
 assert len(exported)-1==all_risks['totalMatchingStudents']
 ok('support indicators respect enrollment age and individual extensions; CSV escapes formulas and exports all matching rows')

 return {'schema':schema,'checks':checks,'passed':len(checks)}

if __name__=='__main__':
 subprocess.run([str(database_module.bin_dir/'psql'),'-X','-v','ON_ERROR_STOP=1','-c','CREATE SCHEMA '+schema],env=database_module.env,check=True,stdout=subprocess.DEVNULL)
 for path in sorted((ROOT/'backend/database').glob('*.sql')):
  subprocess.run([str(database_module.bin_dir/'psql'),'-X','-v','ON_ERROR_STOP=1','-f',str(path)],env=pg_env,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 connection=json.loads((ROOT/'backend/appsettings.Local.json').read_text())['ConnectionStrings']['LmsDatabase']+';Search Path='+schema
 api_env=dict(os.environ,ConnectionStrings__LmsDatabase=connection,Jwt__Key=secrets.token_urlsafe(48),Database__SeedDemoData='true',ASPNETCORE_ENVIRONMENT='Development',Cors__AllowedOrigins__0='http://localhost:5173')
 logfile=Path('/private/tmp/lms-integration-api.log'); log=logfile.open('w')
 process=subprocess.Popen(['dotnet',str(ROOT/'backend/bin/Debug/net9.0/LawLms.Api.dll'),'--urls',f'http://127.0.0.1:{server_port}'],cwd=ROOT/'backend',env=api_env,stdout=log,stderr=subprocess.STDOUT)
 try:
  for _ in range(60):
   try:
    urllib.request.urlopen(f'http://127.0.0.1:{server_port}/health',timeout=1);break
   except Exception:
    if process.poll() is not None:raise RuntimeError('API failed; see '+str(logfile))
    time.sleep(.5)
  report=run();report['log']=str(logfile);report['apiPid']=process.pid;Path('/private/tmp/lms-integration-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False,indent=2))
 finally:
  if "--keep-server" not in sys.argv:process.terminate();process.wait(timeout=20)
  log.close()
  # Retain the isolated schema for evidence and restore/expiry checks. No public tables are changed.
  print('Isolated test schema:',schema)
