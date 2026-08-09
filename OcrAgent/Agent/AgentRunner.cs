using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace OcrAgent.Agent;

/// <summary>
/// Interactive console session with an OpenAI chat agent wired to OCR preprocessing tools.
/// </summary>
public sealed class AgentRunner
{
    private readonly AIAgent _agent;

    /// <summary>Creates a runner that uses the given agent for chat.</summary>
    /// <param name="agent">Agent instance from <see cref="CreateAgent"/> or equivalent.</param>
    public AgentRunner(AIAgent agent)
    {
        _agent = agent;
    }

    /// <summary>Prompts on stdin until "exit" or EOF; streams the model reply each turn.</summary>
    /// <returns>Task that finishes when the loop ends.</returns>
    public async Task RunConversationLoopAsync()
    {
        var session = await _agent.CreateSessionAsync();

        while (true)
        {
            Console.WriteLine();
            Console.Write("User> ");
            var userInput = Console.ReadLine();

            if (userInput == null)
                break;

            if (userInput.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            Console.WriteLine();
            Console.Write("Agent> ");
            await foreach (var update in _agent.RunStreamingAsync(userInput, session))
            {
                Console.Write(update.Text);
            }
            Console.WriteLine();
        }
    }

    /// <summary>Builds the chat agent from OPENAI_API_KEY and optional OPENAI_CHATCOMPLETION_DEPLOYMENT.</summary>
    /// <param name="tools">Tools registered on the agent.</param>
    /// <returns>Configured agent with system instructions for preprocessing workflows.</returns>
    public static AIAgent CreateAgent(IEnumerable<AITool> tools)
    {
        string apiKey = GetEnvOrThrow("OPENAI_API_KEY");
        string deployment = Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT") ?? "gpt-4o-mini";

        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        var chatClient = client.GetChatClient(deployment);

        return chatClient.AsIChatClient().AsAIAgent(
            instructions:
                "You are an OCR Preprocessing Agent that helps improve document images before OCR extraction.\n\n" +
                "You will analyze the user prompt and map their demand to the available list of tools and utilize them\n\n" +
                "=== AVAILABLE TOOLS ===\n" +
                "1. RegisterImage(filePath, ref?) - Store an image and get a reference ID\n\n" +

                "ANALYSIS:\n" +
                "2. AssessQuality(ref) - Analyze image quality (blur, noise, brightness, contrast, skew, resolution)\n" +
                "3. ExtractDocumentContours(ref) - Detect document boundaries and extract quad corners\n\n" +

                "GEOMETRIC:\n" +
                "4. ZoomInImage(ref, start%, end%, step%) - Zoom into images\n" +
                "5. RotateImage(ref, startAngle, endAngle, angleStep) - Rotate images\n" +
                "6. CorrectPerspective(ref, sensitivity?, contourData?) - Fix angled photos (USE FIRST for mobile captures!)\n" +
                "7. DeskewImage(ref, maxAngle?, method?, preserveSize?) - Auto-correct skew/tilt\n" +
                "8. ResizeImage(ref, targetWidth?, targetHeight?, targetDPI?, documentType?) - Resize for OCR\n" +
                "9. AutoCrop(ref, marginPercent?, trimBorders?, contourData?) - Crop to document content area\n\n" +

                "ENHANCEMENT:\n" +
                "10. EnhanceContrast(ref, method='auto'|'histogram'|'clahe'|'auto_lab', qualityProfile?) - Improve contrast\n" +
                "11. AdjustBrightness(ref, start, end, step) - Adjust brightness range\n" +
                "12. AutoAdjustBrightness(ref, qualityProfile?) - Auto-normalize brightness for OCR\n" +
                "13. DenoiseImage(ref, method='auto'|'bilateral'|'median'|'nlm', qualityProfile?) - Remove noise\n" +
                "14. CorrectColor(ref, method='auto'|'gray_world'|'white_patch'|'histogram_match') - Fix color casts\n" +
                "15. InvertImage(ref, mode='auto'|'always'|'smart') - Invert colors for dark backgrounds\n" +
                "16. SharpenImage(ref, strength?, method?, adaptiveStrength?) - Sharpen text edges\n" +
                "17. NormalizeIllumination(ref, kernelSize?, strength?, qualityProfile?) - Remove shadows\n\n" +

                "SEGMENTATION:\n" +
                "18. BinarizeImage(ref, method='auto'|'otsu'|'adaptive_mean'|'adaptive_gaussian'|'sauvola', qualityProfile?) - Convert to B&W\n" +
                "19. ApplyMorphology(ref, operation='close'|'open'|'erode'|'dilate'|'gradient'|'remove_speckles', minArea?) - Cleanup\n" +
                "20. RemoveDocumentBackground(ref, mode='on_white'|'mask_only'|'transparent', contourData?) - Remove background\n\n" +

                "OCR (only available if Tesseract is installed):\n" +
                "21. RunOcr(ref, language?, pageSegMode?, variant?, dpi?, qualityProfile?) - Extract text with bounding boxes. " +
                    "variant='auto' tries all binarizations (natural/otsu/adaptive/sauvola) with multi-PSM and picks best. " +
                    "variant options: 'natural', 'otsu', 'adaptive_gaussian', 'sauvola', 'auto'. " +
                    "pageSegMode='multi' tries SingleColumn+SingleBlock and picks best.\n\n" +

                "WORKFLOWS:\n" +
                "22. ListWorkflows() - Show available preprocessing workflows\n" +
                "23. RunWorkflow(workflowId, imageRef, overridesJson?) - Run a complete deterministic pipeline. " +
                    "Steps with unavailable tools are automatically skipped.\n\n" +

                "RUN HISTORY (only available if a run store is configured):\n" +
                "24. ListRuns(top?, mode?) - List recently executed jobs, newest first\n" +
                "25. GetRun(runId) - Look up one past job by its run ID\n\n" +
                "IMPORTANT WORKFLOW RULES:\n" +
                "- When the user asks to 'use the workflow' or 'extract data', call RunWorkflow ONCE with the image reference. " +
                  "Do NOT call individual tools manually when a workflow is requested.\n" +
                "- The workflow runs ALL steps automatically in sequence. You do NOT need to call tools one by one.\n" +
                "- If RunWorkflow returns an error, report the error to the user instead of retrying individual steps.\n" +
                "- When the user asks what workflows are available, use ListWorkflows.\n" +
                "- For manual processing (no workflow), run AssessQuality first, then use the quality profile to guide subsequent tools.\n\n" +
                "WORKFLOW OUTPUT FORMATTING:\n" +
                "- When reporting workflow completion, include: workflow ID, run ID, output directory, processed image reference\n" +
                "- List the workflow steps executed (step name and tool name only, e.g., 'assess_quality (AssessQuality)', 'ocr (RunOcr)')\n" +
                "- Do NOT include: step durations, input/output filenames, JSON dumps, workflow context, quality profiles, or contour data\n" +
                "- Keep the response concise and user-friendly\n\n" +
                "CHAINING RULE:\n" +
                "- When the user asks for multiple steps in sequence (e.g., 'correct perspective, then denoise, then binarize'), " +
                  "use the OUTPUT reference of each tool as the INPUT for the next tool. Do NOT reuse the original reference for every step.\n\n" +
                "Always return the generated reference IDs to the user in numbered style.\n" +
                "If required parameters are missing, ask a short follow-up question.",
            name: "OCR Preprocessing Agent",
            tools: tools.ToArray()
        );
    }
    private static string GetEnvOrThrow(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing {key} environment variable.");
        return value;
    }
}