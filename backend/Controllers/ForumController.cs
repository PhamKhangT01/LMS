using System.Security.Claims;
using LawLms.Domain;
using LawLms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Api.Controllers;

[ApiController, Authorize, Route("api/forum")]
public sealed class ForumController(LmsDbContext db) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private UserRole Role => Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!);

    [HttpGet("topics")]
    public async Task<IActionResult> Topics([FromQuery] Guid teachingClassId, CancellationToken ct)
    {
        await RequireReadAccess(teachingClassId, ct);
        var rows = await (from topic in db.ForumTopics.AsNoTracking()
                          join author in db.Users.AsNoTracking() on topic.AuthorId equals author.Id
                          where topic.TeachingClassId == teachingClassId
                          orderby topic.LastActivityAt descending
                          select new ForumTopicSummary(topic.Id, topic.TeachingClassId, topic.Title, topic.Body, topic.AuthorId, author.FullName, topic.CreatedAt, topic.LastActivityAt, topic.Revision,
                              db.ForumReplies.Count(reply => reply.TopicId == topic.Id),
                              db.ForumSubscriptions.Any(follow => follow.TopicId == topic.Id && follow.UserId == ActorId),
                              db.ForumReplies.Where(reply => reply.TopicId == topic.Id && reply.IsPinnedAnswer).Select(reply => (Guid?)reply.Id).FirstOrDefault(),
                              db.ForumReplies.Where(reply => reply.TopicId == topic.Id && reply.IsPinnedAnswer).Select(reply => reply.Body).FirstOrDefault()))
            .Take(100).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("topics/{id:guid}")]
    public async Task<IActionResult> Thread(Guid id, CancellationToken ct)
    {
        var topic = await db.ForumTopics.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new LawLms.Application.NotFoundException("Không tìm thấy chủ đề.");
        await RequireReadAccess(topic.TeachingClassId, ct);
        var author = await db.Users.AsNoTracking().Where(x => x.Id == topic.AuthorId).Select(x => x.FullName).SingleAsync(ct);
        var replies = await (from reply in db.ForumReplies.AsNoTracking()
                             join user in db.Users.AsNoTracking() on reply.AuthorId equals user.Id
                             where reply.TopicId == id
                             orderby reply.IsPinnedAnswer descending, reply.CreatedAt
                             select new ForumReplyDto(reply.Id, reply.TopicId, reply.AuthorId, user.FullName, reply.Body, reply.IsPinnedAnswer, reply.CreatedAt))
            .Take(500).ToListAsync(ct);
        var summary = new ForumTopicSummary(topic.Id, topic.TeachingClassId, topic.Title, topic.Body, topic.AuthorId, author, topic.CreatedAt, topic.LastActivityAt, topic.Revision, replies.Count,
            await db.ForumSubscriptions.AnyAsync(x => x.TopicId == id && x.UserId == ActorId, ct),
            replies.Where(x => x.IsPinnedAnswer).Select(x => (Guid?)x.Id).FirstOrDefault(), replies.FirstOrDefault(x => x.IsPinnedAnswer)?.Body);
        return Ok(new ForumThreadDto(summary, replies));
    }

    [HttpPost("topics")]
    public async Task<IActionResult> CreateTopic(CreateForumTopicRequest request, CancellationToken ct)
    {
        await RequirePostAccess(request.TeachingClassId, ct);
        ValidateText(request.Title, 200, "Tiêu đề", 4);
        ValidateText(request.Body, 10000, "Nội dung", 8);
        var now = DateTime.UtcNow;
        var topic = new ForumTopic { TeachingClassId = request.TeachingClassId, AuthorId = ActorId, Title = request.Title.Trim(), Body = request.Body.Trim(), CreatedAt = now, LastActivityAt = now };
        db.ForumTopics.Add(topic);
        db.ForumSubscriptions.Add(new ForumSubscription { TopicId = topic.Id, UserId = ActorId, SubscribedAt = now });
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Thread), new { id = topic.Id }, new { topicId = topic.Id });
    }

    [HttpPost("topics/{id:guid}/replies")]
    public async Task<IActionResult> Reply(Guid id, CreateForumReplyRequest request, CancellationToken ct)
    {
        ValidateText(request.Body, 10000, "Nội dung phản hồi", 2);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2026101702)", ct);
        var topic = await db.ForumTopics.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new LawLms.Application.NotFoundException("Không tìm thấy chủ đề.");
        await RequirePostAccess(topic.TeachingClassId, ct);
        var now = DateTime.UtcNow;
        var reply = new ForumReply { TopicId = id, AuthorId = ActorId, Body = request.Body.Trim(), CreatedAt = now };
        db.ForumReplies.Add(reply);
        topic.LastActivityAt = now; topic.Revision++;
        if (!await db.ForumSubscriptions.AnyAsync(x => x.TopicId == id && x.UserId == ActorId, ct))
            db.ForumSubscriptions.Add(new ForumSubscription { TopicId = id, UserId = ActorId, SubscribedAt = now });
        await db.SaveChangesAsync(ct);
        var followers = await db.ForumSubscriptions.Where(x => x.TopicId == id && x.UserId != ActorId).Select(x => x.UserId).ToListAsync(ct);
        var activeFollowers = await db.Users.AsNoTracking().Where(x => followers.Contains(x.Id) && x.IsActive).Select(x => x.Id).ToListAsync(ct);
        foreach (var follower in activeFollowers)
            db.Notifications.Add(new Notification { UserId = follower, TeachingClassId = topic.TeachingClassId, DedupKey = $"forum-reply:{id}:{reply.Id}", Message = $"Có phản hồi mới trong diễn đàn: {topic.Title}." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CreatedAtAction(nameof(Thread), new { id }, new { replyId = reply.Id });
    }

    [HttpPut("topics/{id:guid}/follow")]
    public async Task<IActionResult> Follow(Guid id, FollowForumTopicRequest request, CancellationToken ct)
    {
        var topic = await db.ForumTopics.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new LawLms.Application.NotFoundException("Không tìm thấy chủ đề.");
        await RequireReadAccess(topic.TeachingClassId, ct);
        var subscription = await db.ForumSubscriptions.SingleOrDefaultAsync(x => x.TopicId == id && x.UserId == ActorId, ct);
        if (request.Follow && subscription is null) db.ForumSubscriptions.Add(new ForumSubscription { TopicId = id, UserId = ActorId });
        if (!request.Follow && subscription is not null) db.ForumSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("replies/{id:guid}/pin")]
    public async Task<IActionResult> Pin(Guid id, PinForumReplyRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2026101702)", ct);
        var reply = await db.ForumReplies.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new LawLms.Application.NotFoundException("Không tìm thấy phản hồi.");
        var topic = await db.ForumTopics.SingleAsync(x => x.Id == reply.TopicId, ct);
        await RequireLecturerAccess(topic.TeachingClassId, ct);
        if (request.Pinned)
        {
            foreach (var previous in await db.ForumReplies.Where(x => x.TopicId == topic.Id && x.IsPinnedAnswer).ToListAsync(ct)) previous.IsPinnedAnswer = false;
            reply.IsPinnedAnswer = true;
        }
        else reply.IsPinnedAnswer = false;
        topic.Revision++; topic.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var followers = await db.ForumSubscriptions.Where(x => x.TopicId == topic.Id && x.UserId != ActorId).Select(x => x.UserId).ToListAsync(ct);
        var activeFollowers = await db.Users.AsNoTracking().Where(x => followers.Contains(x.Id) && x.IsActive).Select(x => x.Id).ToListAsync(ct);
        foreach (var follower in activeFollowers)
            db.Notifications.Add(new Notification { UserId = follower, TeachingClassId = topic.TeachingClassId, DedupKey = $"forum-pin:{topic.Id}:{topic.Revision}:{reply.Id}", Message = request.Pinned ? $"Giảng viên đã ghim câu trả lời trong chủ đề: {topic.Title}." : $"Giảng viên đã bỏ ghim câu trả lời trong chủ đề: {topic.Title}." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    private async Task RequireReadAccess(Guid classId, CancellationToken ct)
    {
        if (Role is UserRole.Admin or UserRole.DepartmentHead) return;
        var canRead = Role == UserRole.Lecturer
            ? await IsAssignedLecturer(classId, ct)
            : await db.OfferingEnrollments.AnyAsync(x => x.TeachingClassId == classId && x.UserId == ActorId && x.Status == EnrollmentStatus.Active, ct);
        if (!canRead) throw new LawLms.Application.UnauthorizedAppException("Bạn không có quyền truy cập diễn đàn lớp học phần này.");
    }

    private async Task RequirePostAccess(Guid classId, CancellationToken ct)
    {
        if (Role is not (UserRole.Student or UserRole.Lecturer)) throw new LawLms.Application.UnauthorizedAppException("Chỉ học viên và giảng viên của lớp mới được đăng bài hoặc phản hồi.");
        var canPost = Role == UserRole.Lecturer
            ? await IsAssignedLecturer(classId, ct)
            : await db.OfferingEnrollments.AnyAsync(x => x.TeachingClassId == classId && x.UserId == ActorId && x.Status == EnrollmentStatus.Active, ct);
        if (!canPost) throw new LawLms.Application.UnauthorizedAppException("Bạn không được đăng trong diễn đàn lớp học phần này.");
    }

    private async Task RequireLecturerAccess(Guid classId, CancellationToken ct)
    {
        if (Role != UserRole.Lecturer || !await IsAssignedLecturer(classId, ct))
            throw new LawLms.Application.UnauthorizedAppException("Chỉ giảng viên được phân công lớp này mới được ghim câu trả lời.");
    }

    private Task<bool> IsAssignedLecturer(Guid classId, CancellationToken ct) => db.TeachingClasses.AnyAsync(x => x.Id == classId && (x.PrimaryLecturerId == ActorId || x.AssistantLecturerOneId == ActorId || x.AssistantLecturerTwoId == ActorId), ct);
    private static void ValidateText(string? value, int max, string label, int min) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < min || value.Trim().Length > max) throw new LawLms.Application.ConflictException($"{label} phải từ {min} đến {max} ký tự."); }
}

public sealed record CreateForumTopicRequest(Guid TeachingClassId, string Title, string Body);
public sealed record CreateForumReplyRequest(string Body);
public sealed record FollowForumTopicRequest(bool Follow);
public sealed record PinForumReplyRequest(bool Pinned);
public sealed record ForumTopicSummary(Guid Id, Guid TeachingClassId, string Title, string Body, Guid AuthorId, string Author, DateTime CreatedAt, DateTime LastActivityAt, int Revision, int ReplyCount, bool IsFollowing, Guid? PinnedReplyId, string? PinnedReplyBody);
public sealed record ForumReplyDto(Guid Id, Guid TopicId, Guid AuthorId, string Author, string Body, bool IsPinnedAnswer, DateTime CreatedAt);
public sealed record ForumThreadDto(ForumTopicSummary Topic, IReadOnlyList<ForumReplyDto> Replies);
