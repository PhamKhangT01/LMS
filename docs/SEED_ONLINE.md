# Nạp dữ liệu chạy thử trên Neon

Gói ngày 20/10/2026 được tạo từ schema local trống, không xuất dữ liệu thật của người dùng. Các tài khoản/lớp/điểm/lịch/bài học là dữ liệu kiểm thử có nhãn. Không thay thế dữ liệu đào tạo chính thức.

## Tệp riêng tư

Gói nằm trong `.backups/preview-export-401d4846/` (Git bỏ qua):

- `seed-all.sql`: SQL chạy một lần trong Neon SQL Editor. Có transaction và ON CONFLICT DO NOTHING; không reset mật khẩu hoặc ghi đè dữ liệu có sẵn.
- `tai-khoan.txt`: 39 tài khoản kiểm thử, mật khẩu ngẫu nhiên riêng. Tài khoản active được yêu cầu đổi mật khẩu lần đầu. Có một tài khoản khóa có chủ đích.
- `nguon-cau-hoi.json`: thông tin 8 câu, trang, đáp án, URL và SHA-256 PDF nguồn.
- `ket-qua.json`: số lượng và kết quả kiểm tra local. Chưa phải kết quả seed online.

## Thứ tự thực hiện

1. Neon: kiểm tra đúng project/database LMS đang dùng trên Render; tạo branch khôi phục hoặc backup trước khi nạp dữ liệu.
2. Chạy toàn bộ `seed-all.sql` trong Neon SQL Editor. Dừng nếu có lỗi; transaction phải rollback, không chạy tiếp từng đoạn để bỏ qua lỗi.
3. Push thay đổi Dockerfile và thư mục `deployment/preview-uploads` rồi redeploy Render để có đủ 4 ZIP học liệu. Không push `.backups`, mật khẩu hoặc SQL có password hash lên GitHub.
4. Đăng nhập bằng `seed.admin@lms.test`, `seed.truongkhoa@lms.test`, `seed.giangvien1@lms.test` (ngân hàng Pháp luật đại cương), hoặc `seed.hocvien01@lms.test`. Lấy mật khẩu trong tệp riêng, đổi khi được yêu cầu. Các tài khoản `bootstrap.*` cũ vẫn được giữ nguyên.
5. Kiểm tra lớp, lịch, diễn đàn, bảng điểm; ngân hàng thuộc học phần `[TEST] Pháp luật đại cương`. Chỉ giảng viên sở hữu mới sửa được câu hỏi. Hai đề nguồn là đề nháp, chưa công bố.

## Đưa tệp học liệu kiểm thử lên Render

Chạy từ thư mục dự án:

```bash
git add Dockerfile deployment/preview-uploads scripts/create_login_seed.py scripts/export_preview_seed.py docs/SEED_ONLINE.md
git diff --cached --stat
git commit -m "Add verified LMS preview seed and packaged library fixtures"
git push origin HEAD
```

Render cần deploy commit mới để có tệp. Nếu autodeploy tắt, chọn Manual Deploy → Deploy latest commit. Bốn ZIP chỉ là tài liệu kiểm thử có nhãn, không phải PDF đề gốc.

Để tạo lại gói: build backend rồi chạy `python3 scripts/export_preview_seed.py`; mỗi lần sinh mật khẩu mới cho gói mới, không dùng tệp mật khẩu mới để đăng nhập các tài khoản đã nạp trước đó.

## Nguồn câu hỏi

Đã tải PDF gốc và kiểm tra đáp án gạch chân bằng render ảnh trang. 8 câu là **biên soạn lại**, không phải bản chép nguyên văn hay tuyên bố rằng đề năm 2020 là quy định pháp luật hiện hành. Giữ nguyên xuất xứ trong nội dung câu hỏi. Trước khi dùng đánh giá chính thức, giảng viên cần rà soát lại phạm vi học phần và quy định hiện hành.

- Đại học Xây dựng Miền Tây: Pháp luật đại cương, HKII 2019–2020, thi 01/07/2020, mã 001, câu 1/2/3/5 (trang 1).
- Cùng kỳ thi, mã 002, câu 13 (trang 2), 32/34/38 (trang 4).
- Link đầy đủ và SHA-256 trong `nguon-cau-hoi.json`. Link PDF cũ của HCMUTE không truy cập được ở lần kiểm tra nên không được dùng trong gói này.

## Kiểm tra đã thực hiện

- 23 bảng có dữ liệu, kiểm tra quan hệ lớp, ba giảng viên, ghi danh, câu trả lời, trạng thái bài thi, trọng số, điểm, phúc khảo, lịch, điểm danh.
- Replay SQL vào schema thứ hai hai lần; số lượng và fingerprint toàn bộ dữ liệu không đổi.
- Không sửa schema public local; cả hai schema tạm được dọn sau khi xác minh.
- Chưa áp dụng hoặc xác minh gói này trên Neon online.

Render Free vẫn làm mất tệp upload mới khi restart; bốn tệp kiểm thử đóng gói theo image sẽ có lại sau redeploy. Cần kho lưu trữ bền vững cho học liệu thật.
