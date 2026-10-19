namespace Marquee.ReleaseTool.Tests.TestSupport;

/// Stands in for the console prompt: answers each question with the next password and records the questions. Asking
/// for more passwords than the test gave fails the test.
sealed class FakePasswords(params string[] answers)
{
    readonly Queue<string> answers = new(answers);

    public List<string> Prompts { get; } = [];

    public char[] Ask(string prompt)
    {
        Prompts.Add(prompt);
        Assert.True(answers.Count > 0, $"Asked for a password the test didn't expect: {prompt}");
        return answers.Dequeue().ToCharArray();
    }
}
