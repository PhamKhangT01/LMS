using System.Data;
using System.Text;
using LawLms.Application;
using LawLms.Domain;
using LawLms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Infrastructure.Services;

public sealed class AcademicReportService(LmsDbContext db) : IAcademicReportService
{
    private async Task<AcademicReport> Build(AcademicReportFilter filter, Actor actor, CancellationToken ct)
    {
        if (!actor.IsHead && actor.Role != UserRole.Lecturer) throw new UnauthorizedAppException("Báo cáo học vụ chỉ dành cho Trưởng khoa và giảng viên được phân công.");
        if (filter.State is not ("running" or "all" or "closed") || filter.Risk is not ("all" or "missing" or "inactive" or "progress" or "failed") || filter.InactiveDays is < 1 or > 90 || filter.ProgressLag is < 0 or > 100 || filter.Page < 1 || filter.PageSize is < 1 or > 200)
            throw new ConflictException("Bộ lọc báo cáo không hợp lệ.");
        var now = DateTime.UtcNow;
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var scope = db.TeachingClasses.AsNoTracking().AsQueryable();
        if (!actor.IsHead) scope = scope.Where(x => x.PrimaryLecturerId == actor.Id || x.AssistantLecturerOneId == actor.Id || x.AssistantLecturerTwoId == actor.Id);
        var allowed = await scope.OrderByDescending(x => x.AcademicYear).ThenBy(x => x.Term).ThenBy(x => x.Code).ThenBy(x => x.Id).ToListAsync(ct);
        var offerings = allowed.Where(x => (string.IsNullOrWhiteSpace(filter.AcademicYear) || x.AcademicYear == filter.AcademicYear) &&
            (string.IsNullOrWhiteSpace(filter.Term) || x.Term == filter.Term) && (!filter.OfferingId.HasValue || x.Id == filter.OfferingId) &&
            (filter.State == "all" || filter.State == "running" && x.Status is OfferingStatus.Active or OfferingStatus.Grading || filter.State == "closed" && x.Status >= OfferingStatus.Completed)).ToList();
        var ids = offerings.Select(x => x.Id).ToArray();
        var courseIds = offerings.Select(x => x.CourseId).Distinct().ToArray();
        var roster = await (from e in db.OfferingEnrollments.AsNoTracking() join u in db.Users.AsNoTracking() on e.UserId equals u.Id
            where ids.Contains(e.TeachingClassId) && e.Status != EnrollmentStatus.Withdrawn
            select new { e.Id, e.TeachingClassId, e.UserId, e.EnrolledAt, u.FullName, u.Email }).ToListAsync(ct);
        var enrollmentIds = roster.Select(x => x.Id).ToArray();
        var lessons = await db.Lessons.AsNoTracking().Where(x => courseIds.Contains(x.CourseId)).Select(x => new { x.Id, x.CourseId }).ToListAsync(ct);
        var completed = await db.LessonCompletions.AsNoTracking().Where(x => enrollmentIds.Contains(x.EnrollmentId)).Select(x => new { x.EnrollmentId, x.LessonId }).ToListAsync(ct);
        var quizzes = await db.Quizzes.AsNoTracking().Where(x => x.TeachingClassId.HasValue && ids.Contains(x.TeachingClassId.Value) && (x.Status == QuizStatus.Published || x.Status == QuizStatus.Closed))
            .Select(x => new { x.Id, x.TeachingClassId, x.Title, x.Status, x.ClosesAt, x.Weight, Maximum = x.Questions.Sum(q => q.Points) }).ToListAsync(ct);
        var quizIds = quizzes.Select(x => x.Id).ToArray();
        var attempts = await db.QuizAttempts.AsNoTracking().Where(x => quizIds.Contains(x.QuizId)).Select(x => new { x.Id, x.QuizId, x.StudentId, x.Status, x.FinalScore, x.SubmittedAt }).ToListAsync(ct);
        var allowances = await db.QuizAllowances.AsNoTracking().Where(x => quizIds.Contains(x.QuizId)).ToListAsync(ct);
        var activities = await db.AuditEntries.AsNoTracking().Where(x => ids.Contains(x.EntityId) && (x.Action == "lesson.complete" || x.Action == "attempt.start" || x.Action == "attempt.submit"))
            .GroupBy(x => new { x.EntityId, x.ActorId }).Select(x => new { x.Key.EntityId, x.Key.ActorId, LastAt = x.Max(a => a.At) }).ToListAsync(ct);
        var appeals = await db.GradeAppeals.AsNoTracking().Where(x => ids.Contains(x.TeachingClassId) && (x.Status == AppealStatus.Pending || x.Status == AppealStatus.Reviewed))
            .Select(x => new { x.TeachingClassId, x.StudentId }).ToListAsync(ct);
        await snapshot.CommitAsync(ct);

        var completedCounts = completed.GroupBy(x => x.EnrollmentId).ToDictionary(x => x.Key, x => x.Count());
        var lessonCounts = lessons.GroupBy(x => x.CourseId).ToDictionary(x => x.Key, x => x.Count());
        var activityLookup = activities.ToDictionary(x => (x.EntityId, x.ActorId), x => x.LastAt);
        var attemptsByStudentQuiz = attempts.ToLookup(x => (x.StudentId, x.QuizId));
        var allowanceLookup = allowances.ToDictionary(x => (x.StudentId, x.QuizId));
        var rows = new List<StudentRiskReport>();
        var classReports = new List<OfferingReport>();
        var sumProgress = 0; var publishedCount = 0; var passedCount = 0;
        foreach (var offering in offerings)
        {
            var members = roster.Where(x => x.TeachingClassId == offering.Id).ToList();
            var assessments = quizzes.Where(x => x.TeachingClassId == offering.Id).ToList();
            var expected = AcademicReportRules.ExpectedProgress(offering.Status, offering.StartsAt, offering.EndsAt, offering.RequiredProgress, now);
            var published = offering.GradeStatus is GradebookStatus.Published or GradebookStatus.Locked;
            var classProgress = 0; var classRisk = 0; var missingCount = 0; var pendingCount = 0; var passed = 0;
            foreach (var member in members)
            {
                var totalLessons = lessonCounts.GetValueOrDefault(offering.CourseId);
                var progress = totalLessons == 0 ? 0 : completedCounts.GetValueOrDefault(member.Id) * 100 / totalLessons;
                DateTime? lastActivity = activityLookup.TryGetValue((offering.Id, member.UserId), out var last) ? last : null;
                var missing = 0; var pending = 0;
                var grades = new List<AssessmentGrade>();
                foreach (var quiz in assessments)
                {
                    var own = attemptsByStudentQuiz[(member.UserId, quiz.Id)].ToList();
                    allowanceLookup.TryGetValue((member.UserId, quiz.Id), out var allowance);
                    var closes = quiz.Status == QuizStatus.Closed ? quiz.ClosesAt : allowance?.ExtendedUntil ?? quiz.ClosesAt;
                    if (closes <= now && !own.Any(x => x.Status != AttemptStatus.InProgress && x.SubmittedAt.HasValue)) missing++;
                    pending += own.Count(x => x.Status == AttemptStatus.Submitted && !x.FinalScore.HasValue);
                    var best = own.Where(x => x.FinalScore.HasValue && x.Status == AttemptStatus.Graded).OrderByDescending(x => x.FinalScore).ThenBy(x => x.SubmittedAt).FirstOrDefault();
                    grades.Add(new(quiz.Id, quiz.Title, quiz.Weight, best?.Id, best?.FinalScore, quiz.Maximum, own.Any(x => x.Status == AttemptStatus.Submitted)));
                }
                var passedFinal = AssessmentRules.Total(grades) >= offering.PassingGrade && progress >= offering.RequiredProgress;
                var reasons = new List<string>();
                if (offering.Status is OfferingStatus.Active or OfferingStatus.Grading)
                {
                    if (missing > 0) reasons.Add("missing");
                    if (offering.Status == OfferingStatus.Active && AcademicReportRules.IsInactive(offering.StartsAt, member.EnrolledAt, lastActivity, filter.InactiveDays, now)) reasons.Add("inactive");
                    if (progress + filter.ProgressLag < expected) reasons.Add("progress");
                }
                if (published && !passedFinal) reasons.Add("failed");
                if (reasons.Count > 0) { classRisk++; rows.Add(new(member.UserId, member.FullName, member.Email, offering.Id, offering.Code, offering.CourseTitle, offering.AcademicYear, offering.Term, progress, expected, lastActivity, missing, pending, reasons)); }
                classProgress += progress; missingCount += missing; pendingCount += pending;
                if (published) { publishedCount++; if (passedFinal) { passed++; passedCount++; } }
            }
            sumProgress += classProgress;
            classReports.Add(new(offering.Id, offering.Code, offering.CourseTitle, offering.AcademicYear, offering.Term, offering.Status, offering.GradeStatus, members.Count,
                members.Count == 0 ? 0 : Math.Round((decimal)classProgress / members.Count, 1), expected, classRisk, missingCount, pendingCount, appeals.Count(x => x.TeachingClassId == offering.Id), published ? passed : null));
        }
        var matches = rows.Where(x => filter.Risk == "all" || x.Reasons.Contains(filter.Risk)).OrderByDescending(x => x.Reasons.Count).ThenByDescending(x => x.MissingSubmissions)
            .ThenBy(x => x.Progress).ThenBy(x => x.FullName, StringComparer.Ordinal).ThenBy(x => x.OfferingId).ThenBy(x => x.StudentId).ToList();
        return new(now, allowed.Select(x => x.AcademicYear).Distinct().ToList(), allowed.Select(x => x.Term).Distinct().Order().ToList(),
            new(classReports.Count, roster.Count, roster.Select(x => x.UserId).Distinct().Count(), roster.Count == 0 ? 0 : Math.Round((decimal)sumProgress / roster.Count, 1), rows.Count, classReports.Sum(x => x.MissingSubmissions), classReports.Sum(x => x.PendingGrading), appeals.Count, publishedCount, passedCount),
            classReports, matches, matches.Count, filter.Page, filter.PageSize);
    }
    public async Task<AcademicReport> GetAsync(AcademicReportFilter filter, Actor actor, CancellationToken ct)
    {
        var all = await Build(filter, actor, ct);
        var offset = (long)(filter.Page - 1) * filter.PageSize;
        return all with { Students = offset >= all.Students.Count ? [] : all.Students.Skip((int)offset).Take(filter.PageSize).ToList() };
    }
    public async Task<string> ExportAsync(AcademicReportFilter filter, string kind, Actor actor, CancellationToken ct)
    {
        if (kind is not ("classes" or "students")) throw new ConflictException("Loại báo cáo không hợp lệ.");
        var report = await Build(filter, actor, ct); var csv = new StringBuilder();
        void Row(params object?[] values) => csv.AppendLine(string.Join(',', values.Select(AcademicReportRules.CsvCell)));
        if (kind == "classes")
        {
            Row("Mã lớp", "Học phần", "Năm học", "Học kỳ", "Giai đoạn", "Lượt ghi danh", "Tiến độ trung bình (%)", "Mốc tiến độ (%)", "Cần hỗ trợ", "Bài chưa nộp", "Bài chờ chấm", "Phúc khảo tồn", "Đạt sau công bố", "Thời điểm báo cáo UTC");
            foreach (var x in report.Offerings) Row(x.Code, x.CourseTitle, x.AcademicYear, x.Term, x.Status, x.Enrollments, x.AverageProgress, x.ExpectedProgress, x.AtRiskEnrollments, x.MissingSubmissions, x.PendingGrading, x.PendingAppeals, x.PublishedPassed, report.GeneratedAt.ToString("O"));
        }
        else
        {
            Row("Họ tên", "Email", "Mã lớp", "Học phần", "Năm học", "Học kỳ", "Tiến độ (%)", "Mốc tiến độ (%)", "Hoạt động cuối UTC", "Bài chưa nộp", "Bài chờ chấm", "Lý do cần hỗ trợ", "Thời điểm báo cáo UTC");
            foreach (var x in report.Students) Row(x.FullName, x.Email, x.ClassCode, x.CourseTitle, x.AcademicYear, x.Term, x.Progress, x.ExpectedProgress, x.LastActivityAt?.ToString("O"), x.MissingSubmissions, x.PendingGrading, string.Join("; ", x.Reasons), report.GeneratedAt.ToString("O"));
        }
        return csv.ToString();
    }
}
