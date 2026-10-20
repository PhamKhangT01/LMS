# Frontend Vercel + API Render

## Cấu hình project

- Repository: PhamKhangT01/LMS.
- Root Directory: `frontend`.
- Framework: Vite.
- Install: `npm ci`.
- Build: `VITE_API_URL=/api npm run build` (đã có trong `frontend/vercel.json`).
- Output: `dist`.
- Chọn tài khoản/gói Hobby nếu đủ điều kiện; không bật add-on trả phí.

`frontend/vercel.json` proxy `/api/*` tới `https://law-lms-14hk.onrender.com/api/*`. JWT Authorization và request body đi qua proxy; browser gọi cùng origin Vercel. Không đưa connection string hoặc JWT secret backend vào Vercel/VITE variables.

Đường dẫn SPA được trả `index.html`; asset không có vẫn trả 404. API chưa được di chuyển: Render và Neon phải tiếp tục hoạt động. Thời gian cold start và giới hạn lưu tệp của Render Free vẫn áp dụng cho API.

## Xác minh sau deploy

- Trang chủ và modal đăng nhập tải được trên desktop/mobile.
- Request đăng nhập đi qua `/api/auth/login` trên domain Vercel, không gọi localhost.
- Đăng nhập sai trả thông báo hợp lệ; tài khoản thật đăng nhập được rồi tải dữ liệu lớp/học liệu theo quyền.
- Truy cập lại một đường dẫn SPA; asset thiếu trả 404; API bảo vệ trả 401 khi chưa đăng nhập.

Chưa có URL Vercel được xác minh tại thời điểm tạo tài liệu. Frontend đã build local thành công với `/api`; chưa xác minh proxy Vercel thực tế.
