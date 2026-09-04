using OcrAgent.Ocr;

namespace UnitTests.Plugins;

/// <summary>Deterministic OCR engine test double with call and option capture.</summary>
internal sealed class FakeOcrEngine : IOcrEngine
{
    private readonly string _text;
    private readonly double _confidence;

    /// <summary>Initializes deterministic OCR text and confidence.</summary>
    public FakeOcrEngine(string text, double confidence)
    {
        _text = text;
        _confidence = confidence;
    }

    /// <summary>Gets how many recognition calls were made.</summary>
    public int CallCount { get; private set; }

    /// <summary>Gets the options supplied to the most recent call.</summary>
    public OcrOptions? LastOptions { get; private set; }

    /// <summary>Gets every non-null option set supplied to recognition.</summary>
    public List<OcrOptions> AllOptions { get; } = [];

    /// <inheritdoc />
    public Task<OcrResult> RecognizeAsync(
        string imagePath,
        OcrOptions? options = null)
    {
        CallCount++;
        LastOptions = options;
        if (options is not null)
            AllOptions.Add(options);

        var words = _text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new OcrResult
        {
            Text = _text,
            Confidence = _confidence,
            EngineName = "FakeEngine",
            Lines =
            [
                new OcrLine
                {
                    Text = _text,
                    Confidence = _confidence,
                    Words = words.Select((word, index) => new OcrWord
                    {
                        Text = word,
                        Confidence = _confidence,
                        BoundingBox = new OcrBoundingBox
                        {
                            X = index * 50,
                            Y = 10,
                            Width = 40,
                            Height = 20
                        }
                    }).ToList()
                }
            ]
        };

        return Task.FromResult(result);
    }
}
