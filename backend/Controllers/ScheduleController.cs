using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LawLms.Domain;
using LawLms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Api.Controllers;

[ApiController, Authorize, Route("api/schedule")]
public sealed class ScheduleController(LmsDbContext db) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private UserRole Role => Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!);

    [HttpGet("{id:guid}/attendance")]
    public async Task<IActionResult> Attendance(Guid id, CancellationToken ct)
    {
        var item = await db.ScheduleEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new LawLms.Application.NotFoundException("Không tìm thấy buổi học.");
        var offering = await db.TeachingClasses.AsNoTracking().SingleAsync(x => x.Id == item.TeachingClassId, ct);
        RequireAttendanceRead(offering);
        var rows = await (from enrollment in db.OfferingEnrollments.AsNoTracking()
                          join student in db.Users.AsNoTracking() on enrollment.UserId equals student.Id
                          where enrollment.TeachingClassId == offering.Id && enrollment.Status == EnrollmentStatus.Active
                          orderby student.FullName
                          select new AttendanceRosterRow(student.Id, student.FullName,
                              db.AttendanceRecords.Where(x => x.ScheduleEventId == id && x.StudentId == student.Id).Select(x => (AttendanceStatus?)x.Status).FirstOrDefault(),
                              db.AttendanceRecords.Where(x => x.ScheduleEventId == id && x.StudentId == student.Id).Select(x => (DateTime?)x.MarkedAt).FirstOrDefault()))
            .ToListAsync(ct);
        return Ok(new AttendanceView(id, item.Title, item.StartsAt, item.EndsAt, item.Type, item.IsCancelled, rows));
    }

    [HttpPut("{id:guid}/attendance")]
    public async Task<IActionResult> MarkAttendance(Guid id, MarkAttendanceRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2026101703)", ct);
        var item = await db.ScheduleEvents.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new LawLms.Application.NotFoundException("Không tìm thấy buổi học.");
        var offering = await db.TeachingClasses.AsNoTracking().SingleAsync(x => x.Id == item.TeachingClassId, ct);
        RequireAssignedLecturer(offering);
        if (item.Type != ScheduleEventType.ClassSession || item.IsCancelled || item.EndsAt > DateTime.UtcNow)
            throw new LawLms.Application.ConflictException("Chỉ được điểm danh sau khi buổi học đã kết thúc và chưa bị hủy.");
        if (request.Records is null || request.Records.Count == 0 || request.Records.Select(x => x.StudentId).Distinct().Count() != request.Records.Count || request.Records.Any(x => !Enum.IsDefined(x.Status)))
            throw new LawLms.Application.ConflictException("Danh sách điểm danh không hợp lệ.");
        var roster = await db.OfferingEnrollments.Where(x => x.TeachingClassId == offering.Id && x.Status == EnrollmentStatus.Active).Select(x => x.UserId).ToListAsync(ct);
        if (request.Records.Count != roster.Count || !request.Records.Select(x => x.StudentId).ToHashSet().SetEquals(roster))
            throw new LawLms.Application.ConflictException("Danh sách điểm danh phải bao gồm đầy đủ học viên đang theo học.");
        var existing = await db.AttendanceRecords.Where(x => x.ScheduleEventId == id).ToDictionaryAsync(x => x.StudentId, ct);
        var now = DateTime.UtcNow;
        foreach (var mark in request.Records)
        {
            if (existing.TryGetValue(mark.StudentId, out var record)) { record.Status = mark.Status; record.MarkedById = ActorId; record.MarkedAt = now; }
            else db.AttendanceRecords.Add(new AttendanceRecord { ScheduleEventId = id, StudentId = mark.StudentId, Status = mark.Status, MarkedById = ActorId, MarkedAt = now });
            db.ScheduleChanges.Add(new ScheduleChange { ScheduleEventId = id, ActorId = ActorId, Action = "attendance.marked", SnapshotJson = JsonSerializer.Serialize(new { studentId = mark.StudentId, status = mark.Status }), At = now });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var start = (from ?? DateTimeOffset.UtcNow.AddDays(-7)).UtcDateTime;
        var end = (to ?? DateTimeOffset.UtcNow.AddDays(60)).UtcDateTime;
        if (end <= start || end - start > TimeSpan.FromDays(120)) return BadRequest(new { detail = "Khoảng xem lịch phải từ 0 đến 120 ngày." });
        var rows = await QueryVisibleEvents(start, end, ct);
        return Ok(rows.Select(Map));
    }

    [HttpGet("calendar.ics")]
    public async Task<IActionResult> Calendar(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var events = (await QueryVisibleEvents(now.AddDays(-30), now.AddDays(180), ct)).Where(x => !x.IsCancelled).ToList();
        var sb = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Law LMS//Schedule//VI\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\n");
        foreach (var item in events)
        {
            sb.Append("BEGIN:VEVENT\r\nUID:").Append(item.Id).Append("@lms\r\nDTSTAMP:").Append(DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")).Append("\r\nDTSTART:").Append(item.StartsAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")).Append("\r\nDTEND:").Append(item.EndsAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")).Append("\r\nSUMMARY:").Append(IcsEscape(item.Title)).Append("\r\nLOCATION:").Append(IcsEscape(string.Join(" - ", new[] { item.Room, item.Location }.Where(x => !string.IsNullOrWhiteSpace(x))))).Append("\r\nDESCRIPTION:").Append(IcsEscape($"{item.CourseTitle} · {item.ClassCode}. {item.Notes}")).Append("\r\nEND:VEVENT\r\n");
        }
        sb.Append("END:VCALENDAR\r\n");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/calendar; charset=utf-8", "lich-lms.ics");
    }

    [Authorize(Roles = "Admin,DepartmentHead")]
    [HttpPost]
    public async Task<IActionResult> Create(ScheduleRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2026101701)", ct);
        var offering = await db.TeachingClasses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TeachingClassId, ct)
            ?? throw new LawLms.Application.NotFoundException("Không tìm thấy lớp học phần.");
        Validate(request);
        await EnsureNoConflict(request, offering, null, ct);
        var item = new ScheduleEvent { TeachingClassId = offering.Id, Type = request.Type, Title = request.Title.Trim(), Room = request.Room.Trim(), Location = request.Location?.Trim() ?? string.Empty, Notes = request.Notes?.Trim() ?? string.Empty, StartsAt = request.StartsAt.UtcDateTime, EndsAt = request.EndsAt.UtcDateTime, CreatedById = ActorId };
        db.ScheduleEvents.Add(item);
        AddHistory(item, "created");
        await db.SaveChangesAsync(ct);
        await NotifyOffering(item, offering, $"Đã xếp lịch {item.Title}: {item.StartsAt.ToUniversalTime():dd/MM HH:mm UTC}, phòng {item.Room}.", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CreatedAtAction(nameof(List), new { from = item.StartsAt.ToString("O"), to = item.EndsAt.ToString("O") }, Map(item, offering));
    }

    [Authorize(Roles = "Admin,DepartmentHead")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ScheduleRequest request, [FromQuery] int revision, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2026101701)", ct);
        var item = await db.ScheduleEvents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new LawLms.Application.NotFoundException("Không tìm thấy sự kiện lịch.");
        if (item.Revision != revision) return Conflict(new { detail = "Lịch đã được người khác cập nhật. Hãy tải lại trước khi lưu." });
        if (item.IsCancelled) return Conflict(new { detail = "Không thể sửa sự kiện đã hủy." });
        var offering = await db.TeachingClasses.AsNoTracking().SingleAsync(x => x.Id == request.TeachingClassId, ct);
        if (offering.Id != item.TeachingClassId) throw new LawLms.Application.ConflictException("Không chuyển lịch sang lớp học phần khác; hãy tạo lịch mới cho lớp đích rồi hủy lịch cũ.");
        Validate(request);
        await EnsureNoConflict(request, offering, id, ct);
        item.TeachingClassId = offering.Id; item.Type = request.Type; item.Title = request.Title.Trim(); item.Room = request.Room.Trim(); item.Location = request.Location?.Trim() ?? string.Empty; item.Notes = request.Notes?.Trim() ?? string.Empty; item.StartsAt = request.StartsAt.UtcDateTime; item.EndsAt = request.EndsAt.UtcDateTime; item.Revision++; item.UpdatedAt = DateTime.UtcNow;
        AddHistory(item, "updated");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { detail = "Lịch vừa được thay đổi. Hãy tải lại." }); }
        await NotifyOffering(item, offering, $"Lịch đã thay đổi: {item.Title}, {item.StartsAt.ToUniversalTime():dd/MM HH:mm UTC}, phòng {item.Room}.", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(Map(item, offering));
    }

    [Authorize(Roles = "Admin,DepartmentHead")]
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelScheduleRequest request, [FromQuery] int revision, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2026101701)", ct);
        var item = await db.ScheduleEvents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new LawLms.Application.NotFoundException("Không tìm thấy sự kiện lịch.");
        if (item.Revision != revision) return Conflict(new { detail = "Lịch đã được người khác cập nhật. Hãy tải lại trước khi lưu." });
        if (item.IsCancelled) return NoContent();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000) return BadRequest(new { detail = "Nhập lý do hủy (tối đa 1000 ký tự)." });
        var offering = await db.TeachingClasses.AsNoTracking().SingleAsync(x => x.Id == item.TeachingClassId, ct);
        item.IsCancelled = true; item.Revision++; item.UpdatedAt = DateTime.UtcNow;
        AddHistory(item, "cancelled", request.Reason.Trim());
        await db.SaveChangesAsync(ct);
        await NotifyOffering(item, offering, $"Đã hủy lịch {item.Title} vào {item.StartsAt.ToUniversalTime():dd/MM HH:mm UTC}, phòng {item.Room}. Lý do: {request.Reason.Trim()}", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [Authorize(Roles = "Admin,DepartmentHead")]
    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct)
    {
        var exists = await db.ScheduleEvents.AnyAsync(x => x.Id == id, ct);
        if (!exists) throw new LawLms.Application.NotFoundException("Không tìm thấy sự kiện lịch.");
        return Ok(await db.ScheduleChanges.AsNoTracking().Where(x => x.ScheduleEventId == id).OrderByDescending(x => x.At).ToListAsync(ct));
    }

    private async Task<List<VisibleScheduleEvent>> QueryVisibleEvents(DateTime start, DateTime end, CancellationToken ct)
    {
        var query = from e in db.ScheduleEvents.AsNoTracking()
                    join t in db.TeachingClasses.AsNoTracking() on e.TeachingClassId equals t.Id
                    where e.StartsAt < end && e.EndsAt > start
                    orderby e.StartsAt
                    select new VisibleScheduleEvent(e.Id,e.TeachingClassId,t.Code,t.Name,t.CourseTitle,t.PrimaryLecturerId,t.AssistantLecturerOneId,t.AssistantLecturerTwoId,e.Type,e.Title,e.Room,e.Location,e.Notes,e.StartsAt,e.EndsAt,e.IsCancelled,e.Revision);
        var rows = await query.ToListAsync(ct);
        if (Role is UserRole.Admin or UserRole.DepartmentHead) return rows;
        var classIds = Role == UserRole.Lecturer
            ? await db.TeachingClasses.AsNoTracking().Where(x => x.PrimaryLecturerId == ActorId || x.AssistantLecturerOneId == ActorId || x.AssistantLecturerTwoId == ActorId).Select(x => x.Id).ToListAsync(ct)
            : await db.OfferingEnrollments.AsNoTracking().Where(x => x.UserId == ActorId && x.Status == EnrollmentStatus.Active).Select(x => x.TeachingClassId).ToListAsync(ct);
        var allowed = classIds.ToHashSet();
        return rows.Where(x => allowed.Contains(x.TeachingClassId)).ToList();
    }

    private void RequireAttendanceRead(TeachingClass offering)
    {
        if (Role is UserRole.Admin or UserRole.DepartmentHead) return;
        RequireAssignedLecturer(offering);
    }

    private void RequireAssignedLecturer(TeachingClass offering)
    {
        if (Role != UserRole.Lecturer || (offering.PrimaryLecturerId != ActorId && offering.AssistantLecturerOneId != ActorId && offering.AssistantLecturerTwoId != ActorId))
            throw new LawLms.Application.UnauthorizedAppException("Chỉ giảng viên được phân công lớp này mới được quản lý điểm danh.");
    }

    private async Task EnsureNoConflict(ScheduleRequest request, TeachingClass offering, Guid? excludeId, CancellationToken ct)
    {
        var overlapping = await db.ScheduleEvents.AsNoTracking().Where(x => !x.IsCancelled && x.StartsAt < request.EndsAt.UtcDateTime && x.EndsAt > request.StartsAt.UtcDateTime && x.Id != excludeId).Join(db.TeachingClasses.AsNoTracking(), x => x.TeachingClassId, t => t.Id, (e,t) => new { Event=e, Class=t }).ToListAsync(ct);
        var roomConflict = overlapping.FirstOrDefault(x => string.Equals(x.Event.Room.Trim(), request.Room.Trim(), StringComparison.OrdinalIgnoreCase));
        if (roomConflict is not null) throw new LawLms.Application.ConflictException($"Phòng {request.Room.Trim()} đã được xếp cho {roomConflict.Class.Code} trong khung giờ này.");
        var lecturerIds = new[] { offering.PrimaryLecturerId, offering.AssistantLecturerOneId, offering.AssistantLecturerTwoId };
        var lecturerConflict = overlapping.FirstOrDefault(x => lecturerIds.Intersect(new[] { x.Class.PrimaryLecturerId, x.Class.AssistantLecturerOneId, x.Class.AssistantLecturerTwoId }).Any());
        if (lecturerConflict is not null) throw new LawLms.Application.ConflictException($"Tổ giảng viên bị trùng lịch với lớp {lecturerConflict.Class.Code}.");
    }

    private void Validate(ScheduleRequest request)
    {
        if (!Enum.IsDefined(request.Type)) throw new LawLms.Application.ConflictException("Loại lịch không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 300 || string.IsNullOrWhiteSpace(request.Room) || request.Room.Trim().Length > 100 || (request.Location?.Trim().Length ?? 0) > 300 || (request.Notes?.Trim().Length ?? 0) > 4000) throw new LawLms.Application.ConflictException("Kiểm tra tên sự kiện, phòng, địa điểm và ghi chú.");
        if (request.EndsAt <= request.StartsAt || request.EndsAt - request.StartsAt > TimeSpan.FromHours(12)) throw new LawLms.Application.ConflictException("Thời lượng phải lớn hơn 0 và không quá 12 giờ.");
    }

    private void AddHistory(ScheduleEvent item, string action, string reason = "")
    {
        var snapshot = new { item.TeachingClassId,item.Type,item.Title,item.Room,item.Location,item.Notes,item.StartsAt,item.EndsAt,item.IsCancelled,item.Revision,reason };
        db.ScheduleChanges.Add(new ScheduleChange { ScheduleEventId=item.Id,ActorId=ActorId,Action=action,SnapshotJson=JsonSerializer.Serialize(snapshot),At=DateTime.UtcNow });
    }

    private async Task NotifyOffering(ScheduleEvent item, TeachingClass offering, string message, CancellationToken ct)
    {
        var recipients = await db.OfferingEnrollments.AsNoTracking().Where(x => x.TeachingClassId == offering.Id && x.Status == EnrollmentStatus.Active).Select(x => x.UserId).ToListAsync(ct);
        recipients.AddRange([offering.PrimaryLecturerId,offering.AssistantLecturerOneId,offering.AssistantLecturerTwoId]);
        var active = await db.Users.AsNoTracking().Where(x => recipients.Contains(x.Id) && x.IsActive).Select(x => x.Id).Distinct().ToListAsync(ct);
        foreach (var userId in active)
            if (!await db.Notifications.AnyAsync(x => x.UserId==userId && x.DedupKey==$"schedule-change:{item.Id}:{item.Revision}",ct)) db.Notifications.Add(new Notification { UserId=userId,TeachingClassId=offering.Id,DedupKey=$"schedule-change:{item.Id}:{item.Revision}",Message=message });
    }

    private static ScheduleDto Map(VisibleScheduleEvent x) => new(x.Id,x.TeachingClassId,x.ClassCode,x.ClassName,x.CourseTitle,x.Type,x.Title,x.Room,x.Location,x.Notes,x.StartsAt,x.EndsAt,x.IsCancelled,x.Revision);
    private static ScheduleDto Map(ScheduleEvent x, TeachingClass t) => new(x.Id,t.Id,t.Code,t.Name,t.CourseTitle,x.Type,x.Title,x.Room,x.Location,x.Notes,x.StartsAt,x.EndsAt,x.IsCancelled,x.Revision);
    private static string IcsEscape(string value) => value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");
}

