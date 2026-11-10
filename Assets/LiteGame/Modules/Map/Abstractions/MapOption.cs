namespace LiteGame.Modules.Map
{
    /// <summary>开房选图行（表 <c>#map</c> 全行按表序）。</summary>
    public sealed class MapOption
    {
        public readonly int Id;
        public readonly string Name;

        public MapOption(int id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }
    }
}
