# Bộ dữ liệu kiểm thử LMS

Bộ `law-lms-qa-v1` được nạp chủ động vào PostgreSQL local. Đây là dữ liệu giả lập lưu trong database, không phải dữ liệu dự phòng trên giao diện. Học phần có tiền tố `[TEST]`, mã học phần `TEST-*`, lớp `TEST-K49A*`, tài khoản `seed.*@lms.test`.

## Tài khoản

Mật khẩu ban đầu mặc định cho **các tài khoản seed mới**: `LmsTest@2026!`. Có thể đặt `Testing__SeedPassword` khi chạy lần đầu để dùng mật khẩu khác. Seed lại không đặt lại mật khẩu, điểm, trạng thái hoặc các bản ghi đã tồn tại. Tài khoản cũ của hệ thống không bị thay đổi.

| Vai trò / tình huống | Email |
| --- | --- |
| Quản trị viên | `seed.admin@lms.test` |
| Trưởng khoa | `seed.truongkhoa@lms.test` |
| Giảng viên chính lớp Dân sự đang học | `seed.giangvien3@lms.test` |
| Các giảng viên còn lại | `seed.giangvien1@lms.test`, `seed.giangvien2@lms.test`, `seed.giangvien4@lms.test` |
| Học viên TEST-K49A1 | `seed.hocvien01@lms.test` đến `seed.hocvien10@lms.test` |
| Học viên TEST-K49A2 | `seed.hocvien11@lms.test` đến `seed.hocvien20@lms.test` |
| Học viên TEST-K49A3 | `seed.hocvien21@lms.test` đến `seed.hocvien30@lms.test` |
| Bắt buộc đổi mật khẩu lần đầu | `seed.doimatkhau@lms.test` |
| Tài khoản bị khóa | `seed.bikhoa@lms.test` |
| Học viên ngoài lớp, dùng thử chặn truy cập | `seed.ngoailop@lms.test` |

Tổng cộng 39 tài khoản seed: 1 Admin, 1 Trưởng khoa, 4 giảng viên, 30 học viên trong ba lớp và 3 học viên kiểm tra tình huống tài khoản/phân quyền. Học viên số 10 đã rút khỏi riêng lớp Dân sự; vẫn giữ ghi danh ở các học phần khác của TEST-K49A1.

## Nội dung và các trạng thái

Có 8 học phần, mỗi học phần 6 bài học: Pháp luật đại cương, Hiến pháp, Dân sự, Hình sự, Hành chính, Tố tụng hình sự, Tố tụng dân sự và Pháp luật quốc tế. Sáu học phần đã công bố; Tố tụng dân sự ở bản nháp và Pháp luật quốc tế đang chờ duyệt.

| Lớp | Học phần | Kỳ | Trạng thái / việc có thể kiểm thử |
| --- | --- | --- | --- |
| TEST-K49A3 | Pháp luật đại cương | HK1 2026–2027 | Nháp, chưa cấu hình lịch và chưa đồng bộ danh sách |
| TEST-K49A3 | Hiến pháp | HK1 2026–2027 | Sẵn sàng mở, có danh sách và đề trắc nghiệm chờ duyệt |
| TEST-K49A1 | Dân sự | HK1 2026–2027 | Đang học, có bài tự luận đang mở, trắc nghiệm đã đóng, tiến độ khác nhau, bài thiếu/chờ chấm và gia hạn |
| TEST-K49A2 | Hình sự | HK1 2026–2027 | Đang học, đề kết hợp trắc nghiệm/tự luận, bài đang làm và đã nộp |
| TEST-K49A1 | Hành chính | HK1 2026–2027 | Đã chấm đủ, bảng điểm chờ Trưởng khoa duyệt |
| TEST-K49A1 | Pháp luật đại cương | HK1 2026–2027 | Điểm đã công bố, bốn yêu cầu phúc khảo ở các trạng thái khác nhau |
| TEST-K49A2 | Hiến pháp | HK2 2025–2026 | Đã hoàn thành, bảng điểm khóa |
| TEST-K49A3 | Tố tụng hình sự | HK2 2025–2026 | Đã lưu trữ, có thể xem và xuất hồ sơ |

Mỗi lớp có đúng 1 giảng viên chính và 2 giảng viên phụ khác nhau. Các lớp đang mở có lịch và đề còn hiệu lực tương đối với thời điểm seed lần đầu. Mốc thời gian này được giữ khi chạy lại, không tự dịch chuyển thời hạn hoặc khôi phục bài làm đã thay đổi trong lúc kiểm thử.

