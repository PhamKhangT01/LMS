using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LawLms.Domain;

namespace LawLms.Application;

public record ChatHistoryEntry(string Role, string Content);
public record ChatMessageRequest(string Mode, string Message, Guid? CourseId, Guid? LessonId, IReadOnlyList<ChatHistoryEntry>? History);
public record ChatCourse(Guid Id, string Code, string Title);
public record ChatContext(bool Available, string? Message, IReadOnlyList<ChatCourse> Courses);
public record ChatSource(string Id, string Title, string Kind, Guid? CourseId = null, Guid? LessonId = null, string? View = null);
public record ChatReply(string Answer, IReadOnlyList<ChatSource> Sources);
public record ChatMaterial(ChatSource Source, string Content);
public record ChatCompletionRequest(string Mode, string Message, string Role, IReadOnlyList<ChatHistoryEntry> History, IReadOnlyList<ChatMaterial> Materials);
public record ChatCompletion(string Answer, IReadOnlyList<string> SourceIds);

public interface IChatCompletionProvider
{
    bool Available { get; }
    Task<ChatCompletion> CompleteAsync(ChatCompletionRequest request, CancellationToken ct);
}
public interface IChatService
{
    Task<ChatContext> ContextAsync(Actor actor, CancellationToken ct);
    Task<ChatReply> SendAsync(ChatMessageRequest request, Actor actor, CancellationToken ct);
}

// These exceptions contain only messages intended for the user, never provider response bodies.
public sealed class ChatInputException(string message) : Exception(message);
public sealed class ChatProviderException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static class ChatInputRules
{
    public const int MaxHistoryEntries = 12;
    public const int MaxHistoryCharacters = 16000;

    public static IReadOnlyList<ChatHistoryEntry> Validate(ChatMessageRequest request)
    {
        if (request.Mode is not ("learning" or "guide")) throw new ChatInputException("Chọn chế độ Hỏi bài học hoặc Hướng dẫn LMS.");
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 2000) throw new ChatInputException("Câu hỏi cần từ 1 đến 2.000 ký tự.");
        if (request.Mode == "learning" && (!request.CourseId.HasValue || request.CourseId == Guid.Empty)) throw new ChatInputException("Chọn học phần trước khi hỏi bài học.");
        if (request.LessonId == Guid.Empty || request.Mode == "guide" && request.LessonId.HasValue) throw new ChatInputException("Bài học được chọn không hợp lệ.");
        var history = request.History ?? [];
        if (history.Count > MaxHistoryEntries) throw new ChatInputException("Cuộc hội thoại quá dài. Hãy bắt đầu cuộc trò chuyện mới.");
        var total = 0;
        foreach (var entry in history)
        {
            if (entry is null || entry.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(entry.Content) || entry.Content.Length > 4000)
                throw new ChatInputException("Lịch sử trò chuyện không hợp lệ.");
            total += entry.Content.Length;
        }
        if (total > MaxHistoryCharacters) throw new ChatInputException("Lịch sử trò chuyện vượt giới hạn 16.000 ký tự. Hãy bắt đầu cuộc trò chuyện mới.");
        return history.Select(x => new ChatHistoryEntry(x.Role, x.Content.Trim())).ToList();
    }
}

public sealed class ChatService(ICourseService courses, IChatCompletionProvider provider) : IChatService
{
    public async Task<ChatContext> ContextAsync(Actor actor, CancellationToken ct)
    {
        RequireRole(actor);
        IReadOnlyList<ChatCourse> availableCourses;
        if (actor.Role == UserRole.Student)
        {
            var readable = new List<ChatCourse>();
            foreach (var item in (await courses.GetMyLearningAsync(actor.Id, ct)).DistinctBy(x => x.CourseId))
            {
                try
                {
                    var detail = await courses.GetAsync(item.CourseId, actor.Id, actor.Role, ct);
                    readable.Add(new(detail.Id, detail.Code, detail.Title));
                }
                catch (Exception exception) when (exception is UnauthorizedAppException or NotFoundException) { }
            }
            availableCourses = readable;
        }
        else
            availableCourses = (actor.IsHead ? await courses.GetManagementAsync(ct) : await courses.GetTeachingAsync(actor.Id, ct))
                .Select(x => new ChatCourse(x.Id, x.Code, x.Title)).ToList();
        return new(provider.Available, provider.Available ? null : "Chatbot chưa được cấu hình dịch vụ AI. Vui lòng liên hệ quản trị viên.",
            availableCourses.DistinctBy(x => x.Id).OrderBy(x => x.Title).ToList());
    }

