namespace EventStore.Tests
{
    internal static class Config
    {
        public static readonly string DatabaseConnectionString =
            "Server=(local);Initial Catalog=EventStore;Integrated Security=SSPI;Connection Timeout=60;";
    }
}
