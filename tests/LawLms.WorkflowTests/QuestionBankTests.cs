using System.Text.Json;
using LawLms.Application;
using LawLms.Domain;
using Xunit;

public class QuestionBankTests
{
    [Fact] public void InvalidMultipleChoiceAnswersAndDuplicateOptionsAreRejected()
    {
        var row=new CreateQuizQuestionCommand("Câu hỏi",QuestionType.MultipleChoice,["A","B"],"[\"A\"]",1);
        BankQuestionRules.Validate(row,"Chủ đề",QuestionDifficulty.Medium);
        Assert.Throws<ConflictException>(()=>BankQuestionRules.Validate(row with {CorrectAnswer="[\"C\"]"},"",QuestionDifficulty.Easy));
        Assert.Throws<ConflictException>(()=>BankQuestionRules.Validate(row with {Options=["A","A"]},"",QuestionDifficulty.Easy));
        Assert.Throws<ConflictException>(()=>BankQuestionRules.Validate(row with {CorrectAnswer="A"},"",QuestionDifficulty.Easy));
    }
    [Fact] public void EssayGuideChangesContentIdentityButClassificationAndPointsDoNot()
    {
        var row=new CreateQuizQuestionCommand("Phân tích tình huống",QuestionType.Essay,[],"Tiêu chí A",5);
        Assert.Equal(BankQuestionRules.Fingerprint(row),BankQuestionRules.Fingerprint(row with {Points=10}));
        Assert.NotEqual(BankQuestionRules.Fingerprint(row),BankQuestionRules.Fingerprint(row with {CorrectAnswer="Tiêu chí B"}));
        Assert.Throws<ConflictException>(()=>BankQuestionRules.Validate(row with {Options=["A"]},"",QuestionDifficulty.Easy));
    }
    private static Quiz Quiz(bool shuffle=true) => new() {ShuffleQuestions=shuffle,ShuffleOptions=shuffle,Questions=Enumerable.Range(1,12).Select(i=>new QuizQuestion{Position=i,Content="Câu "+i,Type=i==12?QuestionType.Essay:QuestionType.SingleChoice,OptionsJson=i==12?"[]":"[\"A\",\"B\",\"C\",\"D\"]",CorrectAnswer=i==12?"Hướng dẫn chấm nội bộ":"B",Points=1}).ToList()};
    [Fact] public void RandomPresentationIsStableAcrossResumeAndNeverExposesSolutions()
    {
        var quiz=Quiz();var attempt=new QuizAttempt();QuizPresentation.Initialize(quiz,attempt);
        var first=QuizPresentation.Questions(quiz,attempt);
        var restored=new QuizAttempt{QuestionOrderJson=attempt.QuestionOrderJson,OptionOrderJson=attempt.OptionOrderJson};
        var again=QuizPresentation.Questions(quiz,restored);
        Assert.Equal(JsonSerializer.Serialize(first),JsonSerializer.Serialize(again));
        Assert.Equal(quiz.Questions.Select(x=>x.Id).Order(),first.Select(x=>x.Id).Order());
        foreach(var q in first)
        {
            var original=quiz.Questions.Single(x=>x.Id==q.Id);
            Assert.Equal(JsonSerializer.Deserialize<string[]>(original.OptionsJson)!.Order(),q.Options.Order());
            Assert.Equal(original.Points,q.Points);
        }
        Assert.DoesNotContain("CorrectAnswer",JsonSerializer.Serialize(first));
        Assert.DoesNotContain("Hướng dẫn chấm nội bộ",JsonSerializer.Serialize(first));
    }
    [Fact] public void ShufflingCannotChangeAutomaticScoresOrAuthoringOrder()
    {
        var quiz=Quiz();var original=quiz.Questions.OrderBy(x=>x.Position).Select(x=>x.Id).ToArray();
        var attempt=new QuizAttempt();QuizPresentation.Initialize(quiz,attempt);
        var presentation=QuizPresentation.Questions(quiz,attempt);
        foreach(var q in presentation.Where(x=>x.Type!=QuestionType.Essay))
        {
            var source=quiz.Questions.Single(x=>x.Id==q.Id);
            Assert.Contains(source.CorrectAnswer,q.Options);
            Assert.Equal(1,AssessmentRules.Score(q.Type,source.CorrectAnswer,source.CorrectAnswer,q.Points));
        }
        Assert.Equal(original,quiz.Questions.OrderBy(x=>x.Position).Select(x=>x.Id));
    }
    [Fact] public void ExistingAttemptsAndUnshuffledQuizzesRetainOriginalSequence()
    {
        var quiz=Quiz(false);var legacy=new QuizAttempt();
        var original=QuizPresentation.Questions(quiz,legacy);
        var newer=new QuizAttempt();QuizPresentation.Initialize(quiz,newer);
        Assert.Equal(JsonSerializer.Serialize(original),JsonSerializer.Serialize(QuizPresentation.Questions(quiz,newer)));
    }
    [Fact] public void SeparateAttemptsHaveIndependentPresentationSnapshots()
    {
        var quiz=Quiz();var rows=Enumerable.Range(0,8).Select(_=>{var a=new QuizAttempt();QuizPresentation.Initialize(quiz,a);return a.QuestionOrderJson+a.OptionOrderJson;}).ToList();
        // With 12 questions and 11 independent 4-option permutations, identical snapshots are negligibly likely.
        Assert.True(rows.Distinct().Count()>1);
    }
}
