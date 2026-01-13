using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

namespace OcrAgent.Plugins;

public sealed class ImageProcessingPlugin
{
    private readonly IImageStore _store;

    public ImageProcessingPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Registers an image into the storage system and returns its reference ID. The image is provided as a local file path.")]
    public string RegisterImage(
        [Description("Local file path of the image to register.")] string filePath,
        [Description("Optional reference ID to use (e.g., 123ABC). If empty, a new reference is generated.")] string? desiredReference = null) 
        => string.Empty;

    [Description("Performs zooming in of the given image. It returns the comma-separated list of references of generated images.")]
    public Task<string> ZoomInImage(
        [Description("The reference number of the image, which should be zoomed in.")] string imageReference,
        [Description("The first zoom in in percent.")] int startZoom,
        [Description("The The last zoom in in percent")] int endZoom,
        [Description("The delta zoom, which defines zoomin step.")] int zoomStep) 
        => Task.FromResult(string.Empty);

    private string MakeUniqueRef(string baseRef) => string.Empty;

    public IEnumerable<AITool> AsAITools()
    {
        yield return AIFunctionFactory.Create(this.RegisterImage);
        yield return AIFunctionFactory.Create(this.ZoomInImage);
    }
}