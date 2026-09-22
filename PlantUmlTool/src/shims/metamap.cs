// MetaMap シム（PlantUmlTool の旧 Part 2 のうち、出力エンジンが使う ModelOf だけ）。
// AgentReview と NdMcp が転記時に使う。PlantUmlTool 自身の生成には含めない。
public static class MetaMap
{
    public static IModel ModelOf(object shape)
    {
        var representation = shape as IRepresentation;
        return representation != null ? representation.Model : null;
    }
}
