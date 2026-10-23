"""Document-import API checks; executed inside test_question_bank's disposable schema."""
import io
import json
import urllib.request
import urllib.error
import uuid
import zipfile
from xml.sax.saxutils import escape
import seed_test_data as seed


def docx(lines, unsafe=False):
    body=''.join('<w:p><w:r><w:t>'+escape(line)+'</w:t></w:r></w:p>' for line in lines)
    xml=('<?xml version="1.0"?>'+ ('<!DOCTYPE x [<!ENTITY a SYSTEM "file:///etc/passwd">]>' if unsafe else '') + '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>'+body+'</w:body></w:document>')
    out=io.BytesIO()
    with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:z.writestr('word/document.xml',xml)
    return out.getvalue()


def pdf(lines):
    content='BT /F1 12 Tf 50 750 Td '+' '.join(('0 -20 Td ' if i else '')+'('+line.replace('\\','\\\\').replace('(','\\(').replace(')','\\)')+') Tj' for i,line in enumerate(lines))+' ET'
    objects=[b'<< /Type /Catalog /Pages 2 0 R >>', b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>', ('<< /Length '+str(len(content.encode()))+' >>\nstream\n'+content+'\nendstream').encode()]
    data=b'%PDF-1.4\n';offsets=[0]
    for i,obj in enumerate(objects,1):offsets.append(len(data));data+=str(i).encode()+b' 0 obj\n'+obj+b'\nendobj\n'
    start=len(data);data+=b'xref\n0 6\n0000000000 65535 f \n'+b''.join(f'{n:010} 00000 n \n'.encode() for n in offsets[1:]);data+=f'trailer << /Size 6 /Root 1 0 R >>\nstartxref\n{start}\n%%EOF'.encode()
    return data


def run_document_checks(password,pg_env):
    import test_question_bank as bank
    req,ok=bank.req,bank.ok
    teacher=req('/auth/login',method='POST',body={'email':'seed.giangvien3@lms.test','password':password})['token']
    student=req('/auth/login',method='POST',body={'email':'seed.hocvien01@lms.test','password':password})['token']
    other=req('/auth/login',method='POST',body={'email':'seed.giangvien2@lms.test','password':password})['token']
    cid=next(c['id'] for c in req('/question-bank/courses',teacher) if c['code']=='TEST-DS')
    def upload(name,data,token=teacher,status=200):
        boundary='doc-'+uuid.uuid4().hex
        body=(f'--{boundary}\r\nContent-Disposition: form-data; name="courseId"\r\n\r\n{cid}\r\n--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode()+data+f'\r\n--{boundary}--\r\n'.encode())
        request=urllib.request.Request(bank.BASE+'/question-bank/import-preview',data=body,headers={'Authorization':'Bearer '+token,'Content-Type':'multipart/form-data; boundary='+boundary})
        try:
            with urllib.request.urlopen(request,timeout=30) as r:code,payload=r.status,r.read()
        except urllib.error.HTTPError as e:code,payload=e.code,e.read()
        assert code==status,(code,payload[:500]);return json.loads(payload) if payload else None
    data=docx(['Câu 1: Nội dung xem trước mới', 'A. Một B. Hai', 'Đáp án: B', 'Câu 2: Câu thiếu đáp án', 'A. Ba','B. Bốn','Câu 3: Phân tích tình huống mở'])
    before=seed.query('SELECT count(*) FROM bank_questions',pg_env)
    p=upload('questions.docx',data);assert len(p['questions'])==3
    assert p['questions'][0]['correctAnswer']=='Hai' and not p['questions'][1]['correctAnswer'] and p['questions'][2]['type']==3
    assert seed.query('SELECT count(*) FROM bank_questions',pg_env)==before
    upload('questions.docx',data,student,403);upload('questions.docx',data,other,403)
    upload('bad.docx',b'not-a-docx-file-but-long-enough',status=400)
    upload('bad.exe',data,status=400);upload('entity.docx',docx(['Câu 1: Dữ liệu'],True),status=400)
    ok('DOCX preview retains incomplete rows without writing; file validation and role/course access enforced')
    def command(preview):return {'courseId':cid,'fileName':preview['fileName'],'format':preview['format'],'questions':[{k:q[k] for k in ['content','type','options','correctAnswer','page','number']}|{'points':1} for q in preview['questions']]}
    cmd=command(p);checks=req('/question-bank/import-check',teacher,'POST',cmd);assert checks[1]['errors'] and not checks[0]['errors']
    req('/question-bank/import-confirm',teacher,'POST',cmd,status=409)
    assert seed.query('SELECT count(*) FROM bank_questions',pg_env)==before
    cmd['questions']=cmd['questions'][:1]
    first=req('/question-bank/import-confirm',teacher,'POST',cmd);assert first=={'added':1,'skipped':0}
    checks=req('/question-bank/import-check',teacher,'POST',cmd);assert checks[0]['duplicateId']
    assert req('/question-bank/import-confirm',teacher,'POST',cmd)=={'added':0,'skipped':1}
    cmd['questions'][0]['content']+=' đã sửa';cmd['questions']*=2
    checks=req('/question-bank/import-check',teacher,'POST',cmd);assert not checks[0]['duplicateId'] and checks[1]['duplicateInFile']
    assert req('/question-bank/import-confirm',teacher,'POST',cmd)=={'added':1,'skipped':1}
    req('/question-bank/import-confirm',student,'POST',cmd,status=403)
    assert seed.query("SELECT count(*) FROM audit_entries WHERE action='bank.document' AND detail::json->>'fileName'='questions.docx'",pg_env)=='2'
    ok('review validates selected rows atomically, detects existing/in-file duplicates, rechecks at save and retains file/page audit')
    p=upload('questions.pdf',pdf(['Question 1: PDF preview question','A. First choice','B. Second choice']))
    assert len(p['questions'])==1 and not p['questions'][0]['correctAnswer']
    cmd=command(p);cmd['questions'][0]['correctAnswer']=cmd['questions'][0]['options'][1]
    assert req('/question-bank/import-confirm',teacher,'POST',cmd)['added']==1
    assert seed.query('SELECT count(*) FROM bank_questions WHERE source=3',pg_env)=='1'
    scan=upload('scan.pdf',pdf([]),status=400);assert 'OCR' in scan['detail']
    upload('broken.pdf',b'%PDF-1.4\ninvalid content here',status=400)
    ok('PDF text extraction imports reviewed answers with PDF source; scan and corrupt files give clear errors')
