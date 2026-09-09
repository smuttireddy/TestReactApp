namespace LOGAnalyzer.Core.Settings;

/// <summary>
/// Strongly-typed settings for IBM watsonx.ai bound from appsettings.json "WatsonX" section.
/// </summary>
public class WatsonXSettings
{
    public string ApiKey     { get; set; } = string.Empty;
    public string ProjectId  { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
    public string ModelId    { get; set; } = "ibm/granite-13b-instruct-v2";
}