    public async Task<ChatReply> SendAsync(ChatMessageRequest request, Actor actor, CancellationToken ct)
    {
        RequireRole(actor);
        var history = ChatInputRules.Validate(request);
        IReadOnlyList<ChatMaterial> materials;
        if (request.Mode == "learning")
        {
            // Authorization happens on every message; client history never grants content access.
            var course = await courses.GetAsync(request.CourseId!.Value, actor.Id, actor.Role, ct);
            if (request.LessonId.HasValue && !course.Lessons.Any(x => x.Id == request.LessonId))
                throw new ChatInputException("Bài học không thuộc học phần đã chọn.");
            materials = ChatLessonRetrieval.Select(course, request.LessonId, request.Message, history);
        }
        else materials = ChatGuideCatalog.ForRole(actor.Role);
        if (!provider.Available) throw new ChatProviderException(503, "Chatbot chưa được cấu hình dịch vụ AI. Vui lòng liên hệ quản trị viên.");
        if (materials.Count == 0)
            return new("Học phần chưa có nội dung bài học để tham khảo. Bạn có thể chọn học phần khác hoặc liên hệ giảng viên.", []);
        var completion = await provider.CompleteAsync(new(request.Mode, request.Message.Trim(), actor.Role.ToString(), history, materials), ct);
        var allowedSources = materials.Select(x => x.Source).DistinctBy(x => x.Id).ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (completion.SourceIds.Any(id => !allowedSources.ContainsKey(id)))
            throw new ChatProviderException(502, "Chatbot chưa xác định được nguồn phù hợp cho câu trả lời. Vui lòng thử lại.");
        if (completion.SourceIds.Count == 0)
            return new(request.Mode == "learning" ? "Tôi chưa tìm thấy đủ thông tin trong các bài học được chọn để trả lời. Bạn có thể hỏi cụ thể hơn hoặc trao đổi với giảng viên." : "Tôi chưa tìm thấy hướng dẫn phù hợp trong LMS cho vai trò của bạn. Bạn có thể hỏi cụ thể hơn hoặc liên hệ quản trị viên.", []);
        var sources = completion.SourceIds.Distinct(StringComparer.Ordinal).Select(id => allowedSources[id]).ToList();
        return new(completion.Answer, sources);
    }

    private static void RequireRole(Actor actor)
    {
        if (!Enum.IsDefined(actor.Role)) throw new UnauthorizedAppException("Vai trò không được phép sử dụng chatbot.");
    }
}

public static class ChatLessonRetrieval
{
    public const int MaxContextCharacters = 12000;
    private const int ChunkSize = 1800;

    public static IReadOnlyList<ChatMaterial> Select(CourseDetail course, Guid? preferredLesson, string message, IReadOnlyList<ChatHistoryEntry> history)
    {
        // The last user turns help resolve short follow-up questions without widening the authorized course scope.
        var query = message + " " + string.Join(" ", history.Where(x => x.Role == "user").TakeLast(2).Select(x => x.Content));
        var terms = Regex.Matches(Normalize(query), @"[\p{L}\p{N}]{2,}").Select(x => x.Value).Distinct().Take(80).ToArray();
        var candidates = new List<(LessonDto Lesson, int Position, string Text, int Score)>();
        foreach (var lesson in course.Lessons)
        {
            if (string.IsNullOrWhiteSpace(lesson.Content)) continue;
            var title = Normalize(lesson.Title);
            for (var start = 0; start < lesson.Content.Length; start += ChunkSize - 150)
            {
                var text = lesson.Content.Substring(start, Math.Min(ChunkSize, lesson.Content.Length - start));
                var searchable = Normalize(text);
                var score = terms.Sum(term => (title.Contains(term, StringComparison.Ordinal) ? 3 : 0) + (searchable.Contains(term, StringComparison.Ordinal) ? 1 : 0));
                candidates.Add((lesson, start, text, score));
                if (start + ChunkSize >= lesson.Content.Length) break;
            }
        }
        var result = new List<ChatMaterial>();
        var remaining = MaxContextCharacters;
        foreach (var chunk in candidates.OrderByDescending(x => x.Lesson.Id == preferredLesson).ThenByDescending(x => x.Score).ThenBy(x => x.Lesson.Position).ThenBy(x => x.Position).Take(8))
        {
            if (remaining == 0) break;
            var text = chunk.Text[..Math.Min(chunk.Text.Length, remaining)];
            remaining -= text.Length;
            result.Add(new(new($"lesson:{chunk.Lesson.Id}", chunk.Lesson.Title, "lesson", course.Id, chunk.Lesson.Id), text));
        }
        return result;
    }

    private static string Normalize(string text)
    {
        var value = new StringBuilder();
        foreach (var character in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) value.Append(character == 'đ' || character == 'Đ' ? 'd' : char.ToLowerInvariant(character));
        return value.ToString().Normalize(NormalizationForm.FormC);
    }
}

