using System.Text;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;

namespace AbacController.Pep;

/// <summary>
/// Generates human-readable marking strings from security labels using SPIF rules.
/// Example output: "NATO SECRET//REL TO USA, GBR"
/// </summary>
public sealed class MarkingGenerator : IMarkingGenerator
{
    /// <inheritdoc />
    public string GenerateMarking(SecurityLabel label, ISpifIndex spifIndex, string markingCode = "pageTop")
    {
        var sb = new StringBuilder();

        // Classification marking
        var cls = spifIndex.GetClassification(label.ClassificationLacv);
        if (cls is not null)
        {
            var clsMarking = cls.MarkingData
                .FirstOrDefault(m => m.Codes.Contains(markingCode));
            sb.Append(clsMarking?.Phrase ?? cls.Name);
        }
        else
        {
            sb.Append(label.ClassificationName ?? label.ClassificationLacv.ToString());
        }

        // Category markings
        foreach (var tagSet in label.CategoryTagSets)
        {
            var spifTagSet = spifIndex.GetTagSet(tagSet.TagSetOid);
            if (spifTagSet is null) continue;

            foreach (var tag in tagSet.Tags)
            {
                var spifTag = spifTagSet.Tags.FirstOrDefault(t => t.Name == tag.Name);
                if (spifTag is null) continue;

                // Find qualifiers for this marking code
                var qualifier = spifIndex.Spif.GlobalMarkingQualifiers
                    .FirstOrDefault(q => q.MarkingCode == markingCode);

                var prefix = qualifier?.Qualifiers
                    .FirstOrDefault(q => q.Code == QualifierCode.Prefix)?.Text;
                var separator = qualifier?.Qualifiers
                    .FirstOrDefault(q => q.Code == QualifierCode.Separator)?.Text ?? ", ";

                var catPhrases = new List<string>();
                foreach (var cat in tag.Categories)
                {
                    var spifCat = spifTag.Categories.FirstOrDefault(c => c.Lacv == cat.Lacv);
                    if (spifCat is null) continue;

                    var catMarking = spifCat.MarkingData
                        .FirstOrDefault(m => m.Codes.Contains(markingCode));
                    catPhrases.Add(catMarking?.Phrase ?? spifCat.Name);
                }

                if (catPhrases.Count > 0)
                {
                    sb.Append("//");
                    if (prefix is not null)
                        sb.Append(prefix);
                    sb.Append(string.Join(separator, catPhrases));
                }
            }
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string GenerateShortMarking(SecurityLabel label, ISpifIndex spifIndex)
    {
        var sb = new StringBuilder();

        var cls = spifIndex.GetClassification(label.ClassificationLacv);
        if (cls is not null)
        {
            var shortMarking = cls.MarkingData
                .FirstOrDefault(m => m.ShortPhrase is not null);
            sb.Append(shortMarking?.ShortPhrase ?? cls.Name);
        }
        else
        {
            sb.Append(label.ClassificationName ?? "?");
        }

        foreach (var tagSet in label.CategoryTagSets)
        {
            var spifTagSet = spifIndex.GetTagSet(tagSet.TagSetOid);
            if (spifTagSet is null) continue;

            foreach (var tag in tagSet.Tags)
            {
                var spifTag = spifTagSet.Tags.FirstOrDefault(t => t.Name == tag.Name);
                if (spifTag is null) continue;

                foreach (var cat in tag.Categories)
                {
                    var spifCat = spifTag.Categories.FirstOrDefault(c => c.Lacv == cat.Lacv);
                    if (spifCat is null) continue;

                    var shortMarking = spifCat.MarkingData
                        .FirstOrDefault(m => m.ShortPhrase is not null);
                    if (shortMarking?.ShortPhrase is not null)
                        sb.Append($"//{shortMarking.ShortPhrase}");
                }
            }
        }

        return sb.ToString();
    }
}
