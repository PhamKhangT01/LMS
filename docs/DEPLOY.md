# Deploy bản chạy thử miễn phí: Render + Neon

## Trạng thái

Đã chuẩn bị Dockerfile build React + ASP.NET Core trong một image, API cùng origin `/api`, và Blueprint `render.yaml`. Chưa triển khai lên tài khoản hosting. Chưa kiểm thử container vì máy hiện tại chưa có Docker.

## Kiểm tra tại máy phát triển

- Frontend production build với `VITE_API_URL=/api`: đạt.
- .NET Release publish: đạt; đã xác nhận không có `appsettings.Local.json` trong gói.
- Chạy tạm gói publish cùng frontend: `/`, `/dashboard`, `/health/live` trả 200; API không tồn tại và asset thiếu trả 404.
- Đây là kiểm tra HTTP routing với database giả không kết nối; chưa xác minh PostgreSQL, đăng nhập hay URL hosting thật.

## Điều kiện và giới hạn

- Cần tài khoản Render, Neon và quyền kết nối repository GitHub.
- Chỉ chọn gói Free; không thêm thẻ/nâng gói nếu chưa được chủ tài khoản đồng ý.
- Render Free có thời gian ngủ và khởi động lại; không phù hợp cho thi trực tuyến có yêu cầu liên tục.
- Tệp trong `/data/uploads` trên Render Free mất khi restart/redeploy. PostgreSQL Neon chỉ giữ metadata, không giữ tệp học liệu. Cần adapter object storage hoặc persistent disk trước khi dùng chức năng upload lâu dài.
- Không chuyển database local, tài khoản kiểm thử hoặc học liệu lên public tự động. Database mới không có tài khoản quản trị; cần quyết định cách cấp tài khoản ban đầu trước khi nghiệm thu đăng nhập.

## Triển khai

1. Tạo PostgreSQL Free trên Neon. Giữ mật khẩu trong secret của hosting, không đưa vào Git/chat.
2. Với database mới trống, áp dụng các file `backend/database/0001_*.sql` đến `0014_*.sql` theo thứ tự tăng dần bằng PostgreSQL client/Neon SQL Editor. Dừng khi gặp lỗi. Với database đã có, sao lưu và chỉ áp dụng migration còn thiếu; không chạy lại baseline.
3. Đưa thay đổi đã kiểm tra lên GitHub, tạo Blueprint từ repository trong Render, dùng `render.yaml` tại root.
4. Nhập secret `ConnectionStrings__LmsDatabase` theo định dạng Npgsql, không dán nguyên URI `postgresql://...`:

   ```text
   Host=NEON_HOST;Port=5432;Database=NEON_DATABASE;Username=NEON_USER;Password=NEON_PASSWORD;SSL Mode=Require
   ```

   Lấy từng trường từ Neon; nếu mật khẩu có dấu chấm phẩy/dấu nháy, dùng công cụ tạo connection string Npgsql để escape đúng. `Jwt__Key` được Render sinh tự động. Không bật `Database__SeedDemoData`.
5. Deploy rồi kiểm tra URL Render: `/` trả HTML, `/health/live` và `/health/ready` trả 200, `/api/does-not-exist` trả 404. Kiểm tra trang đăng nhập và Network: request dùng `/api`, không gọi `localhost:5155`.
6. Cấu hình tài khoản ban đầu bằng quy trình riêng được chủ hệ thống duyệt, rồi kiểm tra đăng nhập/phân quyền trên URL public. Không coi HTTP 200 là đã nghiệm thu toàn bộ LMS.

## Tài liệu dịch vụ

- https://render.com/docs/free
- https://render.com/docs/blueprint-spec
- https://neon.com/pricing

## Build tại máy phát triển

```bash
VITE_API_URL=/api npm run build --prefix frontend
dotnet publish backend/LawLms.Api.csproj -c Release
docker build -t law-lms .
```

Docker context loại trừ `appsettings.Local.json`, `.env`, dữ liệu uploads, backup và báo cáo kiểm thử. Project .NET cũng loại `appsettings.Local.json` khỏi gói publish. Vẫn phải kiểm tra nội dung gói trước khi tải lên hosting.