public static class ChatGuideCatalog
{
    public static IReadOnlyList<ChatMaterial> ForRole(UserRole role)
    {
        var student = role == UserRole.Student;
        var lecturer = role == UserRole.Lecturer;
        var head = role is UserRole.Admin or UserRole.DepartmentHead;
        var guides = new List<ChatMaterial>
        {
            Guide("password", "Đổi mật khẩu", "Chọn Đổi mật khẩu trong điều hướng. Nhập mật khẩu hiện tại, mật khẩu mới 12–128 ký tự và xác nhận, rồi bấm Đổi mật khẩu. Tài khoản dùng mật khẩu tạm phải đổi mật khẩu trước khi sử dụng LMS. Nếu quên mật khẩu, liên hệ Quản trị viên để được cấp mật khẩu tạm; chatbot không cấp lại mật khẩu hoặc yêu cầu bạn gửi mật khẩu."),
            Guide("resources", "Kho học liệu", student ? "Mở Kho học liệu, tìm theo từ khóa hoặc lọc nhóm tài liệu, rồi tải tài liệu cần đọc. Học viên không có quyền tải học liệu lên." : "Mở Kho học liệu để tìm kiếm, lọc và tải tài liệu. Giảng viên, Trưởng khoa và Quản trị viên có thể chọn Tải học liệu, điền thông tin và chọn tệp. Tệp PDF, Office hoặc ZIP tối đa 25 MB. Chatbot hiện chỉ tham khảo nội dung bài học, chưa đọc nội dung tệp trong Kho học liệu."),
            Guide("schedule", "Thời khóa biểu", student ? "Mở Thời khóa biểu để xem lịch học, lịch thi trong những lớp được ghi danh, chuyển tuần và xem chi tiết buổi học. Học viên xem kết quả điểm danh của mình; giảng viên ghi nhận điểm danh." : lecturer ? "Mở Thời khóa biểu để xem lịch lớp được phân công. Chọn buổi học của lớp được phân công để điểm danh học viên; ghi nhận Có mặt, Vắng hoặc Có phép và lưu. Giảng viên không tự tạo lịch toàn trường." : "Mở Thời khóa biểu để xem và quản lý lịch học, lịch thi của các lớp học phần. Khi tạo hoặc sửa lịch, chọn lớp, thời gian và địa điểm; hệ thống kiểm tra xung đột lịch phòng và giảng viên."),
            Guide("forum", "Diễn đàn lớp học", student || lecturer ? "Mở Diễn đàn lớp học, chọn lớp thuộc phạm vi của bạn, đọc hoặc tạo chủ đề, phản hồi và theo dõi thảo luận. Học viên phải được ghi danh đang học trong lớp; giảng viên phải được phân công. Giảng viên được ghim câu trả lời của lớp mình." : "Mở Diễn đàn lớp học để theo dõi thảo luận theo lớp. Trưởng khoa và Quản trị viên xem nội dung; việc đăng bài và phản hồi dành cho học viên và giảng viên của lớp.")
        };
        if (student)
        {
            guides.Add(Guide("courses", "Học phần của tôi", "Mở Học phần của tôi rồi mở học phần để đọc từng bài. Học phần chỉ xuất hiện khi đã được công bố và bạn được ghi danh trong lớp đã mở. Nếu chưa có học phần, liên hệ Trưởng khoa hoặc Quản trị viên kiểm tra mã lớp, ghi danh và trạng thái mở lớp; học viên không tự tạo hoặc tự ghi danh học phần."));
            guides.Add(Guide("operations", "Lớp học phần & kết quả", "Mở Lớp học phần & kết quả, chọn lớp học phần. Ở Bài học, đọc bài và bấm Xác nhận đã học xong khi lớp đang học. Ở Kiểm tra, chọn Bắt đầu / tiếp tục trong thời gian được phép, lưu bản nháp và Nộp bài. Ở Bảng điểm, xem điểm sau khi đã công bố. Ở Phúc khảo, gửi yêu cầu đúng thời hạn của lớp. Chatbot không làm bài kiểm tra thay bạn và không cung cấp đáp án đề thi."));
            guides.Add(Guide("student-report", "Báo cáo học tập", "Mở Báo cáo học tập để xem tiến độ bài học, chuyên cần và điểm thành phần đã công bố của chính bạn. Bấm Cập nhật để tải dữ liệu mới. Tỷ lệ chuyên cần chỉ tính trên buổi đã ghi nhận điểm danh; điểm chưa công bố sẽ chưa hiển thị."));
        }
        if (lecturer)
        {
            guides.Add(Guide("courses", "Học phần được phân công", "Mở Học phần được phân công để xem học phần do Trưởng khoa phân công. Mở học phần để thêm hoặc sửa bài học khi còn bản nháp, sau đó Gửi duyệt. Khi chờ duyệt hoặc đã công bố, nội dung bị khóa. Trưởng khoa hoặc Quản trị viên tạo học phần và phân công lớp; giảng viên quản lý nội dung trong phạm vi được phân công."));
            guides.Add(Guide("operations", "Lớp học phần & kết quả", "Mở Lớp học phần & kết quả và chọn lớp được phân công. Giảng viên tạo hoặc sửa đề nháp rồi Gửi duyệt đề; Trưởng khoa/Admin duyệt và công bố. Tạo câu hỏi thủ công dùng tự luận, nhập DOCX/PDF dùng luồng nhập, hoặc chọn từ ngân hàng câu hỏi. Ở Chấm bài, nhập điểm và nhận xét cho bài tự luận. Giảng viên chính gửi duyệt bảng điểm khi đã đóng bài kiểm tra và tổng trọng số đủ 100%; Trưởng khoa/Admin duyệt và công bố điểm. Xử lý phúc khảo trong phạm vi được giao."));
            guides.Add(Guide("question-bank", "Ngân hàng câu hỏi", "Mở Ngân hàng câu hỏi, chọn học phần được phân công, tìm hoặc lọc câu hỏi để xem và tái sử dụng. Có thể tạo câu hỏi hoặc Nhập DOCX/PDF: tải tệp, xem trước và sửa hàng lỗi, kiểm tra trùng, chọn câu cần lưu, rồi xác nhận. PDF phải có văn bản trích xuất được. Khi tạo bài kiểm tra, chọn từ ngân hàng là một luồng riêng. Chỉ người tạo được sửa hoặc lưu trữ câu hỏi của mình."));
        }
        if (head)
        {
            guides.Add(Guide("courses", "Học phần & phân công", "Mở Học phần & phân công để Tạo học phần, Phân công lớp và tổ giảng viên. Mở Nội dung & duyệt, kiểm tra bản đã gửi, Duyệt & công bố hoặc Yêu cầu chỉnh sửa kèm lý do. Học phần đã công bố được khóa; dùng Tạo phiên bản mới để sao chép sang mã mới rồi chỉnh sửa và duyệt lại. Sau phân công cần đối soát ghi danh và mở lớp trong Lớp học phần & kết quả."));
            guides.Add(Guide("operations", "Lớp học phần & kết quả", "Mở Lớp học phần & kết quả, chọn lớp. Ở Danh sách lớp, đối soát theo mã lớp hoặc điều chỉnh ghi danh kèm lý do. Ở Vận hành lớp, cấu hình thời gian và chính sách, chuyển Bản nháp → Sẵn sàng → Đang học khi đủ điều kiện: học phần công bố, có bài học, có học viên và đúng lịch. Duyệt/công bố đề đã được giảng viên gửi. Bảng điểm theo luồng giảng viên chính gửi → duyệt → công bố. Chuyển chấm điểm, hoàn thành hoặc lưu trữ theo điều kiện và nhập lý do khi được yêu cầu."));
            guides.Add(Guide("question-bank", "Ngân hàng câu hỏi", "Trưởng khoa và Quản trị viên có thể xem ngân hàng câu hỏi theo phạm vi quản lý. Việc tạo, nhập, sửa và lưu trữ câu hỏi dành cho giảng viên được phân công; chatbot không hướng dẫn cấp quyền vượt vai trò này."));
        }
        if (!student) guides.Add(Guide("reports", "Báo cáo học vụ", lecturer ? "Mở Báo cáo học vụ để xem báo cáo của những lớp được phân công; chọn bộ lọc phù hợp và tải dữ liệu báo cáo khi cần. Chỉ giảng viên chính gửi duyệt bảng điểm; trợ giảng không có quyền gửi thay." : "Mở Báo cáo học vụ để xem dữ liệu tổng hợp theo lớp, học phần và kỳ học. Chọn bộ lọc, xem tình trạng học tập, chuyên cần và kết quả theo dữ liệu hiện có; tải báo cáo khi cần. Điểm chính thức cần hoàn tất luồng duyệt và công bố."));
        if (role == UserRole.Admin) guides.Add(Guide("accounts", "Quản lý tài khoản", "Mở Quản lý tài khoản để tạo tài khoản, khóa/mở khóa hoặc cấp mật khẩu tạm; thông tin kích hoạt được tải thành tệp để bàn giao riêng cho chủ tài khoản. Chọn Nhập danh sách học viên để tải mẫu XLSX, điền Họ và tên, Email, Mã lớp rồi nhập và xem kết quả từng dòng. Tài khoản mới phải đổi mật khẩu trước khi dùng LMS. Không gửi mật khẩu vào chatbot."));
        return guides;
    }

    private static ChatMaterial Guide(string view, string title, string content) => new(new($"guide:{view}", title, "guide", View: view), content);
}
