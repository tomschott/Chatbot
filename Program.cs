using System;
using Microsoft.CognitiveServices.Speech;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

class Program
{
    // Set your Azure Speech and Azure OpenAI API credentials
    private static string AZURE_SPEECH_KEY = Environment.GetEnvironmentVariable("SPEECH_KEY");
    private static string AZURE_REGION = Environment.GetEnvironmentVariable("SPEECH_REGION");
    private static string AZURE_OPENAI_KEY = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
    private static string AZURE_OPENAI_ENDPOINT = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

    // Azure Speech configuration
    private static SpeechConfig speechConfig = SpeechConfig.FromSubscription(AZURE_SPEECH_KEY, AZURE_REGION);

    static async Task Main(string[] args)
    {
        await ConversationalBot();
    }

    public static async Task<string> RecognizeSpeechAsync()
    {
        var speechRecognizer = new SpeechRecognizer(speechConfig);
        Console.WriteLine("Say something...");

        var result = await speechRecognizer.RecognizeOnceAsync();

        if (result.Reason == ResultReason.RecognizedSpeech)
        {
            Console.WriteLine($"Recognized: {result.Text}");
            return result.Text;
        }
        else if (result.Reason == ResultReason.NoMatch)
        {
            Console.WriteLine("No speech could be recognized.");
        }
        else if (result.Reason == ResultReason.Canceled)
        {
            var cancellation = CancellationDetails.FromResult(result);
            Console.WriteLine($"Speech Recognition canceled: {cancellation.Reason}");
            if (cancellation.Reason == CancellationReason.Error)
            {
                Console.WriteLine($"Error details: {cancellation.ErrorDetails}");
            }
        }

        return null;
    }

    public static async Task<string> GetAzureOpenAIResponseAsync(string userInput)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("api-key", AZURE_OPENAI_KEY);

            var message = new
            {
                model = "gpt-4o-mini",
                messages = new List<Dictionary<string, string>>()
                {
                    new Dictionary<string, string>
                    {
                        {"role", "system"},
                        {"content", "You are Albert Einstein"}
                    },
                    new Dictionary<string, string>
                    {
                        {"role", "user"},
                        {"content", userInput}
                    }
                }
            };

            var content = new StringContent(JsonConvert.SerializeObject(message), System.Text.Encoding.UTF8, "application/json");
            
            // Assuming /openai/deployments/{deployment-id}/chat/completions is the correct endpoint for Azure OpenAI
            var response = await client.PostAsync($"{AZURE_OPENAI_ENDPOINT}/openai/deployments/gpt-4o-mini/chat/completions?api-version=2023-03-15-preview", content);
            //var response = await client.PostAsync($"{AZURE_OPENAI_ENDPOINT}/openai/deployments/YOUR_DEPLOYMENT_ID/chat/completions?api-version=2023-03-15-preview", content);
            
            var responseString = await response.Content.ReadAsStringAsync();
            var responseJson = JObject.Parse(responseString);
            var responseText = responseJson["choices"][0]["message"]["content"].ToString();

            Console.WriteLine($"Azure GPT-4 Response: {responseText}");
            return responseText;
        }
    }

    public static async Task SynthesizeSpeechAsync(string text)
    {
        var speechSynthesizer = new SpeechSynthesizer(speechConfig);
        var result = await speechSynthesizer.SpeakTextAsync(text);

        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            Console.WriteLine("Speech synthesized to speaker.");
        }
        else if (result.Reason == ResultReason.Canceled)
        {
            var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
            Console.WriteLine($"Speech synthesis canceled: {cancellation.Reason}");
            if (cancellation.Reason == CancellationReason.Error)
            {
                Console.WriteLine($"Error details: {cancellation.ErrorDetails}");
            }
        }
    }

    public static async Task ConversationalBot()
    {
        while (true)
        {
            // Step 1: Recognize speech (speech-to-text)
            var userInput = await RecognizeSpeechAsync();
            if (string.IsNullOrEmpty(userInput))
            {
                Console.WriteLine("Didn't catch that, please try again.");
                continue;
            }

            // End conversation if users says stop
            if (userInput.ToLower().Trim() == "stop.")
            {
                break;
            }

            // Step 2: Get response from Azure OpenAI
            var response = await GetAzureOpenAIResponseAsync(userInput);

            // Step 3: Convert response text to speech (text-to-speech)
            await SynthesizeSpeechAsync(response);
        }
    }
}
