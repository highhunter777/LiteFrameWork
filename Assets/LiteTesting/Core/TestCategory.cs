namespace LiteTesting
{
    // Canonical trait keys keep NUnit and dotnet filters aligned across all test lanes.
    public static class TestTrait
    {
        public const string Category = "Category";
        public const string Duration = "Duration";
        public const string Priority = "Priority";
        public const string Owner = "Owner";
    }

    public static class TestCategory
    {
        public const string Unit = "Unit";
        public const string Contract = "Contract";
        public const string Integration = "Integration";
        public const string Asset = "Asset";
        public const string Smoke = "Smoke";
        public const string EndToEnd = "EndToEnd";
        public const string Performance = "Performance";
        public const string Quarantine = "Quarantine";
    }

    public static class TestDuration
    {
        public const string Fast = "Fast";
        public const string Medium = "Medium";
        public const string LongRunning = "LongRunning";
    }

    public static class TestPriority
    {
        public const string P0 = "P0";
        public const string P1 = "P1";
        public const string P2 = "P2";
        public const string P3 = "P3";
    }
}
