# Cổng sẵn sàng vận hành LMS

Tài liệu này phân biệt những gì repository đã thực hiện với những điều kiện bắt buộc phải nghiệm thu ở môi trường của Học viện. Build thành công trên máy phát triển không đồng nghĩa hệ thống đã sẵn sàng nhận dữ liệu học viên thật.

## Trạng thái hiện tại

| Hạng mục | Hiện trạng | Điều kiện để ký nghiệm thu |
| --- | --- | --- |
| Workflow học vụ | Có luồng tài khoản, lớp học phần, nội dung, bài kiểm tra, chấm điểm, phúc khảo, báo cáo và lưu trữ. | Trưởng khoa ký chính sách điểm, lịch, tổ giảng viên, hạn phúc khảo và mẫu đầu ra cho từng học kỳ. |
| Phân quyền | API có kiểm tra vai trò và phạm vi lớp/học phần; quy tắc giảng viên chính và duyệt điểm đã được siết. | Kiểm thử ma trận quyền bằng tài khoản đại diện thật; xác nhận phân công và quy trình thay người. |
| Dữ liệu | Migration SQL được quản lý theo phiên bản; không bật seed demo mặc định. | Đối soát danh sách học viên, mã lớp, email, học phần và dữ liệu lịch sử với hệ thống nguồn. |
| API cơ bản | Có `/health/live`, `/health/ready` (PostgreSQL) và các response header bảo mật cơ bản. | Probe và cảnh báo được cấu hình trong môi trường chạy; HTTPS kết thúc tại reverse proxy tin cậy. |
| Tệp học liệu | Lưu ở thư mục cục bộ cấu hình được; giới hạn loại và dung lượng ở API. | Gắn volume bền vững hoặc object storage, quét mã độc, thiết lập quyền truy cập và backup cùng database. |
| Đóng gói/chạy dịch vụ | Compose hiện chỉ khởi chạy PostgreSQL; API và frontend vẫn chạy theo hướng dẫn local. | Chọn nền tảng triển khai, cấu hình domain/TLS, reverse proxy, secret manager, health checks, restart và cập nhật có rollback. |
| Backup/khôi phục | Có script backup database và kho tệp, cùng script phục hồi thử thủ công. | Lịch tự động, mã hóa và lưu bản sao ngoài máy chủ; định nghĩa RPO/RTO; diễn tập khôi phục toàn bộ DB+tệp và ký biên bản. |
| Quan sát vận hành | Chưa có hệ thống log tập trung, metric, cảnh báo hay theo dõi lỗi được kết nối. | Theo dõi đăng nhập lỗi, lỗi API, dung lượng, DB, worker hết hạn, backup và chứng thư TLS; phân công người trực. |
| Tích hợp | Thông báo nội bộ; chưa cấu hình email/SMS, SSO hoặc SIS. | Học viện quyết định tích hợp nào bắt buộc cho pilot, cấp endpoint/tài khoản dịch vụ và nghiệm thu luồng lỗi. |
| Tải và an toàn | Build đã chạy; kiểm thử workflow tồn tại trong repo. Chưa có kết quả tải trên hạ tầng đích hoặc quét bảo mật độc lập. | Chạy test hồi quy trên DB sạch, tải đồng thời theo sĩ số dự kiến, kiểm thử quyền và khôi phục trước khi mở cho học viên. |
| Vòng đời nền tảng | Backend đang dùng .NET 9. | Nâng lên .NET 10 LTS và cập nhật package/runtime trước khi mở production mới; .NET 9 kết thúc hỗ trợ vào tháng 11/2026. |

## Trình tự mở pilot

1. **Chốt chủ sở hữu:** Học viện chỉ định Admin vận hành, người phụ trách hạ tầng, đầu mối dữ liệu, Trưởng khoa duyệt học vụ và người nhận cảnh báo.
2. **Chuẩn hóa dữ liệu:** nhập thử danh sách ẩn danh hoặc môi trường staging; đối soát số lượng, lớp, học phần và quyền; ký xác nhận trước khi nhập dữ liệu thật.
3. **Dựng staging giống production:** dùng HTTPS, secrets riêng, database riêng, storage bền vững; seed demo tắt; bật migration có kiểm soát và ghi lại phiên bản triển khai.
4. **Nghiệm thu nghiệp vụ:** chạy một lớp mẫu từ mở lớp đến công bố điểm, phúc khảo, kết thúc và xuất hồ sơ; xác nhận từng bước bởi đúng vai trò.
5. **Nghiệm thu kỹ thuật:** chạy test, tải dự kiến, kiểm tra giới hạn đăng nhập/tệp, log và cảnh báo; backup rồi phục hồi sang môi trường tách biệt, đo RPO/RTO đạt yêu cầu.
6. **Mở pilot có kiểm soát:** giới hạn khoa/lớp và số người; theo dõi hàng ngày; có đầu mối hỗ trợ, lịch backup và phương án quay lui.
7. **Mở rộng:** chỉ sau khi xử lý lỗi pilot, xác nhận báo cáo và hồ sơ, hoàn tất đào tạo Admin/giảng viên/học viên và ký biên bản go-live.

## Điều kiện chặn go-live

- Còn tài khoản mặc định/mật khẩu tạm chưa bàn giao an toàn, hoặc quyền của tài khoản thực chưa được đối soát.
- Chưa có HTTPS, secret riêng, storage bền vững, backup mã hóa ngoài máy chủ hoặc diễn tập phục hồi database và tệp.
- Chưa có chính sách học vụ được Trưởng khoa ký, chưa xác nhận dữ liệu nguồn hoặc chưa nghiệm thu điểm/phúc khảo.
- Chưa có người nhận cảnh báo và quy trình xử lý sự cố; chưa kiểm tra tải theo số lượng người dùng dự kiến.
- Chạy production trên runtime .NET đã hết hoặc sắp hết hỗ trợ mà chưa có kế hoạch nâng cấp được nghiệm thu.

Các giá trị domain, nhà cung cấp hạ tầng, số người dùng đồng thời, RPO/RTO và tích hợp bắt buộc cần do Học viện xác định; không điền giá trị giả trong cấu hình mẫu.
