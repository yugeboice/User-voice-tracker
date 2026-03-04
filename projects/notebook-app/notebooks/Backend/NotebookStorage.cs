using System.Collections.Concurrent;
using System.Text.Json;

namespace MinimalApiCall;

/// <summary>
/// File-based storage for notebooks using JSON files.
/// Each notebook gets its own folder with separate JSON files.
/// </summary>
public class NotebookStorage
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public NotebookStorage(string baseDirectory = "notebooks")
    {
        // Convert to absolute path to ensure skills can access data regardless of their working directory
        _baseDirectory = Path.IsPathRooted(baseDirectory) 
            ? baseDirectory 
            : Path.GetFullPath(baseDirectory);
            
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Ensure base directory exists
        Directory.CreateDirectory(_baseDirectory);
    }

    private string GetNotebookDirectory(string notebookId)
    {
        return Path.Combine(_baseDirectory, notebookId);
    }

    private string GetMetadataPath(string notebookId)
    {
        return Path.Combine(GetNotebookDirectory(notebookId), "metadata.json");
    }

    private string GetSourcesPath(string notebookId)
    {
        return Path.Combine(GetNotebookDirectory(notebookId), "sources.json");
    }

    private string GetChatHistoryPath(string notebookId)
    {
        return Path.Combine(GetNotebookDirectory(notebookId), "chat-history.json");
    }

    private string GetGenerationsPath(string notebookId)
    {
        return Path.Combine(GetNotebookDirectory(notebookId), "generations.json");
    }

    private string GetIndexPath()
    {
        return Path.Combine(_baseDirectory, "index.json");
    }

    public string GetImagesDirectory(string notebookId)
    {
        var imagesDir = Path.Combine(GetNotebookDirectory(notebookId), "images");
        Directory.CreateDirectory(imagesDir);
        return imagesDir;
    }

    // ========== Notebook Metadata Operations ==========

    public List<LuminaApiDemo.Notebook> LoadAllNotebooks()
    {
        try
        {
            var indexPath = GetIndexPath();
            if (!File.Exists(indexPath))
                return new List<LuminaApiDemo.Notebook>();

            var json = File.ReadAllText(indexPath);
            return JsonSerializer.Deserialize<List<LuminaApiDemo.Notebook>>(json, _jsonOptions) 
                   ?? new List<LuminaApiDemo.Notebook>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error loading notebooks index: {ex.Message}");
            return new List<LuminaApiDemo.Notebook>();
        }
    }

    public void SaveNotebookIndex(IEnumerable<LuminaApiDemo.Notebook> notebooks)
    {
        try
        {
            var json = JsonSerializer.Serialize(notebooks, _jsonOptions);
            File.WriteAllText(GetIndexPath(), json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error saving notebooks index: {ex.Message}");
        }
    }

    public LuminaApiDemo.Notebook? LoadNotebook(string notebookId)
    {
        try
        {
            var metadataPath = GetMetadataPath(notebookId);
            if (!File.Exists(metadataPath))
                return null;

            var json = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize<LuminaApiDemo.Notebook>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error loading notebook {notebookId}: {ex.Message}");
            return null;
        }
    }

    public void SaveNotebook(LuminaApiDemo.Notebook notebook)
    {
        try
        {
            var notebookDir = GetNotebookDirectory(notebook.Id);
            Directory.CreateDirectory(notebookDir);

            var metadataPath = GetMetadataPath(notebook.Id);
            var json = JsonSerializer.Serialize(notebook, _jsonOptions);
            File.WriteAllText(metadataPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error saving notebook {notebook.Id}: {ex.Message}");
        }
    }

    public void DeleteNotebook(string notebookId)
    {
        try
        {
            var notebookDir = GetNotebookDirectory(notebookId);
            if (Directory.Exists(notebookDir))
            {
                Directory.Delete(notebookDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error deleting notebook {notebookId}: {ex.Message}");
        }
    }

    // ========== Source Operations ==========

    public List<Source> LoadSources(string notebookId)
    {
        try
        {
            var sourcesPath = GetSourcesPath(notebookId);
            if (!File.Exists(sourcesPath))
                return new List<Source>();

            var json = File.ReadAllText(sourcesPath);
            return JsonSerializer.Deserialize<List<Source>>(json, _jsonOptions) 
                   ?? new List<Source>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error loading sources for {notebookId}: {ex.Message}");
            return new List<Source>();
        }
    }

    public void SaveSources(string notebookId, IEnumerable<Source> sources)
    {
        try
        {
            var notebookDir = GetNotebookDirectory(notebookId);
            Directory.CreateDirectory(notebookDir);

            var sourcesPath = GetSourcesPath(notebookId);
            var json = JsonSerializer.Serialize(sources, _jsonOptions);
            File.WriteAllText(sourcesPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error saving sources for {notebookId}: {ex.Message}");
        }
    }

    // ========== Chat History Operations ==========

    public List<ChatMessage> LoadChatHistory(string notebookId)
    {
        try
        {
            var chatPath = GetChatHistoryPath(notebookId);
            if (!File.Exists(chatPath))
                return new List<ChatMessage>();

            var json = File.ReadAllText(chatPath);
            return JsonSerializer.Deserialize<List<ChatMessage>>(json, _jsonOptions) 
                   ?? new List<ChatMessage>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error loading chat history for {notebookId}: {ex.Message}");
            return new List<ChatMessage>();
        }
    }

    public void SaveChatHistory(string notebookId, IEnumerable<ChatMessage> messages)
    {
        try
        {
            var notebookDir = GetNotebookDirectory(notebookId);
            Directory.CreateDirectory(notebookDir);

            var chatPath = GetChatHistoryPath(notebookId);
            var json = JsonSerializer.Serialize(messages, _jsonOptions);
            File.WriteAllText(chatPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error saving chat history for {notebookId}: {ex.Message}");
        }
    }

    // ========== Generation Operations ==========

    public List<StudioGeneration> LoadGenerations(string notebookId)
    {
        try
        {
            var genPath = GetGenerationsPath(notebookId);
            if (!File.Exists(genPath))
                return new List<StudioGeneration>();

            var json = File.ReadAllText(genPath);
            return JsonSerializer.Deserialize<List<StudioGeneration>>(json, _jsonOptions) 
                   ?? new List<StudioGeneration>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error loading generations for {notebookId}: {ex.Message}");
            return new List<StudioGeneration>();
        }
    }

    public void SaveGenerations(string notebookId, IEnumerable<StudioGeneration> generations)
    {
        try
        {
            var notebookDir = GetNotebookDirectory(notebookId);
            Directory.CreateDirectory(notebookDir);

            var genPath = GetGenerationsPath(notebookId);
            var json = JsonSerializer.Serialize(generations, _jsonOptions);
            File.WriteAllText(genPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error saving generations for {notebookId}: {ex.Message}");
        }
    }

    public void DeleteGenerationImage(string notebookId, string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
            return;

        try
        {
            var imagesDir = GetImagesDirectory(notebookId);
            var fullPath = Path.Combine(imagesDir, imagePath);
            
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                Console.WriteLine($"[Storage] Deleted image: {fullPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error deleting image {imagePath}: {ex.Message}");
        }
    }

    public void ClearImagesDirectory(string notebookId)
    {
        try
        {
            var imagesDir = GetImagesDirectory(notebookId);
            
            if (Directory.Exists(imagesDir))
            {
                var files = Directory.GetFiles(imagesDir);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                Console.WriteLine($"[Storage] Cleared {files.Length} images from {notebookId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Error clearing images directory: {ex.Message}");
        }
    }
}
