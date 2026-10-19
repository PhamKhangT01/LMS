# Quy chuẩn UI toàn hệ thống LMS

Trạng thái: đề xuất workflow để duyệt trước khi triển khai diện rộng.

## Mục tiêu

Một ngôn ngữ giao diện xuyên suốt Admin, Trưởng khoa, Giảng viên và Học viên: cùng màu, chữ, khoảng cách, điều hướng và cách phản hồi thao tác. Giữ nhận diện Khoa Luật/Học viện ANND; các màn hình vẫn có bố cục phù hợp với công việc thực tế.

Rà mã nguồn ngày 19/10/2026 cho thấy CSS từng module đang định nghĩa riêng nhiều cỡ chữ 9–13px, nút bo tròn/bo góc, bảng, thông báo và hộp thoại. Đây là kết quả rà mã nguồn; chưa phải kết luận kiểm tra toàn bộ màn hình trên trình duyệt.

## Quy chuẩn đề xuất

| Nhóm | Quy chuẩn |
| --- | --- |
| Màu | Xanh chính `#28543A`, xanh đậm `#193D2B`, nền `#F5F7F3`, bề mặt trắng, viền `#DBE3DB`; vàng `#AC8937` cho focus/điểm nhấn hạn chế. |
| Chữ | Be Vietnam Pro cho toàn bộ nghiệp vụ. Nội dung 14–16px; thông tin phụ 12–13px; tiêu đề trang 28–32px, tiêu đề vùng 18–22px. Chữ trang giới thiệu lớn hơn khi cần; dùng cùng hệ màu và font nội dung. |
| Khoảng cách | Thang 4/8/12/16/24/32/48px. Lề nội dung desktop 24–32px; mobile 16px. |
| Nút | Chính, phụ, dạng chữ, nguy hiểm, icon. Cao tối thiểu 44px; cùng kiểu chữ, icon và trạng thái hover/focus/loading/disabled. Bo góc 8px cho nút thường. |
| Ô nhập | Cao tối thiểu 44px, nhãn phía trên, đánh dấu bắt buộc, hướng dẫn và lỗi đặt cạnh trường. Checkbox/radio có nhãn và vùng bấm rõ ràng. |
| Khối nội dung | Bề mặt trắng, bo góc 12px; cùng padding, màu viền và cấp độ tiêu đề. Bảng/lịch/soạn câu hỏi có bố cục riêng theo nghiệp vụ. |
| Điều hướng | Cùng header/sidebar, tiêu đề trang, vị trí thao tác chính, trạng thái mục đang mở và điều hướng trên điện thoại. |
| Bảng | Cùng header, padding dòng, bộ lọc, trạng thái rỗng, phân trang. Bảng nhiều cột cuộn trong vùng bảng, không đẩy tràn toàn trang. |
| Trạng thái | Nháp, chờ duyệt, công bố, đã đóng, lưu trữ: cùng nhãn và màu giữa các module; thành công/cảnh báo/lỗi có cả chữ và dấu hiệu trực quan. |
| Hộp thoại | Mẫu xác nhận ngắn và mẫu tác vụ. Form nghiệp vụ dài dùng toàn màn hình; thanh Hủy/Lưu dễ truy cập, quản lý focus/Escape và cảnh báo chưa lưu phù hợp. |
| Phản hồi | Có loading, empty, error/retry và success theo mẫu chung; lỗi không nằm khuất cuối form dài. |

## Workflow triển khai

1. **Lập danh mục màn hình và trạng thái:** đối chiếu các module, bốn vai trò, form tạo/sửa, bảng và hộp thoại. Ghi nhận phần đã có để tái sử dụng, đặc biệt bộ chọn ngân hàng và bảo vệ nội dung soạn đề.
2. **Tạo nền tảng dùng chung:** tách design tokens màu/chữ/khoảng cách, định nghĩa quy tắc CSS và thứ tự import; xây dựng các thành phần cần tái sử dụng như nút, trường nhập, tiêu đề trang, trạng thái và phản hồi. Thay các quy tắc cũ có liên quan theo từng module để tránh chồng lớp override.
3. **Làm mẫu chuẩn:** hoàn thiện khung dashboard, một màn hình danh sách có bộ lọc/bảng và một form nghiệp vụ. Kiểm tra desktop/mobile trước khi nhân rộng.
4. **Áp dụng toàn hệ thống:** tổng quan → học phần/phân công → lớp học phần/đánh giá/bảng điểm/phúc khảo → tài khoản/import → ngân hàng/soạn đề → kho học liệu → lịch → diễn đàn → báo cáo/học tập của học viên → đăng nhập/trang giới thiệu.
5. **Đồng nhất tương tác:** nhãn thao tác, xác nhận, lỗi trường nhập, tải lại, loading, dữ liệu rỗng, điều hướng bàn phím và cảnh báo chưa lưu theo mức độ tác vụ.
6. **Nghiệm thu:** build frontend; kiểm tra ảnh và thao tác trên trình duyệt ở 1287×1204, 768×1024, 390×844. Đi qua các luồng đại diện của cả bốn vai trò và ghi rõ các trạng thái chưa xác minh.

## Phạm vi và điều kiện hoàn thành

- Thống nhất cách trình bày cho dữ liệu và quyền đang có; giữ ranh giới quyền theo vai trò.
- Nội dung nghiệp vụ, quy trình duyệt, cách tính điểm và nguồn dữ liệu tiếp tục theo chức năng hiện hành.
- Trang giới thiệu dùng chung nhận diện nhưng bố cục phục vụ giới thiệu; màn hình nghiệp vụ ưu tiên đọc, lọc và xử lý dữ liệu.
- Hoàn thành khi toàn bộ nhóm màn hình trong danh mục đã áp dụng quy chuẩn, các luồng đại diện hoạt động và có bằng chứng kiểm tra bố cục thực tế.
- Mỗi đợt ghi rõ file đã đổi, phần đã kiểm tra và vấn đề còn lại; không dùng kết quả build thay cho kiểm tra giao diện.
