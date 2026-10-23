# Kiểm tra bản Render — 20/10/2026

URL: https://law-lms-14hk.onrender.com/

- Trang chủ hiển thị; hai ảnh/logo trong DOM tải thành công.
- Desktop 1528px và mobile 390×844: chiều rộng trang bằng viewport, không tràn ngang.
- FAQ mở được, modal đăng nhập hiển thị trên mobile và đóng được bằng Escape.
- Đăng nhập một lần bằng tài khoản giả @example.invalid trả “Email hoặc mật khẩu không chính xác”; không có lỗi console được ghi nhận trong lần kiểm tra.
- GET /health/live: 200 live; /health/ready: 200 ready.
- GET /api/courses: 200 JSON (endpoint public).
- GET /api/library-resources và /api/teaching-classes không token: 401.
- GET /api/not-found và /assets/missing.js: 404.

Chưa kiểm tra đăng nhập thành công hoặc nghiệp vụ theo vai trò vì chưa có tài khoản online hợp lệ. Không tạo tài khoản, lớp học, lượt thi, học liệu hoặc thay đổi cấu hình hosting trong lần kiểm tra này. Kết quả không thay thế nghiệm thu nghiệp vụ, tải hoặc lưu trữ bền vững.
