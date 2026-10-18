using LawLms.Application;
using LawLms.Domain;
using LawLms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Infrastructure.Repositories;

public sealed class UserRepository(LmsDbContext db) : IUserRepository
{
    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct) => db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
    public async Task<IReadOnlyList<AppUser>> GetLecturersAsync(CancellationToken ct) => await db.Users.AsNoTracking().Where(x => x.Role == UserRole.Lecturer).OrderBy(x => x.FullName).ToListAsync(ct);
    public async Task<IReadOnlyList<AppUser>> GetStudentsByClassCodeAsync(string classCode, CancellationToken ct) => await db.Users.AsNoTracking().Where(x => x.Role == UserRole.Student && x.ClassCode == classCode).OrderBy(x => x.FullName).ToListAsync(ct);
    public async Task<IReadOnlyList<AppUser>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
}
public sealed class CourseRepository(LmsDbContext db) : ICourseRepository
{
    public Task AddLessonAsync(Lesson lesson, CancellationToken ct) => db.Lessons.AddAsync(lesson, ct).AsTask();
    public async Task<IReadOnlyList<Course>> GetAllAsync(CancellationToken ct) => await db.Courses.AsNoTracking().Include(x => x.Lecturer).Include(x => x.Lessons).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    public async Task<IReadOnlyList<Course>> GetPublishedAsync(CancellationToken ct) => await db.Courses.AsNoTracking().Where(x => x.IsPublished).Include(x => x.Lecturer).Include(x => x.Lessons).OrderBy(x => x.Title).ToListAsync(ct);
    public async Task<IReadOnlyList<Course>> GetForLecturerAsync(Guid lecturerId, CancellationToken ct) => await db.Courses.AsNoTracking().Where(x => x.LecturerId == lecturerId).Include(x => x.Lecturer).Include(x => x.Lessons).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    public async Task<IReadOnlyList<Course>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => await db.Courses.AsNoTracking().Where(x => ids.Contains(x.Id)).Include(x => x.Lecturer).Include(x => x.Lessons).OrderBy(x => x.Title).ToListAsync(ct);
    public Task<Course?> GetDetailAsync(Guid id, CancellationToken ct) => db.Courses.Include(x => x.Lecturer).Include(x => x.Lessons).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task AddAsync(Course course, CancellationToken ct) => db.Courses.AddAsync(course, ct).AsTask();
    public void Remove(Course course) => db.Courses.Remove(course);
}
public sealed class EnrollmentRepository(LmsDbContext db) : IEnrollmentRepository
{
    public Task<bool> ExistsAsync(Guid userId, Guid courseId, CancellationToken ct) => db.OfferingEnrollments.AnyAsync(x => x.UserId == userId && x.Status != EnrollmentStatus.Withdrawn && db.TeachingClasses.Any(t => t.Id == x.TeachingClassId && t.CourseId == courseId && t.Status >= OfferingStatus.Active), ct);
    public Task<bool> AnyForCourseAsync(Guid courseId, CancellationToken ct) => db.Enrollments.AnyAsync(x => x.CourseId == courseId, ct);
    public Task AddAsync(Enrollment enrollment, CancellationToken ct) => db.Enrollments.AddAsync(enrollment, ct).AsTask();
    public async Task<IReadOnlyList<Enrollment>> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        var rows = await (from e in db.OfferingEnrollments.AsNoTracking() join t in db.TeachingClasses on e.TeachingClassId equals t.Id join c in db.Courses.Include(x => x.Lecturer) on t.CourseId equals c.Id
            where e.UserId == userId && e.Status != EnrollmentStatus.Withdrawn && t.Status >= OfferingStatus.Active
            select new Enrollment { Id = e.Id, UserId = e.UserId, CourseId = c.Id, Course = c, EnrolledAt = e.EnrolledAt,
                ProgressPercent = db.Lessons.Count(l => l.CourseId == c.Id) == 0 ? 0 : db.LessonCompletions.Count(l => l.EnrollmentId == e.Id) * 100 / db.Lessons.Count(l => l.CourseId == c.Id) }).ToListAsync(ct);
        return rows.OrderByDescending(x => x.EnrolledAt).GroupBy(x => x.CourseId).Select(x => x.First()).ToList();
    }
}
public sealed class TeachingClassRepository(LmsDbContext db) : ITeachingClassRepository
{
    public async Task SyncRosterAsync(TeachingClass item, CancellationToken ct)
    {
        var students = await db.Users.Where(x => x.Role == UserRole.Student && x.IsActive && x.ClassCode == item.Code).Select(x => x.Id).ToListAsync(ct);
        var existing = await db.OfferingEnrollments.Where(x => x.TeachingClassId == item.Id).ToListAsync(ct);
        foreach (var enrollment in existing) enrollment.Status = students.Contains(enrollment.UserId) ? EnrollmentStatus.Active : EnrollmentStatus.Withdrawn;
        foreach (var student in students.Where(x => existing.All(e => e.UserId != x))) db.OfferingEnrollments.Add(new() { TeachingClassId = item.Id, UserId = student });
    }

