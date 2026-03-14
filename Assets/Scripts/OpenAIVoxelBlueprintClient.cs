using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class OpenAIVoxelBlueprint
{
    public string concept_name;
    public string archetype;
    public string summary;
    public string symmetry_mode;
    public bool floating_base;
    public string[] style_tags;
    public string[] iconic_parts;
    public OpenAIVoxelPalette palette;
    public OpenAIVoxelLayer[] layers;
}

[Serializable]
public sealed class OpenAIVoxelPalette
{
    public string primary;
    public string secondary;
    public string accent;
    public string detail;
    public string glow;
    public string ground;
}

[Serializable]
public sealed class OpenAIVoxelLayer
{
    public string[] rows;
}

public static class OpenAIVoxelBlueprintClient
{
    private const string Endpoint = "https://api.openai.com/v1/responses";
    public const int GridSize = 16;
    private const int RequestTimeoutSeconds = 60;
    private const float HeartbeatIntervalSeconds = 3f;

    private const string PlannerInstructions =
        "You are a voxel concept planner for a Unity app. " +
        "Transform the user's prompt into a readable 16x16x16 voxel sculpture blueprint. " +
        "Return only the schema fields. " +
        "Treat layers as bottom-to-top Y slices. Each layer must contain exactly 16 rows. " +
        "Each row must be exactly 16 characters using only digits 0,1,2,3,4,5,6. " +
        "Digit 0 means empty. Digit 1 is primary mass. Digit 2 is secondary or shadow mass. " +
        "Digit 3 is accent surfaces. Digit 4 is detail points. Digit 5 is glow or highlight points. Digit 6 is ground or support material. " +
        "Keep the object mostly connected, iconic, centered, and readable from a distance. " +
        "Prioritize the most recognizable silhouette over literal detail. " +
        "For animals, emphasize head, legs, wings, ears, tail, or horns. " +
        "For vehicles, emphasize wheels, cockpit, wings, hull, or handlebar. " +
        "For tools and furniture, exaggerate the most identifying outline. " +
        "Use symmetry_mode to describe whether the object should be mirrored across X, Z, or both. " +
        "Leave at least one cell of margin when possible. Avoid noisy single-cube speckles. " +
        "If the prompt is abstract or unusual, infer the most iconic silhouette rather than refusing.";

