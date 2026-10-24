using System.Text.Json;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Analysis;

public partial class RecommendationEngine
{
    private static bool TryReadCandidate(JsonElement item, AnalysisDiagnostics diagnostics, out CleanupRecommendation recommendation)
    {
        recommendation = new CleanupRecommendation();
        if (item.ValueKind != JsonValueKind.Object)
        {
            diagnostics.MalformedRecommendationCount++;
            return false;
        }

        string[] requiredFields = ["path", "category", "safety_rating", "explanation"];
        foreach (var field in requiredFields)
        {
            if (!item.TryGetProperty(field, out var value))
            {
                diagnostics.MissingFieldRecommendationCount++;
                return false;
            }

            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                diagnostics.MalformedRecommendationCount++;
                return false;
            }
        }

        var categoryText = item.GetProperty("category").GetString()!;
        var safetyText = item.GetProperty("safety_rating").GetString()!;
        if (!Enum.TryParse<RecommendationCategory>(categoryText, true, out var category) ||
            !Enum.IsDefined(category) || !Enum.GetNames<RecommendationCategory>().Contains(categoryText, StringComparer.OrdinalIgnoreCase))
        {
            category = RecommendationCategory.Other;
        }
        if (!Enum.TryParse<SafetyRating>(safetyText, true, out var safety) ||
            !Enum.IsDefined(safety) || !Enum.GetNames<SafetyRating>().Contains(safetyText, StringComparer.OrdinalIgnoreCase))
        {
            safety = SafetyRating.ReviewFirst;
        }

        recommendation.TargetPath = item.GetProperty("path").GetString()!;
        recommendation.Category = category;
        recommendation.SafetyRating = safety;
        recommendation.Explanation = item.GetProperty("explanation").GetString()!;
        return true;
    }
}