    public Task AddAsync(TeachingClass teachingClass, CancellationToken ct) => db.TeachingClasses.AddAsync(teachingClass, ct).AsTask();
    public Task<TeachingClass?> GetAsync(Guid id, CancellationToken ct) => db.TeachingClasses.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<TeachingClass>> GetAllAsync(CancellationToken ct) => await db.TeachingClasses.AsNoTracking().OrderByDescending(x => x.AssignedAt).ToListAsync(ct);
    public async Task<IReadOnlyList<TeachingClass>> GetForLecturerAsync(Guid lecturerId, CancellationToken ct) => await db.TeachingClasses.AsNoTracking().Where(x => x.PrimaryLecturerId == lecturerId || x.AssistantLecturerOneId == lecturerId || x.AssistantLecturerTwoId == lecturerId).OrderByDescending(x => x.AssignedAt).ToListAsync(ct);
    public void Remove(TeachingClass teachingClass) => db.TeachingClasses.Remove(teachingClass);
}
public sealed class LibraryResourceRepository(LmsDbContext db) : ILibraryResourceRepository
{
    public Task AddAsync(LibraryResource resource, CancellationToken ct) => db.LibraryResources.AddAsync(resource, ct).AsTask();
    public Task<LibraryResource?> GetAsync(Guid id, CancellationToken ct) => db.LibraryResources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<LibraryResource>> SearchAsync(string? query, CancellationToken ct) { IQueryable<LibraryResource> rows = db.LibraryResources.AsNoTracking(); if (!string.IsNullOrWhiteSpace(query)) { var term = query.Trim().ToLower(); rows = rows.Where(x => x.Title.ToLower().Contains(term) || x.Category.ToLower().Contains(term) || x.OriginalFileName.ToLower().Contains(term)); } return await rows.OrderByDescending(x => x.UploadedAt).ToListAsync(ct); }
}
public sealed class QuizRepository(LmsDbContext db) : IQuizRepository
{
    public Task AddAsync(Quiz quiz, CancellationToken ct) => db.Quizzes.AddAsync(quiz, ct).AsTask();
    public Task<Quiz?> GetAsync(Guid id, CancellationToken ct) => db.Quizzes.Include(x => x.Questions).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<bool> AnyForCourseAsync(Guid courseId, CancellationToken ct) => db.Quizzes.AnyAsync(x => x.CourseId == courseId, ct);
    public async Task<IReadOnlyList<Quiz>> GetForCourseAndClassAsync(Guid courseId, string classCode, CancellationToken ct) => await db.Quizzes.AsNoTracking().Include(x => x.Questions).Where(x => x.CourseId == courseId && x.ClassCode == classCode).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    public async Task<IReadOnlyList<Quiz>> GetForCreatorAsync(Guid userId, CancellationToken ct) => await db.Quizzes.AsNoTracking().Include(x => x.Questions).Where(x => x.CreatedById == userId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    public Task<int> AttemptCountAsync(Guid quizId, Guid studentId, CancellationToken ct) => db.QuizAttempts.CountAsync(x => x.QuizId == quizId && x.StudentId == studentId, ct);
    public Task AddAttemptAsync(QuizAttempt attempt, CancellationToken ct) => db.QuizAttempts.AddAsync(attempt, ct).AsTask();
}
