using LawLms.Domain;

namespace LawLms.Application;

public record AcademicReportFilter(string? AcademicYear = null, string? Term = null, Guid? OfferingId = null,
    string State = "running", string Risk = "all", int InactiveDays = 7, int ProgressLag = 20, int Page = 1, int PageSize = 25);
public record AcademicReportTotals(int Offerings, int Enrollments, int DistinctStudents, decimal AverageProgress,
    int AtRiskEnrollments, int MissingSubmissions, int PendingGrading, int PendingAppeals, int PublishedEnrollments, int PublishedPassed);
public record OfferingReport(Guid Id, string Code, string CourseTitle, string AcademicYear, string Term,
    OfferingStatus Status, GradebookStatus GradeStatus, int Enrollments, decimal AverageProgress, int ExpectedProgress,
    int AtRiskEnrollments, int MissingSubmissions, int PendingGrading, int PendingAppeals, int? PublishedPassed);
public record StudentRiskReport(Guid StudentId, string FullName, string Email, Guid OfferingId, string ClassCode,
    string CourseTitle, string AcademicYear, string Term, int Progress, int ExpectedProgress, DateTime? LastActivityAt,
    int MissingSubmissions, int PendingGrading, IReadOnlyList<string> Reasons);
public record AcademicReport(DateTime GeneratedAt, IReadOnlyList<string> AcademicYears, IReadOnlyList<string> Terms,
    AcademicReportTotals Totals, IReadOnlyList<OfferingReport> Offerings, IReadOnlyList<StudentRiskReport> Students,
    int TotalMatchingStudents, int Page, int PageSize);
public interface IAcademicReportService
{
    Task<AcademicReport> GetAsync(AcademicReportFilter filter, Actor actor, CancellationToken ct);
    Task<string> ExportAsync(AcademicReportFilter filter, string kind, Actor actor, CancellationToken ct);
}

public static class AcademicReportRules
{
    public static int ExpectedProgress(OfferingStatus status, DateTime? startsAt, DateTime? endsAt, int requiredProgress, DateTime now)
    {
        if (status >= OfferingStatus.Grading) return requiredProgress;
        if (status != OfferingStatus.Active || !startsAt.HasValue || !endsAt.HasValue || endsAt <= startsAt) return 0;
        return (int)Math.Clamp(Math.Floor((now - startsAt.Value).TotalSeconds / (endsAt.Value - startsAt.Value).TotalSeconds * requiredProgress), 0, requiredProgress);
    }
    public static bool IsInactive(DateTime? startsAt, DateTime enrolledAt, DateTime? lastActivity, int days, DateTime now)
    {
        if (!startsAt.HasValue) return false;
        var baseline = startsAt.Value > enrolledAt ? startsAt.Value : enrolledAt;
        return baseline <= now.AddDays(-days) && (!lastActivity.HasValue || lastActivity <= now.AddDays(-days));
    }
    public static string CsvCell(object? value)
    {
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        if (text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') || text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@')) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
