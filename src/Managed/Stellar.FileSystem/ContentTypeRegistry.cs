using System.Collections.Concurrent;
using Stellar.Kernel.FileSystem;

namespace Stellar.FileSystem;

/// <summary>
/// The content types of a file system, by extension; the extensions compare in any case.
/// </summary>
public sealed class ContentTypeRegistry
    : IContentTypeRegistry
{
    private readonly ConcurrentDictionary<string, ContentType> _types = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Makes a registry with the formats every engine reads: text, data, images, sounds, models, fonts, shaders.
    /// </summary>
    /// <returns>The registry.</returns>
    public static ContentTypeRegistry CreateDefault()
    {
        var registry = new ContentTypeRegistry();

        foreach (var (extension, mediaType) in new[]
                 {
                     (".txt", "text/plain"), (".md", "text/markdown"), (".csv", "text/csv"),
                     (".json", "application/json"), (".xml", "application/xml"), (".yaml", "application/yaml"),
                     (".yml", "application/yaml"), (".toml", "application/toml"), (".ini", "text/plain"),
                     (".cfg", "text/plain"), (".gltf", "model/gltf+json"), (".obj", "model/obj"),
                     (".hlsl", "text/x-hlsl"), (".glsl", "text/x-glsl"), (".wgsl", "text/wgsl"),
                     (".shader", "text/x-shader"), (".lua", "text/x-lua"), (".cs", "text/x-csharp"),
                     (".js", "text/javascript"), (".html", "text/html"), (".css", "text/css"), (".svg", "image/svg+xml")
                 })
            registry.Register(extension, new ContentType(mediaType, ContentKind.Text, "utf-8"));

        foreach (var (extension, mediaType) in new[]
                 {
                     (".png", "image/png"), (".jpg", "image/jpeg"), (".jpeg", "image/jpeg"), (".bmp", "image/bmp"),
                     (".tga", "image/x-tga"), (".dds", "image/vnd-ms.dds"), (".ktx2", "image/ktx2"),
                     (".hdr", "image/vnd.radiance"), (".exr", "image/x-exr"), (".webp", "image/webp"),
                     (".wav", "audio/wav"), (".ogg", "audio/ogg"), (".mp3", "audio/mpeg"), (".flac", "audio/flac"),
                     (".glb", "model/gltf-binary"), (".fbx", "model/x-fbx"), (".ttf", "font/ttf"), (".otf", "font/otf"),
                     (".spv", "application/x-spirv"), (".bin", "application/octet-stream"), (".zip", "application/zip"),
                     (".dll", "application/x-msdownload")
                 })
            registry.Register(extension, new ContentType(mediaType, ContentKind.Binary));

        return registry;
    }

    /// <inheritdoc/>
    public void Register(string extension, ContentType contentType)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (extension.Length < 2 || extension[0] != '.' || extension.IndexOfAny(['/', '\\', ' ']) >= 0)
            throw new ArgumentException($"'{extension}' is not an extension: expected a dot and a name, such as .png", nameof(extension));
        if (contentType.IsUnknown) throw new ArgumentException("The unknown content type cannot be registered", nameof(contentType));

        _types[extension] = contentType;
    }

    /// <inheritdoc/>
    public bool TryGet(string extension, out ContentType contentType)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return _types.TryGetValue(extension, out contentType);
    }

    /// <inheritdoc/>
    public ContentType Resolve(FilePath path)
    {
        var extension = path.Extension;
        return extension.Length > 0 && _types.TryGetValue(extension, out var contentType) ? contentType : ContentType.OctetStream;
    }
}
