// MetaMap シム（PlantUmlTool の旧 Part 2 のうち、出力エンジンが使う ModelOf だけ）。
// PlantUmlTool・AgentReview・NdMcp・ClassImportProbe がビルドに含める（旧 Part 2 本体は SequenceImportProbe/src/40-legacy-import.cs）。
public static class MetaMap
{
    public static IModel ModelOf(object shape)
    {
        var representation = shape as IRepresentation;
        return representation != null ? representation.Model : null;
    }
}
