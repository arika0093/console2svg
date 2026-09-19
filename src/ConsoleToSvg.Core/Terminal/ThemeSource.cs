namespace ConsoleToSvg.Terminal;

public sealed record ThemeSource(string CloneUrl, string? Ref, string Subdirectory)
{
    public static ThemeSource Parse(string value)
    {
        var source = RepositorySource.Parse(value);
        return new ThemeSource(source.CloneUrl, source.Ref, source.Subdirectory);
    }

    public RepositorySource ToRepositorySource() => new(CloneUrl, Ref, Subdirectory);
}
