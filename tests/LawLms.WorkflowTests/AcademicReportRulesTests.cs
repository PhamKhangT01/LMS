using LawLms.Application;
using LawLms.Domain;
using Xunit;

public class AcademicReportRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData(-20, 0)]
    [InlineData(0, 40)]
    [InlineData(20, 80)]
    public void PaceClampsToRequiredProgress(int elapsedOffset, int expected)
        => Assert.Equal(expected, AcademicReportRules.ExpectedProgress(OfferingStatus.Active, Now.AddDays(-10), Now.AddDays(10), 80, Now.AddDays(elapsedOffset)));
    [Fact] public void MissingDatesDoNotInventPace()
        => Assert.Equal(0, AcademicReportRules.ExpectedProgress(OfferingStatus.Active, null, null, 100, Now));
    [Fact] public void GradingExpectsRequiredProgress()
        => Assert.Equal(80, AcademicReportRules.ExpectedProgress(OfferingStatus.Grading, null, null, 80, Now));
    [Fact] public void NewEnrollmentDoesNotTriggerInactivity()
        => Assert.False(AcademicReportRules.IsInactive(Now.AddDays(-30), Now.AddDays(-1), null, 7, Now));
    [Fact] public void HumanActivityResetsInactivity()
    {
        Assert.True(AcademicReportRules.IsInactive(Now.AddDays(-30), Now.AddDays(-20), null, 7, Now));
        Assert.False(AcademicReportRules.IsInactive(Now.AddDays(-30), Now.AddDays(-20), Now.AddDays(-1), 7, Now));
    }
    [Theory]
    [InlineData("=SUM(1,1)", "\"'=SUM(1,1)\"")]
    [InlineData(" a\"b", "\" a\"\"b\"")]
    public void CsvQuotesAndNeutralizesFormulas(string input, string expected)
        => Assert.Equal(expected, AcademicReportRules.CsvCell(input));
}
