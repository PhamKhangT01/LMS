"""Export a verified, synthetic LMS preview dataset for Neon SQL Editor.

Only touches two randomly named local schemas, never local public data or Neon.
Real-exam adaptations are separate from synthetic quiz fixtures.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import tempfile
import unicodedata
import uuid

import database
import seed_test_data as seed
from create_login_seed import identity_hash

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOT = 'https://mtu.edu.vn/Resources/Docs/Khoa%20-%20Bo%20mon/Khoa%20LLCT/Thong%20bao/2020/dap-an/'
SOURCES = {
    '001': SOURCE_ROOT + 'THI%20PLDC%20TR%E1%BA%AEC%20NGHI%E1%BB%86M%207%20-2020%20%C4%90%E1%BB%80%20001.pdf',
    '002': SOURCE_ROOT + 'THI%20PL%C4%90C%20TR%E1%BA%AEC%20NGHI%E1%BB%86M%207%202-2-%20%C4%90%E1%BB%80%20002.pdf',
}
# Short adaptations, not verbatim copies. Answers visually checked against
# the underlined answer in the official PDF; options have been rewritten.
QUESTIONS = [
    ('001', 1, 1, 'Xác định số chương và số điều của bản Hiến pháp được thông qua năm 2013.', ['11 / 120', '10 / 120', '11 / 147', '10 / 147'], 0),
    ('001', 2, 1, 'Cặp thiết chế nào được hình thành qua việc người dân trực tiếp bỏ phiếu?', ['Quốc hội; Hội đồng nhân dân', 'Quốc hội; Chính phủ', 'Chính phủ; Chủ tịch nước', 'Ủy ban nhân dân; Chính phủ'], 0),
    ('001', 3, 1, 'Ai bầu chức danh Phó Chủ tịch nước theo bản Hiến pháp 2013?', ['Quốc hội', 'Chính phủ', 'Chủ tịch nước', 'Ủy ban thường vụ Quốc hội'], 0),
    ('001', 5, 1, 'Trong các loại văn bản sau, đâu là loại thuộc thẩm quyền ban hành của Quốc hội?', ['Luật', 'Thông tư', 'Nghị định', 'Chỉ thị của Bộ trưởng'], 0),
    ('002', 13, 2, 'Một nhiệm kỳ thông thường của Quốc hội kéo dài mấy năm theo Hiến pháp 2013?', ['5', '3', '4', '6'], 0),
    ('002', 32, 4, 'Chọn cơ quan đảm nhiệm đồng thời công tố và kiểm sát hoạt động tư pháp.', ['Viện kiểm sát nhân dân', 'Tòa án nhân dân', 'Chính phủ', 'Quốc hội'], 0),
    ('002', 34, 4, 'Hiến pháp 2013 xác định thiết chế nào giữ vị trí quyền lực nhà nước cao nhất?', ['Quốc hội', 'Chính phủ', 'Tòa án', 'Mặt trận Tổ quốc'], 0),
    ('002', 38, 4, 'Hoạt động xét xử thuộc chức năng của hệ thống cơ quan nào?', ['Tòa án nhân dân', 'Viện kiểm sát nhân dân', 'Chính phủ', 'Quốc hội'], 0),
]


def literal(value):
    return "'" + value.replace("'", "''") + "'"


def identifier(value):
    return '"' + value.replace('"', '""') + '"'


def stable_id(key):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, 'law-lms-preview-exams-v1:' + key))


def fingerprint(content, options, correct):
    payload = {'Type': 0, 'content': unicodedata.normalize('NFC', content.strip()), 'Options': options, 'CorrectAnswer': correct}
    raw = json.dumps(payload, ensure_ascii=True, separators=(',', ':'))
    raw = re.sub(r'\\u([0-9a-f]{4})', lambda m: '\\u' + m[1].upper(), raw)
    for char in "<>&'+":
        raw = raw.replace(char, '\\u%04X' % ord(char))
    return hashlib.sha256(raw.encode()).hexdigest().upper()


def insert_record(table, row, env):
    seed.query(f'INSERT INTO {identifier(table)} SELECT * FROM json_populate_record(NULL::{identifier(table)}, {literal(json.dumps(row, ensure_ascii=False))}::json)', env)


def main():
    if database.lookup.get('host', 'localhost') not in ('localhost', '127.0.0.1', '::1'):
        raise SystemExit('Only local PostgreSQL may generate preview exports.')
    out = ROOT / '.backups' / ('preview-export-' + uuid.uuid4().hex[:8])
    out.mkdir(parents=True, mode=0o700)
    schemas = ['lms_preview_' + uuid.uuid4().hex[:12] for _ in range(2)]
    created = []
    try:
        for schema in schemas:
            seed.query('CREATE SCHEMA ' + schema, database.env)
            created.append(schema)
            env = dict(database.env, PGOPTIONS='-c search_path=' + schema)
            for migration in sorted((ROOT / 'backend/database').glob('[0-9]*.sql')):
                subprocess.run([str(database.bin_dir / 'psql'), '-X', '-v', 'ON_ERROR_STOP=1', '-f', str(migration)], env=env, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        schema = schemas[0]
        env = dict(database.env, PGOPTIONS='-c search_path=' + schema)
        with tempfile.TemporaryDirectory(prefix='lms-preview-storage-') as storage:
            seed.run_seed(database.config['ConnectionStrings']['LmsDatabase'] + ';Search Path=' + schema, storage, secrets.token_urlsafe(30))
            # Generated QA bank questions are not part of this requested bank.
            seed.query('DELETE FROM bank_questions', env)
            users = json.loads(seed.query('SELECT json_agg(u ORDER BY email) FROM users u', env))
            credentials = ['Tài khoản KIỂM THỬ, không phải hồ sơ học viên thật.', 'Đổi mật khẩu ở lần đăng nhập đầu. Nếu đã seed cùng ID, mật khẩu cũ được giữ.', '']
            for user in users:
                password = secrets.token_urlsafe(20)
                seed.query('UPDATE users SET password_hash=' + literal(identity_hash(password)) + ',must_change_password=true WHERE id=' + literal(user['id']), env)
                credentials.append(f"{user['full_name']} | role={user['role']} | active={user['is_active']}\n{user['email']}\n{password}\n")
            template = json.loads(seed.query("SELECT row_to_json(q) FROM quizzes q JOIN courses c ON q.course_id=c.id WHERE c.code='TEST-PLDC' AND q.status=0 LIMIT 1", env))
            source_manifest = []
            for code in SOURCES:
                quiz = dict(template, id=stable_id('quiz:' + code), title='[THAM KHẢO] MTU 01/07/2020 · ' + code + ' · biên soạn lại', description='Bốn câu biên soạn lại từ đề công khai, không phải bản sao nguyên văn. Đáp án đối chiếu bản gốc năm 2020; giảng viên duyệt trước khi sử dụng. Nguồn: ' + SOURCES[code], status=0, weight=100, revision=1, attempts_allowed=1, multiple_choice_weight=100, essay_weight=0)
                insert_record('quizzes', quiz, env)
                selected = [q for q in QUESTIONS if q[0] == code]
                for position, (_, number, page, content, options, answer) in enumerate(selected, 1):
                    note = f'\n\nBiên soạn lại từ MTU, đề {code}, câu {number}, trang {page}, ngày 01/07/2020. Nguồn: {SOURCES[code]}'
                    text = content + note
                    correct = options[answer]
                    target = {("001", 1): 2, ("001", 2): 0, ("001", 3): 0, ("001", 5): 3, ("002", 13): 1, ("002", 32): 2, ("002", 34): 2, ("002", 38): 0}[(code, number)]
                    options = options[-target:] + options[:-target] if target else options
                    answer = target
                    question = dict(id=stable_id(f'question:{code}:{number}'), quiz_id=quiz['id'], content=text, type=0, options_json=json.dumps(options, ensure_ascii=False), correct_answer=correct, points=2.5, position=position)
                    insert_record('quiz_questions', question, env)
                    bank = dict(id=stable_id(f'bank:{code}:{number}'), course_id=quiz['course_id'], created_by_id=quiz['created_by_id'], content=text, type=0, options_json=question['options_json'], correct_answer=correct, points=2.5, topic=f'MTU 2020 · đề {code} · biên soạn lại', difficulty=1, source=2, fingerprint=fingerprint(text, options, correct), is_archived=False, revision=1, created_at=quiz['created_at'], updated_at=quiz['created_at'])
                    insert_record('bank_questions', bank, env)
                    source_manifest.append({'exam': code, 'question': number, 'page': page, 'url': SOURCES[code], 'mode': 'adapted', 'correct_option': answer, 'fingerprint': bank['fingerprint'], 'content': text, 'options': options, 'correct_answer': correct})
            counts = seed.check_integrity(env)
            # Export only isolated synthetic rows. pg_dump orders dependent tables.
            dumped = subprocess.run([str(database.bin_dir / 'pg_dump'), '--data-only', '--column-inserts', '--on-conflict-do-nothing', '--schema=' + schema], env=database.env, check=True, capture_output=True, text=True).stdout
            dumped = '\n'.join(line for line in dumped.splitlines() if not line.startswith('\\') and 'transaction_timeout' not in line)
            portable = dumped.replace(schema + '.', 'public.')
            sql = '-- Synthetic preview dataset + 8 source-linked exam adaptations.\nBEGIN;\n' + portable + '\nCOMMIT;\n'
            sql_path = out / 'seed-all.sql'
            sql_path.write_text(sql)
            # Replay twice into the second empty schema: SQL validity + idempotence.
            target_env = dict(database.env, PGOPTIONS='-c search_path=' + schemas[1])
            replay = sql.replace('public.', schemas[1] + '.')
            for _ in range(2):
                subprocess.run([str(database.bin_dir / 'psql'), '-X', '-v', 'ON_ERROR_STOP=1'], input=replay, env=target_env, check=True, text=True, capture_output=True)
                assert seed.check_integrity(target_env) == counts
            assert seed.fingerprint(env) == seed.fingerprint(target_env)
            (out / 'tai-khoan.txt').write_text('\n'.join(credentials))
            (out / 'nguon-cau-hoi.json').write_text(json.dumps(source_manifest, ensure_ascii=False, indent=2))
            uploads = out / 'uploads'
            shutil.copytree(storage, uploads)
            report = {'verified': True, 'replayTwice': True, 'counts': counts, 'realExamAdaptations': len(source_manifest), 'onlineApplied': False, 'libraryFiles': len(list(uploads.glob('*')))}
            (out / 'ket-qua.json').write_text(json.dumps(report, ensure_ascii=False, indent=2))
            for path in out.iterdir():
                if path.is_file(): path.chmod(0o600)
            print(json.dumps({'output': str(out), **report}, ensure_ascii=False, indent=2))
    finally:
        for schema in reversed(created):
            seed.query('DROP SCHEMA ' + schema + ' CASCADE', database.env)


if __name__ == '__main__':
    main()