public sealed record AttendanceMark(Guid StudentId, AttendanceStatus Status);
public sealed record MarkAttendanceRequest(IReadOnlyList<AttendanceMark> Records);
public sealed record AttendanceRosterRow(Guid StudentId, string FullName, AttendanceStatus? Status, DateTime? MarkedAt);
public sealed record AttendanceView(Guid ScheduleEventId, string Title, DateTime StartsAt, DateTime EndsAt, ScheduleEventType Type, bool IsCancelled, IReadOnlyList<AttendanceRosterRow> Students);

public sealed record ScheduleRequest(Guid TeachingClassId, ScheduleEventType Type, string Title, string Room, string Location, string Notes, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record CancelScheduleRequest(string Reason);
public sealed record ScheduleDto(Guid Id, Guid TeachingClassId, string ClassCode, string ClassName, string CourseTitle, ScheduleEventType Type, string Title, string Room, string Location, string Notes, DateTime StartsAt, DateTime EndsAt, bool IsCancelled, int Revision);
internal sealed record VisibleScheduleEvent(Guid Id, Guid TeachingClassId, string ClassCode, string ClassName, string CourseTitle, Guid PrimaryLecturerId, Guid AssistantLecturerOneId, Guid AssistantLecturerTwoId, ScheduleEventType Type, string Title, string Room, string Location, string Notes, DateTime StartsAt, DateTime EndsAt, bool IsCancelled, int Revision);
