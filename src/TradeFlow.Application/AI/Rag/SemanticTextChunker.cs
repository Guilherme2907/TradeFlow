using System.Text;
using System.Text.RegularExpressions;

namespace TradeFlow.Application.AI.Rag;

/// <summary>
/// Splits unstructured technical documents and B2B contracts on sentence/paragraph boundaries
/// with sliding word overlap, prepending a Contextual Header to every chunk so isolated chunks
/// never lose document identity (ADR-0021).
/// </summary>
public static partial class SemanticTextChunker
{
    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryRegex();

    public static IReadOnlyList<string> ChunkWithContextualHeader(
        string contextHeader,
        string bodyText,
        int targetWordsPerChunk = 90,
        int overlapWords = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextHeader);

        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return [$"{contextHeader.Trim()}\n"];
        }

        if (targetWordsPerChunk < 20)
        {
            targetWordsPerChunk = 20;
        }

        if (overlapWords < 0 || overlapWords >= targetWordsPerChunk)
        {
            overlapWords = Math.Max(0, targetWordsPerChunk / 5);
        }

        var sentences = SentenceBoundaryRegex()
            .Split(bodyText.Trim())
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (sentences.Count == 0)
        {
            return [$"{contextHeader.Trim()}\n{bodyText.Trim()}"];
        }

        var chunks = new List<string>();
        var currentSentences = new List<string>();
        var currentWordCount = 0;

        foreach (var sentence in sentences)
        {
            var sentenceWords = CountWords(sentence);

            if (currentWordCount > 0 && currentWordCount + sentenceWords > targetWordsPerChunk)
            {
                chunks.Add(FormatChunk(contextHeader, currentSentences));

                // Retain trailing sentences up to overlapWords so boundary clauses stay intact
                var overlapBuffer = new List<string>();
                var keptWords = 0;
                for (var i = currentSentences.Count - 1; i >= 0; i--)
                {
                    var w = CountWords(currentSentences[i]);
                    if (keptWords + w > overlapWords && overlapBuffer.Count > 0)
                    {
                        break;
                    }

                    overlapBuffer.Insert(0, currentSentences[i]);
                    keptWords += w;
                    if (keptWords >= overlapWords)
                    {
                        break;
                    }
                }

                currentSentences = overlapBuffer;
                currentWordCount = keptWords;
            }

            currentSentences.Add(sentence);
            currentWordCount += sentenceWords;
        }

        if (currentSentences.Count > 0)
        {
            var finalChunk = FormatChunk(contextHeader, currentSentences);
            if (chunks.Count == 0 || !string.Equals(chunks[^1], finalChunk, StringComparison.Ordinal))
            {
                chunks.Add(finalChunk);
            }
        }

        return chunks;
    }

    private static string FormatChunk(string header, IReadOnlyList<string> sentences)
    {
        var sb = new StringBuilder();
        sb.AppendLine(header.Trim());
        sb.Append(string.Join(' ', sentences));
        return sb.ToString();
    }

    private static int CountWords(string text)
        => text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
}
