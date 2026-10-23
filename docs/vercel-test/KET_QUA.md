# Vercel frontend — 20/10/2026

- Production: https://lms-t01.vercel.app/
- Deployment: https://vercel.com/t5-1526/lms/BqsFs2uEcce8V8Qcd31qFfUZumy9
- Commit: 2aafac47ed44e96e4e4cd2b04403bfcac8b49b6b
- Vercel production Ready / Latest, build 13 giây.
- Frontend production build local thành công, bundle không có API localhost.
- GET / và /dashboard: 200 HTML.
- GET /api/courses: 200 JSON từ Render (có dữ liệu học phần kiểm thử).
- GET /api/library-resources không token: 401.
- GET /api/not-found: 404; /assets/missing.js: 404.
- Browser: đăng nhập bằng tài khoản giả trả thông báo email/mật khẩu không chính xác; không có console error được ghi nhận.
- Mobile 390×844: ảnh đã kiểm tra; scrollWidth=390, không tràn ngang.
- Backend tiếp tục chạy ở Render, database Neon. Chưa kiểm tra đăng nhập thành công/luồng học vụ bằng tài khoản thật trong lần deploy frontend này.

Ảnh: deployment-ready.jpg, home-mobile.jpg.
