# LMS Khoa Luật — Học viện An ninh nhân dân

Hệ thống LMS gồm React/Vite, ASP.NET Core 9 và PostgreSQL. Backend được tổ chức theo **Clean Architecture** để tách biệt nghiệp vụ, hạ tầng và HTTP API.

## Cấu trúc backend

```text
backend/
├── src/LawLms.Domain/          # Entity và luật nghiệp vụ cốt lõi
├── src/LawLms.Application/     # Use case, DTO, interface repository/service
├── src/LawLms.Infrastructure/  # EF Core, PostgreSQL, repository, JWT/password
├── Controllers/                # HTTP adapter mỏng
├── Middleware/                 # Xử lý lỗi dùng chung
└── Program.cs                  # Composition root / DI / pipeline
```

## Chuẩn bị môi trường

Không lưu mật khẩu PostgreSQL hoặc JWT secret trong Git. Cấu hình local bằng user secrets:

```bash
cd backend
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:LmsDatabase" "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=YOUR_PASSWORD"
dotnet user-secrets set "Jwt:Key" "a-long-random-secret-with-at-least-32-characters"
```

Xem biến môi trường cho server tại [backend/.env.example](backend/.env.example). Ở production, inject các giá trị này qua secret manager của nền tảng triển khai.

## Database schema và migration

Không dùng `EnsureCreated` trong runtime. Baseline schema được version tại [backend/database/0001_initial.sql](backend/database/0001_initial.sql).

Với database đã có, chạy script một lần bằng PostgreSQL client hoặc khởi tạo container local:

```bash
cp .env.example .env
docker compose up -d postgres
```

Schema được quản lý bằng các script SQL đánh số trong `backend/database/`; không trộn với `EnsureCreated` hoặc một migration EF chưa có baseline tương ứng. Với database đã có, sao lưu rồi áp dụng các script mới qua PostgreSQL client/pipeline triển khai. Database local có cấu hình riêng có thể dùng:

```bash
python3 scripts/database.py backup
python3 scripts/database.py upgrade 0006_course_review_workflow.sql 0007_operational_workflow.sql 0008_appeal_proposal.sql 0009_notifications_allowances.sql 0010_quiz_component_weights.sql 0011_schedule.sql 0012_class_forum.sql 0013_attendance.sql 0014_question_bank_shuffle.sql
```

Script cần các bảng của `0001`–`0005` đã có. Bảng `lms_schema_versions` ghi nhận script đã áp dụng. Database mới từ Docker Compose nhận toàn bộ script SQL khi khởi tạo volume lần đầu.

## Kho học liệu số

- Giảng viên, Trưởng khoa và Quản trị viên có thể tải PDF, Office hoặc ZIP tối đa 25 MB.
- Học viên đã đăng nhập có thể tìm kiếm và tải xuống học liệu.
- Metadata nằm trong PostgreSQL; tệp vật lý nằm tại `Storage:RootPath` (mặc định `uploads`). Khi deploy, đặt `Storage__RootPath` vào một persistent volume hoặc object-storage adapter; không lưu thư mục `uploads` trong Git.

## Khởi chạy

```bash
# API
cd backend && dotnet run

# Frontend (terminal khác)
cd frontend && npm install && npm run dev
```

API mặc định chạy ở `http://localhost:5155`; health check: `GET /health`. API có JWT role-based authorization, CORS allowlist, global error response và fixed-window rate limiting.

Tài khoản demo chỉ được seed khi đặt `Database__SeedDemoData=true`; không bật ở production.

Bộ dữ liệu kiểm thử đầy đủ cho mọi chức năng được nạp riêng bằng `python3 scripts/seed_test_data.py` sau khi build backend. Lệnh sao lưu trước khi nạp, giữ nguyên dữ liệu cũ và có thể chạy lại. Xem [Tài khoản và kịch bản kiểm thử](docs/DU_LIEU_KIEM_THU.md); dùng `--verify-only` để kiểm tra bộ seed trong schema tạm trước.

## Luồng nghiệp vụ đã có

Workflow mục tiêu, phân quyền, khoảng trống cần hoàn thiện và checklist đưa vào vận hành được mô tả tại [Workflow vận hành LMS](docs/WORKFLOW_VAN_HANH.md). Tài liệu phân biệt chức năng đã có với đề xuất cần triển khai.

Xem [Bàn giao vận hành và nghiệm thu](docs/VAN_HANH_VA_NGHIEM_THU.md) để chạy lớp từ nhập danh sách đến lưu trữ. Các luồng đã có giao diện và API:

- Tài khoản: mật khẩu tạm riêng, đổi mật khẩu lần đầu, khóa/mở khóa và cấp lại mật khẩu.
- Nhập XLSX có xem trước, kiểm tra trùng/lỗi và báo cáo; đối soát ghi danh theo lớp có lý do.
- Lớp học phần độc lập theo học kỳ/năm học, đúng 1 giảng viên chính và 2 giảng viên phụ.
- Học phần nháp → gửi duyệt → công bố, trả lại có lý do; tạo phiên bản mới giữ nguyên nội dung lớp cũ.
- Bài học có ghi nhận hoàn thành và tiến độ theo lớp học phần.
- Ngân hàng câu hỏi theo học phần, lọc/chọn ngẫu nhiên, tái sử dụng từ đề hiện có; chỉ chủ sở hữu sửa/lưu trữ. Trộn câu và phương án được lưu riêng theo lượt làm, giữ nguyên khi tiếp tục. Xem [Workflow ngân hàng câu hỏi](docs/NGAN_HANG_CAU_HOI.md).
- Tự luận thủ công và trắc nghiệm nhập DOCX; trọng số riêng cho từng dạng câu trong đề và trọng số đề trong tổng kết; xem đề rồi duyệt công bố.
- Lượt làm bài theo giờ máy chủ, tự lưu nháp, nộp chống trùng, tự nộp khi hết giờ; gia hạn/thêm lượt cá nhân có phê duyệt.
- Chấm bài, phản hồi, tổng kết hệ 10, duyệt/công bố điểm; học viên chỉ xem kết quả và nhận xét của mình sau công bố.
- Phúc khảo qua người chấm lại và người duyệt, giữ điểm cũ trong nhật ký; khóa/lưu trữ lớp và xuất hồ sơ JSON/CSV.
- Thông báo nội bộ nhắc hạn/cần duyệt/thiếu hoạt động, có chống trùng và trạng thái đã đọc.

Kiểm thử: `dotnet test tests/LawLms.WorkflowTests/LawLms.WorkflowTests.csproj`; tích hợp PostgreSQL/API: `LMS_TEST_PORT=5168 python3 scripts/test_workflow.py`; frontend: `npm run build --prefix frontend`.

API local mặc định cổng 5155. Có thể chạy bản đã build bằng `python3 scripts/run_local.py`; `--restart` khởi động lại tiến trình backend của chính dự án. Trưởng khoa cần cấu hình lịch và chính sách thật cho các lớp cũ trước khi mở; script migration giữ chúng ở bản nháp thay vì tự suy diễn lịch đào tạo.

Email/SMS, SSO/SIS và hạ tầng production chưa được cấu hình. Bản local và kiểm thử tự động không thay cho nghiệm thu học vụ, kiểm thử tải và cam kết RPO/RTO của đơn vị.
