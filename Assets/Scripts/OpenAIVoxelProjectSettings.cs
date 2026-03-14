using UnityEngine;

[CreateAssetMenu(fileName = "OpenAIVoxelProjectSettings", menuName = "Voxel/OpenAI Project Settings")]
public sealed class OpenAIVoxelProjectSettings : ScriptableObject
{
    [SerializeField] private bool enableOnlinePlanner = true;
    [SerializeField] private string model = OpenAIVoxelBlueprintClient.DefaultModel;
    [SerializeField] private string apiKey = "";

    public bool EnableOnlinePlanner
    {
        get { return enableOnlinePlanner; }
    }

    public string Model
    {
        get { return string.IsNullOrWhiteSpace(model) ? OpenAIVoxelBlueprintClient.DefaultModel : model.Trim(); }
    }

    public string ApiKey
    {
        get { return apiKey == null ? string.Empty : apiKey.Trim(); }
    }

    public bool HasApiKey
    {
        get { return !string.IsNullOrWhiteSpace(ApiKey); }
    }
}
