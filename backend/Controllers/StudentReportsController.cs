using System.Security.Claims;
using LawLms.Domain;
using LawLms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Api.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/student/reports")]
public sealed class StudentReportsController(LmsDbContext db) : ControllerBase
{
    private Guid StudentId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("learning")]
    public async Task<IActionResult> Learning(CancellationToken ct)
    {
        var enrollments = await (from enrollment in db.OfferingEnrollments.AsNoTracking()
                                 join offering in db.TeachingClasses.AsNoTracking() on enrollment.TeachingClassId equals offering.Id
                                 where enrollment.UserId == StudentId && enrollment.Status != EnrollmentStatus.Withdrawn && offering.Status >= OfferingStatus.Active
                                 orderby offering.StartsAt descending
                                 select new { Enrollment = enrollment, Offering = offering }).ToListAsync(ct);
        var rows = new List<StudentLearningOffering>();
        foreach (var row in enrollments)
        {
            var lessons = await db.Lessons.AsNoTracking().Where(x => x.CourseId == row.Offering.CourseId).OrderBy(x => x.Position).Select(x => new { x.Id, x.Title }).ToListAsync(ct);
            var completedIds = await db.LessonCompletions.AsNoTracking().Where(x => x.EnrollmentId == row.Enrollment.Id).Select(x => x.LessonId).ToListAsync(ct);
            var completed = completedIds.ToHashSet();
            var lessonRows = lessons.Select(x => new StudentLessonProgress(x.Id, x.Title, completed.Contains(x.Id))).ToList();
            var sessions = await db.ScheduleEvents.AsNoTracking().Where(x => x.TeachingClassId == row.Offering.Id && x.Type == ScheduleEventType.ClassSession && !x.IsCancelled && x.EndsAt <= DateTime.UtcNow).OrderBy(x => x.StartsAt).ToListAsync(ct);
            var attendance = await db.AttendanceRecords.AsNoTracking().Where(x => x.StudentId == StudentId && sessions.Select(s => s.Id).Contains(x.ScheduleEventId)).ToListAsync(ct);
            var present = attendance.Count(x => x.Status == AttendanceStatus.Present);
            var absent = attendance.Count(x => x.Status == AttendanceStatus.Absent);
            var excused = attendance.Count(x => x.Status == AttendanceStatus.Excused);
            var marked = present + absent + excused;
            var attendanceSummary = new StudentAttendanceSummary(sessions.Count, marked, present, absent, excused, sessions.Count - marked, marked == 0 ? null : Math.Round(100m * present / marked, 1));

            var published = row.Offering.GradeStatus is GradebookStatus.Published or GradebookStatus.Locked;
            var components = new List<StudentAssessmentComponent>();
            decimal? total = null;
            if (published)
            {
                var quizzes = await db.Quizzes.AsNoTracking().Include(x => x.Questions)
                    .Where(x => x.TeachingClassId == row.Offering.Id && (x.Status == QuizStatus.Published || x.Status == QuizStatus.Closed))
                    .OrderBy(x => x.CreatedAt).ToListAsync(ct);
                var quizIds = quizzes.Select(x => x.Id).ToList();
                var attempts = await db.QuizAttempts.AsNoTracking().Where(x => quizIds.Contains(x.QuizId) && x.StudentId == StudentId && x.Status != AttemptStatus.InProgress).ToListAsync(ct);
                foreach (var quiz in quizzes)
                {
                    var best = attempts.Where(x => x.QuizId == quiz.Id && x.FinalScore.HasValue).OrderByDescending(x => x.FinalScore).ThenBy(x => x.SubmittedAt).FirstOrDefault();
                    var maximum = quiz.Questions.Sum(x => x.Points);
                    decimal? contribution = best is null || maximum <= 0 ? null : Math.Round(best.FinalScore!.Value / maximum * 10m * quiz.Weight / 100m, 2, MidpointRounding.AwayFromZero);
                    components.Add(new StudentAssessmentComponent(quiz.Id, quiz.Title, quiz.Weight, best?.FinalScore, maximum, contribution));
                }
                total = components.Sum(x => x.WeightedContribution ?? 0m);
            }
            rows.Add(new StudentLearningOffering(row.Offering.Id, row.Offering.Code, row.Offering.CourseTitle, row.Offering.AcademicYear, row.Offering.Term, row.Offering.Status,
                lessons.Count == 0 ? 0 : completed.Count * 100 / lessons.Count, lessonRows, attendanceSummary, published, row.Offering.GradesPublishedAt, components, total));
        }
        return Ok(new StudentLearningReport(DateTime.UtcNow, rows));
    }
}

public sealed record StudentLessonProgress(Guid Id, string Title, bool Completed);
public sealed record StudentAttendanceSummary(int HeldSessions, int RecordedSessions, int Present, int Absent, int Excused, int Unrecorded, decimal? AttendanceRate);
public sealed record StudentAssessmentComponent(Guid QuizId, string Title, decimal Weight, decimal? Score, decimal Maximum, decimal? WeightedContribution);
public sealed record StudentLearningOffering(Guid Id, string Code, string CourseTitle, string AcademicYear, string Term, OfferingStatus Status, int Progress, IReadOnlyList<StudentLessonProgress> Lessons, StudentAttendanceSummary Attendance, bool GradesPublished, DateTime? PublishedAt, IReadOnlyList<StudentAssessmentComponent> Components, decimal? TotalGrade);
public sealed record StudentLearningReport(DateTime GeneratedAt, IReadOnlyList<StudentLearningOffering> Offerings);