    private const string SchemaJson =
        "{" +
        "\"type\":\"object\"," +
        "\"properties\":{" +
            "\"concept_name\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":40}," +
            "\"archetype\":{\"type\":\"string\",\"pattern\":\"^(animal|vehicle|tool|building|character|food|symbol|furniture|plant|artifact|abstract)$\"}," +
            "\"summary\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":120}," +
            "\"symmetry_mode\":{\"type\":\"string\",\"pattern\":\"^(none|mirror_x|mirror_z|mirror_xz)$\"}," +
            "\"floating_base\":{\"type\":\"boolean\"}," +
            "\"style_tags\":{\"type\":\"array\",\"minItems\":3,\"maxItems\":3,\"items\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":20}}," +
            "\"iconic_parts\":{\"type\":\"array\",\"minItems\":2,\"maxItems\":4,\"items\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":20}}," +
            "\"palette\":{" +
                "\"type\":\"object\"," +
                "\"properties\":{" +
                    "\"primary\":{\"type\":\"string\",\"minLength\":7,\"maxLength\":7,\"pattern\":\"^#[0-9A-Fa-f]{6}$\"}," +
                    "\"secondary\":{\"type\":\"string\",\"minLength\":7,\"maxLength\":7,\"pattern\":\"^#[0-9A-Fa-f]{6}$\"}," +
                    "\"accent\":{\"type\":\"string\",\"minLength\":7,\"maxLength\":7,\"pattern\":\"^#[0-9A-Fa-f]{6}$\"}," +
                    "\"detail\":{\"type\":\"string\",\"minLength\":7,\"maxLength\":7,\"pattern\":\"^#[0-9A-Fa-f]{6}$\"}," +
                    "\"glow\":{\"type\":\"string\",\"minLength\":7,\"maxLength\":7,\"pattern\":\"^#[0-9A-Fa-f]{6}$\"}," +
                    "\"ground\":{\"type\":\"string\",\"minLength\":7,\"maxLength\":7,\"pattern\":\"^#[0-9A-Fa-f]{6}$\"}" +
                "}," +
                "\"required\":[\"primary\",\"secondary\",\"accent\",\"detail\",\"glow\",\"ground\"]," +
                "\"additionalProperties\":false" +
            "}," +
            "\"layers\":{" +
                "\"type\":\"array\",\"minItems\":16,\"maxItems\":16," +
                "\"items\":{" +
                    "\"type\":\"object\"," +
                    "\"properties\":{" +
                        "\"rows\":{" +
                            "\"type\":\"array\",\"minItems\":16,\"maxItems\":16," +
                            "\"items\":{\"type\":\"string\",\"minLength\":16,\"maxLength\":16,\"pattern\":\"^[0-6]{16}$\"}" +
                        "}" +
                    "}," +
                    "\"required\":[\"rows\"]," +
                    "\"additionalProperties\":false" +
                "}" +
            "}" +
        "}," +
        "\"required\":[\"concept_name\",\"archetype\",\"summary\",\"symmetry_mode\",\"floating_base\",\"style_tags\",\"iconic_parts\",\"palette\",\"layers\"]," +
        "\"additionalProperties\":false" +
        "}";

    public const string DefaultModel = "gpt-5-mini";

    public static IEnumerator RequestBlueprint(
        string apiKey,
        string model,
        string prompt,
        Action<OpenAIVoxelBlueprint> onSuccess,
        Action<string> onError,
        Action<string> onLog = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            onLog?.Invoke("OpenAI API Key 为空，无法发起在线生成。");
            onError?.Invoke("缺少 OpenAI API Key。");
            yield break;
        }

        string body = BuildRequestBody(string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim(), prompt);
        onLog?.Invoke("已构建 OpenAI 请求体。模型: " + (string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim()));

        using (UnityWebRequest request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] payload = Encoding.UTF8.GetBytes(body);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
            request.disposeUploadHandlerOnDispose = true;
            request.disposeDownloadHandlerOnDispose = true;

            onLog?.Invoke("正在向 OpenAI 发送请求...");
            float startTime = Time.realtimeSinceStartup;
            float nextHeartbeat = HeartbeatIntervalSeconds;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                if (elapsed >= nextHeartbeat)
                {
                    onLog?.Invoke("请求进行中... " + elapsed.ToString("0.0") + "s");
                    nextHeartbeat += HeartbeatIntervalSeconds;
                }

                yield return null;
            }

            string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            float totalSeconds = Time.realtimeSinceStartup - startTime;
            onLog?.Invoke("OpenAI 响应已返回。HTTP " + request.responseCode + "，耗时 " + totalSeconds.ToString("0.0") + "s。");
            if (request.result != UnityWebRequest.Result.Success)
            {
                string message = ExtractApiError(responseText);
                onLog?.Invoke("请求失败: " + (string.IsNullOrEmpty(message) ? request.error : message));
                onError?.Invoke(string.IsNullOrEmpty(message) ? request.error : message);
                yield break;
            }

            OpenAIVoxelBlueprint blueprint;
            string parseError;
            onLog?.Invoke("开始解析 OpenAI 返回的 voxel 蓝图...");
            if (!TryParseBlueprint(responseText, out blueprint, out parseError))
            {
                onLog?.Invoke("蓝图解析失败: " + parseError);
                onError?.Invoke(parseError);
                yield break;
            }

            onLog?.Invoke("蓝图解析成功，体素数 " + CountFilledVoxels(blueprint) + "。");
            onSuccess?.Invoke(blueprint);
        }
    }

    public static int CountFilledVoxels(OpenAIVoxelBlueprint blueprint)
    {
        if (blueprint == null || blueprint.layers == null)
        {
            return 0;
        }

        int filledVoxelCount = 0;
        for (int layerIndex = 0; layerIndex < blueprint.layers.Length; layerIndex++)
        {
            OpenAIVoxelLayer layer = blueprint.layers[layerIndex];
            if (layer == null || layer.rows == null)
            {
                continue;
            }

            for (int rowIndex = 0; rowIndex < layer.rows.Length; rowIndex++)
            {
                string row = layer.rows[rowIndex];
                if (string.IsNullOrEmpty(row))
                {
                    continue;
                }

                for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                {
                    if (row[columnIndex] != '0')
                    {
                        filledVoxelCount++;
                    }
                }
            }
        }

        return filledVoxelCount;
    }

    public static bool IsValidBlueprint(OpenAIVoxelBlueprint blueprint)
    {
        if (blueprint == null || blueprint.layers == null || blueprint.layers.Length != GridSize || blueprint.palette == null)
        {
            return false;
        }

        int filledVoxelCount = 0;
        for (int layerIndex = 0; layerIndex < blueprint.layers.Length; layerIndex++)
        {
            OpenAIVoxelLayer layer = blueprint.layers[layerIndex];
            if (layer == null || layer.rows == null || layer.rows.Length != GridSize)
            {
                return false;
            }

            for (int rowIndex = 0; rowIndex < layer.rows.Length; rowIndex++)
            {
                string row = layer.rows[rowIndex];
                if (string.IsNullOrEmpty(row) || row.Length != GridSize)
                {
                    return false;
                }

                for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                {
                    char value = row[columnIndex];
                    if (value < '0' || value > '6')
                    {
                        return false;
                    }

                    if (value != '0')
                    {
                        filledVoxelCount++;
                    }
                }
            }
        }

        return filledVoxelCount > 64;
    }

    private static bool TryParseBlueprint(string responseJson, out OpenAIVoxelBlueprint blueprint, out string error)
    {
        blueprint = null;
        error = null;

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            error = "OpenAI 返回了空响应。";
            return false;
        }

        ResponsesEnvelope envelope;
        try
        {
            envelope = JsonUtility.FromJson<ResponsesEnvelope>(responseJson);
        }
        catch (Exception exception)
        {
            error = "无法解析 OpenAI 响应: " + exception.Message;
            return false;
        }

        if (envelope != null && envelope.error != null && !string.IsNullOrWhiteSpace(envelope.error.message))
        {
            error = envelope.error.message;
            return false;
        }

        string outputText = ExtractOutputText(envelope);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            error = "OpenAI 没有返回可用的 voxel 蓝图文本。";
            return false;
        }

        try
        {
            blueprint = JsonUtility.FromJson<OpenAIVoxelBlueprint>(outputText);
        }
        catch (Exception exception)
        {
            error = "无法解析蓝图 JSON: " + exception.Message;
            return false;
        }

        if (!IsValidBlueprint(blueprint))
        {
            error = "蓝图格式不完整，已放弃本次在线生成。";
            return false;
        }

        return true;
    }

    private static string ExtractOutputText(ResponsesEnvelope envelope)
    {
        if (envelope == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(envelope.output_text))
        {
            return envelope.output_text;
        }

        if (envelope.output == null)
        {
            return null;
        }

        for (int outputIndex = 0; outputIndex < envelope.output.Length; outputIndex++)
        {
            ResponsesOutputItem item = envelope.output[outputIndex];
            if (item == null || item.content == null)
            {
                continue;
            }

            for (int contentIndex = 0; contentIndex < item.content.Length; contentIndex++)
            {
                ResponsesContentPart content = item.content[contentIndex];
                if (content != null && !string.IsNullOrWhiteSpace(content.text))
                {
                    return content.text;
                }
            }
        }

        return null;
    }

    private static string BuildRequestBody(string model, string prompt)
    {
        StringBuilder builder = new StringBuilder(8192);
        builder.Append('{');
        builder.Append("\"model\":\"").Append(EscapeJson(model)).Append("\",");
        builder.Append("\"store\":false,");
        builder.Append("\"reasoning\":{\"effort\":\"low\"},");
        builder.Append("\"instructions\":\"").Append(EscapeJson(PlannerInstructions)).Append("\",");
        builder.Append("\"input\":\"").Append(EscapeJson(string.IsNullOrWhiteSpace(prompt) ? "mysterious voxel sculpture" : prompt.Trim())).Append("\",");
        builder.Append("\"text\":{\"format\":{");
        builder.Append("\"type\":\"json_schema\",");
        builder.Append("\"name\":\"voxel_blueprint\",");
        builder.Append("\"strict\":true,");
        builder.Append("\"schema\":").Append(SchemaJson);
        builder.Append("}}}");
        return builder.ToString();
    }

    private static string ExtractApiError(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            ErrorEnvelope errorEnvelope = JsonUtility.FromJson<ErrorEnvelope>(responseText);
            if (errorEnvelope != null && errorEnvelope.error != null && !string.IsNullOrWhiteSpace(errorEnvelope.error.message))
            {
                return errorEnvelope.error.message;
            }
        }
        catch
        {
        }

        return responseText.Length <= 220 ? responseText : responseText.Substring(0, 220) + "...";
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(value.Length + 16);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (character < 32)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    [Serializable]
    private sealed class ResponsesEnvelope
    {
        public ResponsesOutputItem[] output;
        public string output_text;
        public ResponseError error;
    }

    [Serializable]
    private sealed class ResponsesOutputItem
    {
        public string type;
        public ResponsesContentPart[] content;
    }

    [Serializable]
    private sealed class ResponsesContentPart
    {
        public string type;
        public string text;
    }

    [Serializable]
    private sealed class ErrorEnvelope
    {
        public ResponseError error;
    }

    [Serializable]
    private sealed class ResponseError
    {
        public string message;
    }
}