Dữ liệu bao phủ toàn bộ 22 bảng nghiệp vụ: tài khoản, học phần, bài học, ghi danh học phần, lớp học phần, danh sách lớp, hoàn thành bài học, đề/câu hỏi, bài làm/câu trả lời, gia hạn, phúc khảo, lịch/lịch sử thay đổi, điểm danh, chủ đề/phản hồi/theo dõi diễn đàn, học liệu, thông báo và nhật ký.

Có 4 học liệu ZIP tải được, mỗi danh mục một tệp: Bài giảng, Văn bản pháp luật, Đề cương, Tài liệu tham khảo. Bên trong là Markdown có nội dung kiểm thử; không gắn tên văn bản pháp luật chính thức cho nội dung giả lập.

## Nạp và kiểm tra

Từ thư mục gốc dự án:

```bash
dotnet build LMS.sln --no-restore
python3 scripts/seed_test_data.py --verify-only
python3 scripts/seed_test_data.py
python3 scripts/check_seed_data.py
```

- `--verify-only` tạo schema tạm, áp dụng các SQL hiện có, seed hai lần, kiểm tra quan hệ/trạng thái/lịch, so sánh toàn bộ nội dung trước và sau lần hai, kiểm tra ZIP rồi xóa schema tạm.
- Lệnh nạp thật tạo bản sao lưu database và học liệu trong `.backups/`, chỉ thêm bản ghi thiếu và xác minh các hàng cũ giữ nguyên. Thực hiện trong transaction; có lỗi thì rollback và xóa các tệp vừa tạo.
- Lệnh seed C# chỉ cho chạy trong `Development` với PostgreSQL ở localhost. Không bật seed toàn bộ trong quá trình khởi động API thông thường; `DbSeeder` cũ vẫn chỉ phục vụ tài khoản tối thiểu.
- `check_seed_data.py` cần API local ở cổng 5155; có thể đặt `LMS_SEED_API_URL`. Kiểm tra đọc dữ liệu và phân quyền, không nộp bài, sửa điểm hoặc giải quyết phúc khảo. Nếu đã đổi mật khẩu của tài khoản seed, đặt `Testing__SeedPassword` tương ứng hoặc dùng tài khoản chưa đổi.

Kết quả nạp và kiểm tra API được lưu tại `docs/SEED_KIEM_THU_KET_QUA.json` và `docs/SEED_KIEM_THU_API.json`. Không lưu mật khẩu trong hai báo cáo này.

## Gợi ý kiểm thử thủ công

1. Đăng nhập `seed.hocvien01@lms.test`: mở Dân sự, đọc bài, xem lịch và diễn đàn, làm đề tự luận; xem điểm Pháp luật đại cương đã công bố. Điểm Dân sự/Hành chính chưa được phép xem.
2. Đăng nhập `seed.giangvien3@lms.test`: chấm bài tự luận Dân sự đang chờ, xem bài đang làm và trường hợp được gia hạn. Thử truy cập lớp không được phân công để xác minh từ chối.
3. Đăng nhập Trưởng khoa: duyệt bảng điểm Hành chính, xem đề chờ duyệt của lớp Hiến pháp, phân người chấm lại và duyệt đề xuất phúc khảo ở Pháp luật đại cương.
4. Đăng nhập Admin: lọc tài khoản seed, thử khóa/mở khóa, cấp lại mật khẩu và đối soát danh sách lớp. Các mẫu XLSX/DOCX của hệ thống vẫn sử dụng theo luồng import hiện có.
5. Mở báo cáo học vụ với bộ lọc tất cả kỳ: có học viên chậm tiến độ, thiếu bài, bài chờ chấm, phúc khảo tồn, kết quả đạt và chưa đạt. Lớp đã lưu trữ có hồ sơ JSON kèm bài làm, điểm, phúc khảo và nhật ký.

## Kết quả đã kiểm tra

- Đã nạp bộ seed vào database local ngày 18/10/2026 và xác minh các hàng có trước khi seed giữ nguyên.
- Kiểm tra trong schema tạm: đủ 22 bảng nghiệp vụ, quan hệ và trạng thái hợp lệ, lịch không trùng, ZIP đọc được; seed hai lần không tạo thêm hoặc sửa bản ghi.
- Build backend thành công; 31 kiểm thử nghiệp vụ đạt, gồm hồi quy lỗi truy vấn DbContext đồng thời khi kho học liệu có nhiều tài liệu.
- Sáu nhóm kiểm tra API thật đạt: vai trò/tài khoản, phạm vi lớp, chấm điểm/phúc khảo, báo cáo, diễn đàn/lịch/thông báo/học liệu và xuất hồ sơ.
