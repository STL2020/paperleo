using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Services;

public static class SettingsDtoExtensions
{
    public static ProcessingOptions ToProcessingOptions(this SettingsDto s) => new(
        EnableTagsAssignment: s.EnableTagsAssignment,
        EnableCorrespondentDetection: s.EnableCorrespondentDetection,
        EnableDocumentTypeClassification: s.EnableDocumentTypeClassification,
        EnableTitleGeneration: s.EnableTitleGeneration,
        UseExistingCorrespondentsOnly: s.UseExistingCorrespondentsOnly,
        UseExistingDocumentTypesOnly: s.UseExistingDocumentTypesOnly,
        UseExistingTagsOnly: s.UseExistingTagsOnly,
        CustomSystemPrompt: s.CustomSystemPrompt,
        EnableCustomFields: s.EnableCustomFields,
        DefaultTagVocabulary: s.DefaultTagVocabulary,
        DefaultDocumentTypeVocabulary: s.DefaultDocumentTypeVocabulary,
        IsProMode: LicenseCheck.IsProMode(s.PremiumLicenseKey));

    public static LlmConnectionConfig ToLlmConfig(this SettingsDto s) => new(
        Model: s.LlmModel, ApiKey: s.LlmApiKey, ApiBase: s.LlmApiBase,
        // Defensiv geklemmt auf 0-2 (Standardbereich der meisten OpenAI-kompatiblen
        // Provider) - unabhängig davon, was in der settings.env steht. Ein Wert
        // außerhalb dieses Bereichs (z.B. durch manuelle Bearbeitung der Datei oder
        // einen UI-Ausreißer) würde sonst jeden LLM-Aufruf mit einem API-Fehler
        // scheitern lassen, statt einfach auf einen sinnvollen Wert begrenzt zu werden.
        Temperature: Math.Clamp(s.LlmTemperature, 0.0, 2.0),
        MaxTokens: s.LlmMaxTokens);
}
