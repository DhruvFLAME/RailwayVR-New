using UnityEditor;
using UnityEngine;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class ClaudeAssistant : EditorWindow
{
    string apiKey      = "";
    string userPrompt  = "";
    string response    = "";
    bool   isLoading   = false;
    Vector2 scrollPos;

    static readonly HttpClient http = new HttpClient();

    [MenuItem("Tools/Claude Assistant")]
    public static void Open() => GetWindow<ClaudeAssistant>("Claude Assistant");

    void OnGUI()
    {
        GUILayout.Label("Claude Assistant", EditorStyles.boldLabel);

        // API Key field
        EditorGUILayout.Space();
        GUILayout.Label("API Key:");
        apiKey = EditorGUILayout.PasswordField(apiKey);

        // Prompt input
        EditorGUILayout.Space();
        GUILayout.Label("Prompt:");
        userPrompt = EditorGUILayout.TextArea(userPrompt, GUILayout.Height(80));

        // Send button
        EditorGUI.BeginDisabledGroup(isLoading || string.IsNullOrEmpty(userPrompt) || string.IsNullOrEmpty(apiKey));
        if (GUILayout.Button(isLoading ? "Thinking..." : "Ask Claude"))
            _ = AskClaude();
        EditorGUI.EndDisabledGroup();

        // Response area
        EditorGUILayout.Space();
        GUILayout.Label("Response:");
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));
        EditorGUILayout.TextArea(response, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(response) && GUILayout.Button("Copy to Clipboard"))
            GUIUtility.systemCopyBuffer = response;
    }

    async Task AskClaude()
    {
        isLoading = true;
        response  = "";
        Repaint();

        try
        {
            http.DefaultRequestHeaders.Clear();
            http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var body = new
            {
                model      = "claude-opus-4-5",
                max_tokens = 1024,
                messages   = new[] { new { role = "user", content = userPrompt } }
            };

            var json    = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var res     = await http.PostAsync("https://api.anthropic.com/v1/messages", content);
            var raw     = await res.Content.ReadAsStringAsync();

            dynamic parsed = JsonConvert.DeserializeObject(raw);
            response = parsed.content[0].text;
        }
        catch (System.Exception e)
        {
            response = $"Error: {e.Message}";
        }

        isLoading = false;
        Repaint();
    }
}

