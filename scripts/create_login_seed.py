#!/usr/bin/env python3
"""Generate SQL and private credentials for four initial preview accounts.

Does not connect to a database. Existing email addresses are never overwritten.
Outputs stay in the git-ignored .backups directory.
"""
import base64
import hashlib
import os
from pathlib import Path
import secrets
import struct
import uuid


def identity_hash(password):
    # ASP.NET Identity V3: marker, PRF (HMACSHA512), iterations, salt size,
    # followed by salt and PBKDF2 subkey. Integers use network byte order.
    salt = secrets.token_bytes(16)
    subkey = hashlib.pbkdf2_hmac('sha512', password.encode(), salt, 100_000, 32)
    return base64.b64encode(b'\x01' + struct.pack('!III', 2, 100_000, 16) + salt + subkey).decode()


def main():
    root = Path(__file__).resolve().parents[1]
    out = root / '.backups' / ('login-seed-' + uuid.uuid4().hex[:8])
    out.mkdir(parents=True, mode=0o700)
    rows = []
    credentials = ['Tài khoản chạy thử — đổi mật khẩu ngay lần đăng nhập đầu.',
                   'Nếu email đã tồn tại, SQL giữ nguyên tài khoản và mật khẩu cũ.', '']
    accounts = [('admin', 'Quản trị chạy thử', 0),
                ('truongkhoa', 'Trưởng khoa chạy thử', 1),
                ('giangvien', 'Giảng viên chạy thử', 2),
                ('hocvien', 'Học viên chạy thử', 3)]
    for name, full_name, role in accounts:
        email = f'bootstrap.{name}@lms.test'
        password = secrets.token_urlsafe(20)
        rows.append(f"('{uuid.uuid4()}', '{full_name}', '{email}', '{identity_hash(password)}', {role}, now(), true, true, 0)")
        credentials.append(f'{full_name}\nEmail: {email}\nMật khẩu tạm: {password}\n')
    sql = ('-- Run in Neon SQL Editor on the LMS preview database.\n'
           '-- Inserts only; never resets passwords or elevates existing users.\n'
           'BEGIN;\n'
           'INSERT INTO users (id, full_name, email, password_hash, role, created_at, is_active, must_change_password, token_version)\nVALUES\n'
           + ',\n'.join(rows) + '\nON CONFLICT (email) DO NOTHING\nRETURNING email, role;\nCOMMIT;\n')
    for filename, content in [('seed-login.sql', sql), ('tai-khoan.txt', '\n'.join(credentials))]:
        path = out / filename
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, 'w') as stream:
            stream.write(content)
        print(path)


if __name__ == '__main__':
    main()
