using LawLms.Application;
using LawLms.Domain;
using Xunit;

public class AssessmentRulesTests
{
    [Fact] public void DeadlineUsesEarlierClosingTime()
    {
        var start=DateTime.UtcNow;Assert.Equal(start.AddMinutes(5),AssessmentRules.Deadline(start,30,start.AddMinutes(5)));
        Assert.Equal(start.AddMinutes(30),AssessmentRules.Deadline(start,30,start.AddHours(2)));
    }
    [Theory]
    [InlineData("[\"B\",\"A\"]","[\"A\",\"B\"]",4)]
    [InlineData("[\"A\"]","[\"A\",\"B\"]",0)]
    [InlineData("[\"A\",\"B\",\"C\"]","[\"A\",\"B\"]",0)]
    [InlineData("broken","[\"A\"]",0)]
    public void MultipleChoiceUsesAnUnorderedExactSet(string answer,string correct,decimal expected)
        =>Assert.Equal(expected,AssessmentRules.Score(QuestionType.MultipleChoice,answer,correct,4));
    [Fact] public void WeightedGradeNormalizesDifferentMaximumScores()
    {
        var result=AssessmentRules.Total([new(Guid.NewGuid(),"Midterm",40,null,8,10,false),new(Guid.NewGuid(),"Final",60,null,15,20,false)]);
        Assert.Equal(7.7m,result);
    }
    [Fact] public void MissingSubmissionContributesZero()
        =>Assert.Equal(4,AssessmentRules.Total([new(Guid.NewGuid(),"A",40,null,10,10,false),new(Guid.NewGuid(),"B",60,null,null,20,false)]));
}
