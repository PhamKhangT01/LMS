# Chatbot Trợ lý LMS

Trợ lý LMS có hai chế độ: giải đáp nội dung học phần và hướng dẫn sử dụng hệ thống theo vai trò. Chỉ người dùng đã đăng nhập, có tài khoản hợp lệ và đã hoàn tất yêu cầu đổi mật khẩu mới truy cập API chatbot.

## Cấu hình OpenAI

Đặt API key ở backend. Với môi trường local, thêm khối sau vào `backend/appsettings.Local.json`, giữ nguyên các khối cấu hình khác:

```json
{
  "OpenAI": {
    "ApiKey": "YOUR_PROJECT_API_KEY",
    "Model": "gpt-4.1-mini",
    "TimeoutSeconds": 45,
    "RequestsPerMinute": 10
  }
}
```

File `appsettings.Local.json` đã được bỏ qua trong Git. Khi triển khai, cấu hình secret `OpenAI__ApiKey` và các biến `OpenAI__Model`, `OpenAI__TimeoutSeconds`, `OpenAI__RequestsPerMinute` trên máy chủ API, rồi khởi động lại backend. Các dòng trong `backend/.env.example` là ví dụ biến môi trường; ứng dụng không tự đọc file `.env`.

Không đặt API key trong biến `VITE_*`, mã frontend, Git hoặc nội dung hội thoại. API OpenAI được gọi từ backend qua Responses API. Có thể thay model bằng model hỗ trợ Responses API và Structured Outputs trong tài khoản OpenAI của đơn vị.

Khi chưa cấu hình khóa, LMS vẫn hoạt động; trợ lý hiển thị trạng thái chưa sẵn sàng. Lỗi từ OpenAI được chuyển thành thông báo tiếng Việt, không trả nội dung lỗi gốc hoặc khóa bí mật cho trình duyệt.

## Nguồn trả lời và phân quyền

- **Học phần:** chọn học phần hoặc mở trợ lý từ một bài học. Backend kiểm tra quyền đọc học phần trước khi chọn trích đoạn bài học. Học viên cần học phần đã công bố và ghi danh hợp lệ theo lớp học phần; giảng viên cần quyền sở hữu hoặc phân công theo chính sách hiện có.
- **Hướng dẫn sử dụng:** nguồn là hướng dẫn được biên soạn theo giao diện và nghiệp vụ thực tế, lọc theo vai trò. Nguồn hướng dẫn không truy vấn thông tin cá nhân, điểm hoặc đáp án.
- **Nguồn dẫn:** backend tạo danh sách nguồn và chỉ chấp nhận mã nguồn nằm trong ngữ cảnh đã cung cấp. Trình duyệt mở bài học hoặc mục chức năng tương ứng.

Nội dung học phần ở bản này là văn bản bài học đã lưu trong LMS. Chưa có bước lập chỉ mục nội dung tệp PDF/DOCX trong kho học liệu. Ngân hàng câu hỏi, đáp án, hướng dẫn chấm và hồ sơ học viên khác không được đưa vào ngữ cảnh.

Trợ lý được yêu cầu trả lời dựa trên nguồn, nêu rõ khi nguồn chưa đủ thông tin. Các nguồn dẫn giúp người dùng đối chiếu; đầu ra AI vẫn cần được kiểm tra với bài học gốc. Hệ thống không tự xác nhận tính cập nhật của văn bản pháp luật ngoài nội dung học phần.

## Hội thoại và giới hạn

Hội thoại được giữ trong bộ nhớ giao diện, tách theo ngữ cảnh và phiên người dùng; không ghi vào `localStorage` hoặc tạo bảng lịch sử chat. Backend giới hạn độ dài câu hỏi, số lượt lịch sử, tổng ngữ cảnh và tần suất yêu cầu. Chuyển ngữ cảnh hoặc đóng phiên sẽ hủy yêu cầu đang chờ theo luồng giao diện.

Yêu cầu OpenAI đặt `store: false`; tùy chọn này không thay thế chính sách lưu giữ dữ liệu của nhà cung cấp. Khi đưa vào vận hành, đơn vị cần lựa chọn học liệu phù hợp để gửi tới dịch vụ OpenAI.

## Kiểm tra sau cấu hình

1. Khởi động backend và frontend; kiểm tra `GET /health`.
2. Đăng nhập, mở **Trợ lý LMS**, kiểm tra trạng thái sẵn sàng.
3. Hỏi cách xem kết quả học tập và mở nguồn hướng dẫn tương ứng.
4. Chọn một học phần có bài học, yêu cầu tóm tắt và mở nguồn bài học.
5. Hỏi tiếp một câu liên quan để kiểm tra ngữ cảnh hội thoại.
6. Kiểm tra tài khoản ngoài phân công/ghi danh không đọc được học phần đó.

Kiểm thử tự động và bằng chứng giao diện được ghi ở cuối tài liệu sau khi hoàn tất triển khai. Kiểm thử dùng phản hồi nhà cung cấp giả lập không chứng minh kết nối OpenAI thật.

## Tài liệu OpenAI

- [Responses API](https://developers.openai.com/api/reference/responses/overview)
- [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
- [Bảo vệ API key ở máy chủ](https://developers.openai.com/api/reference/overview#authentication)
