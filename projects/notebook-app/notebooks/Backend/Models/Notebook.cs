namespace LuminaApiDemo;

public class Notebook
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Untitled Notebook";
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int SourceCount { get; set; } = 0;
    public bool IsDeleted { get; set; } = false;
}
