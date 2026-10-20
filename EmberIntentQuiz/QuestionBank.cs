using System.IO.Compression;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace EmberIntentQuiz;

internal sealed class QuestionBankFile
{
    public List<QuizQuestion> Questions { get; set; } = [];
}

internal sealed class QuizQuestion
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "行测真题";
    public string Exam { get; set; } = "";
    public string Year { get; set; } = "";
    public string Stem { get; set; } = "";
    public string Material { get; set; } = "";
    public List<string> Options { get; set; } = [];
    public int Answer { get; set; }
}

internal static class QuestionBank
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly List<QuizQuestion> Questions = [];
    private static readonly Random Random = new();
    private static int _cursor;

    public static int Count => Questions.Count;

    public static void Load(string modDirectory)
    {
        Questions.Clear();
        _cursor = 0;

        var file = Path.Combine(modDirectory, "question-bank.json.gz");
        try
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Packaged question bank is missing.", file);
            using var compressed = File.OpenRead(file);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            var bank = JsonSerializer.Deserialize<QuestionBankFile>(gzip, JsonOptions);
            if (bank?.Questions is null || bank.Questions.Count == 0)
                throw new InvalidDataException("Packaged question bank has no questions.");

            foreach (var question in bank.Questions)
            {
                if (string.IsNullOrWhiteSpace(question.Id)
                    || string.IsNullOrWhiteSpace(question.Stem)
                    || question.Options.Count is < 2 or > 4
                    || question.Answer < 0 || question.Answer >= question.Options.Count)
                {
                    throw new InvalidDataException($"Question {question.Id} is invalid or is not single-answer.");
                }
            }

            Questions.AddRange(bank.Questions);
            Shuffle();
            Log.Info($"[EmberIntentQuiz] Loaded {Questions.Count:N0} single-answer questions.");
        }
        catch (Exception exception)
        {
            Questions.Clear();
            Log.Error($"[EmberIntentQuiz] Question bank could not be loaded; intents will stay hidden during player turns. {exception}");
        }
    }

    public static QuizQuestion? Draw()
    {
        if (Questions.Count == 0) return null;
        if (_cursor >= Questions.Count) Shuffle();
        return Questions[_cursor++];
    }

    private static void Shuffle()
    {
        for (var i = Questions.Count - 1; i > 0; i--)
        {
            var j = Random.Next(i + 1);
            (Questions[i], Questions[j]) = (Questions[j], Questions[i]);
        }
        _cursor = 0;
    }
}
